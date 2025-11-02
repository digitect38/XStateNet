using System.Collections.Concurrent;
using CMPSimXS2.Console.Models;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Event-driven hybrid scheduler combining byte optimizations with event-driven dispatch.
///
/// Key differences from AutonomousArrayScheduler:
/// - Event-driven dispatch (no polling loops!)
/// - Triggered on RequestTransfer() and UpdateRobotState()
/// - Immediate response instead of 10ms polling delay
///
/// Combines:
/// - Array: Byte-indexed states for O(1) comparisons
/// - Event-driven: Immediate dispatch on state transitions
/// - Lock-free: ConcurrentQueue/Dictionary where possible
/// </summary>
public class EventDrivenHybridScheduler : IRobotScheduler, IDisposable
{
    #region Byte-indexed State Constants (Array optimization)

    // Robot states (byte-indexed for fast comparison)
    private const byte STATE_IDLE = 0;
    private const byte STATE_BUSY = 1;
    private const byte STATE_CARRYING = 2;

    // Route identifiers (for faster route matching)
    private const byte ROUTE_CARRIER_POLISHER = 0;
    private const byte ROUTE_POLISHER_CLEANER = 1;
    private const byte ROUTE_CLEANER_BUFFER = 2;
    private const byte ROUTE_BUFFER_CARRIER = 3;
    private const byte ROUTE_POLISHER_CARRIER = 4;

    #endregion

    #region Fields (Lock-free + Event-driven)

    private readonly ConcurrentDictionary<string, RobotContext> _robots = new();
    private readonly ConcurrentDictionary<string, StationContext> _stations = new();
    private readonly ConcurrentQueue<TransferRequest> _pendingRequests = new();

    // Dispatch coordination (optimized to reduce contention)
    private readonly object _dispatchLock = new();
    private volatile bool _isDispatching = false;
    private volatile bool _dispatchRequested = false;

    #endregion

    public EventDrivenHybridScheduler()
    {
        Logger.Instance.Log("[EventDrivenHybridScheduler] Initializing EVENT-DRIVEN HYBRID scheduler (Byte optimization + Event-driven dispatch)");
    }

    #region IRobotScheduler Implementation

    public void RegisterRobot(string robotId, Akka.Actor.IActorRef robotActor)
    {
        var context = new RobotContext
        {
            RobotId = robotId,
            RobotActor = robotActor,
            StateByte = STATE_IDLE,  // Byte instead of string!
            HeldWaferId = null
        };

        _robots[robotId] = context;
        Logger.Instance.Log($"[EventDrivenHybridScheduler] Registered robot: {robotId} (state byte: {STATE_IDLE})");
    }

    public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
    {
        if (_robots.TryGetValue(robotId, out var context))
        {
            var oldStateByte = context.StateByte;
            var oldWafer = context.HeldWaferId;

            // Convert string state to byte (Array optimization)
            context.StateByte = ConvertStateToByte(state);
            context.HeldWaferId = heldWaferId;
            context.WaitingFor = waitingFor;

            Logger.Instance.Log($"[EventDrivenHybridScheduler] {robotId} state: byte {oldStateByte} → {context.StateByte} (wafer: {oldWafer} → {heldWaferId})");

            // EVENT-DRIVEN: If robot becomes idle, try to dispatch pending work
            if (context.StateByte == STATE_IDLE)
            {
                Logger.Instance.Log($"[EventDrivenHybridScheduler] {robotId} is now idle, triggering dispatch...");
                TriggerDispatch();
            }
        }
    }

    public void RequestTransfer(TransferRequest request)
    {
        try
        {
            request.Validate();
            _pendingRequests.Enqueue(request);
            Logger.Instance.Log($"[EventDrivenHybridScheduler] Transfer queued: {request} (Queue size: {_pendingRequests.Count})");

            // EVENT-DRIVEN: Trigger dispatch (optimized to reduce contention)
            TriggerDispatch();
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[EventDrivenHybridScheduler:ERROR] Invalid transfer request: {ex.Message}");
        }
    }

    public int GetQueueSize()
    {
        return _pendingRequests.Count;
    }

    public string GetRobotState(string robotId)
    {
        if (_robots.TryGetValue(robotId, out var context))
        {
            // Convert byte back to string for external API
            return ConvertByteToState(context.StateByte);
        }
        return "unknown";
    }

    #endregion

    #region Station Registration

    public void RegisterStation(string stationName, string initialState = "idle", int? wafer = null)
    {
        var context = new StationContext
        {
            StationName = stationName,
            State = initialState,
            WaferId = wafer
        };

        _stations[stationName] = context;
        Logger.Instance.Log($"[EventDrivenHybridScheduler] Registered station: {stationName} (state: {initialState})");
    }

    public void UpdateStationState(string stationName, string state, int? waferId = null)
    {
        if (_stations.TryGetValue(stationName, out var context))
        {
            var oldState = context.State;
            var oldWafer = context.WaferId;

            context.State = state;
            context.WaferId = waferId;

            Logger.Instance.Log($"[EventDrivenHybridScheduler] Station {stationName}: {oldState} → {state} (wafer: {oldWafer} → {waferId})");
        }
    }

    #endregion

    #region Event-Driven Dispatch (NO POLLING!)

    /// <summary>
    /// Trigger dispatch - optimized to reduce contention
    /// Only starts a new dispatch if one isn't already running
    /// </summary>
    private void TriggerDispatch()
    {
        // Quick check without locking
        if (_isDispatching)
        {
            _dispatchRequested = true; // Flag for re-dispatch after current one finishes
            return;
        }

        // Start dispatch on background thread (don't wait for it)
        _ = Task.Run(() => TryDispatchPendingRequests());
    }

    /// <summary>
    /// Event-driven dispatch: Try to assign pending requests to idle robots
    /// Uses lock instead of semaphore for better performance
    /// Called when:
    /// 1. New request arrives (RequestTransfer)
    /// 2. Robot becomes idle (UpdateRobotState)
    /// </summary>
    private void TryDispatchPendingRequests()
    {
        // Use lock for synchronous dispatch (faster than semaphore)
        lock (_dispatchLock)
        {
            if (_isDispatching)
            {
                _dispatchRequested = true;
                return; // Already dispatching
            }

            _isDispatching = true;
        }

        try
        {
            // Keep dispatching while there are requests or re-dispatch was requested
            do
            {
                _dispatchRequested = false;

                // Process all pending requests with idle robots
                while (_pendingRequests.TryPeek(out var request))
                {
                    bool assigned = false;

                    // Find an idle robot that can handle this request
                    // Use byte comparison for fast state checking!
                    foreach (var kvp in _robots)
                    {
                        var robotId = kvp.Key;
                        var robot = kvp.Value;

                        // BYTE COMPARISON (Array optimization!)
                        if (robot.StateByte == STATE_IDLE)
                        {
                            // Byte-indexed route matching
                            bool canHandle = CanRobotHandleTransferFast(robotId, request);

                            if (canHandle)
                            {
                                // Try to dequeue (might have been taken by another thread)
                                if (_pendingRequests.TryDequeue(out var dequeuedRequest))
                                {
                                    Logger.Instance.Log($"[EventDrivenHybridScheduler] ✅ Dispatching to {robotId}: wafer {dequeuedRequest.WaferId} {dequeuedRequest.From}→{dequeuedRequest.To}");
                                    AssignTransferToRobotSync(robotId, robot, dequeuedRequest);
                                    assigned = true;
                                    break; // Found robot for this request
                                }
                            }
                        }
                    }

                    // If no robot available, stop dispatching
                    if (!assigned)
                    {
                        Logger.Instance.Log($"[EventDrivenHybridScheduler] No idle robot available for request: {request.WaferId} {request.From}→{request.To}");
                        break;
                    }
                }
            } while (_dispatchRequested); // Re-dispatch if requested while we were processing
        }
        finally
        {
            _isDispatching = false;
        }
    }

    #endregion

    #region Helper Methods (Array-optimized)

    /// <summary>
    /// Array-optimized route matching using byte identifiers
    /// Faster than string comparisons!
    /// </summary>
    private bool CanRobotHandleTransferFast(string robotId, TransferRequest request)
    {
        // Check preferred robot
        if (!string.IsNullOrEmpty(request.PreferredRobotId))
        {
            return robotId == request.PreferredRobotId;
        }

        // Convert route to byte identifier for fast comparison
        byte routeByte = GetRouteByte(request.From, request.To);

        // Array-optimized route matching
        return robotId switch
        {
            "Robot 1" => routeByte == ROUTE_CARRIER_POLISHER ||
                        routeByte == ROUTE_BUFFER_CARRIER ||
                        routeByte == ROUTE_POLISHER_CARRIER,
            "Robot 2" => routeByte == ROUTE_POLISHER_CLEANER,
            "Robot 3" => routeByte == ROUTE_CLEANER_BUFFER,
            _ => false
        };
    }

    /// <summary>
    /// Convert route to byte identifier (Array optimization)
    /// </summary>
    private byte GetRouteByte(string from, string to)
    {
        return (from, to) switch
        {
            ("Carrier", "Polisher") => ROUTE_CARRIER_POLISHER,
            ("Polisher", "Cleaner") => ROUTE_POLISHER_CLEANER,
            ("Cleaner", "Buffer") => ROUTE_CLEANER_BUFFER,
            ("Buffer", "Carrier") => ROUTE_BUFFER_CARRIER,
            ("Polisher", "Carrier") => ROUTE_POLISHER_CARRIER,
            _ => byte.MaxValue  // Invalid route
        };
    }

    /// <summary>
    /// Convert string state to byte (Array optimization)
    /// </summary>
    private byte ConvertStateToByte(string state)
    {
        return state switch
        {
            "idle" => STATE_IDLE,
            "busy" => STATE_BUSY,
            "carrying" => STATE_CARRYING,
            _ => STATE_IDLE
        };
    }

    /// <summary>
    /// Convert byte state to string (for external API compatibility)
    /// </summary>
    private string ConvertByteToState(byte stateByte)
    {
        return stateByte switch
        {
            STATE_IDLE => "idle",
            STATE_BUSY => "busy",
            STATE_CARRYING => "carrying",
            _ => "unknown"
        };
    }

    private void AssignTransferToRobotSync(string robotId, RobotContext robot, TransferRequest request)
    {
        Logger.Instance.Log($"[EventDrivenHybridScheduler] Assigning {robotId} for transfer: wafer {request.WaferId} from {request.From} to {request.To}");

        // Update robot state (using byte)
        robot.StateByte = STATE_BUSY;
        robot.HeldWaferId = request.WaferId;

        // Send PICKUP event to robot actor
        var pickupData = new Dictionary<string, object>
        {
            ["waferId"] = request.WaferId,
            ["wafer"] = request.WaferId,
            ["from"] = request.From,
            ["to"] = request.To
        };

        robot.RobotActor.Tell(new XStateNet2.Core.Messages.SendEvent("PICKUP", pickupData), Akka.Actor.ActorRefs.NoSender);
        Logger.Instance.Log($"[EventDrivenHybridScheduler] Sent PICKUP to {robotId}");

        // Invoke completion callback if provided (synchronously for benchmark performance)
        request.OnCompleted?.Invoke(request.WaferId);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Logger.Instance.Log("[EventDrivenHybridScheduler] Disposed");
    }

    #endregion

    #region Context Classes (Byte-optimized)

    private class RobotContext
    {
        public string RobotId { get; set; } = "";
        public Akka.Actor.IActorRef RobotActor { get; set; } = null!;
        public byte StateByte { get; set; } = STATE_IDLE;  // BYTE instead of string!
        public int? HeldWaferId { get; set; }
        public string? WaitingFor { get; set; }
    }

    private class StationContext
    {
        public string StationName { get; set; } = "";
        public string State { get; set; } = "idle";
        public int? WaferId { get; set; }
    }

    #endregion
}
