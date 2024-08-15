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

    #region regacy transition
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
    #endregion


    /// <summary>
    /// 
    /// </summary>
    /// <param name="postAction"></param>
    /// <param name="recursive"></param>
    /// <param name="historyType"></param>
    /// <returns></returns>
    public override Task EntryState(bool postAction = false, bool recursive = false, HistoryType historyType = HistoryType.None)
    {
        if (postAction)
        {
            if (recursive && InitialStateName != null)
            {
                GetState(InitialStateName)?.EntryState(postAction, recursive, historyType);
            }
            base.EntryState(postAction, recursive);
        }
        else // pre action
        {
            base.EntryState(postAction, recursive);

            if (recursive && InitialStateName != null)
            {
                GetState(InitialStateName)?.EntryState(postAction, recursive, historyType);
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

        string? targetStateName = null;

        //History type designation is triggered to a non-history state only when the target state is a history state.During entry propagation,
        //if the history state is shallow, it transitions to a normal state, and if the history state is deep, it continues to propagate as a history state.

        if (historyType == HistoryType.None)
        {
            if (InitialStateName == null) return;
            targetStateName = InitialStateName;
        }
        else
        {
            if (LastActiveStateName == null) return;
            targetStateName = LastActiveStateName;
        }

        var state = GetState(targetStateName);

        collection.Add(targetStateName);

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