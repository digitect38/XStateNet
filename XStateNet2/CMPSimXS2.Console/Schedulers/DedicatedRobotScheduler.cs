using Akka.Actor;
using CMPSimXS2.Console.Models;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Dedicated scheduler for a single robot that reacts to state publications.
///
/// Architecture:
/// - Each robot has its own dedicated scheduler
/// - Scheduler subscribes to state changes from:
///   * The robot it manages
///   * Stations relevant to the robot's routes
/// - Reacts to state publications to coordinate work
/// - Pure event-driven - no polling!
///
/// Example:
/// Robot 1 Scheduler subscribes to:
/// - Robot 1 state (idle → busy → carrying → idle)
/// - Carrier state (for pickup/delivery)
/// - Polisher state (for delivery)
/// - Buffer state (for pickup)
/// </summary>
public class DedicatedRobotScheduler : ReceiveActor
{
    private readonly string _robotId;
    private readonly IActorRef _robotActor;
    private readonly Dictionary<string, IActorRef> _stationPublishers;
    private readonly Queue<TransferRequest> _pendingRequests = new();

    // Current state knowledge from publications
    private string _robotState = "unknown";
    private int? _robotWaferId = null;
    private readonly Dictionary<string, string> _stationStates = new();
    private readonly Dictionary<string, int?> _stationWafers = new();

    public DedicatedRobotScheduler(
        string robotId,
        IActorRef robotActor,
        IActorRef robotStatePublisher,
        Dictionary<string, IActorRef> stationPublishers)
    {
        _robotId = robotId;
        _robotActor = robotActor;
        _stationPublishers = stationPublishers;

        // Subscribe to robot state changes
        robotStatePublisher.Tell(new StatePublisherActor.SubscribeMessage(Self));
        Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] 📡 Subscribed to robot state publications");

        // Subscribe to relevant station state changes
        foreach (var (stationName, publisher) in stationPublishers)
        {
            publisher.Tell(new StatePublisherActor.SubscribeMessage(Self));
            Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] 📡 Subscribed to {stationName} state publications");
        }

        // Handle state change publications
        Receive<StateChangeEvent>(HandleStateChange);

        // Handle new transfer request
        Receive<TransferRequest>(HandleTransferRequest);

        // Query handlers
        Receive<GetRobotStateQuery>(_ => Sender.Tell(_robotState));
        Receive<GetQueueSizeQuery>(_ => Sender.Tell(_pendingRequests.Count));

        Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] ✅ Dedicated scheduler started (publication-based, event-driven)");
    }

    private void HandleStateChange(StateChangeEvent evt)
    {
        Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] 📡 Received state change: {evt.EntityType} {evt.EntityId} → {evt.NewState} (wafer: {evt.WaferId})");

        if (evt.EntityType == "Robot" && evt.EntityId == _robotId)
        {
            // Our robot's state changed
            var previousState = _robotState;
            _robotState = evt.NewState;
            _robotWaferId = evt.WaferId;

            Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] 🤖 Robot state: {previousState} → {_robotState}");

            // React to robot becoming idle
            if (_robotState == "idle" && previousState != "idle")
            {
                OnRobotBecameIdle();
            }
        }
        else if (evt.EntityType == "Station")
        {
            // A station's state changed
            var stationName = evt.EntityId;
            var previousState = _stationStates.GetValueOrDefault(stationName, "unknown");

            _stationStates[stationName] = evt.NewState;
            _stationWafers[stationName] = evt.WaferId;

            Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] ⚙️  Station {stationName}: {previousState} → {evt.NewState}");

            // React to station becoming ready
            if (evt.NewState == "done" || evt.NewState == "occupied")
            {
                OnStationBecameReady(stationName);
            }
        }
    }

    private void HandleTransferRequest(TransferRequest request)
    {
        Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] 📨 New transfer request: wafer {request.WaferId} {request.From}→{request.To}");

        // Validate that this robot can handle this route
        if (!CanHandleRoute(request.From, request.To))
        {
            Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] ❌ Cannot handle route {request.From}→{request.To}");
            return;
        }

        _pendingRequests.Enqueue(request);
        Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] ➕ Request queued (queue size: {_pendingRequests.Count})");

        // Try to process immediately if robot is idle
        TryProcessNextRequest();
    }

    private void OnRobotBecameIdle()
    {
        Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] 🟢 Robot became idle, checking for work...");
        TryProcessNextRequest();
    }

    private void OnStationBecameReady(string stationName)
    {
        Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] 🟢 Station {stationName} became ready, checking for relevant work...");
        TryProcessNextRequest();
    }

    private void TryProcessNextRequest()
    {
        // Only process if robot is idle
        if (_robotState != "idle")
        {
            Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] ⏸️  Robot not idle (state: {_robotState}), waiting...");
            return;
        }

        // Check if we have pending requests
        if (_pendingRequests.Count == 0)
        {
            Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] 📭 No pending requests");
            return;
        }

        // Try to find a request we can execute now
        var tempQueue = new Queue<TransferRequest>();
        TransferRequest? executableRequest = null;

        while (_pendingRequests.Count > 0)
        {
            var request = _pendingRequests.Dequeue();

            if (CanExecuteNow(request))
            {
                executableRequest = request;
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

        if (executableRequest != null)
        {
            ExecuteTransfer(executableRequest);
        }
        else
        {
            Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] ⏳ Requests pending but conditions not met yet");
        }
    }

    private bool CanExecuteNow(TransferRequest request)
    {
        // Check source station state
        var fromState = _stationStates.GetValueOrDefault(request.From, "unknown");

        // For pickup from processing stations, they must be "done"
        if (request.From == "Polisher" || request.From == "Cleaner")
        {
            if (fromState != "done")
            {
                Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] ⏳ {request.From} not ready (state: {fromState})");
                return false;
            }
        }
        // For pickup from Buffer, it must be "occupied"
        else if (request.From == "Buffer")
        {
            if (fromState != "occupied")
            {
                Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] ⏳ Buffer not ready (state: {fromState})");
                return false;
            }
        }

        // Check destination station state
        var toState = _stationStates.GetValueOrDefault(request.To, "unknown");

        // Destination must be idle (available)
        if (request.To != "Carrier" && toState != "idle")
        {
            Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] ⏳ {request.To} not available (state: {toState})");
            return false;
        }

        return true;
    }

    private void ExecuteTransfer(TransferRequest request)
    {
        Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] 🚀 Executing transfer: wafer {request.WaferId} {request.From}→{request.To}");

        // Send PICKUP command to robot
        var pickupData = new Dictionary<string, object>
        {
            ["waferId"] = request.WaferId,
            ["wafer"] = request.WaferId,
            ["from"] = request.From,
            ["to"] = request.To
        };

        _robotActor.Tell(new XStateNet2.Core.Messages.SendEvent("PICKUP", pickupData));

        // Invoke completion callback
        request.OnCompleted?.Invoke(request.WaferId);

        Logger.Instance.Log($"[DedicatedScheduler:{_robotId}] ✅ Transfer initiated");
    }

    private bool CanHandleRoute(string from, string to)
    {
        return _robotId switch
        {
            "Robot 1" => (from == "Carrier" && to == "Polisher") ||
                        (from == "Buffer" && to == "Carrier") ||
                        (from == "Polisher" && to == "Carrier"),
            "Robot 2" => (from == "Polisher" && to == "Cleaner"),
            "Robot 3" => (from == "Cleaner" && to == "Buffer"),
            _ => false
        };
    }

    public record GetRobotStateQuery();
    public record GetQueueSizeQuery();
}
