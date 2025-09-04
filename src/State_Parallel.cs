using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using static System.Runtime.InteropServices.JavaScript.JSType;
namespace XStateNet;


public class ParallelState : CompoundState
{
    public ParallelState(string name, string? parentName, string? stateMachineId) : base(name, parentName, stateMachineId)
    {
    }

    public override void Start()
    {
        base.Start();

        var tasks = SubStateNames.Select(subStateName => Task.Run(() =>
        {
            GetState(subStateName)?.Start();
        })).ToArray();

        Task.WaitAll(tasks);
    }

    public override void BuildTransitionList(string eventName, List<(CompoundState state, Transition transition, string eventName)> transitionList)
    {
        // parent first evaluation (is not the order exit/entry sequence)
        base.BuildTransitionList(eventName, transitionList);

        // children next evaluation

#if true // serial way - KEEP SERIAL to avoid race conditions with shared list
        foreach (string subStateName in SubStateNames) {
            GetState(subStateName)?.BuildTransitionList(eventName, transitionList);
        }
#else   // parallel way - UNSAFE: List is not thread-safe
        SubStateNames.AsParallel().ForAll(
            subStateName => {
                GetState(subStateName)?.BuildTransitionList(eventName, transitionList);
            }
        );
#endif

    }
    
    // Helper method for base class call
    internal void BuildTransitionListBase(string eventName, List<(CompoundState state, Transition transition, string eventName)> transitionList)
    {
        base.BuildTransitionList(eventName, transitionList);
    }

    /// <summary>
    /// 
    /// </summary>
    public override void OnDone()
    {
        bool done = true;

        foreach(string subStateName in SubStateNames)
        {
            var state = GetState(subStateName);
            done = done && state.IsDone;
        }

        if (done)
        {
            IsDone = true;
            Parent?.OnDone();

            if (OnDoneTransition != null)
            {
                StateMachine?.transitionExecutor.Execute(OnDoneTransition, $"onDone");
            }
            //Parent?.OnDone();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="postAction"></param>
    /// <param name="recursive"></param>
    /// <param name="historyType"></param>
    /// <param name="targetHistoryState"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public override async Task EntryState(bool postAction = false, bool recursive = false, HistoryType historyType = HistoryType.None, HistoryState? targetHistoryState = null)
    {
        //StateMachine.Log(">>>- State_Parallel.EntryState: " + Name);
        if (StateMachine == null) throw new Exception("StateMachine is null");

        if (postAction)
        {
            if (recursive && SubStateNames != null)
            {
                // Only use parallel if we have enough items to benefit
                if (PerformanceOptimizations.ShouldUseParallel(SubStateNames))
                {
                    ConcurrentBag<Task> tasks = new ConcurrentBag<Task>();
                    Parallel.ForEach(SubStateNames, subStateName =>
                    {
                        var subState = StateMachine.GetState(subStateName) as CompoundState;
                        var task = subState?.EntryState(postAction, recursive, historyType);
                        if (task != null)
                        {
                            tasks.Add(task);
                        }
                    });
                    await Task.WhenAll(tasks);
                }
                else
                {
                    // Serial execution for small collections
                    var tasks = new List<Task>();
                    foreach (var subStateName in SubStateNames)
                    {
                        var subState = StateMachine.GetState(subStateName) as CompoundState;
                        var task = subState?.EntryState(postAction, recursive, historyType);
                        if (task != null)
                        {
                            tasks.Add(task);
                        }
                    }
                    if (tasks.Count > 0)
                        await Task.WhenAll(tasks);
                }
            }

            await base.EntryState(postAction, recursive, historyType);
        }
        else
        {
            await base.EntryState(postAction, recursive, historyType);

            if (recursive && SubStateNames != null)
            {
                // Only use parallel if we have enough items to benefit
                if (PerformanceOptimizations.ShouldUseParallel(SubStateNames))
                {
                    ConcurrentBag<Task> tasks = new ConcurrentBag<Task>();
                    Parallel.ForEach(SubStateNames, subStateName =>
                    {
                        var subState = StateMachine.GetState(subStateName) as CompoundState;
                        var task = subState?.EntryState(postAction, recursive, historyType);
                        if (task != null)
                        {
                            tasks.Add(task);
                        }
                    });
                    await Task.WhenAll(tasks);
                }
                else
                {
                    // Serial execution for small collections
                    var tasks = new List<Task>();
                    foreach (var subStateName in SubStateNames)
                    {
                        var subState = StateMachine.GetState(subStateName) as CompoundState;
                        var task = subState?.EntryState(postAction, recursive, historyType);
                        if (task != null)
                        {
                            tasks.Add(task);
                        }
                    }
                    if (tasks.Count > 0)
                        await Task.WhenAll(tasks);
                }
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="postAction">action while return the method</param>
    /// <param name="recursive">recursion to sub states</param>

    public override async Task ExitState(bool postAction = true, bool recursive = false)
    {
        if (postAction)
        {
            if (recursive && SubStateNames != null)
            {
                // Only use parallel if we have enough items to benefit
                if (PerformanceOptimizations.ShouldUseParallel(SubStateNames))
                {
                    ConcurrentBag<Task> tasks = new ConcurrentBag<Task>();
                    Parallel.ForEach(SubStateNames, subStateName =>
                    {
                        var task = GetState(subStateName)?.ExitState(postAction, recursive);
                        if (task != null)
                        {
                            tasks.Add(task);
                        }
                    });
                    await Task.WhenAll(tasks);
                }
                else
                {
                    // Serial execution for small collections
                    var tasks = new List<Task>();
                    foreach (var subStateName in SubStateNames)
                    {
                        var task = GetState(subStateName)?.ExitState(postAction, recursive);
                        if (task != null)
                        {
                            tasks.Add(task);
                        }
                    }
                    if (tasks.Count > 0)
                        await Task.WhenAll(tasks);
                }
            }

            await base.ExitState(postAction, recursive);
        }
        else // pre action
        {
            await base.ExitState(postAction, recursive);

            if (recursive && SubStateNames != null)
            {
                // Only use parallel if we have enough items to benefit
                if (PerformanceOptimizations.ShouldUseParallel(SubStateNames))
                {
                    ConcurrentBag<Task> tasks = new ConcurrentBag<Task>();
                    Parallel.ForEach(SubStateNames, subStateName =>
                    {
                        var task = GetState(subStateName)?.ExitState(postAction, recursive);
                        if (task != null)
                        {
                            tasks.Add(task);
                        }
                    });
                    await Task.WhenAll(tasks);
                }
                else
                {
                    // Serial execution for small collections
                    var tasks = new List<Task>();
                    foreach (var subStateName in SubStateNames)
                    {
                        var task = GetState(subStateName)?.ExitState(postAction, recursive);
                        if (task != null)
                        {
                            tasks.Add(task);
                        }
                    }
                    if (tasks.Count > 0)
                        await Task.WhenAll(tasks);
                }
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="list"></param>
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

    /// <summary>
    /// 
    /// </summary>
    /// <param name="collection"></param>
    /// <param name="singleBrancePath"></param>
    public override void GetSouceSubStateCollection(ICollection<string> collection, bool singleBrancePath = false)
    {
        if (singleBrancePath) 
        {
            string subState = SubStateNames[0];
            collection.Add(subState);
            GetState(subState)?.GetSouceSubStateCollection(collection, singleBrancePath);
        }
        else
        {
            foreach (string subState in SubStateNames)
            {
                collection.Add(subState);
                GetState(subState)?.GetSouceSubStateCollection(collection, singleBrancePath);
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="collection"></param>
    /// <param name="singleBranchPath"></param>
    /// <param name="hist"></param>
    public override void GetTargetSubStateCollection(ICollection<string> collection, bool singleBranchPath, HistoryType hist = HistoryType.None)
    {
        if(singleBranchPath && SubStateNames.Count > 0)
        {
            string subState = SubStateNames[0];
            collection.Add(subState);
            GetState(subState)?.GetTargetSubStateCollection(collection, singleBranchPath);
        }
        else
        {
            foreach (string subState in SubStateNames)
            {
                collection.Add(subState);
                GetState(subState)?.GetTargetSubStateCollection(collection, singleBranchPath);
            }
        }
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="state"></param>
    /// <returns></returns>
    private List<CompoundState> GetInitialStates(CompoundState state)
    {
        var initialStates = new List<CompoundState>();
        if (state.InitialStateName != null)
        {
            var initialSubState = GetState(state.InitialStateName) as CompoundState;
            if (initialSubState != null)
            {
                initialStates.Add(initialSubState);
                initialStates.AddRange(GetInitialStates(initialSubState));
            }
        }
        return initialStates;
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="depth"></param>
    public override void PrintActiveStateTree(int depth)
    {
        Helper.WriteLine(depth * 2, $"- {Name?.Split('.').Last()}");

        foreach (var currentStateName in SubStateNames)
        {
            GetState(currentStateName)?.PrintActiveStateTree(depth + 1);
        }
    }
}

/// <summary>
/// 
/// </summary>
public class Parser_ParallelState : Parser_RealState
{
    public Parser_ParallelState(string? machineId) : base(machineId) { }

    public override StateNode Parse(string stateName, string? parentName, JToken stateToken)
    {
        var state = new ParallelState(stateName, parentName, machineId)
        {
        };        

        ParseActionsAndService(state, stateToken);

        return state;
    }
}


