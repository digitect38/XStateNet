using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XStateNet;

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

public class NamedGuard
{
    public string Name { get; set; }
    public Func<StateMachine, bool> PredicateFunc { get; set; }

    public NamedGuard(string name, Func<StateMachine, bool> predicate)
    {
        Name = name;
        PredicateFunc = predicate;
    }
}

public class NamedService   // for "invoke"
{
    public string Name { get; set; }
    public Func<StateMachine, CancellationToken, Task<object>> ServiceFunc { get; set; }
    public NamedService(string name, Func<StateMachine, CancellationToken, Task<object>> service)
    {
        Name = name;
        ServiceFunc = service;
    }
}

public class NamedDelay   // for "delays"
{
    public string Name { get; set; }
    public Func<StateMachine, int> DelayFunc { get; set; }
    public NamedDelay(string name, Func<StateMachine, int> delayFunc)
    {
        Name = name;
        DelayFunc = delayFunc;
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