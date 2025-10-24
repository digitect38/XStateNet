using CMPSimulator.Models;
using CMPSimulator.StateMachines;
using XStateNet.Orchestration;
using Newtonsoft.Json.Linq;
using LoggerHelper;

namespace CMPSimulator.Controllers;

/// <summary>
/// Robot Scheduler - Manages robot allocation and transfer requests
/// Phase 1: Separates robot management from Master Scheduler
///
/// Responsibilities:
/// - Validate transfer requests (null safety!)
/// - Select best available robot
/// - Manage transfer queue
/// - Track robot states
/// - Prevent null toMachineId issues
/// </summary>
public class RobotScheduler
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly Dictionary<string, RobotMachine> _robots = new();
    private readonly Dictionary<string, RobotState> _robotStates = new();
    private readonly Queue<TransferRequest> _pendingRequests = new();

    // Robot selection strategy
    private readonly RobotSelectionStrategy _selectionStrategy;

    public event EventHandler<TransferCompletedEventArgs>? TransferCompleted;
    public event EventHandler<TransferRequestedEventArgs>? TransferRequested;

    public RobotScheduler(EventBusOrchestrator orchestrator, RobotSelectionStrategy strategy = RobotSelectionStrategy.NearestAvailable)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _selectionStrategy = strategy;
    }

    /// <summary>
    /// Register a robot with the scheduler
    /// </summary>
    public void RegisterRobot(string robotId, RobotMachine robot)
    {
        if (string.IsNullOrEmpty(robotId))
            throw new ArgumentException("Robot ID cannot be null or empty", nameof(robotId));

        if (robot == null)
            throw new ArgumentNullException(nameof(robot));

        _robots[robotId] = robot;
        _robotStates[robotId] = new RobotState
        {
            RobotId = robotId,
            State = "idle",
            HeldWafer = null,
            WaitingFor = null
        };

        Log($"[RobotScheduler] ✓ Registered robot: {robotId}");
    }

    /// <summary>
    /// Request a transfer - PRIMARY ENTRY POINT
    /// This method GUARANTEES null safety
    /// </summary>
    public void RequestTransfer(TransferRequest request)
    {
        try
        {
            // CRITICAL: Validate before anything else
            request.Validate();

            Log($"[RobotScheduler] 📥 Transfer request: {request}");

            TransferRequested?.Invoke(this, new TransferRequestedEventArgs { Request = request });

            // Try to assign immediately
            var assignedRobot = TryAssignTransfer(request);

            if (assignedRobot != null)
            {
                Log($"[RobotScheduler] ✓ Assigned to {assignedRobot}");
            }
            else
            {
                // Queue for later
                _pendingRequests.Enqueue(request);
                Log($"[RobotScheduler] ⏸ Queued (no robots available), queue depth: {_pendingRequests.Count}");
            }
        }
        catch (ArgumentException ex)
        {
            Log($"[RobotScheduler] ❌ INVALID TRANSFER REQUEST: {ex.Message}");
            Log($"[RobotScheduler] ❌ Request details: WaferId={request.WaferId}, From={request.From ?? "NULL"}, To={request.To ?? "NULL"}");
            throw; // Re-throw to alert caller
        }
    }

    /// <summary>
    /// Try to assign a transfer to an available robot
    /// Returns robot ID if successful, null otherwise
    /// </summary>
    private string? TryAssignTransfer(TransferRequest request)
    {
        // Find available robot
        var availableRobot = SelectRobot(request);

        if (availableRobot == null)
            return null;

        // Assign the transfer
        AssignTransfer(availableRobot, request);

        return availableRobot;
    }

    /// <summary>
    /// Select the best robot for this transfer
    /// </summary>
    private string? SelectRobot(TransferRequest request)
    {
        // Preferred robot if specified
        if (!string.IsNullOrEmpty(request.PreferredRobotId))
        {
            if (_robotStates.TryGetValue(request.PreferredRobotId, out var preferredState) &&
                preferredState.State == "idle")
            {
                return request.PreferredRobotId;
            }
        }

        // Strategy-based selection
        switch (_selectionStrategy)
        {
            case RobotSelectionStrategy.RoundRobin:
                return SelectRobotRoundRobin();

            case RobotSelectionStrategy.NearestAvailable:
                return SelectNearestRobot(request.From, request.To);

            case RobotSelectionStrategy.LeastBusy:
                return SelectLeastBusyRobot();

            default:
                return SelectFirstAvailable();
        }
    }

    private string? SelectFirstAvailable()
    {
        return _robotStates
            .Where(kvp => kvp.Value.State == "idle")
            .Select(kvp => kvp.Key)
            .FirstOrDefault();
    }

    private string? SelectRobotRoundRobin()
    {
        // Simple round-robin: R1 → R2 → R3 → R1...
        var robotOrder = new[] { "R1", "R2", "R3" };

        foreach (var robotId in robotOrder)
        {
            if (_robotStates.TryGetValue(robotId, out var state) && state.State == "idle")
                return robotId;
        }

        return null;
    }

    private string? SelectNearestRobot(string from, string to)
    {
        // Heuristic: R1 for LoadPort↔Polisher, R2 for Polisher↔Cleaner, R3 for Cleaner↔Buffer
        if ((from == "LoadPort" && to == "polisher") || (from == "polisher" && to == "LoadPort") ||
            (from == "buffer" && to == "LoadPort") || (from == "LoadPort" && to == "buffer"))
        {
            if (_robotStates.TryGetValue("R1", out var r1State) && r1State.State == "idle")
                return "R1";
        }

        if ((from == "polisher" && to == "cleaner") || (from == "cleaner" && to == "polisher"))
        {
            if (_robotStates.TryGetValue("R2", out var r2State) && r2State.State == "idle")
                return "R2";
        }

        if ((from == "cleaner" && to == "buffer") || (from == "buffer" && to == "cleaner"))
        {
            if (_robotStates.TryGetValue("R3", out var r3State) && r3State.State == "idle")
                return "R3";
        }

        // Fallback: any available
        return SelectFirstAvailable();
    }

    private string? SelectLeastBusyRobot()
    {
        // For now, same as first available
        // Future: track robot workload/history
        return SelectFirstAvailable();
    }

    /// <summary>
    /// Assign a transfer to a specific robot
    /// GUARANTEED: request has been validated, from/to are non-null
    /// </summary>
    private void AssignTransfer(string robotId, TransferRequest request)
    {
        if (!_robots.TryGetValue(robotId, out var robot))
        {
            Log($"[RobotScheduler] ❌ ERROR: Robot {robotId} not found!");
            return;
        }

        // Update robot state
        _robotStates[robotId].State = "busy";
        _robotStates[robotId].HeldWafer = request.WaferId;
        _robotStates[robotId].WaitingFor = request.To;

        // Send TRANSFER command with GUARANTEED non-null values
        // IMPORTANT: RobotScheduler is not a StateMachine, so we must execute deferred sends manually
        var context = _orchestrator.GetOrCreateContext("RobotScheduler");
        context.RequestSend(robotId, "TRANSFER", new JObject
        {
            ["waferId"] = request.WaferId,
            ["from"] = request.From,      // ← GUARANTEED non-null
            ["to"] = request.To            // ← GUARANTEED non-null
        });

        // CRITICAL: Execute deferred sends immediately since RobotScheduler has no StateChanged event
        // Use fire-and-forget to avoid blocking GUI thread
        _ = Task.Run(async () => await context.ExecuteDeferredSends());

        Log($"[RobotScheduler] 📤 TRANSFER command sent: {robotId} ← TRANSFER(wafer={request.WaferId}, from={request.From}, to={request.To})");
    }

    /// <summary>
    /// Update robot state based on status events
    /// Called by Master Scheduler when ROBOT_STATUS events arrive
    /// </summary>
    public void UpdateRobotStatus(string robotId, string state, int? wafer, string? waitingFor)
    {
        if (!_robotStates.ContainsKey(robotId))
        {
            Log($"[RobotScheduler] WARNING: Unknown robot {robotId}");
            return;
        }

        var robotState = _robotStates[robotId];
        var oldState = robotState.State;

        robotState.State = state;
        robotState.HeldWafer = wafer;
        robotState.WaitingFor = waitingFor;

        Log($"[RobotScheduler] 📊 Robot status: {robotId} = {state} (wafer: {wafer?.ToString() ?? "none"})");

        // If robot became idle, try to assign pending requests
        if (state == "idle" && oldState != "idle")
        {
            ProcessPendingRequests();
        }
    }

    /// <summary>
    /// Process pending transfer requests
    /// </summary>
    private void ProcessPendingRequests()
    {
        while (_pendingRequests.Count > 0)
        {
            var request = _pendingRequests.Peek();
            var assignedRobot = TryAssignTransfer(request);

            if (assignedRobot != null)
            {
                _pendingRequests.Dequeue();
                Log($"[RobotScheduler] ✓ Processed pending request: {request} → {assignedRobot}");
            }
            else
            {
                // No robots available, stop processing
                break;
            }
        }
    }

    /// <summary>
    /// Get current status summary
    /// </summary>
    public RobotSchedulerStatus GetStatus()
    {
        return new RobotSchedulerStatus
        {
            TotalRobots = _robots.Count,
            IdleRobots = _robotStates.Count(kvp => kvp.Value.State == "idle"),
            BusyRobots = _robotStates.Count(kvp => kvp.Value.State != "idle"),
            PendingRequests = _pendingRequests.Count,
            RobotStates = new Dictionary<string, RobotState>(_robotStates)
        };
    }

    /// <summary>
    /// Reset all robots (used during carrier swap)
    /// </summary>
    public void ResetAllRobots()
    {
        foreach (var robot in _robots.Values)
        {
            robot.ResetWafer();
        }

        foreach (var state in _robotStates.Values)
        {
            state.State = "idle";
            state.HeldWafer = null;
            state.WaitingFor = null;
        }

        _pendingRequests.Clear();

        Log($"[RobotScheduler] 🔄 All robots reset");
    }

    private void Log(string message)
    {
        Logger.Instance.Log(message);
    }
}

/// <summary>
/// Robot state tracking
/// </summary>
public class RobotState
{
    public string RobotId { get; set; } = string.Empty;
    public string State { get; set; } = "idle";
    public int? HeldWafer { get; set; }
    public string? WaitingFor { get; set; }
}

/// <summary>
/// Robot scheduler status snapshot
/// </summary>
public class RobotSchedulerStatus
{
    public int TotalRobots { get; set; }
    public int IdleRobots { get; set; }
    public int BusyRobots { get; set; }
    public int PendingRequests { get; set; }
    public Dictionary<string, RobotState> RobotStates { get; set; } = new();
}

/// <summary>
/// Robot selection strategies
/// </summary>
public enum RobotSelectionStrategy
{
    /// <summary>
    /// Select first available robot
    /// </summary>
    FirstAvailable,

    /// <summary>
    /// Round-robin selection
    /// </summary>
    RoundRobin,

    /// <summary>
    /// Select nearest robot based on station proximity
    /// </summary>
    NearestAvailable,

    /// <summary>
    /// Select least busy robot
    /// </summary>
    LeastBusy
}

/// <summary>
/// Event args for transfer completed
/// </summary>
public class TransferCompletedEventArgs : EventArgs
{
    public string RobotId { get; set; } = string.Empty;
    public int WaferId { get; set; }
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

/// <summary>
/// Event args for transfer requested
/// </summary>
public class TransferRequestedEventArgs : EventArgs
{
    public TransferRequest Request { get; set; } = new();
}
