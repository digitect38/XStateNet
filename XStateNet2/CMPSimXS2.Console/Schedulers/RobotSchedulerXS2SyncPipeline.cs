using Akka.Actor;
using CMPSimXS2.Console.Models;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// XS2-Sync-Pipeline Robot Scheduler using XStateNet2
///
/// Strategy:
/// - Uses XStateNet2 state machine for synchronized batch execution
/// - Collects pending transfers until all robots are idle
/// - Executes batch transfers in parallel
///
/// NOTE: This is a simplified XStateNet2 version demonstrating
/// batch coordination with state machines. For full implementation,
/// see SynchronizedPipelineScheduler.cs
/// </summary>
public class RobotSchedulerXS2SyncPipeline : IRobotScheduler
{
    private readonly IActorRef _machine;
    private readonly SchedulerContext _context;

    // XState machine JSON for synchronized batch execution
    private const string MachineJson = @"{
        ""id"": ""syncPipeline"",
        ""initial"": ""collecting"",
        ""states"": {
            ""collecting"": {
                ""on"": {
                    ""REGISTER_ROBOT"": { ""actions"": [""registerRobot""] },
                    ""REQUEST_TRANSFER"": { ""actions"": [""queueTransfer"", ""checkSync""] },
                    ""UPDATE_ROBOT_STATE"": { ""actions"": [""updateRobotState"", ""checkSync""] },
                    ""ALL_ROBOTS_IDLE"": { ""target"": ""executing"" }
                }
            },
            ""executing"": {
                ""entry"": [""executeBatchTransfers""],
                ""on"": {
                    ""BATCH_COMPLETE"": { ""target"": ""collecting"" },
                    ""REQUEST_TRANSFER"": { ""actions"": [""queueTransfer""] },
                    ""UPDATE_ROBOT_STATE"": { ""actions"": [""updateRobotState"", ""checkBatchComplete""] }
                }
            }
        }
    }";

    public RobotSchedulerXS2SyncPipeline(ActorSystem actorSystem, string? actorName = null)
    {
        _context = new SchedulerContext();

        var factory = new XStateMachineFactory(actorSystem);

        _machine = factory.FromJson(MachineJson)
            .WithAction("registerRobot", (ctx, data) => RegisterRobotAction(data as Dictionary<string, object> ?? new()))
            .WithAction("updateRobotState", (ctx, data) => UpdateRobotStateAction(data as Dictionary<string, object> ?? new()))
            .WithAction("queueTransfer", (ctx, data) => QueueTransferAction(data as Dictionary<string, object> ?? new()))
            .WithAction("checkSync", (ctx, data) => CheckSyncAction())
            .WithAction("executeBatchTransfers", (ctx, data) => ExecuteBatchTransfersAction())
            .WithAction("checkBatchComplete", (ctx, data) => CheckBatchCompleteAction())
            .BuildAndStart(actorName);

        Logger.Instance.Log("[XS2-Sync-Pipeline] Initialized with XStateNet2 batch coordination");
    }

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

        _machine.Tell(new SendEvent("UPDATE_ROBOT_STATE", eventData));
    }

    public void RequestTransfer(TransferRequest request)
    {
        System.Console.WriteLine($"[XS2-Sync] RequestTransfer: W{request.WaferId} {request.From}→{request.To}");
        var eventData = new Dictionary<string, object>
        {
            ["request"] = request
        };
        _machine.Tell(new SendEvent("REQUEST_TRANSFER", eventData));
    }

    public int GetQueueSize() => _context.PendingRequests.Count;

    public string GetRobotState(string robotId)
    {
        return _context.RobotStates.TryGetValue(robotId, out var state) ? state.State : "unknown";
    }

    #region Action Implementations

    private void RegisterRobotAction(Dictionary<string, object> eventData)
    {
        if (eventData.TryGetValue("robotId", out var robotIdObj) &&
            eventData.TryGetValue("robotActor", out var robotActorObj))
        {
            var robotId = robotIdObj.ToString()!;
            var robotActor = (IActorRef)robotActorObj;

            _context.Robots[robotId] = robotActor;
            _context.RobotStates[robotId] = new RobotState
            {
                RobotId = robotId,
                State = "idle"
            };
        }
    }

    private void UpdateRobotStateAction(Dictionary<string, object> eventData)
    {
        if (eventData.TryGetValue("robotId", out var robotIdObj) &&
            eventData.TryGetValue("state", out var stateObj))
        {
            var robotId = robotIdObj.ToString()!;
            var state = stateObj.ToString()!;

            if (_context.RobotStates.TryGetValue(robotId, out var robot))
            {
                robot.State = state;
                if (eventData.TryGetValue("heldWaferId", out var waferId))
                {
                    robot.HeldWaferId = (int)waferId;
                }

                // Clear active transfer when robot becomes idle and invoke completion callback
                if (state == "idle" && _context.ActiveTransfers.TryGetValue(robotId, out var completedTransfer))
                {
                    _context.ActiveTransfers.Remove(robotId);

                    // Invoke the OnCompleted callback to notify wafer journey
                    if (completedTransfer.OnCompleted != null)
                    {
                        System.Console.WriteLine($"[XS2-Sync] {robotId} completed transfer of W{completedTransfer.WaferId}, invoking callback");
                        completedTransfer.OnCompleted(completedTransfer.WaferId);
                    }
                }
            }
        }
    }

    private void QueueTransferAction(Dictionary<string, object> eventData)
    {
        System.Console.WriteLine($"[XS2-Sync] QueueTransferAction called");
        if (eventData.TryGetValue("request", out var requestObj))
        {
            var request = (TransferRequest)requestObj;
            request.Validate();

            // Clear source station occupancy when transfer is requested
            // (This means the station has finished processing and is ready to release the wafer)
            if (request.From != "Carrier" && _context.OccupiedStations.Contains(request.From))
            {
                _context.OccupiedStations.Remove(request.From);
                System.Console.WriteLine($"[XS2-Sync] Cleared {request.From} (wafer ready to leave)");
            }

            _context.PendingRequests.Enqueue(request);
            System.Console.WriteLine($"[XS2-Sync] Queued: W{request.WaferId}, Queue size now: {_context.PendingRequests.Count}");
        }
    }

    private void CheckSyncAction()
    {
        // Check if all robots are idle and we have pending work
        var allIdle = _context.RobotStates.Values.All(r => r.State == "idle");
        var hasPending = _context.PendingRequests.Count > 0;

        System.Console.WriteLine($"[XS2-Sync] CheckSync: AllIdle={allIdle}, Pending={_context.PendingRequests.Count}, RobotCount={_context.RobotStates.Count}");

        if (hasPending && allIdle)
        {
            System.Console.WriteLine($"[XS2-Sync] All robots idle with {_context.PendingRequests.Count} pending → Sending ALL_ROBOTS_IDLE");
            _machine.Tell(new SendEvent("ALL_ROBOTS_IDLE"));
        }
    }

    private void ExecuteBatchTransfersAction()
    {
        // Execute batch transfers for each robot
        var processed = 0;

        System.Console.WriteLine($"[XS2-Sync] ExecuteBatchTransfers: Pending={_context.PendingRequests.Count}, Active={_context.ActiveTransfers.Count}");

        while (_context.PendingRequests.Count > 0 && processed < 3)
        {
            var request = _context.PendingRequests.Peek();
            var robotId = FindAvailableRobot(request);

            if (robotId != null)
            {
                _context.PendingRequests.Dequeue();
                System.Console.WriteLine($"[XS2-Sync] Executing transfer: {request.WaferId} from {request.From} to {request.To} via {robotId}");
                ExecuteTransfer(request, robotId);
                processed++;
            }
            else
            {
                break;
            }
        }

        System.Console.WriteLine($"[XS2-Sync] Batch execution complete: Processed={processed}, Remaining={_context.PendingRequests.Count}");

        // If no transfers were executed (all robots busy or no suitable work),
        // immediately return to collecting state
        if (processed == 0)
        {
            System.Console.WriteLine($"[XS2-Sync] No transfers executed → BATCH_COMPLETE");
            _machine.Tell(new SendEvent("BATCH_COMPLETE"));
        }
        // Otherwise, CheckBatchCompleteAction will signal when transfers finish
    }

    private string? FindAvailableRobot(TransferRequest request)
    {
        // Check if destination station is occupied
        if (_context.OccupiedStations.Contains(request.To))
        {
            System.Console.WriteLine($"[XS2-Sync] Cannot execute: {request.To} is occupied");
            return null;
        }

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

        // Mark destination as occupied (unless it's Carrier, which is the exit point)
        if (request.To != "Carrier")
        {
            _context.OccupiedStations.Add(request.To);
            System.Console.WriteLine($"[XS2-Sync] Marked {request.To} as occupied");
        }

        var pickupData = new Dictionary<string, object>
        {
            ["waferId"] = request.WaferId,
            ["from"] = request.From,
            ["to"] = request.To
        };

        _context.Robots[robotId].Tell(new XStateNet2.Core.Messages.SendEvent("PICKUP", pickupData));
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

    private void CheckBatchCompleteAction()
    {
        // Check if all active transfers from the batch are complete
        System.Console.WriteLine($"[XS2-Sync] CheckBatchComplete: Active={_context.ActiveTransfers.Count}, Pending={_context.PendingRequests.Count}");

        if (_context.ActiveTransfers.Count == 0)
        {
            System.Console.WriteLine($"[XS2-Sync] All active transfers complete → BATCH_COMPLETE");
            _machine.Tell(new SendEvent("BATCH_COMPLETE"));
        }
    }

    #endregion

    private class SchedulerContext
    {
        public Dictionary<string, IActorRef> Robots { get; } = new();
        public Dictionary<string, RobotState> RobotStates { get; } = new();
        public Queue<TransferRequest> PendingRequests { get; } = new();
        public Dictionary<string, TransferRequest> ActiveTransfers { get; } = new();
        public HashSet<string> OccupiedStations { get; } = new(); // Track which stations have wafers
    }

    private class RobotState
    {
        public required string RobotId { get; set; }
        public string State { get; set; } = "idle";
        public int? HeldWaferId { get; set; }
    }
}
