using Newtonsoft.Json.Linq;
namespace XStateNet;

/// <summary>
/// 
/// </summary>
public class NormalState : CompoundState
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
    public NormalState(string? name, string? parentName, string? stateMachineId) : base(name, parentName, stateMachineId)
    {
    }

    /// <summary>
    /// 
    /// </summary>
    public override async Task Start()
    {
        await base.Start();

        if (InitialStateName != null)
        {
            var initialState = GetState(InitialStateName);
            if (initialState != null)
            {
                await initialState.Start();
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="eventName"></param>
    /// <param name="transitionList"></param>
    public override void BuildTransitionList(string eventName, List<(CompoundState state, Transition transition, string eventName)> transitionList)
    {
        // For onError events, child states should handle first and prevent parent handling
        if (eventName == "onError")
        {
            // Check children first for onError (thread-safe read)
            var activeStateName = ActiveStateName; // Capture once
            if (activeStateName != null)
            {
                var initialCount = transitionList.Count;
                GetState(activeStateName)?.BuildTransitionList(eventName, transitionList);

                // If child added an onError transition, don't add parent's onError
                if (transitionList.Count > initialCount)
                {
                    return;
                }
            }

            // Only add parent's onError if no child handled it
            base.BuildTransitionList(eventName, transitionList);
        }
        else
        {
            // Normal evaluation order: parent first, then children
            base.BuildTransitionList(eventName, transitionList);

            var activeStateName = ActiveStateName; // Capture once for consistency
            if (activeStateName != null)
            {
                GetState(activeStateName)?.BuildTransitionList(eventName, transitionList);
            }
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
    public override async Task<Task> EntryState(bool postAction = false, bool recursive = false, HistoryType historyType = HistoryType.None, HistoryState? targetHistoryState = null)
    {

        string? nextActiveStateName = InitialStateName;
        var childHistoryType = historyType;

        // Check if this state has history enabled and should restore last active state
        if (HistorySubState != null && LastActiveStateName != null && targetHistoryState == null)
        {
            // This compound state has history, so restore the last active state
            nextActiveStateName = LastActiveStateName;
            childHistoryType = HistorySubState.HistoryType;
        }
        // History state stuff
        else if (targetHistoryState != null)
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
                var childTaskTask = GetState(nextActiveStateName)?.EntryState(postAction, recursive, childHistoryType, targetHistoryState);
                if (childTaskTask != null)
                {
                    var childTask = await childTaskTask;
                    if (childTask != null)
                        await childTask;
                }
            }

            var baseTaskTask = base.EntryState(postAction, recursive, historyType, targetHistoryState);
            var baseTask = await baseTaskTask;
            if (baseTask != null)
                await baseTask;
            return Task.FromResult(Task.CompletedTask);
        }
        else // pre action
        {
            var baseTaskTask = base.EntryState(postAction, recursive, historyType, targetHistoryState);
            var baseTask = await baseTaskTask;
            if (baseTask != null)
                await baseTask;

            if (recursive && nextActiveStateName != null)
            {
                var childTaskTask = GetState(nextActiveStateName)?.EntryState(postAction, recursive, childHistoryType, targetHistoryState);
                if (childTaskTask != null)
                {
                    var childTask = await childTaskTask;
                    if (childTask != null)
                        await childTask;
                }
            }

            return Task.FromResult(Task.CompletedTask);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="postAction">action while return the method</param>
    /// <param name="recursive">recursion to sub states</param>

    public override async Task<Task> ExitState(bool postAction = true, bool recursive = false)
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
                var childTaskTask = GetState(ActiveStateName)?.ExitState(postAction, recursive);
                if (childTaskTask != null)
                {
                    var childTask = await childTaskTask;
                    if (childTask != null)
                        await childTask;
                }
            }
            var baseTaskTask = base.ExitState(postAction, recursive);
            var baseTask = await baseTaskTask;
            if (baseTask != null)
                await baseTask;
        }
        else // pre action
        {
            var baseTaskTask = base.ExitState(postAction, recursive);
            var baseTask = await baseTaskTask;
            if (baseTask != null)
                await baseTask;

            if (recursive && ActiveStateName != null)
            {
                var childTaskTask = GetState(ActiveStateName)?.ExitState(postAction, recursive);
                if (childTaskTask != null)
                {
                    var childTask = await childTaskTask;
                    if (childTask != null)
                        await childTask;
                }
            }
        }

        return Task.FromResult(Task.CompletedTask);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="depth"></param>
    public override void PrintActiveStateTree(int depth)
    {
        Helper.WriteLine(depth * 2, $"- {Name?.Split('.').Last()}");

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
    public Parser_NormalState(string? machineId) : base(machineId) { }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="stateName"></param>
    /// <param name="parentName"></param>
    /// <param name="stateToken"></param>
    /// <returns></returns>
    public override StateNode Parse(string? stateName, string? parentName, JToken stateToken)
    {
        var initial = stateToken["initial"];

        var state = new NormalState(stateName, parentName, machineId)
        {
            InitialStateName = (initial != null) ? PerformanceOptimizations.BuildPath(stateName, initial.ToString()) : null,
        };

        state.InitialStateName = state.InitialStateName != null ? StateMachine.ResolveAbsolutePath(stateName, state.InitialStateName) : null;

        ParseActionsAndService(state, stateToken);

        return state;
    }
}