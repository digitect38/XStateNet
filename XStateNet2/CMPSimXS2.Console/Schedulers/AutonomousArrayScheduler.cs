using System.Collections.Concurrent;
using CMPSimXS2.Console.Models;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Hybrid scheduler combining Autonomous + Array optimizations.
/// Combines:
/// - Autonomous: Self-managing polling loops (10ms intervals)
/// - Array: Byte-indexed states for O(1) comparisons
///
/// Best of both worlds:
/// ✅ Autonomous polling behavior (SimpleCMPSchedulerDemo style)
/// ✅ Byte-indexed state comparisons (faster than string)
/// ✅ Lock-free concurrency (ConcurrentQueue/Dictionary)
/// ✅ Route-aware autonomous decisions
/// ✅ Continuous validation
/// </summary>
public class AutonomousArrayScheduler : IRobotScheduler, IDisposable
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

    #region Fields (Lock-free concurrency)

    private readonly ConcurrentDictionary<string, RobotContext> _robots = new();
    private readonly ConcurrentDictionary<string, StationContext> _stations = new();
    private readonly ConcurrentQueue<TransferRequest> _pendingRequests = new();

    private readonly List<Task> _robotTasks = new();
    private CancellationTokenSource? _cts;
    private Task? _validationTask;

    // Validation tracking
    private int _totalWaferCount = 0;
    private readonly object _validationLock = new();

    #endregion

    public AutonomousArrayScheduler()
    {
        Logger.Instance.Log("[AutonomousArrayScheduler] Initializing HYBRID scheduler (Autonomous + Array optimizations)");
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
        Logger.Instance.Log($"[AutonomousArrayScheduler] Registered robot: {robotId} (state byte: {STATE_IDLE})");

        // Auto-start autonomous mode on first robot registration
        if (_cts == null)
        {
            _cts = new CancellationTokenSource();
            _validationTask = RunValidationLoop(_cts.Token);
            Logger.Instance.Log("[AutonomousArrayScheduler] Auto-started autonomous mode");
        }

        // Start polling loop for this robot
        var task = RunRobotPollingLoop(robotId, _cts.Token);
        _robotTasks.Add(task);
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

            Logger.Instance.Log($"[AutonomousArrayScheduler] {robotId} state: byte {oldStateByte} → {context.StateByte} (wafer: {oldWafer} → {heldWaferId})");
        }
    }

    public void RequestTransfer(TransferRequest request)
    {
        try
        {
            request.Validate();
            _pendingRequests.Enqueue(request);
            Logger.Instance.Log($"[AutonomousArrayScheduler] Transfer queued: {request} (Queue size: {_pendingRequests.Count})");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[AutonomousArrayScheduler:ERROR] Invalid transfer request: {ex.Message}");
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
        Logger.Instance.Log($"[AutonomousArrayScheduler] Registered station: {stationName} (state: {initialState})");
    }

    public void UpdateStationState(string stationName, string state, int? waferId = null)
    {
        if (_stations.TryGetValue(stationName, out var context))
        {
            var oldState = context.State;
            var oldWafer = context.WaferId;

            context.State = state;
            context.WaferId = waferId;

            Logger.Instance.Log($"[AutonomousArrayScheduler] Station {stationName}: {oldState} → {state} (wafer: {oldWafer} → {waferId})");
        }
    }

    #endregion

    #region Autonomous Polling Loops (from Autonomous)

    /// <summary>
    /// Start all autonomous loops (robot polling + validation)
    /// </summary>
    public void StartAutonomousMode(int totalWaferCount = 10)
    {
        _totalWaferCount = totalWaferCount;
        _cts = new CancellationTokenSource();

        // Start robot polling loops
        foreach (var robotId in _robots.Keys)
        {
            var task = RunRobotPollingLoop(robotId, _cts.Token);
            _robotTasks.Add(task);
        }

        // Start validation loop
        _validationTask = RunValidationLoop(_cts.Token);

        Logger.Instance.Log($"[AutonomousArrayScheduler] Started autonomous mode with {_robots.Count} robots, expecting {totalWaferCount} wafers");
    }

    /// <summary>
    /// Autonomous polling loop for each robot
    /// Uses BYTE comparisons instead of STRING comparisons (Array optimization!)
    /// </summary>
    private async Task RunRobotPollingLoop(string robotId, CancellationToken token)
    {
        try
        {
            Logger.Instance.Log($"[AutonomousArrayScheduler] ✅ Starting polling loop for {robotId}");

            int pollCount = 0;
            while (!token.IsCancellationRequested)
            {
                pollCount++;

                if (_robots.TryGetValue(robotId, out var robot))
                {
                    // Log every 100 polls (once per second at 10ms intervals)
                    if (pollCount % 100 == 0)
                    {
                        Logger.Instance.Log($"[AutonomousArrayScheduler] {robotId} polling... state byte={robot.StateByte}, queue={_pendingRequests.Count}");
                    }

                    // Array optimization: BYTE comparison instead of string!
                    if (robot.StateByte == STATE_IDLE)
                    {
                        // Robot is idle - check for pending work
                        if (_pendingRequests.TryPeek(out var request))
                        {
                            Logger.Instance.Log($"[AutonomousArrayScheduler] {robotId} found pending request: {request.WaferId} {request.From}→{request.To}");

                            // Array-optimized route matching
                            bool canHandle = CanRobotHandleTransferFast(robotId, request);
                            Logger.Instance.Log($"[AutonomousArrayScheduler] {robotId} canHandle={canHandle}");

                            if (canHandle)
                            {
                                // Dequeue and assign to this robot
                                if (_pendingRequests.TryDequeue(out var dequeuedRequest))
                                {
                                    Logger.Instance.Log($"[AutonomousArrayScheduler] {robotId} dequeued request, assigning...");
                                    await AssignTransferToRobot(robotId, robot, dequeuedRequest);
                                }
                            }
                        }
                    }
                }

                // Poll every 10ms (like SimpleCMPSchedulerDemo)
                await Task.Delay(10, token);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Instance.Log($"[AutonomousArrayScheduler] Polling loop stopped for {robotId}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[AutonomousArrayScheduler:ERROR] Polling loop error for {robotId}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Continuous validation loop
    /// </summary>
    private async Task RunValidationLoop(CancellationToken token)
    {
        try
        {
            Logger.Instance.Log("[AutonomousArrayScheduler] Starting validation loop");

            int consecutiveMismatches = 0;
            int lastMismatchCount = 0;

            while (!token.IsCancellationRequested)
            {
                lock (_validationLock)
                {
                    int totalWafers = CountTotalWafers();

                    if (totalWafers != _totalWaferCount)
                    {
                        consecutiveMismatches++;
                        lastMismatchCount = totalWafers;

                        if (consecutiveMismatches >= 3)
                        {
                            Logger.Instance.Log($"[AutonomousArrayScheduler:WARNING] Wafer count mismatch! Expected: {_totalWaferCount}, Found: {lastMismatchCount} (for {consecutiveMismatches} checks)");
                        }
                    }
                    else
                    {
                        consecutiveMismatches = 0;
                    }
                }

                // Check every 500ms
                await Task.Delay(500, token);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Instance.Log("[AutonomousArrayScheduler] Validation loop stopped");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[AutonomousArrayScheduler:ERROR] Validation loop error: {ex.Message}");
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

    private async Task AssignTransferToRobot(string robotId, RobotContext robot, TransferRequest request)
    {
        Logger.Instance.Log($"[AutonomousArrayScheduler] Assigning {robotId} for transfer: wafer {request.WaferId} from {request.From} to {request.To}");

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
        Logger.Instance.Log($"[AutonomousArrayScheduler] Sent PICKUP to {robotId}");

        // Invoke completion callback if provided
        await Task.Run(() => request.OnCompleted?.Invoke(request.WaferId));
    }

    private int CountTotalWafers()
    {
        var waferIds = new HashSet<int>();

        // Count wafers in robots
        foreach (var robot in _robots.Values)
        {
            if (robot.HeldWaferId.HasValue)
            {
                waferIds.Add(robot.HeldWaferId.Value);
            }
        }

        // Count wafers in stations
        foreach (var station in _stations.Values)
        {
            if (station.WaferId.HasValue)
            {
                waferIds.Add(station.WaferId.Value);
            }
        }

        return waferIds.Count;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();

        Logger.Instance.Log("[AutonomousArrayScheduler] Disposed");
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
