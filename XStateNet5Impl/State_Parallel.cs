using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
namespace XStateNet;


public class ParallelState : CompoundState
{
    public ParallelState(string name, string? parentName, string? stateMachineId) : base(name, parentName, stateMachineId)
    {
    }

    public override async Task StartAsync()
    {
        await base.StartAsync();

        // Start all parallel regions synchronously to ensure they're initialized
        // before returning from StartAsync()
        // Each parallel region should start from its initial state
        foreach (var subStateName in SubStateNames)
        {
            var subState = GetState(subStateName);
            if (subState != null)
            {
                // Mark the substate as active first
                subState.IsActive = true;

                // If it's a compound state with an initial state, start that
                if (subState is CompoundState compoundState && compoundState.InitialStateName != null)
                {
                    var initialState = GetState(compoundState.InitialStateName);
                    if (initialState != null)
                    {
                        await initialState.StartAsync();
                    }
                }
                else
                {
                    // Otherwise just start the substate directly
                    await subState.StartAsync();
                }
            }
        }

        // Check for always transitions after full state tree entry
        // This ensures eventless transitions fire from all active parallel regions
        StateMachine?.CheckAlwaysTransitions();
    }

    public override void BuildTransitionList(string eventName, List<(CompoundState state, Transition transition, string eventName)> transitionList)
    {
        // parent first evaluation (is not the order exit/entry sequence)
        base.BuildTransitionList(eventName, transitionList);

        // children next evaluation
        foreach (string subStateName in SubStateNames)
        {
            GetState(subStateName)?.BuildTransitionList(eventName, transitionList);
        }

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

        foreach (string subStateName in SubStateNames)
        {
            var state = GetState(subStateName);
            done = done && (state?.IsDone ?? false);
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
    public override async Task<Task> EntryState(bool postAction = false, bool recursive = false, HistoryType historyType = HistoryType.None, HistoryState? targetHistoryState = null)
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
                    // Use ConcurrentBag for thread-safe collection
                    var taskBag = new ConcurrentBag<Task>();

                    await Parallel.ForEachAsync(SubStateNames, async (subStateName, ct) =>
                    {
                        var subState = StateMachine.GetState(subStateName) as CompoundState;
                        var task = await subState?.EntryState(postAction, recursive, historyType);
                        if (task != null)
                        {
                            taskBag.Add(task);
                        }
                    });


                    // Wait for all collected tasks
                    if (!taskBag.IsEmpty)
                        await Task.WhenAll(taskBag);
                }
                else
                {
                    // Serial execution for small collections
                    var tasks = new List<Task>(SubStateNames.Count);
                    foreach (var subStateName in SubStateNames)
                    {
                        var subState = StateMachine.GetState(subStateName) as CompoundState;
                        var task = await subState?.EntryState(postAction, recursive, historyType);
                        if (task != null)
                        {
                            tasks.Add(task);
                        }
                    }
                    if (tasks.Count > 0)
                        await Task.WhenAll(tasks);
                }
            }

            return await base.EntryState(postAction, recursive, historyType);
        }
        else
        {
            var baseTask = await base.EntryState(postAction, recursive, historyType);

            if (recursive && SubStateNames != null)
            {
                // Only use parallel if we have enough items to benefit
                //if (PerformanceOptimizations.ShouldUseParallel(SubStateNames))
                {
                    // Use ConcurrentBag for thread-safe collection
                    var taskBag = new ConcurrentBag<Task>();

                    await Parallel.ForEachAsync(SubStateNames, async (subStateName, ct) =>
                    {
                        var subState = StateMachine.GetState(subStateName) as CompoundState;
                        var task = await subState?.EntryState(postAction, recursive, historyType);
                        if (task != null)
                        {
                            taskBag.Add(task);
                        }
                    });

                    // Wait for all collected tasks
                    if (!taskBag.IsEmpty)
                        await Task.WhenAll(taskBag);
                }
                /*
                else
                {
                    // Serial execution for small collections
                    var tasks = new List<Task>(SubStateNames.Count);
                    foreach (var subStateName in SubStateNames)
                    {
                        var subState = StateMachine.GetState(subStateName) as CompoundState;
                        var task = await subState?.EntryState(postAction, recursive, historyType);
                        if (task != null)
                        {
                            tasks.Add(task);
                        }
                    }
                    if (tasks.Count > 0)
                        await Task.WhenAll(tasks);
                }*/
            }

            return baseTask;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="postAction">action while return the method</param>
    /// <param name="recursive">recursion to sub states</param>

    public override async Task<Task> ExitState(bool postAction = true, bool recursive = false)
    {
        if (postAction)
        {
            if (recursive && SubStateNames != null)
            {
                // Only use parallel if we have enough items to benefit
                if (PerformanceOptimizations.ShouldUseParallel(SubStateNames))
                {
                    // Pre-allocate array for better performance
                    var tasks = new Task?[SubStateNames.Count];
                    var index = 0;
                    await Parallel.ForEachAsync(SubStateNames, async (subStateName, ct) =>
                    {
                        var task = await GetState(subStateName)?.ExitState(postAction, recursive);
                        if (task != null)
                        {
                            tasks[Interlocked.Increment(ref index) - 1] = task;
                        }
                    });
                    // Only wait for non-null tasks
                    var validTasks = tasks.Where(t => t != null).Cast<Task>().ToArray();
                    if (validTasks.Length > 0)
                        await Task.WhenAll(validTasks);
                }
                else
                {
                    // Serial execution for small collections
                    var tasks = new List<Task>(SubStateNames.Count);
                    foreach (var subStateName in SubStateNames)
                    {
                        var task = await GetState(subStateName)?.ExitState(postAction, recursive);
                        if (task != null)
                        {
                            tasks.Add(task);
                        }
                    }
                    if (tasks.Count > 0)
                        await Task.WhenAll(tasks);
                }
            }

            return await base.ExitState(postAction, recursive);
        }
        else // pre action
        {
            var baseTask = await base.ExitState(postAction, recursive);

            if (recursive && SubStateNames != null)
            {
                // Only use parallel if we have enough items to benefit
                if (PerformanceOptimizations.ShouldUseParallel(SubStateNames))
                {
                    // Pre-allocate array for better performance
                    var tasks = new Task?[SubStateNames.Count];
                    var index = 0;
                    await Parallel.ForEachAsync(SubStateNames, async (subStateName, ct) =>
                    {
                        var task = await GetState(subStateName)?.ExitState(postAction, recursive);
                        if (task != null)
                        {
                            tasks[Interlocked.Increment(ref index) - 1] = task;
                        }
                    });
                    // Only wait for non-null tasks
                    var validTasks = tasks.Where(t => t != null).Cast<Task>().ToArray();
                    if (validTasks.Length > 0)
                        await Task.WhenAll(validTasks);
                }
                else
                {
                    // Serial execution for small collections
                    var tasks = new List<Task>(SubStateNames.Count);
                    foreach (var subStateName in SubStateNames)
                    {
                        var task = await GetState(subStateName)?.ExitState(postAction, recursive);
                        if (task != null)
                        {
                            tasks.Add(task);
                        }
                    }
                    if (tasks.Count > 0)
                        await Task.WhenAll(tasks);
                }
            }
            return baseTask;
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

            var subState = GetState(subStateName);

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
        if (singleBranchPath && SubStateNames.Count > 0)
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


