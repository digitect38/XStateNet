using System.Collections.Concurrent;
using Akka.Actor;
using CMPSimXS2.Console.Models;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Actor mailbox-based event-driven scheduler.
///
/// Uses Akka.NET actor mailbox for event-driven dispatch instead of manual locks:
/// - ✅ Natural event queueing (actor mailbox)
/// - ✅ Built-in serialization (one message at a time)
/// - ✅ No manual locks needed
/// - ✅ Optimized by Akka.NET
/// - ✅ Byte-indexed states for O(1) comparisons
///
/// Architecture:
/// - DispatcherActor receives dispatch trigger messages
/// - Mailbox automatically queues and serializes them
/// - Combines actor benefits with byte optimizations
/// </summary>
public class ActorMailboxEventDrivenScheduler : IRobotScheduler, IDisposable
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

    #region Fields

    private readonly ActorSystem _actorSystem;
    private readonly IActorRef _dispatcherActor;

    #endregion

    public ActorMailboxEventDrivenScheduler(ActorSystem actorSystem, string? actorNamePrefix = null)
    {
        _actorSystem = actorSystem;

        // Create unique dispatcher actor name
        var actorName = actorNamePrefix != null
            ? $"{actorNamePrefix}-dispatcher"
            : $"event-driven-dispatcher-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        // Create dispatcher actor that uses mailbox for event queueing
        _dispatcherActor = _actorSystem.ActorOf(
            Props.Create(() => new DispatcherActor()),
            actorName
        );

        Logger.Instance.Log($"[ActorMailboxEventDrivenScheduler] Initialized with ACTOR MAILBOX (dispatcher: {actorName})");
    }

    #region IRobotScheduler Implementation

    public void RegisterRobot(string robotId, IActorRef robotActor)
    {
        // Send registration message to dispatcher actor
        _dispatcherActor.Tell(new RegisterRobotMessage(robotId, robotActor));
        Logger.Instance.Log($"[ActorMailboxEventDrivenScheduler] Sent RegisterRobot to dispatcher: {robotId}");
    }

    public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
    {
        // Send state update message to dispatcher actor
        var stateByte = ConvertStateToByte(state);
        _dispatcherActor.Tell(new UpdateRobotStateMessage(robotId, stateByte, heldWaferId, waitingFor));
        Logger.Instance.Log($"[ActorMailboxEventDrivenScheduler] Sent UpdateRobotState to dispatcher: {robotId} → byte {stateByte}");
    }

    public void RequestTransfer(TransferRequest request)
    {
        try
        {
            request.Validate();
            // Send transfer request directly to dispatcher actor mailbox!
            // No ConcurrentQueue - pure Akka.NET!
            _dispatcherActor.Tell(request);
            Logger.Instance.Log($"[ActorMailboxEventDrivenScheduler] Sent TransferRequest to dispatcher mailbox: {request}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[ActorMailboxEventDrivenScheduler:ERROR] Invalid transfer request: {ex.Message}");
        }
    }

    public int GetQueueSize()
    {
        // Ask dispatcher for queue size
        var result = _dispatcherActor.Ask<int>(new GetQueueSizeMessage(), TimeSpan.FromSeconds(1)).Result;
        return result;
    }

    public string GetRobotState(string robotId)
    {
        // Ask dispatcher for robot state
        var stateByte = _dispatcherActor.Ask<byte>(new GetRobotStateMessage(robotId), TimeSpan.FromSeconds(1)).Result;
        return ConvertByteToState(stateByte);
    }

    #endregion

    #region Station Registration

    public void RegisterStation(string stationName, string initialState = "idle", int? wafer = null)
    {
        _dispatcherActor.Tell(new RegisterStationMessage(stationName, initialState, wafer));
        Logger.Instance.Log($"[ActorMailboxEventDrivenScheduler] Sent RegisterStation to dispatcher: {stationName}");
    }

    public void UpdateStationState(string stationName, string state, int? waferId = null)
    {
        _dispatcherActor.Tell(new UpdateStationStateMessage(stationName, state, waferId));
        Logger.Instance.Log($"[ActorMailboxEventDrivenScheduler] Sent UpdateStationState to dispatcher: {stationName}");
    }

    #endregion

    #region Helper Methods

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

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Logger.Instance.Log("[ActorMailboxEventDrivenScheduler] Disposed");
    }

    #endregion

    #region Messages

    internal record RegisterRobotMessage(string RobotId, IActorRef RobotActor);
    internal record UpdateRobotStateMessage(string RobotId, byte StateByte, int? HeldWaferId, string? WaitingFor);
    internal record RegisterStationMessage(string StationName, string InitialState, int? Wafer);
    internal record UpdateStationStateMessage(string StationName, string State, int? WaferId);
    internal record GetQueueSizeMessage();
    internal record GetRobotStateMessage(string RobotId);

    #endregion

    #region DispatcherActor

    /// <summary>
    /// Pure Akka.NET dispatcher actor
    /// - All state stored internally (robots, stations, pending requests)
    /// - All messages processed via mailbox (no ConcurrentQueue!)
    /// - Byte-indexed states for O(1) comparisons
    /// - Actor mailbox handles all queueing and serialization
    /// </summary>
    private class DispatcherActor : ReceiveActor
    {
        private readonly Dictionary<string, RobotContext> _robots = new();
        private readonly Dictionary<string, StationContext> _stations = new();
        private readonly Queue<TransferRequest> _pendingRequests = new();  // Simple Queue - actor ensures serial access!

        public DispatcherActor()
        {
            // Register robot
            Receive<RegisterRobotMessage>(msg =>
            {
                var context = new RobotContext
                {
                    RobotId = msg.RobotId,
                    RobotActor = msg.RobotActor,
                    StateByte = STATE_IDLE,
                    HeldWaferId = null
                };
                _robots[msg.RobotId] = context;
                Logger.Instance.Log($"[DispatcherActor] Registered robot: {msg.RobotId} (state byte: {STATE_IDLE})");
            });

            // Update robot state
            Receive<UpdateRobotStateMessage>(msg =>
            {
                if (_robots.TryGetValue(msg.RobotId, out var context))
                {
                    var oldStateByte = context.StateByte;
                    context.StateByte = msg.StateByte;
                    context.HeldWaferId = msg.HeldWaferId;
                    context.WaitingFor = msg.WaitingFor;

                    Logger.Instance.Log($"[DispatcherActor] {msg.RobotId} state: byte {oldStateByte} → {msg.StateByte} (wafer: {context.HeldWaferId} → {msg.HeldWaferId})");

                    // If robot becomes idle, try to dispatch pending work
                    if (msg.StateByte == STATE_IDLE)
                    {
                        TryDispatchPendingRequests();
                    }
                }
            });

            // Transfer request - queued in mailbox, then added to internal queue
            Receive<TransferRequest>(request =>
            {
                _pendingRequests.Enqueue(request);
                Logger.Instance.Log($"[DispatcherActor] Transfer queued in mailbox: {request} (Queue size: {_pendingRequests.Count})");

                // Try to dispatch immediately
                TryDispatchPendingRequests();
            });

            // Register station
            Receive<RegisterStationMessage>(msg =>
            {
                var context = new StationContext
                {
                    StationName = msg.StationName,
                    State = msg.InitialState,
                    WaferId = msg.Wafer
                };
                _stations[msg.StationName] = context;
                Logger.Instance.Log($"[DispatcherActor] Registered station: {msg.StationName} (state: {msg.InitialState})");
            });

            // Update station state
            Receive<UpdateStationStateMessage>(msg =>
            {
                if (_stations.TryGetValue(msg.StationName, out var context))
                {
                    context.State = msg.State;
                    context.WaferId = msg.WaferId;
                    Logger.Instance.Log($"[DispatcherActor] Station {msg.StationName}: {context.State} → {msg.State}");
                }
            });

            // Query: Get queue size
            Receive<GetQueueSizeMessage>(_ =>
            {
                Sender.Tell(_pendingRequests.Count);
            });

            // Query: Get robot state
            Receive<GetRobotStateMessage>(msg =>
            {
                if (_robots.TryGetValue(msg.RobotId, out var context))
                {
                    Sender.Tell(context.StateByte);
                }
                else
                {
                    Sender.Tell((byte)255); // Unknown
                }
            });
        }

        /// <summary>
        /// Try to dispatch pending requests to idle robots
        /// No locking needed - actor ensures serial execution!
        /// </summary>
        private void TryDispatchPendingRequests()
        {
            while (_pendingRequests.Count > 0)
            {
                var request = _pendingRequests.Peek();
                bool assigned = false;

                // Find an idle robot that can handle this request
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
                            // Dequeue and assign
                            _pendingRequests.Dequeue();
                            Logger.Instance.Log($"[DispatcherActor] ✅ Dispatching to {robotId}: wafer {request.WaferId} {request.From}→{request.To}");
                            AssignTransferToRobot(robotId, robot, request);
                            assigned = true;
                            break;
                        }
                    }
                }

                // If no robot available, stop dispatching
                if (!assigned)
                {
                    Logger.Instance.Log($"[DispatcherActor] No idle robot available for request: {request.WaferId} {request.From}→{request.To}");
                    break;
                }
            }
        }

        /// <summary>
        /// Byte-indexed route matching (Array optimization)
        /// </summary>
        private bool CanRobotHandleTransferFast(string robotId, TransferRequest request)
        {
            if (!string.IsNullOrEmpty(request.PreferredRobotId))
            {
                return robotId == request.PreferredRobotId;
            }

            byte routeByte = GetRouteByte(request.From, request.To);

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

        private byte GetRouteByte(string from, string to)
        {
            return (from, to) switch
            {
                ("Carrier", "Polisher") => ROUTE_CARRIER_POLISHER,
                ("Polisher", "Cleaner") => ROUTE_POLISHER_CLEANER,
                ("Cleaner", "Buffer") => ROUTE_CLEANER_BUFFER,
                ("Buffer", "Carrier") => ROUTE_BUFFER_CARRIER,
                ("Polisher", "Carrier") => ROUTE_POLISHER_CARRIER,
                _ => byte.MaxValue
            };
        }

        private void AssignTransferToRobot(string robotId, RobotContext robot, TransferRequest request)
        {
            Logger.Instance.Log($"[DispatcherActor] Assigning {robotId} for transfer: wafer {request.WaferId} from {request.From} to {request.To}");

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

            robot.RobotActor.Tell(new XStateNet2.Core.Messages.SendEvent("PICKUP", pickupData), ActorRefs.NoSender);
            Logger.Instance.Log($"[DispatcherActor] Sent PICKUP to {robotId}");

            // Invoke completion callback
            request.OnCompleted?.Invoke(request.WaferId);
        }

        private class RobotContext
        {
            public string RobotId { get; set; } = "";
            public IActorRef RobotActor { get; set; } = null!;
            public byte StateByte { get; set; } = STATE_IDLE;
            public int? HeldWaferId { get; set; }
            public string? WaitingFor { get; set; }
        }

        private class StationContext
        {
            public string StationName { get; set; } = "";
            public string State { get; set; } = "idle";
            public int? WaferId { get; set; }
        }
    }

    #endregion
}
