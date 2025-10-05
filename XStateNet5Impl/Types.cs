using System.Collections.Concurrent;

namespace XStateNet;

#if false
public class NamedAction
{
    public string Name { get; set; }
    public Action<StateMachine> Action { get; set; }
    public NamedAction(string name, Action<StateMachine> action)
    {
        Name = name;
        Action = action;
    }
}
#else
public class NamedAction
{
    public string Name { get; set; }
    public Func<StateMachine, Task> Action { get; set; }  // Async

    public NamedAction(Func<StateMachine, Task> action, string name = null)
    {
        Name = name;
        Action = action;
    }

    public NamedAction(string name, Func<StateMachine, Task> action)
    {
        Name = name;
        Action = action;
    }

    // For backward compatibility, add overload for sync actions
    public NamedAction(string name, Action<StateMachine> action)
    {
        Name = name;
        Action = (sm) =>
        {
            action(sm);
            return Task.CompletedTask;
        };
    }
}

#endif

// Simplified: Constructor now accepts just the function, name is set from dictionary key
public class NamedGuard
{
    public string? Name { get; }
    public Func<StateMachine, bool> PredicateFunc { get; }

    public NamedGuard(Func<StateMachine, bool> predicate, string? name = null)
    {
        PredicateFunc = predicate;
        Name = name;
    }

    // Backward compatibility: name-first constructor
    public NamedGuard(string name, Func<StateMachine, bool> predicate)
    {
        PredicateFunc = predicate;
        Name = name;
    }
}

public class NamedService   // for "invoke"
{
    public string? Name { get; }
    public Func<StateMachine, CancellationToken, Task<object>> ServiceFunc { get; }

    public NamedService(Func<StateMachine, CancellationToken, Task<object>> service, string? name = null)
    {
        ServiceFunc = service;
        Name = name;
    }

    // Backward compatibility: name-first constructor
    public NamedService(string name, Func<StateMachine, CancellationToken, Task<object>> service)
    {
        ServiceFunc = service;
        Name = name;
    }
}

public class NamedDelay   // for "delays"
{
    public string? Name { get; }
    public Func<StateMachine, int> DelayFunc { get; }

    public NamedDelay(Func<StateMachine, int> delayFunc, string? name = null)
    {
        DelayFunc = delayFunc;
        Name = name;
    }

    // Backward compatibility: name-first constructor
    public NamedDelay(string name, Func<StateMachine, int> delayFunc)
    {
        DelayFunc = delayFunc;
        Name = name;
    }
}

public enum StateType
{
    Normal,
    Parallel,
    History,
    Final
}

public enum HistoryType
{
    None,
    Shallow,
    Deep
}

public enum TransitionType
{
    On,
    Always,
    After,
    OnDone,
    OnError
}

public class StateMap : ConcurrentDictionary<string, StateNode>
{
    public StateMap() : base() { }
}

public class ContextMap : ConcurrentDictionary<string, object?>
{
    public ContextMap() : base() { }
}

/// <summary>
/// Thread-safe ActionMap that ensures safe access to action lists
/// </summary>
public class ActionMap : ConcurrentDictionary<string, List<NamedAction>>
{
    private readonly ConcurrentDictionary<string, object> _locks = new();

    public ActionMap() : base() { }

    /// <summary>
    /// Thread-safe method to get actions for a given key
    /// </summary>
    public List<NamedAction> GetActions(string key)
    {
        if (!TryGetValue(key, out var list))
            return new List<NamedAction>();

        var lockObj = _locks.GetOrAdd(key, _ => new object());
        lock (lockObj)
        {
            // Return a copy to prevent external modification
            return new List<NamedAction>(list);
        }
    }

    /// <summary>
    /// Thread-safe method to add actions to a key
    /// </summary>
    public void AddActions(string key, IEnumerable<NamedAction> actions)
    {
        var lockObj = _locks.GetOrAdd(key, _ => new object());
        lock (lockObj)
        {
            var list = GetOrAdd(key, _ => new List<NamedAction>());
            list.AddRange(actions);
        }
    }

    /// <summary>
    /// Thread-safe method to set actions for a key (replaces existing)
    /// </summary>
    public void SetActions(string key, List<NamedAction> actions)
    {
        var lockObj = _locks.GetOrAdd(key, _ => new object());
        lock (lockObj)
        {
            this[key] = new List<NamedAction>(actions);
        }
    }
}

public class GuardMap : ConcurrentDictionary<string, NamedGuard>
{
    public GuardMap() : base() { }
}

public class ServiceMap : ConcurrentDictionary<string, NamedService>
{
    public ServiceMap() : base() { }
}

/// <summary>
/// Thread-safe TransitionMap that ensures safe access to transition lists
/// </summary>
public class TransitionMap : ConcurrentDictionary<string, List<Transition>>
{
    private readonly ConcurrentDictionary<string, object> _locks = new();

    public TransitionMap() : base() { }

    /// <summary>
    /// Thread-safe method to get transitions for a given event
    /// </summary>
    public List<Transition> GetTransitions(string eventName)
    {
        if (!TryGetValue(eventName, out var list))
            return new List<Transition>();

        var lockObj = _locks.GetOrAdd(eventName, _ => new object());
        lock (lockObj)
        {
            // Return a copy to prevent external modification
            return new List<Transition>(list);
        }
    }

    /// <summary>
    /// Thread-safe method to add a transition to an event
    /// </summary>
    public void AddTransition(string eventName, Transition transition)
    {
        var lockObj = _locks.GetOrAdd(eventName, _ => new object());
        lock (lockObj)
        {
            var list = GetOrAdd(eventName, _ => new List<Transition>());
            list.Add(transition);
        }
    }

    /// <summary>
    /// Thread-safe method to set transitions for an event (replaces existing)
    /// </summary>
    public void SetTransitions(string eventName, List<Transition> transitions)
    {
        var lockObj = _locks.GetOrAdd(eventName, _ => new object());
        lock (lockObj)
        {
            this[eventName] = new List<Transition>(transitions);
        }
    }
}

public class DelayMap : ConcurrentDictionary<string, NamedDelay>
{
    public DelayMap() : base() { }
}