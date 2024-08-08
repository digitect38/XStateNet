using System;
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
    public Func<StateMachine, bool> Func { get; set; }
    public NamedGuard(string name, Func<StateMachine, bool> func)
    {
        Name = name;
        Func = func;
    }
}

public enum StateType
{
    Normal,
    Parallel,
    History
}

public enum HistoryType
{
    None,
    Shallow,
    Deep
}
