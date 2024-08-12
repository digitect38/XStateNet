using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Diagnostics;
namespace XStateNet;

/// <summary>
/// 
/// </summary>
public class NormalState : RealState
{
    // Important: LastActiveState should be defined here rather than inside history state because deep history state can have multiple last active states.

    public string? LastActiveStateName { set; get; }
    
    public NormalState? LastActiveState
    {
        get
        {
            if (LastActiveStateName != null)
            {
                return GetState(LastActiveStateName) as NormalState;
            }
            return null;
        }
    }

    public HistoryState? HistorySubState { set; get; }   // The parent of history state is always normal state

    /// <summary>
    /// 
    /// </summary>
    /// <param name="name"></param>
    /// <param name="parentName"></param>
    /// <param name="stateMachineId"></param>
    public NormalState(string name, string? parentName, string? stateMachineId) : base(name, parentName, stateMachineId)
    {
    }
    
    /// <summary>
    /// 
    /// </summary>
    public override void Start()
    {
        base.Start();

        if (InitialStateName != null)
        {
            GetState(InitialStateName)?.Start();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="eventName"></param>
    /// <param name="transitionList"></param>
    public override void BuildTransitionList(string eventName, List<(RealState state, Transition transition, string eventName)> transitionList)
    {
        // Evaluation should be top-down direction

        // parent first
        base.BuildTransitionList(eventName, transitionList);    

        // children next
        if (ActiveStateName != null)
        {
            GetState(ActiveStateName)?.BuildTransitionList(eventName, transitionList);
        }
    }
        
    /// <summary>
    /// 
    /// </summary>
    /// <param name="historyType"></param>
    public override void EntryState(HistoryType historyType = HistoryType.None)
    {
        base.EntryState(historyType);                
    }

    /// <summary>
    /// 
    /// </summary>
    public override void ExitState()
    {
        StateMachine.Log(">>>- State_Normal.ExitState: " + Name);
        
        if (Parent is NormalState)
        {
            ((NormalState)Parent).LastActiveStateName = Name;   // Record always for deep history case
        }

        base.ExitState();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="depth"></param>
    public override void PrintActiveStateTree(int depth)
    {
        Helper.WriteLine(depth * 2, $"- {Name.Split('.').Last()}");

        Debug.Assert(IsActive);

        if (ActiveStateName == null) return;
        
        
        var subState = GetState(ActiveStateName);

        if (subState != null)
            subState.PrintActiveStateTree(depth + 1);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="list"></param>
    public override void GetActiveSubStateNames(List<string> list)
    {
        Debug.Assert(IsActive);

        if (ActiveStateName == null) return;

        list.Add(ActiveStateName);

        var subState = GetState(ActiveStateName);

        if (subState != null)
            subState.GetActiveSubStateNames(list);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="collection"></param>
    public override void GetSouceSubStateCollection(ICollection<string> collection)
    {
        if (ActiveStateName != null)
        {
            collection.Add(ActiveStateName);
            ActiveState?.GetSouceSubStateCollection(collection);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="collection"></param>
    /// <param name="historyType"></param>
    public override void GetTargetSubStateCollection(ICollection<string> collection, HistoryType historyType = HistoryType.None)
    {

        string? targetStateName = null;

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

/// <summary>
/// 
/// </summary>
public class Parser_NormalState : Parser_RealState
{
    public Parser_NormalState(string machineId) : base(machineId) { }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="stateName"></param>
    /// <param name="parentName"></param>
    /// <param name="stateToken"></param>
    /// <returns></returns>
    public override StateBase Parse(string stateName, string? parentName, JToken stateToken)
    {
        var initial = stateToken["initial"];

        var state = new NormalState(stateName, parentName, machineId)
        {            
            InitialStateName = (initial != null) ? stateName + "." + initial.ToString() : null,
        };

        state.InitialStateName = state.InitialStateName != null ? StateMachine.ResolveAbsolutePath(stateName, state.InitialStateName) : null;

        state.EntryActions = Parser_Action.ParseActions("entry", StateMachine?.ActionMap, stateToken);
        state.ExitActions = Parser_Action.ParseActions("exit", StateMachine?.ActionMap, stateToken);

        return state;
    }
}