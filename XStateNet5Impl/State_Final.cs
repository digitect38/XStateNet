using Newtonsoft.Json.Linq;
namespace XStateNet;


public class FinalState : NormalState
{
    public FinalState(string name, string? parentName, string? stateMachineId) : base(name, parentName, stateMachineId)
    {
    }

    public override void Start()
    {
        base.Start();
    }

    public override void BuildTransitionList(string eventName, List<(CompoundState state, Transition transition, string eventName)> transitionList)
    {
        // parent first evaluation (is not the order exit/entry sequence)
        base.BuildTransitionList(eventName, transitionList);
    }

    public override Task EntryState(bool postAction = false, bool recursive = false, HistoryType historyType = HistoryType.None, HistoryState? targetHistoryState = null)
    {
        base.EntryState(postAction, recursive, historyType, targetHistoryState);
        IsDone = true;
        Parent?.OnDone();
        
        return Task.CompletedTask;
    }

    public override Task ExitState(bool postAction = true, bool recursive = false)
    {
        base.ExitState(postAction, recursive);

        return Task.CompletedTask;
    }    
    
    public override void PrintActiveStateTree(int depth)
    {
        Helper.WriteLine(depth * 2, $"- {Name?.Split('.').Last()}");
    }
}

/// <summary>
/// 
/// </summary>
public class Parser_FinalState : Parser_RealState
{
    public Parser_FinalState(string? machineId) : base(machineId) { }

    public override StateNode Parse(string stateName, string? parentName, JToken stateToken)
    {
        var state = new FinalState(stateName, parentName, machineId)
        {
        };        

        ParseActionsAndService(state, stateToken);        

        return state;
    }
}


