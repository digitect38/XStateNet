using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using System.Threading;

namespace XStateNet;


public abstract class RealState : StateNode
{
    public bool IsInitial => Parent != null && Parent.InitialStateName == Name;
    public bool IsActive { set; get; }

    public TransitionMap OnTransitionMap { get; set; } = new();

    public AfterTransition? AfterTransition { get; set; }   // Added for after transitions
    public AlwaysTransition? AlwaysTransition { get; set; } // Added for always transitions
    public OnDoneTransition? OnDoneTransition { get; set; } // Added for onDone transitions

    public List<NamedAction>? EntryActions { get; set; }
    public List<NamedAction>? ExitActions { get; set; }
    public NamedService? Service { get; set; }
    public NamedDelay? Delay { get; set; }
    public List<NamedActivity>? Activities { get; set; }

    protected System.Timers.Timer? _afterTransitionTimer;
    private readonly ConcurrentDictionary<string, Action> _activeActivityCleanups = new();
    private CancellationTokenSource? _activityCancellationSource;

    public RealState(string? name, string? parentName, string? stateMachineId) 
        : base(name, parentName, stateMachineId)
    {
    }
    
    /// <summary>
    /// Clean up the after transition timer with proper exception handling
    /// </summary>
    public void CleanupAfterTimer()
    {
        var timer = _afterTransitionTimer;
        if (timer != null)
        {
            _afterTransitionTimer = null; // Clear reference first
            try
            {
                timer.Stop();
                timer.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error disposing timer for state {Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Start all activities for this state
    /// </summary>
    private void StartActivities()
    {
        // Dispose any existing cancellation source first
        _activityCancellationSource?.Dispose();
        _activityCancellationSource = new CancellationTokenSource();
        if (Activities != null && StateMachine != null && StateMachine.ActivityMap != null)
        {
            foreach (var activity in Activities)
            {
                try
                {
                    // Get the activity from the activity map
                    if (StateMachine?.ActivityMap?.TryGetValue(activity.Name, out var namedActivity) == true)
                    {
                        // Execute the activity and get the cleanup function
                        var cleanup = namedActivity.Activity(StateMachine, _activityCancellationSource.Token);

                        // Store the cleanup function for later
                        if (cleanup != null)
                        {
                            _activeActivityCleanups[activity.Name] = cleanup;
                        }

                        // Notify activity started
                        StateMachine.RaiseActivityStarted(activity.Name, Name);
                    }
                }
                catch (Exception ex)
                {
                    // Log error but don't fail state entry
                    Logger.Error($"Failed to start activity '{activity.Name}': {ex.Message}");

                    // Store error in context
                    if (StateMachine?.ContextMap != null)
                    {
                        StateMachine.ContextMap[$"_activityError_{activity.Name}"] = ex.Message;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Stop all activities for this state
    /// </summary>
    private void StopActivities()
    {
        var cancellationSource = _activityCancellationSource;
        if (cancellationSource == null) return;

        // Clear reference first to prevent re-entry
        _activityCancellationSource = null;

        try
        {
            // Cancel all activities
            cancellationSource.Cancel();

            // Execute cleanup functions with timeout
            var cleanupTasks = new List<Task>();
            foreach (var cleanup in _activeActivityCleanups)
            {
                cleanupTasks.Add(Task.Run(() =>
                {
                    try
                    {
                        cleanup.Value?.Invoke();
                        // Notify activity stopped
                        StateMachine?.RaiseActivityStopped(cleanup.Key, Name);
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue cleanup
                        Logger.Error($"Failed to stop activity '{cleanup.Key}': {ex.Message}");
                    }
                }));
            }

            // Wait for all cleanups with timeout (max 5 seconds)
            Task.WhenAll(cleanupTasks).Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Logger.Error($"Error during activity cleanup: {ex.Message}");
        }
        finally
        {
            // Always clear and dispose
            _activeActivityCleanups.Clear();

            try
            {
                cancellationSource.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error disposing cancellation source: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="postAction">action while return the method</param>
    /// <param name="recursive">recursion to sub states</param>

    public virtual Task ExitState(bool postAction = true, bool recursive = false)
    {
        //StateMachine.Log(">>>- State_Real.ExitState: " + Name);

        // Atomic state deactivation
        IsActive = false;

        if (Parent != null)
        {
            Parent.ActiveStateName = null;
        }
        
        // Cancel any active after transition timer
        CleanupAfterTimer();

        // Stop all activities for this state
        StopActivities();

        // Cancel any active services for this state
        if (StateMachine != null && StateMachine.serviceInvoker != null && this is CompoundState compoundState)
        {
            StateMachine.serviceInvoker.CancelService(compoundState);
        }

        if (StateMachine != null && ExitActions != null)
        {
            foreach (var action in ExitActions)
            {
                try
                {
                    // Notify action execution
                    StateMachine?.RaiseActionExecuted(action.Name, Name);
                    if (StateMachine != null)
                    {
                        action.Action?.Invoke(StateMachine);
                    }
                }
                catch (Exception ex)
                {
                    // Store error context and rethrow
                    if (StateMachine?.ContextMap != null)
                    {
                        StateMachine.ContextMap["_error"] = ex;
                        StateMachine.ContextMap["_lastError"] = ex;  // For backward compatibility
                        StateMachine.ContextMap["_errorType"] = ex.GetType().Name;
                        StateMachine.ContextMap["_errorMessage"] = ex.Message;
                    }
                    throw;  // Rethrow to be handled at higher level
                }
            }
        }

        return Task.CompletedTask;
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="historyType"></param>
    public virtual Task EntryState(bool postAction = false, bool recursive = false, HistoryType historyType = HistoryType.None, HistoryState? targetHistoryState = null)
    {
        //StateMachine.Log(">>>- State_Real.EntryState: " + Name);

        // Atomic state activation
        IsActive = true;

        if (Parent != null)
        {
            Parent.ActiveStateName = Name;
        }

        if (StateMachine != null)
        {
            // Execute entry actions with error handling
            if (EntryActions != null)
            {
                foreach (var action in EntryActions)
                {
                    try
                    {
                        if (StateMachine != null)
                        {
                            // Notify action execution
                            StateMachine.RaiseActionExecuted(action.Name, Name);
                            action.Action(StateMachine);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Store error context and rethrow for higher level handling
                        if (StateMachine?.ContextMap != null)
                        {
                            StateMachine.ContextMap["_error"] = ex;
                            StateMachine.ContextMap["_lastError"] = ex;  // For backward compatibility
                            StateMachine.ContextMap["_errorType"] = ex.GetType().Name;
                            StateMachine.ContextMap["_errorMessage"] = ex.Message;
                        }
                        throw;  // Rethrow to be handled by TransitDown
                    }
                }
            }

            // Start activities for this state
            StartActivities();

            // Invoke service using ServiceInvoker for proper onDone/onError handling
            if (Service != null && this is CompoundState compoundState && StateMachine?.serviceInvoker != null)
            {
                _ = StateMachine.serviceInvoker.InvokeService(compoundState, Service.Name, Service);
            }
        }

        return Task.CompletedTask;
    }


    //public abstract void BuildTransitionList(string eventName, List<(CompoundState state, Transition transition, string eventName)> transitionList);
    public abstract void PrintActiveStateTree(int depth);
}
/// <summary>
/// Notes : 
/// Normal and Parallel states can be active or not. 
/// Normal state can have an initial state.
/// Parallel state can not have initial state. But have all as active states
/// Parallel state is defined here, as the state has multiple parallel sub states.
/// </summary>
public abstract class CompoundState : RealState
{
    // Thread-safe state management
    protected readonly ThreadSafeStateInfo _stateInfo = new ThreadSafeStateInfo();

    public List<string> SubStateNames { get; set; }         // state 의 current sub state 들..

    public string? InitialStateName { get; set; }

    // Thread-safe properties using atomic state management
    public string? ActiveStateName
    {
        get => _stateInfo.ActiveStateName;
        set => _stateInfo.UpdateState(_stateInfo.IsActive, value);
    }

    public CompoundState? ActiveState => ActiveStateName != null ? GetState(ActiveStateName!) : null;

    public bool IsParallel => typeof(ParallelState) == this.GetType();

    public new CompoundState? GetState(string stateName)
    {
        return StateMachine?.GetState(stateName) as CompoundState;
    }

    public CompoundState(string? name, string? parentName, string? stateMachineId) : base(name, parentName, stateMachineId)
    {
        SubStateNames = new List<string>();
        EntryActions = new List<NamedAction>();
        ExitActions = new List<NamedAction>();
        OnTransitionMap = new TransitionMap();
        AfterTransition = null;
        AlwaysTransition = null;
        OnDoneTransition = null;
    }

    public virtual void Start()
    {
        EntryState();
    }

    public bool IsDone {set; get; }= false;             // If the state is done, it will not be active anymore.

    public abstract void OnDone();  // for final state
    
    public abstract void GetActiveSubStateNames(List<string> list);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="collection"></param>
    public void GetSuperStateCollection(ICollection<string> collection)
    {
        if (Name == null) throw new Exception("Name is null");

        collection.Add(Name);
        
        var super = Parent;

        if (super != null && super.GetType() != typeof(ParallelState))
        {
            super.GetSuperStateCollection(collection);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="postAction">action while return the method</param>
    /// <param name="recursive">recursion to sub states</param>

    public override Task ExitState(bool postAction = true, bool recursive = false)
    {
        //StateMachine.Log(">>>- State_Real.ExitState: " + Name);
        if (StateMachine == null)
        {
            System.Diagnostics.Debug.WriteLine($"Warning: StateMachine is null for state {Name} during ExitState");
            return Task.CompletedTask;
        }

        IsDone = false; // for next time

        base.ExitState(postAction, recursive);

        return Task.CompletedTask;
    }
        

    /// <summary>
    /// 
    /// </summary>
    /// <param name="historyType"></param>
    public override Task EntryState(bool postAction = false, bool recursive = false, HistoryType historyType = HistoryType.None, HistoryState? targetHistoryState = null)
    {
        //StateMachine.Log(">>>- State_Real.EntryState: " + Name);
        if (StateMachine == null)
        {
            System.Diagnostics.Debug.WriteLine($"Warning: StateMachine is null for state {Name} during EntryState");
            return Task.CompletedTask;
        }

        IsDone = false;

        base.EntryState(postAction, recursive, historyType, targetHistoryState);

        ScheduleAfterTransitionTimer();

        // Check for always transitions after entering the state
        // Only check if we actually have an always transition to avoid unnecessary async operations
        if (AlwaysTransition != null)
        {
            CheckAndExecuteAlwaysTransition();
        }

        return Task.CompletedTask;
    }


    public void ScheduleAfterTransitionTimer()
    {
        if (AfterTransition == null) return;
        if (StateMachine == null) throw new Exception("StateMachine is null");

        Logger.Info($">>> Scheduling after transition for state {Name} in {AfterTransition.Delay} ms");

        // Cancel any existing timer before creating a new one
        if (_afterTransitionTimer != null)
        {
            _afterTransitionTimer.Stop();
            _afterTransitionTimer.Dispose();
            _afterTransitionTimer = null;
        }

        // Create a new timer instead of using pool to avoid event handler issues
        var timer = new System.Timers.Timer();
        _afterTransitionTimer = timer;

        if (int.TryParse(AfterTransition.Delay, out int delay))
        {
            timer.Interval = delay;
        }
        else
        {
            if (StateMachine.DelayMap == null) throw new Exception("DelayMap is null");
            if (AfterTransition?.Delay != null)
            {
                if (StateMachine.DelayMap.TryGetValue(AfterTransition.Delay, out var namedDelay) && namedDelay?.DelayFunc != null)
                {
                    timer.Interval = namedDelay.DelayFunc.Invoke(StateMachine);
                }
                else
                {
                    throw new Exception($"Delay '{AfterTransition.Delay}' not found in DelayMap or has no DelayFunc.");
                }
            }
        }

        var now = DateTime.Now;

        timer.Elapsed += (sender, e) =>
        {
            StateMachine.Log("");
            StateMachine.Log($">>> Scheduled time has come {Name} in {AfterTransition?.Delay} ms");
            StateMachine.Log($">>> Timer elapsed (ms): {(e.SignalTime - now).TotalMilliseconds}");
            StateMachine.Log("");
            StateMachine.transitionExecutor.Execute(AfterTransition, $"after: {timer.Interval}");
            timer.Stop();
            // Dispose timer instead of returning to pool
            if (sender is System.Timers.Timer t)
            {
                t.Dispose();
                // Clear the reference since the timer has been disposed
                if (_afterTransitionTimer == t)
                    _afterTransitionTimer = null;
            }
        };
        
        timer.AutoReset = false;
        timer.Start();

        StateMachine.Log("");
        StateMachine.Log($">>> Scheduled after transition {Name} in {AfterTransition?.Delay} ms");
        StateMachine.Log("");
    }

    private void CheckAndExecuteAlwaysTransition()
    {
        if (AlwaysTransition == null || StateMachine == null) return;

        // Check if guard passes (if there is one)
        bool guardPassed = AlwaysTransition.Guard == null ||
                          AlwaysTransition.Guard.PredicateFunc(StateMachine);

        if (guardPassed)
        {
            Logger.Info($">>> Executing always transition from state {Name}");

            // Execute the always transition immediately
            // Use Task.Run to avoid blocking the current state entry
            Task.Run(() =>
            {
                try
                {
                    StateMachine.transitionExecutor.Execute(AlwaysTransition, "always");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error executing always transition from {Name}: {ex.Message}");
                    // Store error in context
                    if (StateMachine?.ContextMap != null)
                    {
                        StateMachine.ContextMap["_error"] = ex;
                        StateMachine.ContextMap["_errorMessage"] = ex.Message;
                    }
                }
            });
        }
    }

    public virtual void BuildTransitionList(string eventName, List<(CompoundState state, Transition transition, string eventName)> transitionList)
    {
        // Defensive null check with more context
        if(StateMachine == null) 
        {
            // Don't throw during cleanup or disposal
            System.Diagnostics.Debug.WriteLine($"Warning: StateMachine is null for state {Name} when processing event {eventName}");
            return;
        }

        //StateMachine.Log(">>>- State.Real.BuildTransitionList: " + Name);
        // self second - use thread-safe GetTransitions method
        var transitions = OnTransitionMap.GetTransitions(eventName);

        foreach (var transition in transitions)
        {
            bool guardPassed = transition.Guard == null || transition.Guard.PredicateFunc(StateMachine);
            if (transition.Guard != null)
            {
                // Notify guard evaluation
                StateMachine.RaiseGuardEvaluated(transition.Guard.Name, guardPassed);
            }

            if (guardPassed)
            {
                transitionList.Add((this, transition, eventName));
            }
        }

        //
        // Question: Should we execute always transition whenever 'after' and 'onDone' transitions?
        // Clarify always transition rule!
        //

        if (AlwaysTransition != null)
            transitionList.Add((this, AlwaysTransition, "always"));

        // Handle onDone event from service invocations
        if (eventName == "onDone" && OnDoneTransition != null)
        {
            transitionList.Add((this, OnDoneTransition, "onDone"));
        }

        /* After transition should be called by timer
         *
        if (AfterTransition != null)
            transitionList.Add((this, AfterTransition, "after"));
        */
    }

    public abstract void GetTargetSubStateCollection(ICollection<string> collection, bool singleBranchPath, HistoryType hist = HistoryType.None);
    public abstract void GetSouceSubStateCollection(ICollection<string> collection, bool singleBranchPath = false);
    //public abstract void PrintActiveStateTree(int depth);  
}

public abstract class Parser_RealState : Parser_StateBase
{
    public Parser_RealState(string? machineId) : base(machineId)  { }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="state"></param>
    /// <param name="stateToken"></param>
    protected void ParseActionsAndService(CompoundState state, JToken stateToken)
    {
        if (StateMachine == null) throw new Exception("StateMachine is null");
        state.EntryActions = StateMachine.ParseActions("entry", StateMachine.ActionMap, stateToken);
        state.ExitActions = StateMachine.ParseActions("exit", StateMachine.ActionMap, stateToken);
        state.Service = StateMachine.ParseService("invoke", StateMachine.ServiceMap, stateToken);
        state.Activities = StateMachine.ParseActivities("activities", StateMachine.ActivityMap, stateToken);
        
        // Parse onDone and onError transitions from invoke if present
        var invokeToken = stateToken["invoke"];
        if (invokeToken != null)
        {
            // Parse onDone transition from invoke
            var onDoneToken = invokeToken["onDone"];
            if (onDoneToken != null)
            {
                string? targetName = null;
                JToken? actionsToken = null;

                if (onDoneToken.Type == JTokenType.String)
                {
                    targetName = onDoneToken.ToString();
                }
                else if (onDoneToken.Type == JTokenType.Object)
                {
                    targetName = onDoneToken["target"]?.ToString();
                    actionsToken = onDoneToken["actions"];
                }

                if (!string.IsNullOrEmpty(targetName) && !string.IsNullOrEmpty(state.Name))
                {
                    // Ensure target name has the proper full path
                    // state.Name already includes the machine ID and # prefix
                    if (!targetName.StartsWith("#") && !targetName.StartsWith("."))
                    {
                        // Get the parent path from the current state
                        var parentPath = state.Name.Substring(0, state.Name.LastIndexOf('.'));
                        targetName = $"{parentPath}.{targetName}";
                    }
                    else if (targetName.StartsWith("."))
                    {
                        // Relative target - resolve relative to current state's parent
                        var parentPath = state.Name.Substring(0, state.Name.LastIndexOf('.'));
                        targetName = $"{parentPath}{targetName}";
                    }
                    
                    var onDoneTransition = new OnTransition(machineId)
                    {
                        Event = "onDone",
                        SourceName = state.Name,
                        TargetName = targetName
                    };
                    
                    // Parse actions if present
                    if (actionsToken != null)
                    {
                        onDoneTransition.Actions = StateMachine.ParseActions("actions", StateMachine.ActionMap, onDoneToken);
                    }
                    
                    // Use thread-safe AddTransition method
                    state.OnTransitionMap.AddTransition("onDone", onDoneTransition);
                }
            }
            
            // Parse onError transition from invoke
            var onErrorToken = invokeToken["onError"];
            if (onErrorToken != null)
            {
                // Handle array of onError transitions (like guarded transitions)
                if (onErrorToken.Type == JTokenType.Array)
                {
                    foreach (var errorItem in onErrorToken)
                    {
                        var onErrorTransition = ParseSingleOnErrorTransition(errorItem, state.Name ?? string.Empty, machineId ?? string.Empty, StateMachine);
                        if (onErrorTransition != null)
                        {
                            // Use thread-safe AddTransition method
                            state.OnTransitionMap.AddTransition("onError", onErrorTransition);
                        }
                    }
                }
                else
                {
                    // Handle single onError transition (string or object)
                    var onErrorTransition = ParseSingleOnErrorTransition(onErrorToken, state.Name ?? string.Empty, machineId ?? string.Empty, StateMachine);
                    if (onErrorTransition != null)
                    {
                        // Use thread-safe AddTransition method
                        state.OnTransitionMap.AddTransition("onError", onErrorTransition);
                    }
                }
            }
        }
    }

    private static OnTransition? ParseSingleOnErrorTransition(JToken onErrorToken, string stateName, string machineId, StateMachine StateMachine)
    {
        string? targetName = null;
        JToken? actionsToken = null;
        JToken? condToken = null;

        if (onErrorToken.Type == JTokenType.String)
        {
            targetName = onErrorToken.ToString();
        }
        else if (onErrorToken.Type == JTokenType.Object)
        {
            targetName = onErrorToken["target"]?.ToString();
            actionsToken = onErrorToken["actions"];
            condToken = onErrorToken["cond"];
        }

        if (string.IsNullOrEmpty(targetName) || string.IsNullOrEmpty(stateName))
        {
            return null;
        }

        // Ensure target name has the proper full path
        if (!targetName.StartsWith("#") && !targetName.StartsWith("."))
        {
            // Get the parent path from the current state
            var parentPath = stateName.Substring(0, stateName.LastIndexOf('.'));
            targetName = $"{parentPath}.{targetName}";
        }
        else if (targetName.StartsWith("."))
        {
            // Relative target - resolve relative to current state's parent
            var parentPath = stateName.Substring(0, stateName.LastIndexOf('.'));
            targetName = $"{parentPath}{targetName}";
        }

        var onErrorTransition = new OnTransition(machineId)
        {
            Event = "onError",
            SourceName = stateName,
            TargetName = targetName
        };

        // Parse guard condition if present
        if (condToken != null && StateMachine?.GuardMap != null)
        {
            var guardName = condToken.ToString();
            if (StateMachine.GuardMap.TryGetValue(guardName, out var guard))
            {
                onErrorTransition.Guard = guard;
            }
        }

        // Parse actions if present
        if (actionsToken != null && StateMachine?.ActionMap != null)
        {
            onErrorTransition.Actions = StateMachine.ParseActions("actions", StateMachine.ActionMap, onErrorToken);
        }

        return onErrorTransition;
    }
}
