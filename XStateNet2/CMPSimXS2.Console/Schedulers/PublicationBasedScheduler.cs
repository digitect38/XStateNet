using Akka.Actor;
using CMPSimXS2.Console.Models;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Publication-based robot scheduler orchestrator.
///
/// Architecture:
/// - Creates dedicated scheduler for each robot
/// - Creates state publishers for robots and stations
/// - Robots and stations publish state changes
/// - Dedicated schedulers subscribe to relevant publications
/// - Pure event-driven coordination
///
/// Benefits:
/// - Decentralized decision making
/// - Each robot has autonomous scheduler
/// - Reactive to state changes
/// - No polling overhead
/// - Clear separation of concerns
/// </summary>
public class PublicationBasedScheduler : IRobotScheduler, IDisposable
{
    private readonly ActorSystem _actorSystem;
    private readonly Dictionary<string, IActorRef> _dedicatedSchedulers = new();
    private readonly Dictionary<string, IActorRef> _robotStatePublishers = new();
    private readonly Dictionary<string, IActorRef> _stationStatePublishers = new();
    private readonly string _namePrefix;

    public PublicationBasedScheduler(ActorSystem actorSystem, string? namePrefix = null)
    {
        _actorSystem = actorSystem;
        _namePrefix = namePrefix ?? $"pubsched-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        Logger.Instance.Log($"[PublicationBasedScheduler] 📡 Initialized publication-based scheduler (prefix: {_namePrefix})");
    }

    #region IRobotScheduler Implementation

    public void RegisterRobot(string robotId, IActorRef robotActor)
    {
        // Create state publisher for this robot (initialized with "idle" state)
        var publisherName = $"{_namePrefix}-robot-pub-{robotId.ToLower().Replace(" ", "-")}";
        var robotPublisher = _actorSystem.ActorOf(
            Props.Create(() => new StatePublisherActor(robotId, "Robot", "idle", null)),
            publisherName
        );
        _robotStatePublishers[robotId] = robotPublisher;

        Logger.Instance.Log($"[PublicationBasedScheduler] 📡 Created state publisher for {robotId}");

        // Create dedicated scheduler for this robot
        // We'll initialize it with station publishers after stations are registered
        var schedulerName = $"{_namePrefix}-sched-{robotId.ToLower().Replace(" ", "-")}";

        // Determine which stations this robot needs to monitor
        var relevantStations = GetRelevantStations(robotId);
        var stationPublishers = new Dictionary<string, IActorRef>();

        foreach (var stationName in relevantStations)
        {
            if (_stationStatePublishers.TryGetValue(stationName, out var publisher))
            {
                stationPublishers[stationName] = publisher;
            }
            else
            {
                // Create station publisher if not exists (initialized with "idle" state)
                var stationPubName = $"{_namePrefix}-station-pub-{stationName.ToLower()}";
                var stationPublisher = _actorSystem.ActorOf(
                    Props.Create(() => new StatePublisherActor(stationName, "Station", "idle", null)),
                    stationPubName
                );
                _stationStatePublishers[stationName] = stationPublisher;
                stationPublishers[stationName] = stationPublisher;

                Logger.Instance.Log($"[PublicationBasedScheduler] 📡 Created state publisher for station {stationName}");
            }
        }

        var dedicatedScheduler = _actorSystem.ActorOf(
            Props.Create(() => new DedicatedRobotScheduler(
                robotId,
                robotActor,
                robotPublisher,
                stationPublishers
            )),
            schedulerName
        );

        _dedicatedSchedulers[robotId] = dedicatedScheduler;

        Logger.Instance.Log($"[PublicationBasedScheduler] ✅ Registered {robotId} with dedicated scheduler (monitoring {stationPublishers.Count} stations)");
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
            Logger.Instance.Log($"[PublicationBasedScheduler] 📡 Published robot state: {robotId} → {state} (wafer: {heldWaferId})");
        }
    }

    public void RequestTransfer(TransferRequest request)
    {
        try
        {
            request.Validate();

            // Determine which robot should handle this
            var targetRobotId = DetermineRobot(request.From, request.To, request.PreferredRobotId);

            if (targetRobotId != null && _dedicatedSchedulers.TryGetValue(targetRobotId, out var scheduler))
            {
                scheduler.Tell(request);
                Logger.Instance.Log($"[PublicationBasedScheduler] 📨 Routed request to {targetRobotId}: wafer {request.WaferId} {request.From}→{request.To}");
            }
            else
            {
                Logger.Instance.Log($"[PublicationBasedScheduler:ERROR] No robot available for route {request.From}→{request.To}");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[PublicationBasedScheduler:ERROR] Invalid transfer request: {ex.Message}");
        }
    }

    public int GetQueueSize()
    {
        // Aggregate queue sizes from all dedicated schedulers
        int totalQueue = 0;
        foreach (var scheduler in _dedicatedSchedulers.Values)
        {
            try
            {
                var size = scheduler.Ask<int>(new DedicatedRobotScheduler.GetQueueSizeQuery(), TimeSpan.FromMilliseconds(100)).Result;
                totalQueue += size;
            }
            catch
            {
                // Ignore timeout
            }
        }
        return totalQueue;
    }

    public string GetRobotState(string robotId)
    {
        if (_dedicatedSchedulers.TryGetValue(robotId, out var scheduler))
        {
            try
            {
                return scheduler.Ask<string>(new DedicatedRobotScheduler.GetRobotStateQuery(), TimeSpan.FromMilliseconds(100)).Result;
            }
            catch
            {
                return "unknown";
            }
        }
        return "unknown";
    }

    #endregion

    #region Station State Publishing

    /// <summary>
    /// Register a station and create its state publisher
    /// </summary>
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

            Logger.Instance.Log($"[PublicationBasedScheduler] 📡 Registered station publisher: {stationName}");

            // Publish initial state
            UpdateStationState(stationName, initialState, wafer);
        }
    }

    /// <summary>
    /// Publish station state change
    /// </summary>
    public void UpdateStationState(string stationName, string state, int? waferId = null)
    {
        if (_stationStatePublishers.TryGetValue(stationName, out var publisher))
        {
            publisher.Tell(new StatePublisherActor.PublishStateMessage(state, waferId, null));
            Logger.Instance.Log($"[PublicationBasedScheduler] 📡 Published station state: {stationName} → {state} (wafer: {waferId})");
        }
    }

    #endregion

    #region Helper Methods

    private string? DetermineRobot(string from, string to, string? preferredRobotId)
    {
        if (!string.IsNullOrEmpty(preferredRobotId))
        {
            return preferredRobotId;
        }

        // Route assignment based on path
        return (from, to) switch
        {
            ("Carrier", "Polisher") => "Robot 1",
            ("Polisher", "Cleaner") => "Robot 2",
            ("Cleaner", "Buffer") => "Robot 3",
            ("Buffer", "Carrier") => "Robot 1",
            ("Polisher", "Carrier") => "Robot 1",
            _ => null
        };
    }

    private HashSet<string> GetRelevantStations(string robotId)
    {
        // Determine which stations each robot needs to monitor
        return robotId switch
        {
            "Robot 1" => new HashSet<string> { "Carrier", "Polisher", "Buffer" },
            "Robot 2" => new HashSet<string> { "Polisher", "Cleaner" },
            "Robot 3" => new HashSet<string> { "Cleaner", "Buffer" },
            _ => new HashSet<string>()
        };
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Logger.Instance.Log("[PublicationBasedScheduler] 📡 Shutting down publication-based scheduler");
    }

    #endregion
}
