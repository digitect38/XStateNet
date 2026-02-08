using Akka.Actor;
using CMPSimXS2.Console.Models;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Runtime;
using LoggerHelper;
using static CMPSimXS2.Console.Schedulers.RobotSchedulerStateMachine;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// XState-based Robot Scheduler - FrozenDictionary Optimized
/// Uses FrozenDictionary for 10-15% faster lookups (default XState behavior)
/// </summary>
public class RobotSchedulerXState : IRobotScheduler
{
    private readonly IActorRef _machine;
    private readonly SchedulerContext _context;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(5);

    public RobotSchedulerXState(ActorSystem actorSystem, string? actorName = null)
    {
        _context = new SchedulerContext();

        // Create XState machine from JSON definition
        var factory = new XStateMachineFactory(actorSystem);

        _machine = factory.FromJson(MachineJson)
            // Register guards
            .WithGuard("hasNoPendingWork", (ctx, _) => _context.PendingRequests.Count == 0)
            .WithGuard("hasPendingWork", (ctx, _) => _context.PendingRequests.Count > 0)
            // Register actions
            .WithAction("registerRobot", (ctx, data) => RegisterRobotAction(data as Dictionary<string, object> ?? new()))
            .WithAction("updateRobotState", (ctx, data) => UpdateRobotStateAction(data as Dictionary<string, object> ?? new()))
            .WithAction("queueOrAssignTransfer", (ctx, data) => QueueOrAssignTransferAction(data as Dictionary<string, object> ?? new()))
            .WithAction("processTransfers", (ctx, data) => ProcessTransfersAction())
            .BuildAndStart(actorName);

        Logger.Instance.Log("[RobotSchedulerXState] Initialized with XState machine");
    }

    #region IRobotScheduler Implementation

    public void RegisterRobot(string robotId, IActorRef robotActor)
    {
        var eventData = new Dictionary<string, object>
        {
            ["robotId"] = robotId,
            ["robotActor"] = robotActor
        };
        _machine.Tell(new SendEvent("REGISTER_ROBOT", eventData));
    }

    public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
    {
        var eventData = new Dictionary<string, object>
        {
            ["robotId"] = robotId,
            ["state"] = state
        };
        if (heldWaferId.HasValue)
            eventData["heldWaferId"] = heldWaferId.Value;
        if (waitingFor != null)
            eventData["waitingFor"] = waitingFor;

        _machine.Tell(new SendEvent("UPDATE_ROBOT_STATE", eventData));
    }

    public void RequestTransfer(TransferRequest request)
    {
        var eventData = new Dictionary<string, object>
        {
            ["request"] = request
        };
        _machine.Tell(new SendEvent("REQUEST_TRANSFER", eventData));
    }

    public int GetQueueSize()
    {
        return _context.PendingRequests.Count;
    }

    public string GetRobotState(string robotId)
    {
        if (!_context.RobotStates.ContainsKey(robotId))
            return "unknown";

        return _context.RobotStates[robotId].State;
    }

    #endregion

    #region Action Implementations

    private void RegisterRobotAction(Dictionary<string, object> eventData)
    {
        if (eventData.TryGetValue("robotId", out var robotIdObj) &&
            eventData.TryGetValue("robotActor", out var robotActorObj))
        {
            var robotId = robotIdObj.ToString()!;
            var robotActor = (IActorRef)robotActorObj;

            _context.Robots[robotId] = robotActor;
            _context.RobotStates[robotId] = new RobotState();
            Logger.Instance.Log($"[RobotSchedulerXState] Registered robot: {robotId}");
        }
    }

    private void UpdateRobotStateAction(Dictionary<string, object> eventData)
    {
        if (!eventData.TryGetValue("robotId", out var robotIdObj))
            return;

        var robotId = robotIdObj.ToString()!;
        if (!_context.RobotStates.ContainsKey(robotId))
            return;

        var state = eventData.GetValueOrDefault("state")?.ToString() ?? "idle";
        var heldWaferId = eventData.GetValueOrDefault("heldWaferId") as int?;
        var waitingFor = eventData.GetValueOrDefault("waitingFor")?.ToString();

        // Enforce rule: idle robot cannot hold wafer
        if (state == "idle" && heldWaferId.HasValue)
        {
            Logger.Instance.Log($"[RobotSchedulerXState:WARNING] {robotId} cannot be idle while holding wafer {heldWaferId}! Clearing wafer.");
            heldWaferId = null;
        }

        var wasIdle = _context.RobotStates[robotId].State == "idle";
        _context.RobotStates[robotId].State = state;
        _context.RobotStates[robotId].HeldWaferId = heldWaferId;
        _context.RobotStates[robotId].WaitingFor = waitingFor;

        Logger.Instance.Log($"[RobotSchedulerXState:DEBUG] Robot {robotId} state updated: {state} (wafer={heldWaferId ?? 0})");

        // Complete active transfer if robot became idle
        if (state == "idle" && !wasIdle && _context.ActiveTransfers.ContainsKey(robotId))
        {
            var completedTransfer = _context.ActiveTransfers[robotId];
            _context.ActiveTransfers.Remove(robotId);

            completedTransfer.OnCompleted?.Invoke(completedTransfer.WaferId);
            Logger.Instance.Log($"[RobotSchedulerXState] {robotId} completed transfer of wafer {completedTransfer.WaferId}");
        }

        // Process pending transfers when a robot becomes idle
        if (state == "idle" && !wasIdle)
        {
            ProcessTransfersAction();
        }
    }

    private void QueueOrAssignTransferAction(Dictionary<string, object> eventData)
    {
        if (!eventData.TryGetValue("request", out var requestObj))
            return;

        var request = (TransferRequest)requestObj;

        try
        {
            request.Validate();
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[RobotSchedulerXState:ERROR] Invalid transfer request: {ex.Message}");
            return;
        }

        Logger.Instance.Log($"[RobotSchedulerXState] Transfer requested: {request}");

        // Try immediate assignment
        var assignedRobot = TryAssignTransfer(request);
        if (assignedRobot == null)
        {
            _context.PendingRequests.Enqueue(request);
            Logger.Instance.Log($"[RobotSchedulerXState] Queued: {request} (Queue size: {_context.PendingRequests.Count})");
        }
    }

    private void ProcessTransfersAction()
    {
        // Process pending requests if any
        while (_context.PendingRequests.Count > 0)
        {
            var request = _context.PendingRequests.Peek();
            var assignedRobot = TryAssignTransfer(request);

            if (assignedRobot != null)
            {
                _context.PendingRequests.Dequeue();
                Logger.Instance.Log($"[RobotSchedulerXState] Processed pending request: {request} assigned to {assignedRobot}");
            }
            else
            {
                break; // No robots available
            }
        }
    }

    #endregion

    #region Helper Methods

    private string? TryAssignTransfer(TransferRequest request)
    {
        // Check preferred robot
        if (!string.IsNullOrEmpty(request.PreferredRobotId))
        {
            if (IsRobotAvailable(request.PreferredRobotId))
            {
                ExecuteTransfer(request.PreferredRobotId, request);
                return request.PreferredRobotId;
            }
        }

        // Select nearest robot
        var selectedRobot = RobotSelectionStrategy.SelectNearestRobot(request.From, request.To, _context.RobotStates);
        if (selectedRobot != null && IsRobotAvailable(selectedRobot))
        {
            ExecuteTransfer(selectedRobot, request);
            return selectedRobot;
        }

        // Fallback: first available
        var availableRobot = RobotSelectionStrategy.SelectFirstAvailable(_context.RobotStates);
        if (availableRobot != null)
        {
            ExecuteTransfer(availableRobot, request);
            return availableRobot;
        }

        return null;
    }

    private bool IsRobotAvailable(string robotId)
    {
        if (!_context.RobotStates.ContainsKey(robotId))
            return false;

        var robotState = _context.RobotStates[robotId];
        if (robotState.State != "idle")
            return false;

        if (robotState.HeldWaferId.HasValue)
        {
            Logger.Instance.Log($"[RobotSchedulerXState:WARNING] {robotId} is idle but holding wafer {robotState.HeldWaferId}! Clearing...");
            robotState.HeldWaferId = null;
        }

        return true;
    }

    private void ExecuteTransfer(string robotId, TransferRequest request)
    {
        if (!_context.Robots.ContainsKey(robotId))
        {
            Logger.Instance.Log($"[RobotSchedulerXState:ERROR] Robot {robotId} not found");
            return;
        }

        Logger.Instance.Log($"[RobotSchedulerXState] Assigning {robotId} for transfer: {request}");

        // Update robot state
        _context.RobotStates[robotId].State = "busy";
        _context.RobotStates[robotId].HeldWaferId = request.WaferId;

        // Store active transfer
        _context.ActiveTransfers[robotId] = request;

        // Send PICKUP event
        var pickupData = new Dictionary<string, object>
        {
            ["waferId"] = request.WaferId,
            ["wafer"] = request.WaferId,
            ["from"] = request.From,
            ["to"] = request.To
        };

        _context.Robots[robotId].Tell(new SendEvent("PICKUP", pickupData));
        Logger.Instance.Log($"[RobotSchedulerXState] Sent PICKUP to {robotId}: wafer {request.WaferId} from {request.From} to {request.To}");
    }

    #endregion
}
