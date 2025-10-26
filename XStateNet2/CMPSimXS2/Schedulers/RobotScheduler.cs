using Akka.Actor;
using CMPSimXS2.Helpers;
using CMPSimXS2.Models;
using XStateNet2.Core.Messages;

namespace CMPSimXS2.Schedulers;

/// <summary>
/// Robot Scheduler - Manages robot allocation and transfer coordination
/// Handles robot selection, transfer queue, and execution
/// </summary>
public class RobotScheduler
{
    private readonly Dictionary<string, IActorRef> _robots = new();
    private readonly Dictionary<string, RobotState> _robotStates = new();
    private readonly Queue<TransferRequest> _pendingRequests = new();
    private readonly Dictionary<string, TransferRequest> _activeTransfers = new(); // Track active transfers by robotId
    private readonly object _lock = new();

    private class RobotState
    {
        public string State { get; set; } = "idle"; // idle, busy, carrying
        public int? HeldWaferId { get; set; }
        public string? WaitingFor { get; set; }
    }

    public RobotScheduler()
    {
        Logger.Instance.Info("RobotScheduler", "Initialized");
    }

    /// <summary>
    /// Register a robot state machine
    /// </summary>
    public void RegisterRobot(string robotId, IActorRef robotActor)
    {
        lock (_lock)
        {
            _robots[robotId] = robotActor;
            _robotStates[robotId] = new RobotState();
            Logger.Instance.Info("RobotScheduler", $"Registered robot: {robotId}");
        }
    }

    /// <summary>
    /// Update robot state (called when robot state changes)
    /// RULE: Robot must place wafer (become idle with no wafer) before picking up another
    /// </summary>
    public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
    {
        lock (_lock)
        {
            if (!_robotStates.ContainsKey(robotId))
                return;

            // ENFORCE RULE: Idle robot cannot hold a wafer
            if (state == "idle" && heldWaferId.HasValue)
            {
                Logger.Instance.Warning("RobotScheduler",
                    $"{robotId} cannot be idle while holding wafer {heldWaferId}! Clearing wafer.");
                heldWaferId = null;
            }

            _robotStates[robotId].State = state;
            _robotStates[robotId].HeldWaferId = heldWaferId;
            _robotStates[robotId].WaitingFor = waitingFor;

            Logger.Instance.Debug("RobotScheduler", $"Robot {robotId} state updated: {state} (wafer={heldWaferId ?? 0})");

            // When robot becomes idle, complete the active transfer and invoke callback
            if (state == "idle" && _activeTransfers.ContainsKey(robotId))
            {
                var completedTransfer = _activeTransfers[robotId];
                _activeTransfers.Remove(robotId);

                // Invoke the OnCompleted callback
                if (completedTransfer.OnCompleted != null)
                {
                    Logger.Instance.Info("RobotScheduler", $"{robotId} completed transfer of wafer {completedTransfer.WaferId}, invoking callback");
                    completedTransfer.OnCompleted(completedTransfer.WaferId);
                }

                // Process pending requests when robot becomes idle (and has placed its wafer)
                ProcessPendingRequests();
            }
        }
    }

    /// <summary>
    /// Request a transfer - validates and either assigns or queues
    /// </summary>
    public void RequestTransfer(TransferRequest request)
    {
        lock (_lock)
        {
            // Validate request
            try
            {
                request.Validate();
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("RobotScheduler", $"Invalid transfer request: {ex.Message}");
                return;
            }

            Logger.Instance.Info("RobotScheduler", $"Transfer requested: {request}");

            // Try to assign immediately
            var assignedRobot = TryAssignTransfer(request);
            if (assignedRobot == null)
            {
                // No robots available, queue the request
                _pendingRequests.Enqueue(request);
                Logger.Instance.Info("RobotScheduler", $"No robots available, queued: {request} (Queue size: {_pendingRequests.Count})");
            }
        }
    }

    /// <summary>
    /// Try to assign a transfer to an available robot
    /// </summary>
    private string? TryAssignTransfer(TransferRequest request)
    {
        // Check if preferred robot is specified and available
        if (!string.IsNullOrEmpty(request.PreferredRobotId))
        {
            if (IsRobotAvailable(request.PreferredRobotId))
            {
                ExecuteTransfer(request.PreferredRobotId, request);
                return request.PreferredRobotId;
            }
        }

        // Select best robot using strategy
        var selectedRobot = SelectNearestRobot(request.From, request.To);
        if (selectedRobot != null && IsRobotAvailable(selectedRobot))
        {
            ExecuteTransfer(selectedRobot, request);
            return selectedRobot;
        }

        // Fallback: select first available robot
        var availableRobot = SelectFirstAvailable();
        if (availableRobot != null)
        {
            ExecuteTransfer(availableRobot, request);
            return availableRobot;
        }

        return null;
    }

    /// <summary>
    /// Select nearest robot based on transfer route (heuristic)
    /// </summary>
    private string? SelectNearestRobot(string from, string to)
    {
        // R1: Carrier ↔ Polisher, Buffer ↔ Carrier
        if ((from == "Carrier" && to == "Polisher") || (from == "Buffer" && to == "Carrier"))
            return IsRobotAvailable("Robot 1") ? "Robot 1" : null;

        // R2: Polisher ↔ Cleaner
        if ((from == "Polisher" && to == "Cleaner") || (from == "Cleaner" && to == "Polisher"))
            return IsRobotAvailable("Robot 2") ? "Robot 2" : null;

        // R3: Cleaner ↔ Buffer
        if ((from == "Cleaner" && to == "Buffer") || (from == "Buffer" && to == "Cleaner"))
            return IsRobotAvailable("Robot 3") ? "Robot 3" : null;

        return null;
    }

    /// <summary>
    /// Select first available robot (fallback strategy)
    /// </summary>
    private string? SelectFirstAvailable()
    {
        foreach (var kvp in _robotStates)
        {
            if (kvp.Value.State == "idle")
            {
                return kvp.Key;
            }
        }
        return null;
    }

    /// <summary>
    /// Check if a robot is available for transfer
    /// IMPORTANT: Robot must be idle (not holding any wafer) before picking up another
    /// </summary>
    private bool IsRobotAvailable(string robotId)
    {
        if (!_robotStates.ContainsKey(robotId))
            return false;

        var robotState = _robotStates[robotId];

        // Robot must be idle AND not holding any wafer
        if (robotState.State != "idle")
            return false;

        // Safety check: Idle robot should not be holding a wafer
        if (robotState.HeldWaferId.HasValue)
        {
            Logger.Instance.Warning("RobotScheduler",
                $"{robotId} is idle but still holding wafer {robotState.HeldWaferId}! Clearing...");
            robotState.HeldWaferId = null;
        }

        return true;
    }

    /// <summary>
    /// Execute transfer by sending PICKUP event to robot
    /// </summary>
    private void ExecuteTransfer(string robotId, TransferRequest request)
    {
        if (!_robots.ContainsKey(robotId))
        {
            Logger.Instance.Error("RobotScheduler", $"Robot {robotId} not found");
            return;
        }

        Logger.Instance.Info("RobotScheduler", $"Assigning {robotId} for transfer: {request}");

        // Update robot state to busy
        _robotStates[robotId].State = "busy";
        _robotStates[robotId].HeldWaferId = request.WaferId;

        // Store active transfer for completion callback
        _activeTransfers[robotId] = request;

        // Send PICKUP event to robot
        var pickupData = new Dictionary<string, object>
        {
            ["waferId"] = request.WaferId,
            ["wafer"] = request.WaferId,
            ["from"] = request.From,
            ["to"] = request.To
        };

        _robots[robotId].Tell(new SendEvent("PICKUP", pickupData));

        Logger.Instance.Info("RobotScheduler", $"Sent PICKUP to {robotId}: wafer {request.WaferId} from {request.From} to {request.To}");
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
                Logger.Instance.Info("RobotScheduler", $"Processed pending request: {request} assigned to {assignedRobot}");
            }
            else
            {
                // No robots available yet, stop processing
                break;
            }
        }
    }

    /// <summary>
    /// Get current queue size
    /// </summary>
    public int GetQueueSize()
    {
        lock (_lock)
        {
            return _pendingRequests.Count;
        }
    }

    /// <summary>
    /// Get robot state for debugging
    /// </summary>
    public string GetRobotState(string robotId)
    {
        lock (_lock)
        {
            if (!_robotStates.ContainsKey(robotId))
                return "unknown";

            return _robotStates[robotId].State;
        }
    }
}
