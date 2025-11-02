using Akka.Actor;
using CMPSimXS2.Console.Models;
using LoggerHelper;
using XStateNet2.Core.Messages;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Publication-based scheduler using XStateNet2's NATIVE pub/sub features.
///
/// Uses XStateNet2's built-in Subscribe/StateChanged mechanism:
/// - Robot state machines already have Subscribe support
/// - Schedulers subscribe using XStateNet2.Core.Messages.Subscribe
/// - Receive XStateNet2.Core.Messages.StateChanged notifications
/// - Can also use Akka EventStream for system-wide listening
///
/// This is more elegant than custom StatePublisherActor!
/// </summary>
public class XStateNativePubSubScheduler : ReceiveActor
{
    private readonly string _robotId;
    private readonly IActorRef _robotStateMachine;
    private readonly Dictionary<string, IActorRef> _stationActors;
    private readonly Queue<TransferRequest> _pendingRequests = new();

    // Current state knowledge from XStateNet2 notifications
    private string _robotState = "unknown";
    private readonly Dictionary<string, string> _stationStates = new();

    public XStateNativePubSubScheduler(
        string robotId,
        IActorRef robotStateMachine,
        Dictionary<string, IActorRef> stationStateMachines)
    {
        _robotId = robotId;
        _robotStateMachine = robotStateMachine;
        _stationActors = stationStateMachines;

        // Subscribe to robot state changes using XStateNet2's native Subscribe!
        _robotStateMachine.Tell(new XStateNet2.Core.Messages.Subscribe());
        Logger.Instance.Log($"[XStateNativeScheduler:{_robotId}] 📡 Subscribed to robot using XStateNet2 native Subscribe");

        // Subscribe to relevant station state changes
        foreach (var (stationName, stationActor) in stationStateMachines)
        {
            stationActor.Tell(new XStateNet2.Core.Messages.Subscribe());
            Logger.Instance.Log($"[XStateNativeScheduler:{_robotId}] 📡 Subscribed to {stationName} using XStateNet2 native Subscribe");
        }

        // Handle XStateNet2's native StateChanged notifications!
        Receive<StateChanged>(HandleStateChanged);

        // Handle transfer requests
        Receive<TransferRequest>(HandleTransferRequest);

        // Query handlers
        Receive<GetRobotStateQuery>(_ => Sender.Tell(_robotState));
        Receive<GetQueueSizeQuery>(_ => Sender.Tell(_pendingRequests.Count));

        Logger.Instance.Log($"[XStateNativeScheduler:{_robotId}] ✅ XStateNet2 native pub/sub scheduler started");
    }

    private void HandleStateChanged(StateChanged msg)
    {
        Logger.Instance.Log($"[XStateNativeScheduler:{_robotId}] 📡 XStateNet2 StateChanged: {msg.PreviousState} → {msg.CurrentState}");

        // Determine if this is robot or station state change
        // (In real implementation, you'd check Sender or use metadata)

        var previousState = _robotState;
        _robotState = msg.CurrentState;

        // React to robot becoming idle
        if (_robotState == "idle" && previousState != "idle")
        {
            Logger.Instance.Log($"[XStateNativeScheduler:{_robotId}] 🟢 Robot idle, checking for work...");
            TryProcessNextRequest();
        }

        // Could also track station states similarly
        // For now, simplified implementation
    }

    private void HandleTransferRequest(TransferRequest request)
    {
        _pendingRequests.Enqueue(request);
        Logger.Instance.Log($"[XStateNativeScheduler:{_robotId}] 📨 Request queued (size: {_pendingRequests.Count})");
        TryProcessNextRequest();
    }

    private void TryProcessNextRequest()
    {
        if (_robotState != "idle" || _pendingRequests.Count == 0)
        {
            return;
        }

        var request = _pendingRequests.Dequeue();
        Logger.Instance.Log($"[XStateNativeScheduler:{_robotId}] 🚀 Executing: wafer {request.WaferId}");

        // Send event to robot state machine
        var pickupData = new Dictionary<string, object>
        {
            ["waferId"] = request.WaferId,
            ["from"] = request.From,
            ["to"] = request.To
        };

        _robotStateMachine.Tell(new SendEvent("PICKUP", pickupData));
        request.OnCompleted?.Invoke(request.WaferId);
    }

    public record GetRobotStateQuery();
    public record GetQueueSizeQuery();
}

/// <summary>
/// Orchestrator that uses XStateNet2's native pub/sub.
///
/// Key advantage: No custom StatePublisherActor needed!
/// Uses XStateNet2.Core.Messages.Subscribe and StateChanged directly.
/// </summary>
public class XStateNativePubSubOrchestrator : IRobotScheduler, IDisposable
{
    private readonly ActorSystem _actorSystem;
    private readonly Dictionary<string, IActorRef> _dedicatedSchedulers = new();
    private readonly string _namePrefix;

    public XStateNativePubSubOrchestrator(ActorSystem actorSystem, string? namePrefix = null)
    {
        _actorSystem = actorSystem;
        _namePrefix = namePrefix ?? $"xstate-native-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        Logger.Instance.Log($"[XStateNativeOrchestrator] 📡 Using XStateNet2 NATIVE pub/sub (Subscribe/StateChanged)");
    }

    public void RegisterRobot(string robotId, IActorRef robotActor)
    {
        // robotActor is already a StateMachineActor with Subscribe support!

        // Determine relevant station actors (would need to be passed in or stored)
        var stationActors = new Dictionary<string, IActorRef>();

        var schedulerName = $"{_namePrefix}-sched-{robotId.ToLower().Replace(" ", "-")}";
        var scheduler = _actorSystem.ActorOf(
            Props.Create(() => new XStateNativePubSubScheduler(
                robotId,
                robotActor,
                stationActors
            )),
            schedulerName
        );

        _dedicatedSchedulers[robotId] = scheduler;

        Logger.Instance.Log($"[XStateNativeOrchestrator] ✅ Registered {robotId} with XStateNet2 native pub/sub scheduler");
    }

    public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
    {
        // With XStateNet2 native pub/sub, state updates come from the state machine itself!
        // No need to manually publish - the StateMachineActor does it automatically
        Logger.Instance.Log($"[XStateNativeOrchestrator] State managed by XStateNet2 state machine");
    }

    public void RequestTransfer(TransferRequest request)
    {
        try
        {
            request.Validate();

            var targetRobotId = DetermineRobot(request.From, request.To, request.PreferredRobotId);

            if (targetRobotId != null && _dedicatedSchedulers.TryGetValue(targetRobotId, out var scheduler))
            {
                scheduler.Tell(request);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[XStateNativeOrchestrator:ERROR] {ex.Message}");
        }
    }

    public int GetQueueSize()
    {
        int total = 0;
        foreach (var scheduler in _dedicatedSchedulers.Values)
        {
            try
            {
                total += scheduler.Ask<int>(new XStateNativePubSubScheduler.GetQueueSizeQuery(), TimeSpan.FromMilliseconds(100)).Result;
            }
            catch { }
        }
        return total;
    }

    public string GetRobotState(string robotId)
    {
        if (_dedicatedSchedulers.TryGetValue(robotId, out var scheduler))
        {
            try
            {
                return scheduler.Ask<string>(new XStateNativePubSubScheduler.GetRobotStateQuery(), TimeSpan.FromMilliseconds(100)).Result;
            }
            catch { }
        }
        return "unknown";
    }

    private string? DetermineRobot(string from, string to, string? preferredRobotId)
    {
        if (!string.IsNullOrEmpty(preferredRobotId)) return preferredRobotId;

        return (from, to) switch
        {
            ("Carrier", "Polisher") => "Robot 1",
            ("Polisher", "Cleaner") => "Robot 2",
            ("Cleaner", "Buffer") => "Robot 3",
            ("Buffer", "Carrier") => "Robot 1",
            _ => null
        };
    }

    public void Dispose()
    {
        Logger.Instance.Log("[XStateNativeOrchestrator] 📡 Disposed");
    }
}
