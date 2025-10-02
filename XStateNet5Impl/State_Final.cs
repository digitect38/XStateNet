using Newtonsoft.Json.Linq;
namespace XStateNet;


public class FinalState : NormalState
{
    public FinalState(string name, string? parentName, string? stateMachineId) : base(name, parentName, stateMachineId)
    {
    }

    public override async Task Start()
    {
        await base.Start();
    }

    public override void BuildTransitionList(string eventName, List<(CompoundState state, Transition transition, string eventName)> transitionList)
    {
        // parent first evaluation (is not the order exit/entry sequence)
        base.BuildTransitionList(eventName, transitionList);
    }

    public override async Task<Task> EntryState(bool postAction = false, bool recursive = false, HistoryType historyType = HistoryType.None, HistoryState? targetHistoryState = null)
    {
        var baseTaskTask = base.EntryState(postAction, recursive, historyType, targetHistoryState);
        var baseTask = await baseTaskTask;
        if (baseTask != null)
            await baseTask;
        IsDone = true;
        Parent?.OnDone();

        return Task.FromResult(Task.CompletedTask);
    }

    public override async Task<Task> ExitState(bool postAction = true, bool recursive = false)
    {
        var baseTaskTask = base.ExitState(postAction, recursive);
        var baseTask = await baseTaskTask;
        if (baseTask != null)
            await baseTask;
        return Task.FromResult(Task.CompletedTask);
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


