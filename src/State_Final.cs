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

    public override void BuildTransitionList(string eventName, List<(RealState state, Transition transition, string eventName)> transitionList)
    {
        // parent first evaluation (is not the order exit/entry sequence)
        base.BuildTransitionList(eventName, transitionList);
    }

    public override void EntryState(HistoryType historyType = HistoryType.None)
    {
        base.EntryState(historyType);
        Parent?.OnDone();
    }

    public override void ExitState()
    {
        base.ExitState();
    }

    public override void GetActiveSubStateNames(List<string> list)
    {
        foreach (var subStateName in SubStateNames)
        {
            list.Add(subStateName);

            var subState =  GetState(subStateName);

            if (subState != null)
            {
                subState.GetActiveSubStateNames(list);
            }
        }
    }
    
    public override void PrintActiveStateTree(int depth)
    {
        Helper.WriteLine(depth * 2, $"- {Name.Split('.').Last()}");
    }
}

public class Parser_FinalState : Parser_RealState
{
    public Parser_FinalState(string machineId) : base(machineId) { }

    public override StateBase Parse(string stateName, string? parentName, JToken stateToken)
    {
        var state = new FinalState(stateName, parentName, machineId)
        {
        };
        
        if(StateMachine == null) throw new Exception("StateMachine is null");
        state.EntryActions = Parser_Action.ParseActions("entry", StateMachine.ActionMap, stateToken);
        state.ExitActions = Parser_Action.ParseActions("exit", StateMachine.ActionMap, stateToken);
        
        return state;
    }
}


