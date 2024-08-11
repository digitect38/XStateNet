using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Diagnostics;
namespace XStateNet;

public class NormalState : RealState
{
    // Important: LastActiveState should be defined here rather than inside history state because deep history state can have multiple last active states.

    public string LastActiveStateName { set; get; }
    public NormalState LastActiveState => GetState(LastActiveStateName) as NormalState;

    public HistoryState HistorySubState { set; get; }   // The parent of history state is always normal state

    public NormalState(string name, string? parentName, string stateMachineId) : base(name, parentName, stateMachineId)
    {
    }
    
    public override void Start()
    {
        base.Start();

        if (InitialStateName != null)
        {
            GetState(InitialStateName)?.Start();
        }
    }

    public override void BuildTransitionList(string eventName, List<(RealState state, Transition transition, string eventName)> transitionList)
    {
        // Evaluation should be to-down direction
        // parent first
        base.BuildTransitionList(eventName, transitionList);    

        // then sub state
        if (ActiveStateName != null)
        {
            GetState(ActiveStateName)?.BuildTransitionList(eventName, transitionList);
        }
    }
        
    public override void EntryState(HistoryType historyType = HistoryType.None)
    {
        base.EntryState(historyType);                
    }

    public override void ExitState()
    {
        StateMachine.Log(">>>- State_Normal.ExitState: " + Name);
        
        if (Parent is NormalState)
        {
            ((NormalState)Parent).LastActiveStateName = Name;   // Record always for deep history case
        }

        base.ExitState();
    }

    public override void PrintActiveStateTree(int depth)
    {
        Helper.WriteLine(depth * 2, $"- {Name.Split('.').Last()}");

        Debug.Assert(IsActive);

        if (ActiveStateName == null) return;
        
        
        var subState = GetState(ActiveStateName);

        if (subState != null)
            subState.PrintActiveStateTree(depth + 1);
    }

    public override void GetActiveSubStateNames(List<string> list)
    {
        Debug.Assert(IsActive);

        if (ActiveStateName == null) return;

        list.Add(ActiveStateName);

        var subState = GetState(ActiveStateName);

        if (subState != null)
            subState.GetActiveSubStateNames(list);
    }

    public override void GetSouceSubStateCollection(ICollection<string> collection)
    {
        if (ActiveStateName != null)
        {
            collection.Add(ActiveStateName);
            ActiveState?.GetSouceSubStateCollection(collection);
        }
    }

    public override void GetTargetSubStateCollection(ICollection<string> collection, HistoryType historyType = HistoryType.None)
    {

        string targetStateName = null;

        if (historyType == HistoryType.None)
        {
            if (InitialStateName == null) return;
            targetStateName =  InitialStateName;
        }
        else
        {
            if (LastActiveStateName == null) return;
            targetStateName = LastActiveStateName;
        }

        var state = GetState(targetStateName) as NormalState;

        collection.Add(targetStateName);

        if (historyType == HistoryType.Deep)
        {
            state?.GetTargetSubStateCollection(collection, historyType);
        }
        else
        {
            state?.GetTargetSubStateCollection(collection, HistoryType.None);
        }
    }
}


public class Parser_NormalState : Parser_RealState
{
    public Parser_NormalState(string machineId) : base(machineId) { }

    public override StateBase Parse(string stateName, string? parentName, JToken stateToken)
    {

        var state = new NormalState(stateName, parentName, machineId)
        {
            InitialStateName = (stateToken["initial"] != null) ? stateName + "." + stateToken["initial"].ToString() : null,
        };

        state.InitialStateName = state.InitialStateName != null ? StateMachine.ResolveAbsolutePath(stateName, state.InitialStateName) : null;

        state.EntryActions = Parser_Action.ParseActions(state, "entry", StateMachine.ActionMap, stateToken);
        state.ExitActions = Parser_Action.ParseActions(state, "exit", StateMachine.ActionMap, stateToken);

        return state;
    }
}