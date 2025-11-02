using Akka.Actor;
using CMPSimXS2.Console.Models;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Synchronized Pipeline Scheduler with BATCH TRANSFER execution.
///
/// Strategy:
/// - Collect pending transfers and group by robot (R1, R2, R3)
/// - Wait for synchronization point: ALL robots must be idle
/// - Execute transfers in PARALLEL (R1, R2, R3 all transfer simultaneously)
/// - Maximizes throughput by eliminating sequential bottleneck
///
/// Expected Performance:
/// - 3× throughput improvement over sequential schedulers
/// - Sequential: ~23 wafers/sec (Lock-based, Single Publication)
/// - Synchronized: ~69 wafers/sec (theoretical maximum)
///
/// Pipeline Stages:
/// - R1: Carrier → Polisher (feed pipeline)
/// - R2: Polisher → Cleaner (middle stage)
/// - R3: Cleaner → Buffer (output stage)
/// - R1: Buffer → Carrier (unload pipeline)
///
/// Architecture:
/// - Publication-based state visibility ✅
/// - Batch transfer coordination ✅
/// - Synchronization point detection ✅
/// - Parallel execution ✅
/// </summary>
public class SynchronizedPipelineScheduler : IRobotScheduler, IDisposable
{
    private readonly ActorSystem _actorSystem;
    private readonly IActorRef _schedulerActor;
    private readonly Dictionary<string, IActorRef> _robotStatePublishers = new();
    private readonly Dictionary<string, IActorRef> _stationStatePublishers = new();
    private readonly string _namePrefix;

    public SynchronizedPipelineScheduler(ActorSystem actorSystem, string? namePrefix = null)
    {
        _actorSystem = actorSystem;
        _namePrefix = namePrefix ?? $"sync-pipeline-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        // Create scheduler actor with batch coordination
        _schedulerActor = _actorSystem.ActorOf(
            Props.Create(() => new SyncSchedulerActor()),
            $"{_namePrefix}-scheduler"
        );

        Logger.Instance.Log($"[SynchronizedPipelineScheduler] 🔄 Initialized with BATCH TRANSFER coordination");
    }

    #region IRobotScheduler Implementation

    public void RegisterRobot(string robotId, IActorRef robotActor)
    {
        // Create state publisher for this robot
        var publisherName = $"{_namePrefix}-robot-pub-{robotId.ToLower().Replace(" ", "-")}";
        var robotPublisher = _actorSystem.ActorOf(
            Props.Create(() => new StatePublisherActor(robotId, "Robot", "idle", null)),
            publisherName
        );
        _robotStatePublishers[robotId] = robotPublisher;

        // Register robot with scheduler
        _schedulerActor.Tell(new RegisterRobotMessage(robotId, robotActor, robotPublisher));

        Logger.Instance.Log($"[SynchronizedPipelineScheduler] 🔄 Registered {robotId}");
    }

    public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
    {
        if (_robotStatePublishers.TryGetValue(robotId, out var publisher))
        {
            var metadata = new Dictionary<string, object>();
            if (waitingFor != null)
            {
                metadata["waitingFor"] = waitingFor;
            }

            publisher.Tell(new StatePublisherActor.PublishStateMessage(state, heldWaferId, metadata));
        }
    }

    public void RequestTransfer(TransferRequest request)
    {
        _schedulerActor.Tell(request);
    }

    public int GetQueueSize()
    {
        try
        {
            return _schedulerActor.Ask<int>(new GetQueueSizeMessage(), TimeSpan.FromMilliseconds(100)).Result;
        }
        catch
        {
            return 0;
        }
    }

    public string GetRobotState(string robotId)
    {
        try
        {
            return _schedulerActor.Ask<string>(new GetRobotStateMessage(robotId), TimeSpan.FromMilliseconds(100)).Result;
        }
        catch
        {
            return "unknown";
        }
    }

    #endregion

    #region Station State Publishing

    public void RegisterStation(string stationName, string initialState = "idle", int? wafer = null)
    {
        if (!_stationStatePublishers.ContainsKey(stationName))
        {
            var publisherName = $"{_namePrefix}-station-pub-{stationName.ToLower()}";
            var publisher = _actorSystem.ActorOf(
                Props.Create(() => new StatePublisherActor(stationName, "Station", initialState, wafer)),
                publisherName
            );
            _stationStatePublishers[stationName] = publisher;

            // Register station with scheduler
            _schedulerActor.Tell(new RegisterStationMessage(stationName, publisher));

            Logger.Instance.Log($"[SynchronizedPipelineScheduler] 🔄 Registered station: {stationName}");
        }
    }

    public void UpdateStationState(string stationName, string state, int? waferId = null)
    {
        if (_stationStatePublishers.TryGetValue(stationName, out var publisher))
        {
            publisher.Tell(new StatePublisherActor.PublishStateMessage(state, waferId, null));
        }
    }

    #endregion

    #region Messages

    internal record RegisterRobotMessage(string RobotId, IActorRef RobotActor, IActorRef StatePublisher);
    internal record RegisterStationMessage(string StationName, IActorRef StatePublisher);
    internal record GetQueueSizeMessage();
    internal record GetRobotStateMessage(string RobotId);

    #endregion

    #region Synchronized Scheduler Actor

    /// <summary>
    /// Scheduler actor that coordinates BATCH TRANSFERS.
    /// Waits for all robots to be idle, then executes transfers in parallel.
    /// </summary>
    private class SyncSchedulerActor : ReceiveActor
    {
        private readonly Queue<TransferRequest> _pendingRequests = new();
        private readonly Dictionary<string, RobotContext> _robots = new();
        private readonly Dictionary<string, StationContext> _stations = new();
        private readonly Dictionary<string, TransferRequest> _activeTransfers = new();

        private int _batchesExecuted = 0;
        private int _totalTransfersExecuted = 0;

        public SyncSchedulerActor()
        {
            // Register robot
            Receive<RegisterRobotMessage>(msg =>
            {
                _robots[msg.RobotId] = new RobotContext
                {
                    RobotId = msg.RobotId,
                    RobotActor = msg.RobotActor,
                    State = "idle"
                };

                // Subscribe to robot state changes
                msg.StatePublisher.Tell(new StatePublisherActor.SubscribeMessage(Self));

                Logger.Instance.Log($"[SyncScheduler] Registered robot: {msg.RobotId}");
            });

            // Register station
            Receive<RegisterStationMessage>(msg =>
            {
                _stations[msg.StationName] = new StationContext
                {
                    StationName = msg.StationName,
                    State = "idle"
                };

                // Subscribe to station state changes
                msg.StatePublisher.Tell(new StatePublisherActor.SubscribeMessage(Self));

                Logger.Instance.Log($"[SyncScheduler] Registered station: {msg.StationName}");
            });

            // Handle state change publications
            Receive<StateChangeEvent>(evt =>
            {
                if (evt.EntityType == "Robot")
                {
                    if (_robots.TryGetValue(evt.EntityId, out var robot))
                    {
                        var previousState = robot.State;
                        robot.State = evt.NewState;
                        robot.HeldWaferId = evt.WaferId;

                        // React to robot becoming idle
                        if (evt.NewState == "idle" && previousState != "idle")
                        {
                            // Complete active transfer and invoke callback
                            if (_activeTransfers.TryGetValue(evt.EntityId, out var completedTransfer))
                            {
                                _activeTransfers.Remove(evt.EntityId);
                                Logger.Instance.Log($"[SyncScheduler] {evt.EntityId} completed transfer of wafer {completedTransfer.WaferId}, invoking callback");
                                completedTransfer.OnCompleted?.Invoke(completedTransfer.WaferId);
                            }

                            // Check if we can execute a batch transfer
                            TryExecuteBatchTransfer();
                        }
                    }
                }
                else if (evt.EntityType == "Station")
                {
                    if (_stations.TryGetValue(evt.EntityId, out var station))
                    {
                        station.State = evt.NewState;
                        station.WaferId = evt.WaferId;

                        // React to station becoming ready
                        if (evt.NewState == "done" || evt.NewState == "occupied")
                        {
                            TryExecuteBatchTransfer();
                        }
                    }
                }
            });

            // Handle transfer request
            Receive<TransferRequest>(request =>
            {
                try
                {
                    request.Validate();
                    _pendingRequests.Enqueue(request);
                    TryExecuteBatchTransfer();
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"[SyncScheduler:ERROR] Invalid request: {ex.Message}");
                }
            });

            // Query handlers
            Receive<GetQueueSizeMessage>(_ => Sender.Tell(_pendingRequests.Count));

            Receive<GetRobotStateMessage>(msg =>
            {
                var state = _robots.TryGetValue(msg.RobotId, out var robot)
                    ? robot.State
                    : "unknown";
                Sender.Tell(state);
            });

            Logger.Instance.Log("[SyncScheduler] ✅ Started with BATCH TRANSFER coordination");
        }

        private void TryExecuteBatchTransfer()
        {
            if (_pendingRequests.Count == 0)
                return;

            // Check if ALL robots are idle (synchronization point)
            var allRobotsIdle = _robots.Values.All(r => r.State == "idle");

            if (!allRobotsIdle)
            {
                // Not synchronized yet - wait for all robots to be idle
                return;
            }

            // Collect executable transfers for EACH robot
            var batchTransfers = new Dictionary<string, TransferRequest>();
            var tempQueue = new Queue<TransferRequest>();

            while (_pendingRequests.Count > 0)
            {
                var request = _pendingRequests.Dequeue();

                // Find which robot can handle this route
                var robotId = GetRobotForRoute(request.From, request.To);

                if (robotId != null && !batchTransfers.ContainsKey(robotId))
                {
                    // Check if this transfer is executable (stations ready)
                    if (IsTransferExecutable(request))
                    {
                        batchTransfers[robotId] = request;
                    }
                    else
                    {
                        tempQueue.Enqueue(request); // Not ready yet
                    }
                }
                else
                {
                    tempQueue.Enqueue(request); // Robot already assigned or invalid route
                }

                // Stop if we have transfers for all 3 robots (full batch)
                if (batchTransfers.Count == 3)
                    break;
            }

            // Put back requests that couldn't execute
            while (tempQueue.Count > 0)
            {
                _pendingRequests.Enqueue(tempQueue.Dequeue());
            }

            // Execute batch if we have any transfers
            if (batchTransfers.Count > 0)
            {
                _batchesExecuted++;
                _totalTransfersExecuted += batchTransfers.Count;

                Logger.Instance.Log($"[SyncScheduler] 🔄 Batch #{_batchesExecuted}: Executing {batchTransfers.Count} transfers in PARALLEL");

                foreach (var (robotId, request) in batchTransfers)
                {
                    var robot = _robots[robotId];
                    ExecuteTransfer(request, robot);
                    Logger.Instance.Log($"[SyncScheduler]    → {robotId}: W{request.WaferId} ({request.From} → {request.To})");
                }

                Logger.Instance.Log($"[SyncScheduler] 📊 Total: {_batchesExecuted} batches, {_totalTransfersExecuted} transfers");
            }
        }

        private bool IsTransferExecutable(TransferRequest request)
        {
            // Check source station state
            var fromState = _stations.GetValueOrDefault(request.From)?.State ?? "unknown";

            // For pickup from processing stations, they must be "done" or "idle" (after unload)
            if (request.From == "Polisher" || request.From == "Cleaner")
            {
                if (fromState != "done" && fromState != "idle")
                    return false;
            }
            // For pickup from Buffer, it must be "occupied"
            else if (request.From == "Buffer")
            {
                if (fromState != "occupied")
                    return false;
            }

            // Check destination station state
            var toState = _stations.GetValueOrDefault(request.To)?.State ?? "unknown";

            // Destination must be idle (available)
            if (request.To != "Carrier" && toState != "idle")
                return false;

            return true;
        }

        private void ExecuteTransfer(TransferRequest request, RobotContext robot)
        {
            robot.State = "busy";
            robot.HeldWaferId = request.WaferId;

            // Track active transfer (callback will be invoked when robot becomes idle)
            _activeTransfers[robot.RobotId] = request;

            // Send PICKUP command to robot
            var pickupData = new Dictionary<string, object>
            {
                ["waferId"] = request.WaferId,
                ["wafer"] = request.WaferId,
                ["from"] = request.From,
                ["to"] = request.To
            };

            robot.RobotActor.Tell(new XStateNet2.Core.Messages.SendEvent("PICKUP", pickupData));
        }

        private string? GetRobotForRoute(string from, string to)
        {
            // Robot 1: Carrier → Polisher (feed pipeline)
            if (from == "Carrier" && to == "Polisher")
                return "Robot 1";

            // Robot 2: Polisher → Cleaner (middle stage)
            if (from == "Polisher" && to == "Cleaner")
                return "Robot 2";

            // Robot 3: Cleaner → Buffer (output stage)
            if (from == "Cleaner" && to == "Buffer")
                return "Robot 3";

            // Robot 1: Buffer → Carrier (unload pipeline)
            if (from == "Buffer" && to == "Carrier")
                return "Robot 1";

            // Robot 1: Polisher → Carrier (error recovery)
            if (from == "Polisher" && to == "Carrier")
                return "Robot 1";

            return null;
        }

        private class RobotContext
        {
            public string RobotId { get; set; } = "";
            public IActorRef RobotActor { get; set; } = null!;
            public string State { get; set; } = "idle";
            public int? HeldWaferId { get; set; }
        }

        private class StationContext
        {
            public string StationName { get; set; } = "";
            public string State { get; set; } = "idle";
            public int? WaferId { get; set; }
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Logger.Instance.Log("[SynchronizedPipelineScheduler] 🔄 Disposed");
    }

    #endregion
}
