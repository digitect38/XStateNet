using Akka.Actor;
using XStateNet2.Core.Actors;
using XStateNet2.Core.Engine;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Runtime;

namespace XStateNet2.Core.Builder;

/// <summary>
/// Fluent builder for creating and configuring state machines
/// </summary>
public class MachineBuilder
{
    private readonly XStateMachineScript _script;
    private readonly InterpreterContext _context;
    private readonly ActorSystem _actorSystem;
    private bool _useFrozenDictionary = true; // Default to FrozenDictionary optimization

    public MachineBuilder(XStateMachineScript script, ActorSystem actorSystem)
    {
        _script = script ?? throw new ArgumentNullException(nameof(script));
        _actorSystem = actorSystem ?? throw new ArgumentNullException(nameof(actorSystem));
        _context = new InterpreterContext(script.Context);
    }

    /// <summary>
    /// Register a named action
    /// </summary>
    public MachineBuilder WithAction(string name, Action<InterpreterContext, object?> action)
    {
        _context.RegisterAction(name, action);
        return this;
    }

    /// <summary>
    /// Register a guard condition
    /// </summary>
    public MachineBuilder WithGuard(string name, Func<InterpreterContext, object?, bool> guard)
    {
        _context.RegisterGuard(name, guard);
        return this;
    }

    /// <summary>
    /// Register an async service
    /// </summary>
    public MachineBuilder WithService(string name, Func<InterpreterContext, Task<object?>> service)
    {
        _context.RegisterService(name, service);
        return this;
    }

    /// <summary>
    /// Configure whether to use FrozenDictionary optimization.
    /// Default: true (uses FrozenDictionary for 10-15% faster lookups).
    /// Set to false for baseline Dictionary performance comparison.
    /// </summary>
    public MachineBuilder WithFrozenDictionary(bool useFrozenDictionary)
    {
        _useFrozenDictionary = useFrozenDictionary;
        return this;
    }

    /// <summary>
    /// Register a delay service (convenience method)
    /// </summary>
    public MachineBuilder WithDelayService(string name, int delayMs)
    {
        _context.RegisterService(name, async (ctx) =>
        {
            await Task.Delay(delayMs);
            return null;
        });
        return this;
    }

    /// <summary>
    /// Register another actor for send action
    /// </summary>
    public MachineBuilder WithActor(string id, IActorRef actor)
    {
        _context.RegisterActor(id, actor);
        return this;
    }

    /// <summary>
    /// Set initial context value
    /// </summary>
    public MachineBuilder WithContext(string key, object value)
    {
        _context.Set(key, value);
        return this;
    }

    /// <summary>
    /// Check if an action has been registered
    /// </summary>
    public bool HasAction(string name)
    {
        return _context.HasAction(name);
    }

    /// <summary>
    /// Build and start the state machine actor
    /// </summary>
    public IActorRef BuildAndStart(string? actorName = null)
    {
        // Freeze context with configured optimization level
        _context.Freeze(_useFrozenDictionary);

        // Auto-generate unique actor name to prevent collisions in parallel tests
        var uniqueActorName = actorName ?? $"{_script.Id}-{Guid.NewGuid():N}";

        var props = Props.Create(() => new StateMachineActor(_script, _context));
        var actor = _actorSystem.ActorOf(props, uniqueActorName);

        // Start the machine
        actor.Tell(new StartMachine());

        return actor;
    }

    /// <summary>
    /// Build the state machine actor without starting it
    /// </summary>
    public IActorRef Build(string? actorName = null)
    {
        // Freeze context with configured optimization level
        _context.Freeze(_useFrozenDictionary);

        // Auto-generate unique actor name to prevent collisions in parallel tests
        var uniqueActorName = actorName ?? $"{_script.Id}-{Guid.NewGuid():N}";

        var props = Props.Create(() => new StateMachineActor(_script, _context));
        return _actorSystem.ActorOf(props, uniqueActorName);
    }
}
