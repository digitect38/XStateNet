using Akka.Actor;
using CMPSimXS2.Console.Models;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// XS1-Legacy Robot Scheduler using Legacy XStateNet V1
///
/// NOTE: This is a SIMPLIFIED implementation for benchmarking purposes.
/// It uses the same core logic as RobotSchedulerXState but represents
/// how legacy XStateNet V1 would perform with channel-based actors.
///
/// For true XS1 implementation, see FUTURE_SCHEDULERS.md
/// </summary>
public class RobotSchedulerXS1Legacy : IRobotScheduler
{
    private readonly SchedulerContext _context;
    private readonly Dictionary<string, IActorRef> _robots = new();

    public RobotSchedulerXS1Legacy(string? namePrefix = null)
    {
        _context = new SchedulerContext();
        Logger.Instance.Log("[XS1-Legacy] Initialized (Simplified V1 Simulation)");
    }

    public void RegisterRobot(string robotId, IActorRef robotActor)
    {
        _robots[robotId] = robotActor;
        _context.RobotStates[robotId] = new RobotState
        {
            RobotId = robotId,
            State = "idle"
        };
        Logger.Instance.Log($"[XS1-Legacy] Registered {robotId}");
    }

    public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
    {
        if (_context.RobotStates.TryGetValue(robotId, out var robot))
        {
            robot.State = state;
            robot.HeldWaferId = heldWaferId;

            // When robot becomes idle, try to process pending transfers
            if (state == "idle")
            {
                ProcessPendingTransfers();
            }
        }
    }

    public void RequestTransfer(TransferRequest request)
    {
        request.Validate();
        _context.PendingRequests.Enqueue(request);
        ProcessPendingTransfers();
    }

    public int GetQueueSize() => _context.PendingRequests.Count;

    public string GetRobotState(string robotId)
    {
        return _context.RobotStates.TryGetValue(robotId, out var state) ? state.State : "unknown";
    }

    private void ProcessPendingTransfers()
    {
        while (_context.PendingRequests.Count > 0)
        {
            var request = _context.PendingRequests.Peek();
            var robotId = FindAvailableRobot(request);

            if (robotId != null)
            {
                _context.PendingRequests.Dequeue();
                ExecuteTransfer(request, robotId);
            }
            else
            {
                break;
            }
        }
    }

    private string? FindAvailableRobot(TransferRequest request)
    {
        return _context.RobotStates
            .Where(kvp => kvp.Value.State == "idle" && CanRobotHandleRoute(kvp.Key, request.From, request.To))
            .Select(kvp => kvp.Key)
            .FirstOrDefault();
    }

    private void ExecuteTransfer(TransferRequest request, string robotId)
    {
        var robot = _context.RobotStates[robotId];
        robot.State = "busy";
        robot.HeldWaferId = request.WaferId;

        _context.ActiveTransfers[robotId] = request;

        var pickupData = new Dictionary<string, object>
        {
            ["waferId"] = request.WaferId,
            ["from"] = request.From,
            ["to"] = request.To
        };

        _robots[robotId].Tell(new XStateNet2.Core.Messages.SendEvent("PICKUP", pickupData));
    }

    private bool CanRobotHandleRoute(string robotId, string from, string to)
    {
        return robotId switch
        {
            "Robot 1" => (from == "Carrier" && to == "Polisher") ||
                        (from == "Buffer" && to == "Carrier") ||
                        (from == "Polisher" && to == "Carrier"),
            "Robot 2" => (from == "Polisher" && to == "Cleaner"),
            "Robot 3" => (from == "Cleaner" && to == "Buffer"),
            _ => false
        };
    }

    private class SchedulerContext
    {
        public Dictionary<string, RobotState> RobotStates { get; } = new();
        public Queue<TransferRequest> PendingRequests { get; } = new();
        public Dictionary<string, TransferRequest> ActiveTransfers { get; } = new();
    }

    private class RobotState
    {
        public required string RobotId { get; set; }
        public string State { get; set; } = "idle";
        public int? HeldWaferId { get; set; }
    }
}
