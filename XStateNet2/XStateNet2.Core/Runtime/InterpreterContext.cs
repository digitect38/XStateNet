using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Akka.Actor;
using XStateNet2.Core.Engine;

namespace XStateNet2.Core.Runtime;

/// <summary>
/// Interpreter context - manages extended state (context) and registered actions/guards/services
/// Optimized with FrozenDictionary for read-heavy workloads after registration phase
/// </summary>
public class InterpreterContext
{
    private readonly Dictionary<string, object> _context = new();

    // Mutable dictionaries during registration phase
    private Dictionary<string, Action<InterpreterContext, object?>> _actionsMutable = new();
    private Dictionary<string, Func<InterpreterContext, object?, bool>> _guardsMutable = new();
    private Dictionary<string, Func<InterpreterContext, Task<object?>>> _servicesMutable = new();

    // Read-optimized frozen dictionaries after Freeze() is called
    private IReadOnlyDictionary<string, Action<InterpreterContext, object?>> _actions;
    private IReadOnlyDictionary<string, Func<InterpreterContext, object?, bool>> _guards;
    private IReadOnlyDictionary<string, Func<InterpreterContext, Task<object?>>> _services;

    // Actors remain mutable (can be registered at runtime)
    private readonly Dictionary<string, IActorRef> _actors = new();

    private bool _isFrozen;

    public InterpreterContext(IReadOnlyDictionary<string, object>? initialContext = null)
    {
        if (initialContext != null)
        {
            foreach (var (key, value) in initialContext)
            {
                _context[key] = value;
            }
        }

        // Initialize with mutable dictionaries (will be frozen later)
        _actions = _actionsMutable;
        _guards = _guardsMutable;
        _services = _servicesMutable;
        _isFrozen = false;
    }

    /// <summary>
    /// Freeze dictionaries for optimal read performance.
    /// Call this after all actions/guards/services have been registered.
    /// Note: _actors remains mutable for runtime registration.
    /// </summary>
    /// <param name="useFrozenDictionary">If true, uses FrozenDictionary (10-15% faster). If false, keeps mutable Dictionary.</param>
    public void Freeze(bool useFrozenDictionary = true)
    {
        if (_isFrozen)
            return;

        if (useFrozenDictionary)
        {
            // Convert to FrozenDictionary for optimal read performance (10-15% faster lookups)
            _actions = _actionsMutable.ToFrozenDictionary();
            _guards = _guardsMutable.ToFrozenDictionary();
            _services = _servicesMutable.ToFrozenDictionary();

            // Clear mutable dictionaries to free memory
            _actionsMutable = null!;
            _guardsMutable = null!;
            _servicesMutable = null!;
        }
        else
        {
            // Keep mutable dictionaries for baseline comparison
            _actions = _actionsMutable;
            _guards = _guardsMutable;
            _services = _servicesMutable;
        }

        _isFrozen = true;
    }

    #region Context Management

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(string key, object value)
    {
        _context[key] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? Get<T>(string key)
    {
        if (_context.TryGetValue(key, out var value))
        {
            if (value is T typedValue)
                return typedValue;

            // Try to convert JsonElement to target type
            if (value is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }
        }

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(string key) => _context.ContainsKey(key);

    public Dictionary<string, object> GetAll() => new(_context);

    /// <summary>
    /// Execute assign action - updates multiple context variables
    /// </summary>
    public void Assign(IReadOnlyDictionary<string, object> assignment)
    {
        foreach (var (key, value) in assignment)
        {
            _context[key] = value;
        }
    }

    #endregion

    #region Action Registration & Execution

    public void RegisterAction(string name, Action<InterpreterContext, object?> action)
    {
        if (_isFrozen)
            throw new InvalidOperationException("Cannot register actions after context has been frozen");

        _actionsMutable[name] = action;
    }

    public void ExecuteAction(string name, object? eventData)
    {
        if (_actions.TryGetValue(name, out var action))
        {
            action(this, eventData);
        }
        else
        {
            throw new ActionNotFoundException(name);
        }
    }

    public bool HasAction(string name) => _actions.ContainsKey(name);

    /// <summary>
    /// Execute action definition (inline action like assign/send/raise)
    /// </summary>
    public void ExecuteActionDefinition(ActionDefinition actionDef, object? eventData, IActorRef self)
    {
        switch (actionDef.Type.ToLowerInvariant())
        {
            case "assign":
                if (actionDef.Assignment != null)
                {
                    Assign(actionDef.Assignment);
                }
                break;

            case "send":
                if (!string.IsNullOrEmpty(actionDef.Event) && !string.IsNullOrEmpty(actionDef.To))
                {
                    SendTo(actionDef.To, actionDef.Event, actionDef.Data, actionDef.Delay);
                }
                break;

            case "raise":
                if (!string.IsNullOrEmpty(actionDef.Event))
                {
                    // Raise sends event to self
                    self.Tell(new Messages.SendEvent(actionDef.Event, actionDef.Data));
                }
                break;

            case "spawn":
                if (actionDef.Src != null)
                {
                    // Spawn is handled by the state machine actor with access to ActorContext
                    // Store spawn request in context for processing
                    var spawnId = actionDef.Id ?? $"spawned_{Guid.NewGuid():N}";
                    Set($"_spawn_request_{spawnId}", new { Src = actionDef.Src, Id = spawnId, Data = actionDef.Data });
                }
                break;

            case "stop":
                if (!string.IsNullOrEmpty(actionDef.Id))
                {
                    StopActor(actionDef.Id);
                }
                break;

            default:
                // Try to execute as registered action
                if (HasAction(actionDef.Type))
                {
                    ExecuteAction(actionDef.Type, eventData);
                }
                else
                {
                    throw new ActionNotFoundException($"Unknown action type: {actionDef.Type}");
                }
                break;
        }
    }

    #endregion

    #region Guard Registration & Evaluation

    public void RegisterGuard(string name, Func<InterpreterContext, object?, bool> guard)
    {
        if (_isFrozen)
            throw new InvalidOperationException("Cannot register guards after context has been frozen");

        _guardsMutable[name] = guard;
    }

    public bool EvaluateGuard(string name, object? eventData)
    {
        if (_guards.TryGetValue(name, out var guard))
        {
            return guard(this, eventData);
        }

        throw new GuardNotFoundException(name);
    }

    public bool HasGuard(string name) => _guards.ContainsKey(name);

    #endregion

    #region Service Registration & Invocation

    public void RegisterService(string name, Func<InterpreterContext, Task<object?>> service)
    {
        if (_isFrozen)
            throw new InvalidOperationException("Cannot register services after context has been frozen");

        _servicesMutable[name] = service;
    }

    public async Task<object?> InvokeService(string name)
    {
        if (_services.TryGetValue(name, out var service))
        {
            return await service(this);
        }

        throw new ServiceNotFoundException(name);
    }

    public bool HasService(string name) => _services.ContainsKey(name);

    #endregion

    #region Actor Management (for send action)

    public void RegisterActor(string id, IActorRef actor)
    {
        // Actors can be registered at any time (not frozen)
        _actors[id] = actor;
    }

    public IActorRef? GetActor(string id)
    {
        return _actors.TryGetValue(id, out var actor) ? actor : null;
    }

    public void StopActor(string id)
    {
        if (_actors.TryGetValue(id, out var actor))
        {
            actor.Tell(Akka.Actor.PoisonPill.Instance);
            _actors.Remove(id);
        }
    }

    public List<string> GetPendingSpawnRequests()
    {
        return _context.Keys
            .Where(k => k.StartsWith("_spawn_request_"))
            .Select(k => k.Substring("_spawn_request_".Length))
            .ToList();
    }

    public object? GetSpawnRequest(string id)
    {
        return Get<object>($"_spawn_request_{id}");
    }

    public void ClearSpawnRequest(string id)
    {
        var key = $"_spawn_request_{id}";
        if (_context.ContainsKey(key))
        {
            _context.Remove(key);
        }
    }

    public void SendTo(string targetId, string eventType, object? data = null, int? delayMs = null)
    {
        if (_actors.TryGetValue(targetId, out var target))
        {
            var message = new Messages.SendEvent(eventType, data);

            if (delayMs.HasValue && delayMs.Value > 0)
            {
                // Schedule delayed send
                var scheduler = Akka.Actor.ActorSystem.Create("temp").Scheduler;
                scheduler.ScheduleTellOnce(
                    TimeSpan.FromMilliseconds(delayMs.Value),
                    target,
                    message,
                    ActorRefs.NoSender
                );
            }
            else
            {
                target.Tell(message);
            }
        }
        else
        {
            throw new ActorNotFoundException($"Actor not found: {targetId}");
        }
    }

    #endregion
}

#region Exceptions

public class ActionNotFoundException : Exception
{
    public ActionNotFoundException(string name) : base($"Action not found: {name}") { }
}

public class GuardNotFoundException : Exception
{
    public GuardNotFoundException(string name) : base($"Guard not found: {name}") { }
}

public class ServiceNotFoundException : Exception
{
    public ServiceNotFoundException(string name) : base($"Service not found: {name}") { }
}

public class ActorNotFoundException : Exception
{
    public ActorNotFoundException(string message) : base(message) { }
}

#endregion
