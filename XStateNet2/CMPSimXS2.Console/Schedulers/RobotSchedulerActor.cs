using Akka.Actor;
using CMPSimXS2.Console.Models;
using XStateNet2.Core.Messages;
using LoggerHelper;
using static CMPSimXS2.Console.Schedulers.RobotSchedulerMessages;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Robot Scheduler Actor - Lock-free actor-based robot coordination
/// Handles robot selection, transfer queue, and execution
/// NO LOCKS - Actor mailbox provides serialization
/// </summary>
public class RobotSchedulerActor : ReceiveActor
{
    private readonly Dictionary<string, IActorRef> _robots = new();
    private readonly Dictionary<string, RobotState> _robotStates = new();
    private readonly Queue<TransferRequest> _pendingRequests = new();
    private readonly Dictionary<string, TransferRequest> _activeTransfers = new();

    private class RobotState
    {
        public string State { get; set; } = "idle";
        public int? HeldWaferId { get; set; }
        public string? WaitingFor { get; set; }
    }

    public RobotSchedulerActor()
    {
        Logger.Instance.Log("[RobotSchedulerActor] Initialized (Actor-based, NO LOCKS)");

        Receive<RegisterRobot>(msg => HandleRegisterRobot(msg));
        Receive<UpdateRobotState>(msg => HandleUpdateRobotState(msg));
        Receive<RequestTransfer>(msg => HandleRequestTransfer(msg));
        Receive<GetQueueSize>(msg => HandleGetQueueSize());
        Receive<GetRobotState>(msg => HandleGetRobotState(msg));
    }

    private void HandleRegisterRobot(RegisterRobot msg)
    {
        _robots[msg.RobotId] = msg.RobotActor;
        _robotStates[msg.RobotId] = new RobotState();
        Logger.Instance.Log($"[RobotSchedulerActor] Registered robot: {msg.RobotId}");
    }

    private void HandleUpdateRobotState(UpdateRobotState msg)
    {
        if (!_robotStates.ContainsKey(msg.RobotId))
            return;

        // ENFORCE RULE: Idle robot cannot hold a wafer
        var heldWaferId = msg.HeldWaferId;
        if (msg.State == "idle" && heldWaferId.HasValue)
        {
            Logger.Instance.Log($"[RobotSchedulerActor:WARNING] {msg.RobotId} cannot be idle while holding wafer {heldWaferId}! Clearing wafer.");
            heldWaferId = null;
        }

        var wasIdle = _robotStates[msg.RobotId].State == "idle";
        _robotStates[msg.RobotId].State = msg.State;
        _robotStates[msg.RobotId].HeldWaferId = heldWaferId;
        _robotStates[msg.RobotId].WaitingFor = msg.WaitingFor;

        Logger.Instance.Log($"[RobotSchedulerActor:DEBUG] Robot {msg.RobotId} state updated: {msg.State} (wafer={heldWaferId ?? 0})");

        // When robot becomes idle, complete active transfer and process pending
        if (msg.State == "idle" && !wasIdle)
        {
            if (_activeTransfers.ContainsKey(msg.RobotId))
            {
                var completedTransfer = _activeTransfers[msg.RobotId];
                _activeTransfers.Remove(msg.RobotId);

                if (completedTransfer.OnCompleted != null)
                {
                    Logger.Instance.Log($"[RobotSchedulerActor] {msg.RobotId} completed transfer of wafer {completedTransfer.WaferId}, invoking callback");
                    completedTransfer.OnCompleted(completedTransfer.WaferId);
                }
            }

            ProcessPendingRequests();
        }
    }

    private void HandleRequestTransfer(RequestTransfer msg)
    {
        try
        {
            msg.Request.Validate();
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[RobotSchedulerActor:ERROR] Invalid transfer request: {ex.Message}");
            return;
        }

        Logger.Instance.Log($"[RobotSchedulerActor] Transfer requested: {msg.Request}");

        var assignedRobot = TryAssignTransfer(msg.Request);
        if (assignedRobot == null)
        {
            _pendingRequests.Enqueue(msg.Request);
            Logger.Instance.Log($"[RobotSchedulerActor] No robots available, queued: {msg.Request} (Queue size: {_pendingRequests.Count})");
        }
    }

    private void HandleGetQueueSize()
    {
        Sender.Tell(new QueueSize(_pendingRequests.Count));
    }

    private void HandleGetRobotState(GetRobotState msg)
    {
        var state = _robotStates.ContainsKey(msg.RobotId)
            ? _robotStates[msg.RobotId].State
            : "unknown";
        Sender.Tell(new RobotStateResponse(state));
    }

    private string? TryAssignTransfer(TransferRequest request)
    {
        if (!string.IsNullOrEmpty(request.PreferredRobotId))
        {
            if (IsRobotAvailable(request.PreferredRobotId))
            {
                ExecuteTransfer(request.PreferredRobotId, request);
                return request.PreferredRobotId;
            }
        }

        var selectedRobot = SelectNearestRobot(request.From, request.To);
        if (selectedRobot != null && IsRobotAvailable(selectedRobot))
        {
            ExecuteTransfer(selectedRobot, request);
            return selectedRobot;
        }

        var availableRobot = SelectFirstAvailable();
        if (availableRobot != null)
        {
            ExecuteTransfer(availableRobot, request);
            return availableRobot;
        }

        return null;
    }

    private string? SelectNearestRobot(string from, string to)
    {
        if ((from == "Carrier" && to == "Polisher") || (from == "Buffer" && to == "Carrier"))
            return IsRobotAvailable("Robot 1") ? "Robot 1" : null;

        if ((from == "Polisher" && to == "Cleaner") || (from == "Cleaner" && to == "Polisher"))
            return IsRobotAvailable("Robot 2") ? "Robot 2" : null;

        if ((from == "Cleaner" && to == "Buffer") || (from == "Buffer" && to == "Cleaner"))
            return IsRobotAvailable("Robot 3") ? "Robot 3" : null;

        return null;
    }

    private string? SelectFirstAvailable()
    {
        foreach (var kvp in _robotStates)
        {
            if (kvp.Value.State == "idle")
                return kvp.Key;
        }
        return null;
    }

    private bool IsRobotAvailable(string robotId)
    {
        if (!_robotStates.ContainsKey(robotId))
            return false;

        var robotState = _robotStates[robotId];

        if (robotState.State != "idle")
            return false;

        if (robotState.HeldWaferId.HasValue)
        {
            Logger.Instance.Log($"[RobotSchedulerActor:WARNING] {robotId} is idle but still holding wafer {robotState.HeldWaferId}! Clearing...");
            robotState.HeldWaferId = null;
        }

        return true;
    }

    private void ExecuteTransfer(string robotId, TransferRequest request)
    {
        if (!_robots.ContainsKey(robotId))
        {
            Logger.Instance.Log($"[RobotSchedulerActor:ERROR] Robot {robotId} not found");
            return;
        }

        Logger.Instance.Log($"[RobotSchedulerActor] Assigning {robotId} for transfer: {request}");

        _robotStates[robotId].State = "busy";
        _robotStates[robotId].HeldWaferId = request.WaferId;
        _activeTransfers[robotId] = request;

        var pickupData = new Dictionary<string, object>
        {
            ["waferId"] = request.WaferId,
            ["wafer"] = request.WaferId,
            ["from"] = request.From,
            ["to"] = request.To
        };

        _robots[robotId].Tell(new SendEvent("PICKUP", pickupData));

        Logger.Instance.Log($"[RobotSchedulerActor] Sent PICKUP to {robotId}: wafer {request.WaferId} from {request.From} to {request.To}");
    }

    private void ProcessPendingRequests()
    {
        while (_pendingRequests.Count > 0)
        {
            var request = _pendingRequests.Peek();
            var assignedRobot = TryAssignTransfer(request);

            if (assignedRobot != null)
            {
                _pendingRequests.Dequeue();
                Logger.Instance.Log($"[RobotSchedulerActor] Processed pending request: {request} assigned to {assignedRobot}");
            }
            else
            {
                break;
            }
        }
    }
}
