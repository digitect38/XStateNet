using System.Collections.Concurrent;
using System.Threading.Channels;

namespace XStateNet;

/// <summary>
/// Actor status enumeration
/// </summary>
public enum ActorStatus
{
    Idle,
    Running,
    Stopped,
    Error
}

/// <summary>
/// Base actor interface
/// </summary>
public interface IActor
{
    string Id { get; }
    ActorStatus Status { get; }
    Task SendAsync(string eventName, object? data = null);
    Task StartAsync();
    Task StopAsync();
}

/// <summary>
/// Actor system for managing actors
/// </summary>
public class ActorSystem
{
    private static ActorSystem? _instance;
    private readonly ConcurrentDictionary<string, IActor> _actors = new();

    public static ActorSystem Instance => _instance ??= new ActorSystem();

    private ActorSystem() { }

    /// <summary>
    /// Spawns a new actor
    /// </summary>
    public T Spawn<T>(string id, Func<string, T> actorFactory) where T : IActor
    {
        var actor = actorFactory(id);
        if (!_actors.TryAdd(id, actor))
        {
            throw new InvalidOperationException($"Actor with id '{id}' already exists");
        }
        return actor;
    }

    /// <summary>
    /// Gets an actor by ID
    /// </summary>
    public IActor? GetActor(string id)
    {
        _actors.TryGetValue(id, out var actor);
        return actor;
    }

    /// <summary>
    /// Removes an actor
    /// </summary>
    public bool RemoveActor(string id)
    {
        return _actors.TryRemove(id, out _);
    }

    /// <summary>
    /// Sends a message to an actor
    /// </summary>
    public async Task SendToActor(string actorId, string eventName, object? data = null)
    {
        if (_actors.TryGetValue(actorId, out var actor))
        {
            await actor.SendAsync(eventName, data);
        }
        else
        {
            throw new InvalidOperationException($"Actor '{actorId}' not found");
        }
    }

    /// <summary>
    /// Stops all actors
    /// </summary>
    public async Task StopAllActors()
    {
        var tasks = _actors.Values.Select(a => a.StopAsync());
        await Task.WhenAll(tasks);
        _actors.Clear();
    }
}

/// <summary>
/// State machine actor implementation
/// </summary>
public class StateMachineActor : IActor
{
    private readonly StateMachine _stateMachine;
    private readonly Channel<ActorMessage> _messageChannel;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _processingTask;

    public string Id { get; }
    public ActorStatus Status { get; private set; } = ActorStatus.Idle;
    public StateMachine Machine => _stateMachine;

    public StateMachineActor(string id, StateMachine stateMachine)
    {
        Id = id;
        _stateMachine = stateMachine;
        _messageChannel = Channel.CreateUnbounded<ActorMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Starts the actor
    /// </summary>
    public async Task StartAsync()
    {
        if (Status == ActorStatus.Running) return;

        try
        {
            Status = ActorStatus.Running;
            _cancellationTokenSource = new CancellationTokenSource();
            await _stateMachine.StartAsync();

            // Start message processing loop
            _processingTask = ProcessMessages(_cancellationTokenSource.Token);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting actor '{Id}': {ex.Message}");
            Status = ActorStatus.Error;
            throw; // Re-throw to preserve original behavior
        }
    }

    /// <summary>
    /// Stops the actor
    /// </summary>
    public async Task StopAsync()
    {
        if (Status != ActorStatus.Running) return;

        Status = ActorStatus.Stopped;
        _cancellationTokenSource?.Cancel();
        _messageChannel.Writer.TryComplete();

        if (_processingTask != null)
        {
            await _processingTask;
        }

        _stateMachine.Stop();
    }

    /// <summary>
    /// Sends an event to the actor
    /// </summary>
    public async Task SendAsync(string eventName, object? data = null)
    {
        if (Status != ActorStatus.Running)
        {
            throw new InvalidOperationException($"Actor '{Id}' is not running");
        }

        var message = new ActorMessage(eventName, data);
        await _messageChannel.Writer.WriteAsync(message);
    }

    /// <summary>
    /// Processes messages from the channel
    /// </summary>
    private async Task ProcessMessages(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _messageChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    // Update context if data is provided
                    if (message.Data != null && _stateMachine.ContextMap != null)
                    {
                        _stateMachine.ContextMap["_eventData"] = message.Data;
                    }

                    await _stateMachine.SendAsync(message.EventName);

                    // Check if an error was caught by the state machine's error handling
                    if (_stateMachine.ContextMap != null && _stateMachine.ContextMap.ContainsKey("_error"))
                    {
                        var error = _stateMachine.ContextMap["_error"];
                        if (error is Exception ex)
                        {
                            Console.WriteLine($"State machine error detected in actor '{Id}': {ex.Message}");
                            Status = ActorStatus.Error;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing message in actor '{Id}': {ex.Message}");
                    Status = ActorStatus.Error;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
    }

    /// <summary>
    /// Creates a child actor
    /// </summary>
    public StateMachineActor SpawnChild(string childId, StateMachine childMachine)
    {
        var fullId = $"{Id}.{childId}";
        return ActorSystem.Instance.Spawn(fullId, id => new StateMachineActor(id, childMachine));
    }
}

/// <summary>
/// Message sent to an actor
/// </summary>
public class ActorMessage
{
    public string EventName { get; }
    public object? Data { get; }
    public DateTime Timestamp { get; }

    public ActorMessage(string eventName, object? data = null)
    {
        EventName = eventName;
        Data = data;
        Timestamp = DateTime.UtcNow;
    }
}
