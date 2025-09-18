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

public class ActionMap : ConcurrentDictionary<string, List<NamedAction>>
{
    public ActionMap() : base() { }
}

public class GuardMap : ConcurrentDictionary<string, NamedGuard>
{
    public GuardMap() : base() { }
}

public class ServiceMap : ConcurrentDictionary<string, NamedService>
{
    public ServiceMap() : base() { }
}

public class DelayMap : ConcurrentDictionary<string, NamedDelay>
{
    public DelayMap() : base() { }
}