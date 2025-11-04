using Akka.Actor;
using CMPSimXS2.Console.Models;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// XState DSL-powered publication-based scheduler with SINGLE scheduler.
///
/// Combines benefits of:
/// - XStateNet2 JSON DSL: Declarative state machine ✅
/// - Single scheduler: No routing overhead ✅
/// - Publication-based: State visibility (pub/sub pattern) ✅
///
/// Architecture:
/// - ONE XState machine with JSON DSL definition
/// - State publications (pub/sub pattern for observability)
/// - NO routing work (eliminated 0.5ms overhead!)
/// - Declarative state transitions via JSON
///
/// XState Machine Definition (Standard JSON):
/// {
///   "id": "singlePubScheduler",
///   "initial": "idle",
///   "states": {
///     "idle": {
///       "on": {
///         "REGISTER_ROBOT": { "actions": ["registerRobot"] },
///         "REGISTER_STATION": { "actions": ["registerStation"] },
///         "STATE_CHANGE": { "actions": ["handleStateChange", "tryProcessPending"] },
///         "TRANSFER_REQUEST": { "target": "processing" }
///       }
///     },
///     "processing": {
///       "entry": ["processTransfer", "tryProcessPending"],
///       "always": [
///         { "target": "idle", "cond": "queueEmpty" }
///       ],
///       "on": {
///         "STATE_CHANGE": { "actions": ["handleStateChange", "tryProcessPending"] },
///         "TRANSFER_REQUEST": { "actions": ["processTransfer", "tryProcessPending"] }
///       }
///     }
///   }
/// }
/// </summary>
public class SinglePublicationSchedulerXState : IRobotScheduler, IDisposable
{
    private readonly ActorSystem _actorSystem;
    private readonly IActorRef _machine;
    private readonly SchedulerContext _context;
    private readonly Dictionary<string, IActorRef> _robotStatePublishers = new();
    private readonly Dictionary<string, IActorRef> _stationStatePublishers = new();
    private readonly string _namePrefix;

    // XState machine JSON DSL definition
    private const string MachineJson = @"{
        ""id"": ""singlePubScheduler"",
        ""initial"": ""idle"",
        ""states"": {
            ""idle"": {
                ""on"": {
                    ""REGISTER_ROBOT"": { ""actions"": [""registerRobot""] },
                    ""REGISTER_STATION"": { ""actions"": [""registerStation""] },
                    ""STATE_CHANGE"": { ""actions"": [""handleStateChange"", ""tryProcessPending""] },
                    ""TRANSFER_REQUEST"": { ""target"": ""processing"" }
                }
            },
            ""processing"": {
                ""entry"": [""processTransfer"", ""tryProcessPending""],
                ""always"": [
                    { ""target"": ""idle"", ""cond"": ""queueEmpty"" }
                ],
                ""on"": {
                    ""STATE_CHANGE"": { ""actions"": [""handleStateChange"", ""tryProcessPending""] },
                    ""TRANSFER_REQUEST"": { ""actions"": [""processTransfer"", ""tryProcessPending""] }
                }
            }
        }
    }";

    public SinglePublicationSchedulerXState(ActorSystem actorSystem, string? namePrefix = null)
    {
        _actorSystem = actorSystem;
        _namePrefix = namePrefix ?? $"xstate-singlepub-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        _context = new SchedulerContext();

        // Create XState machine from JSON DSL
        var factory = new XStateMachineFactory(actorSystem);

        _machine = factory.FromJson(MachineJson)
            .WithAction("registerRobot", (ctx, data) => RegisterRobotAction(data as Dictionary<string, object>))
            .WithAction("registerStation", (ctx, data) => RegisterStationAction(data as Dictionary<string, object>))
            .WithAction("handleStateChange", (ctx, data) => HandleStateChangeAction(data as Dictionary<string, object>))
            .WithAction("processTransfer", (ctx, data) => ProcessTransferAction(data as Dictionary<string, object>))
            .WithAction("tryProcessPending", (ctx, data) => TryProcessPendingAction())
            .WithGuard("queueEmpty", (ctx, data) => _context.PendingRequests.Count == 0)
            .BuildAndStart($"{_namePrefix}-machine");

        Logger.Instance.Log($"[SinglePublicationSchedulerXState] 📡⚡🎯 Initialized with XState JSON DSL");
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

        // Send REGISTER_ROBOT event to XState machine
        var eventData = new Dictionary<string, object>
        {
            ["robotId"] = robotId,
            ["robotActor"] = robotActor,
            ["statePublisher"] = robotPublisher
        };
        _machine.Tell(new SendEvent("REGISTER_ROBOT", eventData));

        Logger.Instance.Log($"[SinglePublicationSchedulerXState] 📡 Registered {robotId}");
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
        // Send TRANSFER_REQUEST event to XState machine
        var eventData = new Dictionary<string, object>
        {
            ["request"] = request
        };
        _machine.Tell(new SendEvent("TRANSFER_REQUEST", eventData));
    }

    public int GetQueueSize()
    {
        return _context.PendingRequests.Count;
    }

    public string GetRobotState(string robotId)
    {
        return _context.RobotStates.TryGetValue(robotId, out var state) ? state.State : "unknown";
    }

    #endregion

    #region Station State Publishing

    public void RegisterStation(string stationName, string initialState, int? initialWaferId)
    {
        // Create state publisher for this station
        var publisherName = $"{_namePrefix}-station-pub-{stationName.ToLower().Replace(" ", "-")}";
        var stationPublisher = _actorSystem.ActorOf(
            Props.Create(() => new StatePublisherActor(stationName, "Station", initialState, initialWaferId)),
            publisherName
        );
        _stationStatePublishers[stationName] = stationPublisher;

        // Send REGISTER_STATION event to XState machine
        var eventData = new Dictionary<string, object>
        {
            ["stationName"] = stationName,
            ["statePublisher"] = stationPublisher,
            ["initialState"] = initialState
        };
        _machine.Tell(new SendEvent("REGISTER_STATION", eventData));

        Logger.Instance.Log($"[SinglePublicationSchedulerXState] 📡 Registered station {stationName}");
    }

    public void UpdateStationState(string stationName, string state, int? waferId = null)
    {
        if (_stationStatePublishers.TryGetValue(stationName, out var publisher))
        {
            publisher.Tell(new StatePublisherActor.PublishStateMessage(state, waferId, null));
        }
    }

    #endregion

    #region XState Action Implementations

    private void RegisterRobotAction(Dictionary<string, object>? eventData)
    {
        if (eventData == null) return;

        var robotId = eventData["robotId"].ToString()!;
        var robotActor = (IActorRef)eventData["robotActor"];
        var statePublisher = (IActorRef)eventData["statePublisher"];

        _context.Robots[robotId] = robotActor;
        _context.RobotStates[robotId] = new RobotState { State = "idle" };

        // Create event converter proxy that subscribes to state publisher
        // and forwards events to XState machine
        var sanitizedRobotId = robotId.ToLower().Replace(" ", "-");
        var converterActor = _actorSystem.ActorOf(
            Props.Create(() => new StateEventConverter(_machine)),
            $"{_namePrefix}-converter-{sanitizedRobotId}"
        );

        // Subscribe the converter to state changes
        statePublisher.Tell(new StatePublisherActor.SubscribeMessage(converterActor));

        Logger.Instance.Log($"[XStateScheduler] Subscribed to {robotId} state changes");
    }

    private void RegisterStationAction(Dictionary<string, object>? eventData)
    {
        if (eventData == null) return;

        var stationName = eventData["stationName"].ToString()!;
        var statePublisher = (IActorRef)eventData["statePublisher"];

        _context.Stations[stationName] = new StationState { State = eventData["initialState"]?.ToString() ?? "idle" };

        // Create event converter proxy
        var sanitizedStationName = stationName.ToLower().Replace(" ", "-");
        var converterActor = _actorSystem.ActorOf(
            Props.Create(() => new StateEventConverter(_machine)),
            $"{_namePrefix}-converter-{sanitizedStationName}"
        );

        // Subscribe the converter to state changes
        statePublisher.Tell(new StatePublisherActor.SubscribeMessage(converterActor));

        Logger.Instance.Log($"[XStateScheduler] Subscribed to station {stationName} state changes");
    }

    private void HandleStateChangeAction(Dictionary<string, object>? eventData)
    {
        if (eventData == null) return;

        var entityType = eventData["entityType"].ToString()!;
        var entityId = eventData["entityId"].ToString()!;
        var newState = eventData["newState"].ToString()!;
        var waferId = eventData.ContainsKey("waferId") ? (int?)eventData["waferId"] : null;

        if (entityType == "Robot")
        {
            if (_context.RobotStates.TryGetValue(entityId, out var robot))
            {
                var previousState = robot.State;
                robot.State = newState;
                robot.HeldWaferId = waferId;

                Logger.Instance.Log($"[XStateScheduler] Robot {entityId}: {previousState} → {newState}");

                // When robot becomes idle, complete active transfer and invoke callback
                if (newState == "idle" && previousState != "idle")
                {
                    if (_context.ActiveTransfers.TryGetValue(entityId, out var completedTransfer))
                    {
                        _context.ActiveTransfers.Remove(entityId);
                        Logger.Instance.Log($"[XStateScheduler] {entityId} completed transfer of wafer {completedTransfer.WaferId}, invoking callback");
                        completedTransfer.OnCompleted?.Invoke(completedTransfer.WaferId);
                    }
                }
            }
        }
        else if (entityType == "Station")
        {
            if (_context.Stations.TryGetValue(entityId, out var station))
            {
                station.State = newState;
                station.WaferId = waferId;

                Logger.Instance.Log($"[XStateScheduler] Station {entityId}: {newState}");
            }
        }
    }

    private void ProcessTransferAction(Dictionary<string, object>? eventData)
    {
        if (eventData == null || !eventData.TryGetValue("request", out var requestObj)) return;

        var request = (TransferRequest)requestObj;

        try
        {
            request.Validate();
            _context.PendingRequests.Enqueue(request);
            Logger.Instance.Log($"[XStateScheduler] Transfer queued: {request}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[XStateScheduler:ERROR] Invalid request: {ex.Message}");
        }
    }

    private void TryProcessPendingAction()
    {
        if (_context.PendingRequests.Count == 0)
            return;

        // Limited queue search: Try head first, then search up to 10 more items to avoid head-of-line blocking
        var tempQueue = new Queue<TransferRequest>();
        int processed = 0;
        int searchLimit = Math.Min(_context.PendingRequests.Count, 10);  // Only search first 10 items
        int searched = 0;

        while (_context.PendingRequests.Count > 0 && searched < searchLimit)
        {
            var request = _context.PendingRequests.Dequeue();
            searched++;

            // Find available robot
            var robotId = FindAvailableRobot(request);

            if (robotId != null)
            {
                ExecuteTransfer(request, robotId);
                processed++;
                break;  // Stop after finding one executable request
            }
            else
            {
                // Can't execute now - put back for later
                tempQueue.Enqueue(request);
            }
        }

        // Put back all non-executable requests
        while (tempQueue.Count > 0)
        {
            _context.PendingRequests.Enqueue(tempQueue.Dequeue());
        }

        if (processed > 0)
        {
            Logger.Instance.Log($"[XStateScheduler] Processed {processed} request (searched {searched} items)");
        }
    }

    private string? FindAvailableRobot(TransferRequest request)
    {
        // Check source station state
        var fromState = _context.Stations.GetValueOrDefault(request.From)?.State ?? "unknown";

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
        var toState = _context.Stations.GetValueOrDefault(request.To)?.State ?? "unknown";

        // Destination must be idle (available)
        if (request.To != "Carrier" && toState != "idle")
            return null;

        // Find idle robot that can handle this route
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

        // Track active transfer (callback will be invoked when robot becomes idle)
        _context.ActiveTransfers[robotId] = request;

        // Send PICKUP command to robot
        var pickupData = new Dictionary<string, object>
        {
            ["waferId"] = request.WaferId,
            ["wafer"] = request.WaferId,
            ["from"] = request.From,
            ["to"] = request.To
        };

        _context.Robots[robotId].Tell(new XStateNet2.Core.Messages.SendEvent("PICKUP", pickupData));

        Logger.Instance.Log($"[XStateScheduler] Executed: {robotId} transferring wafer {request.WaferId}");
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

    #endregion

    #region Context & State Classes

    private class SchedulerContext
    {
        public Dictionary<string, IActorRef> Robots { get; } = new();
        public Dictionary<string, RobotState> RobotStates { get; } = new();
        public Dictionary<string, StationState> Stations { get; } = new();
        public Queue<TransferRequest> PendingRequests { get; } = new();
        public Dictionary<string, TransferRequest> ActiveTransfers { get; } = new();
    }

    private class RobotState
    {
        public string State { get; set; } = "idle";
        public int? HeldWaferId { get; set; }
    }

    private class StationState
    {
        public string State { get; set; } = "idle";
        public int? WaferId { get; set; }
    }

    #endregion

    #region State Event Converter

    /// <summary>
    /// Converts StateChangeEvent records to XState STATE_CHANGE events.
    /// This bridges the pub/sub pattern with XState machine.
    /// </summary>
    private class StateEventConverter : ReceiveActor
    {
        private readonly IActorRef _stateMachine;

        public StateEventConverter(IActorRef stateMachine)
        {
            _stateMachine = stateMachine;

            // Convert StateChangeEvent to XState event and forward to machine
            Receive<StateChangeEvent>(evt =>
            {
                var eventData = new Dictionary<string, object>
                {
                    ["entityType"] = evt.EntityType,
                    ["entityId"] = evt.EntityId,
                    ["newState"] = evt.NewState,
                    ["waferId"] = evt.WaferId ?? 0
                };

                _stateMachine.Tell(new SendEvent("STATE_CHANGE", eventData));
            });
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Logger.Instance.Log("[SinglePublicationSchedulerXState] 📡⚡ Disposed");
    }

    #endregion
}
