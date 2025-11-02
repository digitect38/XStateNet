using System.Collections.Concurrent;
using CMPSimXS2.Console.Models;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Autonomous robot scheduler inspired by SimpleCMPSchedulerDemo.
/// Each robot runs an independent polling loop and makes its own decisions.
/// Combines SimpleCMPScheduler's autonomous behavior with thread-safe operations.
/// </summary>
public class AutonomousRobotScheduler : IRobotScheduler, IDisposable
{
    private readonly ConcurrentDictionary<string, RobotContext> _robots = new();
    private readonly ConcurrentDictionary<string, StationContext> _stations = new();
    private readonly ConcurrentQueue<TransferRequest> _pendingRequests = new();

    private readonly List<Task> _robotTasks = new();
    private CancellationTokenSource? _cts;
    private Task? _validationTask;

    // Validation tracking
    private int _totalWaferCount = 0;
    private readonly object _validationLock = new();

    public AutonomousRobotScheduler()
    {
        Logger.Instance.Log("[AutonomousRobotScheduler] Initializing autonomous robot scheduler with polling loops");
    }

    #region IRobotScheduler Implementation

    public void RegisterRobot(string robotId, Akka.Actor.IActorRef robotActor)
    {
        var context = new RobotContext
        {
            RobotId = robotId,
            RobotActor = robotActor,
            State = "idle",
            HeldWaferId = null
        };

        _robots[robotId] = context;
        Logger.Instance.Log($"[AutonomousRobotScheduler] Registered robot: {robotId}");

        // Auto-start autonomous mode on first robot registration
        if (_cts == null)
        {
            _cts = new CancellationTokenSource();
            _validationTask = RunValidationLoop(_cts.Token);
            Logger.Instance.Log("[AutonomousRobotScheduler] Auto-started autonomous mode");
        }

        // Start polling loop for this robot
        var task = RunRobotPollingLoop(robotId, _cts.Token);
        _robotTasks.Add(task);
    }

    public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
    {
        if (_robots.TryGetValue(robotId, out var context))
        {
            var oldState = context.State;
            var oldWafer = context.HeldWaferId;

            context.State = state;
            context.HeldWaferId = heldWaferId;
            context.WaitingFor = waitingFor;

            Logger.Instance.Log($"[AutonomousRobotScheduler] {robotId} state: {oldState} → {state} (wafer: {oldWafer} → {heldWaferId})");
        }
    }

    public void RequestTransfer(TransferRequest request)
    {
        try
        {
            request.Validate();
            _pendingRequests.Enqueue(request);
            Logger.Instance.Log($"[AutonomousRobotScheduler] Transfer queued: {request} (Queue size: {_pendingRequests.Count})");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[AutonomousRobotScheduler:ERROR] Invalid transfer request: {ex.Message}");
        }
    }

    public int GetQueueSize()
    {
        return _pendingRequests.Count;
    }

    public string GetRobotState(string robotId)
    {
        return _robots.TryGetValue(robotId, out var context) ? context.State : "unknown";
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
        Logger.Instance.Log($"[AutonomousRobotScheduler] Registered station: {stationName} (state: {initialState})");
    }

    public void UpdateStationState(string stationName, string state, int? waferId = null)
    {
        if (_stations.TryGetValue(stationName, out var context))
        {
            var oldState = context.State;
            var oldWafer = context.WaferId;

            context.State = state;
            context.WaferId = waferId;

            Logger.Instance.Log($"[AutonomousRobotScheduler] Station {stationName}: {oldState} → {state} (wafer: {oldWafer} → {waferId})");
        }
    }

    #endregion

    #region Autonomous Polling Loops

    /// <summary>
    /// Start all autonomous loops (robot polling + validation)
    /// </summary>
    public void StartAutonomousMode(int totalWaferCount = 10)
    {
        _totalWaferCount = totalWaferCount;
        _cts = new CancellationTokenSource();

        // Start robot polling loops (already started in RegisterRobot if _cts exists)
        foreach (var robotId in _robots.Keys)
        {
            var task = RunRobotPollingLoop(robotId, _cts.Token);
            _robotTasks.Add(task);
        }

        // Start validation loop
        _validationTask = RunValidationLoop(_cts.Token);

        Logger.Instance.Log($"[AutonomousRobotScheduler] Started autonomous mode with {_robots.Count} robots, expecting {totalWaferCount} wafers");
    }

    /// <summary>
    /// Autonomous polling loop for each robot (inspired by SimpleCMPSchedulerDemo)
    /// Each robot checks system state every 10ms and decides actions autonomously
    /// </summary>
    private async Task RunRobotPollingLoop(string robotId, CancellationToken token)
    {
        try
        {
            Logger.Instance.Log($"[AutonomousRobotScheduler] ✅ Starting polling loop for {robotId}");

            int pollCount = 0;
            while (!token.IsCancellationRequested)
            {
                pollCount++;

                if (_robots.TryGetValue(robotId, out var robot))
                {
                    // Log every 100 polls (once per second at 10ms intervals)
                    if (pollCount % 100 == 0)
                    {
                        Logger.Instance.Log($"[AutonomousRobotScheduler] {robotId} polling... state={robot.State}, queue={_pendingRequests.Count}");
                    }

                    if (robot.State == "idle")
                    {
                        // Robot is idle - check for pending work
                        if (_pendingRequests.TryPeek(out var request))
                        {
                            Logger.Instance.Log($"[AutonomousRobotScheduler] {robotId} found pending request: {request.WaferId} {request.From}→{request.To}");

                            // Check if this robot can handle the request
                            bool canHandle = CanRobotHandleTransfer(robotId, request);
                            Logger.Instance.Log($"[AutonomousRobotScheduler] {robotId} canHandle={canHandle}");

                            if (canHandle)
                            {
                                // Dequeue and assign to this robot
                                if (_pendingRequests.TryDequeue(out var dequeuedRequest))
                                {
                                    Logger.Instance.Log($"[AutonomousRobotScheduler] {robotId} dequeued request, assigning...");
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
            // Normal cancellation
            Logger.Instance.Log($"[AutonomousRobotScheduler] Polling loop stopped for {robotId}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[AutonomousRobotScheduler:ERROR] Polling loop error for {robotId}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Continuous validation loop (inspired by SimpleCMPSchedulerDemo)
    /// Monitors wafer count and detects anomalies
    /// </summary>
    private async Task RunValidationLoop(CancellationToken token)
    {
        try
        {
            Logger.Instance.Log("[AutonomousRobotScheduler] Starting validation loop");

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
                            Logger.Instance.Log($"[AutonomousRobotScheduler:WARNING] Wafer count mismatch! Expected: {_totalWaferCount}, Found: {lastMismatchCount} (for {consecutiveMismatches} checks)");
                        }
                    }
                    else
                    {
                        consecutiveMismatches = 0;
                    }
                }

                // Check every 500ms (like SimpleCMPSchedulerDemo)
                await Task.Delay(500, token);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Instance.Log("[AutonomousRobotScheduler] Validation loop stopped");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[AutonomousRobotScheduler:ERROR] Validation loop error: {ex.Message}");
        }
    }

    #endregion

    #region Helper Methods

    private bool CanRobotHandleTransfer(string robotId, TransferRequest request)
    {
        // Check preferred robot
        if (!string.IsNullOrEmpty(request.PreferredRobotId))
        {
            return robotId == request.PreferredRobotId;
        }

        // Use nearest robot strategy (like SimpleCMPSchedulerDemo)
        // R1: Carrier ↔ Polisher, Buffer ↔ Carrier
        if (robotId == "Robot 1")
        {
            return (request.From == "Carrier" && request.To == "Polisher") ||
                   (request.From == "Buffer" && request.To == "Carrier") ||
                   (request.From == "Polisher" && request.To == "Carrier");
        }

        // R2: Polisher ↔ Cleaner
        if (robotId == "Robot 2")
        {
            return (request.From == "Polisher" && request.To == "Cleaner") ||
                   (request.From == "Cleaner" && request.To == "Polisher");
        }

        // R3: Cleaner ↔ Buffer
        if (robotId == "Robot 3")
        {
            return (request.From == "Cleaner" && request.To == "Buffer") ||
                   (request.From == "Buffer" && request.To == "Cleaner");
        }

        return false;
    }

    private async Task AssignTransferToRobot(string robotId, RobotContext robot, TransferRequest request)
    {
        Logger.Instance.Log($"[AutonomousRobotScheduler] Assigning {robotId} for transfer: wafer {request.WaferId} from {request.From} to {request.To}");

        // Update robot state
        robot.State = "busy";
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
        Logger.Instance.Log($"[AutonomousRobotScheduler] Sent PICKUP to {robotId}");

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

        Logger.Instance.Log("[AutonomousRobotScheduler] Disposed");
    }

    #endregion

    #region Context Classes

    private class RobotContext
    {
        public string RobotId { get; set; } = "";
        public Akka.Actor.IActorRef RobotActor { get; set; } = null!;
        public string State { get; set; } = "idle";
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
