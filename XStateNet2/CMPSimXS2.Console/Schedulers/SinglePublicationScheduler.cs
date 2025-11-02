using Akka.Actor;
using CMPSimXS2.Console.Models;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Optimized publication-based scheduler with SINGLE scheduler actor.
///
/// Combines benefits of:
/// - Actor-based: Fast submission (no routing overhead)
/// - Publication-based: State visibility (pub/sub pattern)
///
/// Architecture:
/// - ONE scheduler actor (like Actor-based) ✅
/// - State publications (like Publication-based) ✅
/// - NO routing overhead ✅
/// - NO dedicated schedulers per robot ✅
///
/// Expected Performance:
/// - Sequential: ~3M req/sec (like Actor-based)
/// - Concurrent: ~2.5K req/sec (good scaling)
/// - Latency: ~0.003ms (minimal)
/// </summary>
public class SinglePublicationScheduler : IRobotScheduler, IDisposable
{
    private readonly ActorSystem _actorSystem;
    private readonly IActorRef _schedulerActor;
    private readonly Dictionary<string, IActorRef> _robotStatePublishers = new();
    private readonly Dictionary<string, IActorRef> _stationStatePublishers = new();
    private readonly string _namePrefix;

    public SinglePublicationScheduler(ActorSystem actorSystem, string? namePrefix = null)
    {
        _actorSystem = actorSystem;
        _namePrefix = namePrefix ?? $"single-pub-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        // Create SINGLE scheduler actor (no routing needed!)
        _schedulerActor = _actorSystem.ActorOf(
            Props.Create(() => new SingleSchedulerActor()),
            $"{_namePrefix}-scheduler"
        );

        Logger.Instance.Log($"[SinglePublicationScheduler] 📡 Initialized with SINGLE scheduler (no routing overhead)");
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

        // Register robot with single scheduler
        _schedulerActor.Tell(new RegisterRobotMessage(robotId, robotActor, robotPublisher));

        Logger.Instance.Log($"[SinglePublicationScheduler] 📡 Registered {robotId} (single scheduler mode)");
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
        // NO ROUTING! Just Tell() directly to single scheduler ✅
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

            // Register station with single scheduler
            _schedulerActor.Tell(new RegisterStationMessage(stationName, publisher));

            Logger.Instance.Log($"[SinglePublicationScheduler] 📡 Registered station: {stationName}");
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

    #region Single Scheduler Actor

    /// <summary>
    /// Single scheduler actor that handles ALL robots.
    /// Subscribes to state publications for visibility.
    /// No routing overhead - processes all requests directly.
    /// </summary>
    private class SingleSchedulerActor : ReceiveActor
    {
        private readonly Queue<TransferRequest> _pendingRequests = new();
        private readonly Dictionary<string, RobotContext> _robots = new();
        private readonly Dictionary<string, StationContext> _stations = new();
        private readonly Dictionary<string, TransferRequest> _activeTransfers = new();

        public SingleSchedulerActor()
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

                Logger.Instance.Log($"[SingleScheduler] Registered robot: {msg.RobotId}");
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

                Logger.Instance.Log($"[SingleScheduler] Registered station: {msg.StationName}");
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
                                Logger.Instance.Log($"[SingleScheduler] {evt.EntityId} completed transfer of wafer {completedTransfer.WaferId}, invoking callback");
                                completedTransfer.OnCompleted?.Invoke(completedTransfer.WaferId);
                            }

                            TryProcessNextRequest();
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
                            TryProcessNextRequest();
                        }
                    }
                }
            });

            // Handle transfer request (NO ROUTING!)
            Receive<TransferRequest>(request =>
            {
                try
                {
                    request.Validate();
                    _pendingRequests.Enqueue(request);
                    TryProcessNextRequest();
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"[SingleScheduler:ERROR] Invalid request: {ex.Message}");
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

            Logger.Instance.Log("[SingleScheduler] ✅ Started (handles all robots, no routing!)");
        }

        private void TryProcessNextRequest()
        {
            if (_pendingRequests.Count == 0)
                return;

            // Try to find a request we can execute now
            var tempQueue = new Queue<TransferRequest>();
            TransferRequest? executableRequest = null;

            while (_pendingRequests.Count > 0)
            {
                var request = _pendingRequests.Dequeue();

                // Find available robot that can handle this route
                var robot = FindAvailableRobot(request);

                if (robot != null)
                {
                    executableRequest = request;
                    ExecuteTransfer(request, robot);
                    break; // Found one!
                }
                else
                {
                    tempQueue.Enqueue(request); // Not ready yet, put back
                }
            }

            // Put back requests that couldn't execute
            while (tempQueue.Count > 0)
            {
                _pendingRequests.Enqueue(tempQueue.Dequeue());
            }
        }

        private RobotContext? FindAvailableRobot(TransferRequest request)
        {
            // Check source station state
            var fromState = _stations.GetValueOrDefault(request.From)?.State ?? "unknown";

            // For pickup from processing stations, they must be "done" or "idle" (after unload)
            if (request.From == "Polisher" || request.From == "Cleaner")
            {
                if (fromState != "done" && fromState != "idle")
                    return null;
            }
            // For pickup from Buffer, it must be "occupied"
            else if (request.From == "Buffer")
            {
                if (fromState != "occupied")
                    return null;
            }

            // Check destination station state
            var toState = _stations.GetValueOrDefault(request.To)?.State ?? "unknown";

            // Destination must be idle (available)
            if (request.To != "Carrier" && toState != "idle")
                return null;

            // Find idle robot that can handle this route
            return _robots.Values
                .Where(r => r.State == "idle" && CanRobotHandleRoute(r.RobotId, request.From, request.To))
                .FirstOrDefault();
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
        Logger.Instance.Log("[SinglePublicationScheduler] 📡 Disposed");
    }

    #endregion
}
