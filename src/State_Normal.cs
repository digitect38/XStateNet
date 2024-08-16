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

    public override void OnDone()
    {
        IsDone = true;
        Parent?.OnDone();
    }

    public bool CompareHistoryStateIfExist(HistoryState? historyState)
    {
        if (StateMachine == null) throw new Exception("StateMachine is null");
        if (historyState == null) throw new Exception("History state is null");

        if (HistorySubState?.Name == historyState.Name)
            return true;

        return false;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="postAction"></param>
    /// <param name="recursive"></param>
    /// <param name="historyType"></param>
    /// <returns></returns>
    public override Task EntryState(bool postAction = false, bool recursive = false, HistoryType historyType = HistoryType.None, HistoryState? targetHistoryState = null)
    {

        string? nextActiveStateName = InitialStateName;
        var childHistoryType =  historyType;
        
        // History state stuff
        if (targetHistoryState != null)
        {
            // if I have a history state and it's name is same as the history state name or historyType is shallow or deep, then I should go to the last active state
            // otherwise, I should go to the initial state.
            if (HistorySubState?.Name == targetHistoryState.Name || historyType != HistoryType.None)
            {
                nextActiveStateName = LastActiveStateName;
                historyType = targetHistoryState.HistoryType;
            }
            else
            {
                nextActiveStateName = InitialStateName;
            }
        }

        childHistoryType = targetHistoryState?.HistoryType == HistoryType.Deep ? HistoryType.Deep : HistoryType.None;
        

        if (postAction)
        {
            if (recursive && nextActiveStateName != null)
            {

                GetState(nextActiveStateName)?.EntryState(postAction, recursive, childHistoryType, targetHistoryState);
            }

            base.EntryState(postAction, recursive, historyType, targetHistoryState);
        }
        else // pre action
        {
            base.EntryState(postAction, recursive, historyType, targetHistoryState);

            if (recursive && nextActiveStateName != null)
            {
                GetState(nextActiveStateName)?.EntryState(postAction, recursive, childHistoryType, targetHistoryState);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="postAction">action while return the method</param>
    /// <param name="recursive">recursion to sub states</param>

    public override Task ExitState(bool postAction = true, bool recursive = false)
    {
        //StateMachine.Log($">>>- {Name}.OnExit()");

        if (Parent is NormalState)
        {
            ((NormalState)Parent).LastActiveStateName = Name;   // Record always for deep history case
        }

        if (postAction)
        {
            if (recursive && ActiveStateName != null)
            {
                GetState(ActiveStateName)?.ExitState(postAction, recursive);
            }
            base.ExitState(postAction, recursive);
        }
        else // pre action
        {
            base.ExitState(postAction, recursive);

            if (recursive && ActiveStateName != null)
            {
                GetState(ActiveStateName)?.ExitState(postAction, recursive);
            }
        }

        return Task.CompletedTask;
    }



    /// <summary>
    /// 
    /// </summary>
    /// <param name="depth"></param>
    public override void PrintActiveStateTree(int depth)
    {
        Helper.WriteLine(depth * 2, $"- {Name.Split('.').Last()}");

        //Debug.Assert(IsActive);

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
        // Debug.Assert(IsActive);

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
    public override void GetSouceSubStateCollection(ICollection<string> collection, bool singleBrancePath = false)
    {
        if (ActiveStateName != null)
        {
            collection.Add(ActiveStateName);
            ActiveState?.GetSouceSubStateCollection(collection, singleBrancePath);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="collection"></param>
    /// <param name="historyType"></param>
    public override void GetTargetSubStateCollection(ICollection<string> collection, bool singleBranchPath, HistoryType historyType = HistoryType.None)
    {

        string? nextActiveChildStateName = null;

        //History type designation is triggered to a non-history state only when the target state is a history state.During entry propagation,
        //if the history state is shallow, it transitions to a normal state, and if the history state is deep, it continues to propagate as a history state.

        if (historyType == HistoryType.None)
        {
            if (InitialStateName == null) return;
            nextActiveChildStateName = InitialStateName;
        }
        else
        {
            if (LastActiveStateName == null) return;
            nextActiveChildStateName = LastActiveStateName;
        }

        var state = GetState(nextActiveChildStateName);

        collection.Add(nextActiveChildStateName);

        if (historyType == HistoryType.Deep)
        {
            state?.GetTargetSubStateCollection(collection, singleBranchPath, historyType);
        }
        else
        {
            state?.GetTargetSubStateCollection(collection, singleBranchPath, HistoryType.None);
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