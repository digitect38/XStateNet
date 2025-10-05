using System.Collections.Concurrent;
using System.Threading.Channels;

namespace XStateNet.Actors;

/// <summary>
/// Supervision strategy for child actors
/// </summary>
public enum SupervisionStrategy
{
    Restart,      // Restart the failed actor
    Stop,         // Stop the failed actor
    Escalate,     // Escalate the failure to parent
    Resume        // Resume processing, ignoring the failure
}

/// <summary>
/// Actor reference for communication
/// </summary>
public class ActorRef
{
    private readonly string _actorPath;
    private readonly ActorSystemV2 _system;

    public string Path => _actorPath;

    internal ActorRef(string actorPath, ActorSystemV2 system)
    {
        _actorPath = actorPath;
        _system = system;
    }

    /// <summary>
    /// Send a message to this actor
    /// </summary>
    public async Task TellAsync(object message, ActorRef? sender = null)
    {
        await _system.DeliverMessage(_actorPath, message, sender);
    }

    /// <summary>
    /// Send a message and wait for a response
    /// </summary>
    public async Task<T> AskAsync<T>(object message, TimeSpan? timeout = null)
    {
        var timeoutMs = timeout?.TotalMilliseconds ?? 5000;
        return await _system.Ask<T>(_actorPath, message, TimeSpan.FromMilliseconds(timeoutMs));
    }
}

/// <summary>
/// Base actor context for accessing actor system features
/// </summary>
public class ActorContext
{
    public ActorRef Self { get; }
    public ActorRef? Parent { get; }
    public ActorSystemV2 System { get; }
    public ConcurrentDictionary<string, ActorRef> Children { get; }
    public ActorRef? Sender { get; internal set; }

    internal ActorContext(ActorRef self, ActorRef? parent, ActorSystemV2 system)
    {
        Self = self;
        Parent = parent;
        System = system;
        Children = new ConcurrentDictionary<string, ActorRef>();
    }

    /// <summary>
    /// Spawn a child actor
    /// </summary>
    public ActorRef SpawnChild(string name, Props props)
    {
        var childPath = $"{Self.Path}/{name}";
        var childRef = System.SpawnActor(childPath, props, Self);
        Children[name] = childRef;
        return childRef;
    }

    /// <summary>
    /// Stop a child actor
    /// </summary>
    public async Task StopChild(string name)
    {
        if (Children.TryGetValue(name, out var childRef))
        {
            await System.StopActor(childRef.Path);
            Children.TryRemove(name, out _);
        }
    }

    /// <summary>
    /// Watch another actor for termination
    /// </summary>
    public void Watch(ActorRef actorRef)
    {
        System.Watch(Self, actorRef);
    }

    /// <summary>
    /// Stop watching another actor
    /// </summary>
    public void Unwatch(ActorRef actorRef)
    {
        System.Unwatch(Self, actorRef);
    }
}

/// <summary>
/// Props for creating actors
/// </summary>
public class Props
{
    public Type ActorType { get; }
    public object[]? Args { get; }
    public SupervisionStrategy SupervisionStrategy { get; }
    public StateMachine? StateMachine { get; }

    private Props(Type actorType, object[]? args = null,
                  SupervisionStrategy strategy = SupervisionStrategy.Restart,
                  StateMachine? stateMachine = null)
    {
        ActorType = actorType;
        Args = args;
        SupervisionStrategy = strategy;
        StateMachine = stateMachine;
    }

    /// <summary>
    /// Create props for an actor type
    /// </summary>
    public static Props Create<T>(params object[] args) where T : ActorBase
    {
        return new Props(typeof(T), args);
    }

    /// <summary>
    /// Create props for a state machine actor
    /// </summary>
    public static Props CreateStateMachine(StateMachine stateMachine,
                                          SupervisionStrategy strategy = SupervisionStrategy.Restart)
    {
        return new Props(typeof(StateMachineActorV2), null, strategy, stateMachine);
    }

    /// <summary>
    /// Set supervision strategy
    /// </summary>
    public Props WithSupervisionStrategy(SupervisionStrategy strategy)
    {
        return new Props(ActorType, Args, strategy, StateMachine);
    }
}

/// <summary>
/// Base actor class
/// </summary>
public abstract class ActorBase
{
    protected ActorContext Context { get; private set; } = null!;
    private Channel<ActorEnvelope> Mailbox { get; set; } = null!;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _processingTask;

    public ActorStatus Status { get; protected set; } = ActorStatus.Idle;

    /// <summary>
    /// Called when the actor starts
    /// </summary>
    protected virtual Task PreStart()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the actor stops
    /// </summary>
    protected virtual Task PostStop()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the actor restarts
    /// </summary>
    protected virtual Task PreRestart(Exception reason, object? message)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called after the actor restarts
    /// </summary>
    protected virtual Task PostRestart(Exception reason)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Message handler - must be implemented by derived classes
    /// </summary>
    protected abstract Task Receive(object message);

    /// <summary>
    /// Initialize the actor
    /// </summary>
    internal void Initialize(ActorContext context, Channel<ActorEnvelope> mailbox)
    {
        Context = context;
        Mailbox = mailbox;
    }

    /// <summary>
    /// Start the actor
    /// </summary>
    internal async Task StartAsync()
    {
        if (Status == ActorStatus.Running) return;

        Status = ActorStatus.Running;
        _cancellationTokenSource = new CancellationTokenSource();

        await PreStart();

        // Start message processing loop
        _processingTask = ProcessMessages(_cancellationTokenSource.Token);
    }

    /// <summary>
    /// Stop the actor
    /// </summary>
    internal async Task StopAsync()
    {
        if (Status != ActorStatus.Running) return;

        Status = ActorStatus.Stopped;
        _cancellationTokenSource?.Cancel();
        Mailbox.Writer.TryComplete();

        if (_processingTask != null)
        {
            await _processingTask;
        }

        await PostStop();
    }

    /// <summary>
    /// Restart the actor
    /// </summary>
    internal async Task RestartAsync(Exception reason, object? message)
    {
        await PreRestart(reason, message);
        await StopAsync();
        await StartAsync();
        await PostRestart(reason);
    }

    /// <summary>
    /// Process messages from mailbox
    /// </summary>
    private async Task ProcessMessages(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var envelope in Mailbox.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    Context.Sender = envelope.Sender;
                    await Receive(envelope.Message);
                }
                catch (Exception ex)
                {
                    await HandleFailure(ex, envelope.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
    }

    /// <summary>
    /// Handle actor failure
    /// </summary>
    private async Task HandleFailure(Exception exception, object message)
    {
        Status = ActorStatus.Error;

        if (Context.Parent != null)
        {
            // Notify parent of failure
            await Context.Parent.TellAsync(new ActorFailure(Context.Self, exception, message));
        }
    }
}

/// <summary>
/// State machine actor implementation
/// </summary>
public class StateMachineActorV2 : ActorBase
{
    private StateMachine? _stateMachine;

    public void SetStateMachine(StateMachine stateMachine)
    {
        _stateMachine = stateMachine;
    }

    protected override async Task PreStart()
    {
        if (_stateMachine != null)
        {
            await _stateMachine.StartAsync();
        }
        await base.PreStart();
    }

    protected override async Task PostStop()
    {
        if (_stateMachine != null)
        {
            _stateMachine.Stop();
        }
        await base.PostStop();
    }

    protected override async Task Receive(object message)
    {
        if (_stateMachine == null)
        {
            throw new InvalidOperationException("State machine not set");
        }

        switch (message)
        {
            case StateEvent stateEvent:
                await _stateMachine.SendAsync(stateEvent.EventName, stateEvent.Data);
                break;

            case string eventName:
                await _stateMachine.SendAsync(eventName);
                break;

            default:
                // Store message data in context
                if (_stateMachine.ContextMap != null)
                {
                    _stateMachine.ContextMap["_lastMessage"] = message;
                }
                break;
        }
    }
}

/// <summary>
/// State event message
/// </summary>
public class StateEvent
{
    public string EventName { get; }
    public object? Data { get; }

    public StateEvent(string eventName, object? data = null)
    {
        EventName = eventName;
        Data = data;
    }
}

/// <summary>
/// Actor failure notification
/// </summary>
public class ActorFailure
{
    public ActorRef FailedActor { get; }
    public Exception Exception { get; }
    public object? FailedMessage { get; }

    public ActorFailure(ActorRef failedActor, Exception exception, object? failedMessage)
    {
        FailedActor = failedActor;
        Exception = exception;
        FailedMessage = failedMessage;
    }
}

/// <summary>
/// Actor termination notification
/// </summary>
public class Terminated
{
    public ActorRef Actor { get; }

    public Terminated(ActorRef actor)
    {
        Actor = actor;
    }
}

/// <summary>
/// Message envelope with sender information
/// </summary>
internal class ActorEnvelope
{
    public object Message { get; }
    public ActorRef? Sender { get; }
    public DateTime Timestamp { get; }

    public ActorEnvelope(object message, ActorRef? sender = null)
    {
        Message = message;
        Sender = sender;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Enhanced actor system with hierarchy and supervision
/// </summary>
public class ActorSystemV2
{
    private readonly ConcurrentDictionary<string, ActorContainer> _actors = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _watchers = new();
    private readonly Channel<ActorEnvelope> _deadLetterQueue;
    private readonly string _name;

    public string Name => _name;
    public ActorRef DeadLetters { get; }

    public ActorSystemV2(string name = "default")
    {
        _name = name;
        _deadLetterQueue = Channel.CreateUnbounded<ActorEnvelope>();
        DeadLetters = new ActorRef("/deadLetters", this);

        // Start dead letter processor
        _ = ProcessDeadLetters();
    }

    /// <summary>
    /// Spawn a new actor
    /// </summary>
    internal ActorRef SpawnActor(string path, Props props, ActorRef? parent = null)
    {
        if (_actors.ContainsKey(path))
        {
            throw new InvalidOperationException($"Actor already exists at path: {path}");
        }

        ActorBase actor;

        if (props.StateMachine != null && props.ActorType == typeof(StateMachineActorV2))
        {
            var smActor = new StateMachineActorV2();
            smActor.SetStateMachine(props.StateMachine);
            actor = smActor;
        }
        else
        {
            actor = (ActorBase)Activator.CreateInstance(props.ActorType, props.Args ?? Array.Empty<object>())!;
        }

        var actorRef = new ActorRef(path, this);
        var context = new ActorContext(actorRef, parent, this);
        var mailbox = Channel.CreateUnbounded<ActorEnvelope>();

        actor.Initialize(context, mailbox);

        var container = new ActorContainer(actor, actorRef, mailbox, props.SupervisionStrategy);
        _actors[path] = container;

        _ = actor.StartAsync();

        return actorRef;
    }

    /// <summary>
    /// Stop an actor
    /// </summary>
    public async Task StopActor(string path)
    {
        if (_actors.TryRemove(path, out var container))
        {
            // Notify watchers
            NotifyWatchers(container.Reference);

            await container.Actor.StopAsync();
        }
    }

    /// <summary>
    /// Deliver a message to an actor
    /// </summary>
    internal async Task DeliverMessage(string path, object message, ActorRef? sender)
    {
        if (_actors.TryGetValue(path, out var container))
        {
            var envelope = new ActorEnvelope(message, sender);
            await container.Mailbox.Writer.WriteAsync(envelope);
        }
        else
        {
            // Send to dead letter queue
            await _deadLetterQueue.Writer.WriteAsync(new ActorEnvelope(
                new DeadLetter(message, sender, path)));
        }
    }

    /// <summary>
    /// Ask pattern - send and wait for response
    /// </summary>
    internal async Task<T> Ask<T>(string path, object message, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<T>();
        var tempActorPath = $"/temp/{Guid.NewGuid()}";

        // Create temporary actor to receive response
        var tempActor = new ResponseActor<T>(tcs);
        var tempRef = new ActorRef(tempActorPath, this);
        var tempContext = new ActorContext(tempRef, null, this);
        var tempMailbox = Channel.CreateUnbounded<ActorEnvelope>();

        tempActor.Initialize(tempContext, tempMailbox);

        var container = new ActorContainer(tempActor, tempRef, tempMailbox, SupervisionStrategy.Stop);
        _actors[tempActorPath] = container;

        await tempActor.StartAsync();

        // Send message with temp actor as sender
        await DeliverMessage(path, message, tempRef);

        // Wait for response with timeout
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var responseTask = tcs.Task;
            var completedTask = await Task.WhenAny(responseTask, Task.Delay(timeout, cts.Token));

            if (completedTask == responseTask)
            {
                return await responseTask;
            }
            else
            {
                throw new TimeoutException($"Ask timeout after {timeout.TotalMilliseconds}ms");
            }
        }
        finally
        {
            // Clean up temp actor
            await StopActor(tempActorPath);
        }
    }

    /// <summary>
    /// Watch an actor for termination
    /// </summary>
    internal void Watch(ActorRef watcher, ActorRef watchee)
    {
        var key = watchee.Path;
        if (!_watchers.ContainsKey(key))
        {
            _watchers[key] = new HashSet<string>();
        }
        _watchers[key].Add(watcher.Path);
    }

    /// <summary>
    /// Stop watching an actor
    /// </summary>
    internal void Unwatch(ActorRef watcher, ActorRef watchee)
    {
        var key = watchee.Path;
        if (_watchers.TryGetValue(key, out var watchers))
        {
            watchers.Remove(watcher.Path);
            if (watchers.Count == 0)
            {
                _watchers.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Notify watchers of actor termination
    /// </summary>
    private void NotifyWatchers(ActorRef terminatedActor)
    {
        if (_watchers.TryRemove(terminatedActor.Path, out var watchers))
        {
            var terminated = new Terminated(terminatedActor);
            foreach (var watcherPath in watchers)
            {
                _ = DeliverMessage(watcherPath, terminated, null);
            }
        }
    }

    /// <summary>
    /// Process dead letters
    /// </summary>
    private async Task ProcessDeadLetters()
    {
        await foreach (var envelope in _deadLetterQueue.Reader.ReadAllAsync())
        {
            if (envelope.Message is DeadLetter deadLetter)
            {
                Console.WriteLine($"[DeadLetter] Message to {deadLetter.Recipient} from {deadLetter.Sender?.Path ?? "unknown"}: {deadLetter.Message}");
            }
        }
    }

    /// <summary>
    /// Shutdown the actor system
    /// </summary>
    public async Task Shutdown()
    {
        var tasks = _actors.Values.Select(c => c.Actor.StopAsync());
        await Task.WhenAll(tasks);
        _actors.Clear();
        _watchers.Clear();
        _deadLetterQueue.Writer.TryComplete();
    }

    /// <summary>
    /// Create a root actor
    /// </summary>
    public ActorRef ActorOf(Props props, string name)
    {
        var path = $"/{name}";
        return SpawnActor(path, props);
    }

    /// <summary>
    /// Get actor by path
    /// </summary>
    public ActorRef? ActorSelection(string path)
    {
        return _actors.TryGetValue(path, out var container) ? container.Reference : null;
    }
}

/// <summary>
/// Container for actor internals
/// </summary>
internal class ActorContainer
{
    public ActorBase Actor { get; }
    public ActorRef Reference { get; }
    public Channel<ActorEnvelope> Mailbox { get; }
    public SupervisionStrategy SupervisionStrategy { get; }

    public ActorContainer(ActorBase actor, ActorRef reference,
                         Channel<ActorEnvelope> mailbox,
                         SupervisionStrategy supervisionStrategy)
    {
        Actor = actor;
        Reference = reference;
        Mailbox = mailbox;
        SupervisionStrategy = supervisionStrategy;
    }
}

/// <summary>
/// Temporary actor for Ask pattern
/// </summary>
internal class ResponseActor<T> : ActorBase
{
    private readonly TaskCompletionSource<T> _tcs;

    public ResponseActor(TaskCompletionSource<T> tcs)
    {
        _tcs = tcs;
    }

    protected override Task Receive(object message)
    {
        if (message is T response)
        {
            _tcs.TrySetResult(response);
        }
        else
        {
            _tcs.TrySetException(new InvalidCastException($"Expected {typeof(T)} but got {message.GetType()}"));
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Dead letter message
/// </summary>
public class DeadLetter
{
    public object Message { get; }
    public ActorRef? Sender { get; }
    public string Recipient { get; }

    public DeadLetter(object message, ActorRef? sender, string recipient)
    {
        Message = message;
        Sender = sender;
        Recipient = recipient;
    }
}
