using Akka.Actor;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// State change event published by robots and stations
/// </summary>
public record StateChangeEvent(
    string EntityId,        // Robot or Station ID
    string EntityType,      // "Robot" or "Station"
    string NewState,        // New state (idle, busy, processing, done, etc.)
    int? WaferId,          // Wafer involved (if any)
    Dictionary<string, object>? Metadata  // Additional context
);

/// <summary>
/// Interface for entities that publish state changes
/// </summary>
public interface IStatePublisher
{
    /// <summary>
    /// Subscribe to state changes from this entity
    /// </summary>
    void Subscribe(IActorRef subscriber);

    /// <summary>
    /// Unsubscribe from state changes
    /// </summary>
    void Unsubscribe(IActorRef subscriber);
}

/// <summary>
/// Actor that manages state publication subscriptions
/// Implements pub/sub pattern for state changes
/// </summary>
public class StatePublisherActor : ReceiveActor
{
    private readonly string _entityId;
    private readonly string _entityType;
    private readonly HashSet<IActorRef> _subscribers = new();
    private string _currentState;
    private int? _currentWaferId;

    public StatePublisherActor(string entityId, string entityType, string initialState = "unknown", int? initialWaferId = null)
    {
        _entityId = entityId;
        _entityType = entityType;
        _currentState = initialState;
        _currentWaferId = initialWaferId;

        // Subscribe request
        Receive<SubscribeMessage>(msg =>
        {
            _subscribers.Add(msg.Subscriber);

            // Send current state immediately to new subscriber
            var currentStateEvent = new StateChangeEvent(
                _entityId,
                _entityType,
                _currentState,
                _currentWaferId,
                null
            );
            msg.Subscriber.Tell(currentStateEvent);
        });

        // Unsubscribe request
        Receive<UnsubscribeMessage>(msg =>
        {
            _subscribers.Remove(msg.Subscriber);
        });

        // Publish state change
        Receive<PublishStateMessage>(msg =>
        {
            _currentState = msg.NewState;
            _currentWaferId = msg.WaferId;

            var stateEvent = new StateChangeEvent(
                _entityId,
                _entityType,
                msg.NewState,
                msg.WaferId,
                msg.Metadata
            );

            // Broadcast to all subscribers
            foreach (var subscriber in _subscribers)
            {
                subscriber.Tell(stateEvent);
            }
        });
    }

    public record SubscribeMessage(IActorRef Subscriber);
    public record UnsubscribeMessage(IActorRef Subscriber);
    public record PublishStateMessage(string NewState, int? WaferId, Dictionary<string, object>? Metadata);
}
