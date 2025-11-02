using Akka.Actor;
using CMPSimXS2.Console.Models;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Ant Colony pattern for decentralized robot scheduling with Pub/Sub.
///
/// Inspired by ant colony behavior (pheromone trail detection):
/// - 🐜 No central dispatcher (no queen telling ants what to do)
/// - 🐜 Each robot autonomously discovers and claims work
/// - 🐜 Work pool broadcasts "work available" notifications (like pheromones!)
/// - 🐜 Robots make local decisions based on their capabilities
/// - 🐜 Atomic claim mechanism prevents conflicts
/// - ⚡ Pure event-driven - NO POLLING!
///
/// Architecture:
/// - WorkPoolActor: Holds pending requests, broadcasts notifications
/// - RobotWorkerActor: Subscribes to notifications, races to claim work
/// - Pub/Sub pattern: zero polling overhead, instant notifications
/// - No central coordination - pure decentralized autonomy
/// </summary>
public class AntColonyScheduler : IRobotScheduler, IDisposable
{
    #region Byte-indexed State Constants (for fast comparisons)

    private const byte STATE_IDLE = 0;
    private const byte STATE_BUSY = 1;
    private const byte STATE_CARRYING = 2;

    private const byte ROUTE_CARRIER_POLISHER = 0;
    private const byte ROUTE_POLISHER_CLEANER = 1;
    private const byte ROUTE_CLEANER_BUFFER = 2;
    private const byte ROUTE_BUFFER_CARRIER = 3;
    private const byte ROUTE_POLISHER_CARRIER = 4;

    #endregion

    #region Fields

    private readonly ActorSystem _actorSystem;
    private readonly IActorRef _workPoolActor;

    #endregion

    public AntColonyScheduler(ActorSystem actorSystem, string? namePrefix = null)
    {
        _actorSystem = actorSystem;

        var poolName = namePrefix != null
            ? $"{namePrefix}-workpool"
            : $"workpool-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        // Create work pool actor (shared resource - like food sources in ant colony)
        _workPoolActor = _actorSystem.ActorOf(
            Props.Create(() => new WorkPoolActor()),
            poolName
        );

        Logger.Instance.Log($"[AntColonyScheduler] 🐜 Initialized ANT COLONY pattern (work pool: {poolName})");
    }

    #region IRobotScheduler Implementation

    public void RegisterRobot(string robotId, IActorRef robotActor)
    {
        // Create autonomous robot worker actor (like an ant)
        var workerName = $"ant-{robotId.ToLower().Replace(" ", "-")}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        var workerActor = _actorSystem.ActorOf(
            Props.Create(() => new RobotWorkerActor(robotId, robotActor, _workPoolActor)),
            workerName
        );

        // Subscribe robot to work pool (pub/sub pattern - no polling!)
        _workPoolActor.Tell(new SubscribeRobotMessage(robotId, workerActor));

        Logger.Instance.Log($"[AntColonyScheduler] 🐜 Registered autonomous robot ant: {robotId} (worker: {workerName}, pub/sub mode)");
    }

    public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
    {
        var stateByte = ConvertStateToByte(state);
        _workPoolActor.Tell(new UpdateRobotStateMessage(robotId, stateByte, heldWaferId, waitingFor));
        Logger.Instance.Log($"[AntColonyScheduler] 🐜 Robot state update: {robotId} → byte {stateByte}");
    }

    public void RequestTransfer(TransferRequest request)
    {
        try
        {
            request.Validate();
            // Add work to the pool (like discovering a new food source)
            _workPoolActor.Tell(new AddWorkMessage(request));
            Logger.Instance.Log($"[AntColonyScheduler] 🐜 Work added to pool: {request.WaferId} {request.From}→{request.To}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[AntColonyScheduler:ERROR] Invalid transfer request: {ex.Message}");
        }
    }

    public int GetQueueSize()
    {
        var result = _workPoolActor.Ask<int>(new GetQueueSizeMessage(), TimeSpan.FromSeconds(1)).Result;
        return result;
    }

    public string GetRobotState(string robotId)
    {
        var stateByte = _workPoolActor.Ask<byte>(new GetRobotStateMessage(robotId), TimeSpan.FromSeconds(1)).Result;
        return ConvertByteToState(stateByte);
    }

    #endregion

    #region Station Registration

    public void RegisterStation(string stationName, string initialState = "idle", int? wafer = null)
    {
        _workPoolActor.Tell(new RegisterStationMessage(stationName, initialState, wafer));
        Logger.Instance.Log($"[AntColonyScheduler] 🐜 Registered station: {stationName}");
    }

    public void UpdateStationState(string stationName, string state, int? waferId = null)
    {
        _workPoolActor.Tell(new UpdateStationStateMessage(stationName, state, waferId));
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
        Logger.Instance.Log("[AntColonyScheduler] 🐜 Ant colony disposed");
    }

    #endregion

    #region Messages

    internal record AddWorkMessage(TransferRequest Request);
    internal record ClaimWorkMessage(string RobotId);
    internal record SubscribeRobotMessage(string RobotId, IActorRef WorkerActor);
    internal record WorkAvailableNotification(byte RouteByte); // Pub/Sub with route info
    internal record UpdateRobotStateMessage(string RobotId, byte StateByte, int? HeldWaferId, string? WaitingFor);
    internal record RegisterStationMessage(string StationName, string InitialState, int? Wafer);
    internal record UpdateStationStateMessage(string StationName, string State, int? WaferId);
    internal record GetQueueSizeMessage();
    internal record GetRobotStateMessage(string RobotId);

    #endregion

    #region WorkPoolActor (Shared Resource)

    /// <summary>
    /// Work pool actor - holds available work (like food sources in ant colony)
    /// Uses pub/sub pattern: broadcasts "work available" to subscribed robots
    /// No polling needed - pure event-driven!
    /// </summary>
    private class WorkPoolActor : ReceiveActor
    {
        private readonly Queue<TransferRequest> _pendingWork = new();
        private readonly Dictionary<string, RobotContext> _robots = new();
        private readonly Dictionary<string, StationContext> _stations = new();
        private readonly Dictionary<string, IActorRef> _subscribedWorkers = new(); // RobotId -> WorkerActor

        public WorkPoolActor()
        {
            // Subscribe robot worker (pub/sub pattern)
            Receive<SubscribeRobotMessage>(msg =>
            {
                _subscribedWorkers[msg.RobotId] = msg.WorkerActor;
                Logger.Instance.Log($"[WorkPool] 🐜 Robot {msg.RobotId} subscribed to work notifications (Total subscribers: {_subscribedWorkers.Count})");
            });

            // Add new work to the pool
            Receive<AddWorkMessage>(msg =>
            {
                _pendingWork.Enqueue(msg.Request);
                Logger.Instance.Log($"[WorkPool] 🐜 New work available: {msg.Request.WaferId} {msg.Request.From}→{msg.Request.To} (Pool size: {_pendingWork.Count})");

                // PUB/SUB: Calculate route byte and notify only capable robots!
                byte routeByte = GetRouteByte(msg.Request.From, msg.Request.To);
                var notification = new WorkAvailableNotification(routeByte);
                int notifiedCount = 0;

                foreach (var kvp in _subscribedWorkers)
                {
                    var robotId = kvp.Key;
                    var worker = kvp.Value;

                    // Only notify if robot can handle this route
                    if (CanRobotHandleRoute(robotId, routeByte))
                    {
                        worker.Tell(notification);
                        notifiedCount++;
                    }
                }
                Logger.Instance.Log($"[WorkPool] 📢 Broadcasted work notification (route {routeByte}) to {notifiedCount}/{_subscribedWorkers.Count} capable robots (selective pub/sub)");
            });

            // Robot attempts to claim work (atomic operation)
            Receive<ClaimWorkMessage>(msg =>
            {
                if (!_robots.TryGetValue(msg.RobotId, out var robot))
                {
                    Sender.Tell(new ClaimResponse(null, "Robot not registered"));
                    return;
                }

                // Only idle robots can claim work
                if (robot.StateByte != STATE_IDLE)
                {
                    Sender.Tell(new ClaimResponse(null, $"Robot busy (state: {robot.StateByte})"));
                    return;
                }

                // Find first claimable work for this robot
                TransferRequest? claimedWork = null;
                var tempQueue = new Queue<TransferRequest>();

                while (_pendingWork.Count > 0)
                {
                    var work = _pendingWork.Dequeue();

                    if (CanRobotHandleTransfer(msg.RobotId, work))
                    {
                        claimedWork = work;
                        break; // Found work!
                    }
                    else
                    {
                        tempQueue.Enqueue(work); // Not for this robot, put back
                    }
                }

                // Put back unclaimed work
                while (tempQueue.Count > 0)
                {
                    _pendingWork.Enqueue(tempQueue.Dequeue());
                }

                if (claimedWork != null)
                {
                    // Mark robot as busy
                    robot.StateByte = STATE_BUSY;
                    robot.HeldWaferId = claimedWork.WaferId;

                    Logger.Instance.Log($"[WorkPool] 🐜 {msg.RobotId} CLAIMED work: wafer {claimedWork.WaferId} {claimedWork.From}→{claimedWork.To}");
                    Sender.Tell(new ClaimResponse(claimedWork, null));
                }
                else
                {
                    Sender.Tell(new ClaimResponse(null, "No suitable work available"));
                }
            });

            // Update robot state
            Receive<UpdateRobotStateMessage>(msg =>
            {
                if (!_robots.ContainsKey(msg.RobotId))
                {
                    // Register robot on first state update
                    _robots[msg.RobotId] = new RobotContext
                    {
                        RobotId = msg.RobotId,
                        StateByte = msg.StateByte,
                        HeldWaferId = msg.HeldWaferId
                    };
                }
                else
                {
                    var robot = _robots[msg.RobotId];
                    robot.StateByte = msg.StateByte;
                    robot.HeldWaferId = msg.HeldWaferId;
                    robot.WaitingFor = msg.WaitingFor;
                }

                // PUB/SUB: If robot becomes idle AND work is pending, broadcast notification!
                if (msg.StateByte == STATE_IDLE && _pendingWork.Count > 0)
                {
                    Logger.Instance.Log($"[WorkPool] 🐜 {msg.RobotId} became idle, {_pendingWork.Count} work pending");

                    // Notify only robots that can handle pending work
                    int notifiedCount = 0;
                    foreach (var work in _pendingWork)
                    {
                        byte routeByte = GetRouteByte(work.From, work.To);
                        var notification = new WorkAvailableNotification(routeByte);

                        foreach (var kvp in _subscribedWorkers)
                        {
                            var robotId = kvp.Key;
                            var worker = kvp.Value;

                            if (CanRobotHandleRoute(robotId, routeByte))
                            {
                                worker.Tell(notification);
                                notifiedCount++;
                            }
                        }
                    }
                    Logger.Instance.Log($"[WorkPool] 📢 Broadcasted notification (robot idle event) to {notifiedCount} capable robots (selective pub/sub)");
                }
            });

            // Register station
            Receive<RegisterStationMessage>(msg =>
            {
                _stations[msg.StationName] = new StationContext
                {
                    StationName = msg.StationName,
                    State = msg.InitialState,
                    WaferId = msg.Wafer
                };
            });

            // Update station state
            Receive<UpdateStationStateMessage>(msg =>
            {
                if (_stations.TryGetValue(msg.StationName, out var station))
                {
                    station.State = msg.State;
                    station.WaferId = msg.WaferId;
                }
            });

            // Query: Get queue size
            Receive<GetQueueSizeMessage>(_ =>
            {
                Sender.Tell(_pendingWork.Count);
            });

            // Query: Get robot state
            Receive<GetRobotStateMessage>(msg =>
            {
                if (_robots.TryGetValue(msg.RobotId, out var robot))
                {
                    Sender.Tell(robot.StateByte);
                }
                else
                {
                    Sender.Tell((byte)255); // Unknown
                }
            });
        }

        /// <summary>
        /// Check if robot can handle this transfer (byte-indexed route matching)
        /// </summary>
        private bool CanRobotHandleTransfer(string robotId, TransferRequest request)
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

        /// <summary>
        /// Check if a specific robot can handle a given route
        /// (Selective notification - only notify capable robots!)
        /// </summary>
        private bool CanRobotHandleRoute(string robotId, byte routeByte)
        {
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

        private class RobotContext
        {
            public string RobotId { get; set; } = "";
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

    internal record ClaimResponse(TransferRequest? Work, string? ErrorMessage);

    #endregion

    #region RobotWorkerActor (Autonomous Ant)

    /// <summary>
    /// Autonomous robot worker - behaves like an ant
    /// - Pure event-driven: responds to "work available" notifications
    /// - Makes local decisions about work to claim
    /// - No central coordination, no polling!
    /// - Pub/Sub pattern: receives broadcasts from WorkPoolActor
    /// </summary>
    private class RobotWorkerActor : ReceiveActor
    {
        private readonly string _robotId;
        private readonly IActorRef _robotActor;
        private readonly IActorRef _workPoolActor;

        public RobotWorkerActor(string robotId, IActorRef robotActor, IActorRef workPoolActor)
        {
            _robotId = robotId;
            _robotActor = robotActor;
            _workPoolActor = workPoolActor;

            // PUB/SUB: Receive "work available" notification (like detecting pheromone trail!)
            Receive<WorkAvailableNotification>(_ =>
            {
                // Try to claim work immediately (race with other robots)
                _workPoolActor.Ask<ClaimResponse>(new ClaimWorkMessage(_robotId), TimeSpan.FromMilliseconds(100))
                    .ContinueWith(task =>
                    {
                        if (task.IsCompletedSuccessfully && task.Result.Work != null)
                        {
                            return (object)new WorkClaimedMessage(task.Result.Work);
                        }
                        return (object)new NoWorkAvailableMessage();
                    })
                    .PipeTo(Self);
            });

            // Successfully claimed work!
            Receive<WorkClaimedMessage>(msg =>
            {
                Logger.Instance.Log($"[RobotWorker] 🐜 {_robotId} claimed work: wafer {msg.Work.WaferId} {msg.Work.From}→{msg.Work.To} (event-driven, no polling!)");

                // Send PICKUP event to robot actor
                var pickupData = new Dictionary<string, object>
                {
                    ["waferId"] = msg.Work.WaferId,
                    ["wafer"] = msg.Work.WaferId,
                    ["from"] = msg.Work.From,
                    ["to"] = msg.Work.To
                };

                _robotActor.Tell(new XStateNet2.Core.Messages.SendEvent("PICKUP", pickupData), ActorRefs.NoSender);

                // Invoke completion callback
                msg.Work.OnCompleted?.Invoke(msg.Work.WaferId);
            });

            // No work available - wait for next notification (silent)
            Receive<NoWorkAvailableMessage>(_ =>
            {
                // Silent - another robot got the work, wait for next notification
            });

            Logger.Instance.Log($"[RobotWorker] 🐜 {_robotId} started (pure event-driven, pub/sub mode - NO POLLING!)");
        }

        private record WorkClaimedMessage(TransferRequest Work);
        private record NoWorkAvailableMessage();
    }

    #endregion
}
