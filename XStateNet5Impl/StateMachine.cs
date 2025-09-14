
/////////////////////////////////////////////////////////////////////////
// Copyright (c) by SangJeong Woo
/////////////////////////////////////////////////////////////////////////
// [v] Implement basic state machine
// [v] Implement entry and exit actions
// [v] Implement transition actions
// [v] Implement parallel state
// [v] Implement In condition
// [v] Implement unit tests and passed (7 tests)
// [v] Implement always transition
// [v] Implement UnitTest for always transition
// [v] Implement guards based on context
// [v] Implement implicit shallow history states (24/07/21)
// [v] Implement explicit shallow history states (24/07/21)
// [v] Implement deep history states (24/07/21)
// [v] Implement UnitTest for history states (24/07/21)
// [v] Change current state string as the single full path of leaf level states for each parallel states for unit test (24/07/22)
// [v] Implement List based Exit and Entry for simple and symmetry operation 
// [v] Implement parallel state using concurrent dictionary (.. substates arise err when converted to concurrent dictionary)
// [v] Implement after property to store delayed transitions
// [v] Implement Start() method for initial state entry actions (Need to decide whther call it explicitely or not)
// [v] Implement Pause() and Stop() methods (Need to decide whther call it explicitely or not)
// [v] Refactoring between State, StateMachine and Parser
// [ ] Make multiple machine run together
// [v] Implement parser for single event multiple transition array that surrounded with []
// [v] implement OnTransition event
// [ ] implement RESET default event processor
// [v] implement parsing .child state as a target
// [v] Bug fix for MOUSE_MOVE event handling of original Diagramming Framework script (to be resizing state)
// [ ] Precise analysis of transition path including complex (parallel, history) states
//     [v] Simple case analysis (No history, No Parallel)
//     [v] Simple case implementation (No history, No Parallel)
//     [v] History case analysis (Shallow, Deep)
//     [v] History case implementation (Shallow, Deep)
//     [ ] Parallel case analysis
//     [ ] Parallel case implementation
// [ ] Implement and prove by unittest self transition 
// [v] Implement 'final' keyword processing code as IsDone property for RealState.
// [v] Implement 'onDone' transition. Treat it as a special event similar to 'always' or 'reset'
// [ ] Implement 'onError' transition.
// [ ] Implement 'invoke' keyword.
//      [v] Simple Unit test for invoke
//      [ ] Heavy Unit test for invoke
// [ ] Implement 'activities' keyword.
// [ ] Implement 'internal' keyword.
// [x] State branch block transition (Parallel by parallel) --> Not work
// [v] Implement top down transition algorithm for full transition --> this is the solution for the above issue
// [ ] Implement single action expression (not an array, embraced using square bracket) for entry, exit, transition
/////////////////////////////////////////////////////////////////////////
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Threading;


namespace XStateNet;

public enum MachineState
{
    Running,
    Paused,
    Stopped
}

public partial class StateMachine
{
    public static StateMachine? GetInstance(string Id)
    {
        if (string.IsNullOrWhiteSpace(Id))
            return null;
        
        _instanceMap.TryGetValue(Id, out var instance);
        return instance;
    }

    public string? machineId { set; get; }
    public CompoundState? RootState { set; get; }
    private StateMap? StateMap { set; get; }
    public ContextMap? ContextMap { get; private set; } // use object because context can have various types of data

    public ActionMap? ActionMap { set; get; }
    public GuardMap? GuardMap { set; get; }
    public ServiceMap? ServiceMap { set; get; }
    public DelayMap? DelayMap { set; get; }

    public TransitionExecutor transitionExecutor { private set; get; } = null!;
    public ServiceInvoker serviceInvoker { private set; get; } = null!;
    private EventQueue? _eventQueue;
    private StateMachineSync? _sync;
    private readonly ReaderWriterLockSlim _stateLock = new ReaderWriterLockSlim();

    //
    // This state machine map is for interact each other in a process.
    // For multiple machine interact each other in the range of multiple process or multiple hosts in the network, we need implement messaging mechanism.
    //

    private static readonly ConcurrentDictionary<string, StateMachine> _instanceMap = new();
    private readonly ConcurrentDictionary<string, StateNode?> _stateCache = new();
    
    /// <summary>
    /// Clear all static state for test isolation - should only be used in tests
    /// </summary>
    public static void ClearAllStaticState()
    {
        _instanceMap.Clear();
    }

    // Event handler delegates for monitoring
    public delegate void TransitionHandler(CompoundState? fromState, StateNode? toState, string eventName);
    public event TransitionHandler? OnTransition;

    public delegate void EventReceivedHandler(string eventName, object? eventData);
    public event EventReceivedHandler? OnEventReceived;

    public delegate void ActionExecutedHandler(string actionName, string? stateName);
    public event ActionExecutedHandler? OnActionExecuted;

    public delegate void GuardEvaluatedHandler(string guardName, bool result);
    public event GuardEvaluatedHandler? OnGuardEvaluated;

    // Protected methods to raise events - allows derived classes and internal components to trigger events
    internal void RaiseTransition(CompoundState? fromState, StateNode? toState, string eventName)
    {
        OnTransition?.Invoke(fromState, toState, eventName);
    }

    internal void RaiseEventReceived(string eventName, object? eventData)
    {
        OnEventReceived?.Invoke(eventName, eventData);
    }

    internal void RaiseActionExecuted(string actionName, string? stateName)
    {
        OnActionExecuted?.Invoke(actionName, stateName);
    }

    internal void RaiseGuardEvaluated(string guardName, bool result)
    {
        OnGuardEvaluated?.Invoke(guardName, result);
    }

    private volatile MachineState machineState = MachineState.Stopped;

    /// <summary>
    /// Enable thread-safe operation mode
    /// </summary>
    public bool EnableThreadSafety { get; set; } = false;
    
    /// <summary>
    /// 
    /// </summary>
    public StateMachine()
    {
        StateMap = new();
        ContextMap = new();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="jsonFilePath"></param>
    /// <param name="actionCallbacks"></param>
    /// <param name="guardCallbacks"></param>
    /// <returns></returns>
    public static StateMachine CreateFromFile(
        string jsonFilePath,
        ActionMap? actionCallbacks = null,
        GuardMap? guardCallbacks = null,
        ServiceMap? serviceCallbacks = null,
        DelayMap? delayCallbacks = null
    )
    {
        // Use secure file reading with validation
        var jsonScript = Security.SafeReadFile(jsonFilePath);
        return ParseStateMachine(jsonScript, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="jsonScript"></param>
    /// <param name="actionCallbacks"></param>
    /// <param name="guardCallbacks"></param>
    /// <returns></returns>
    public static StateMachine CreateFromScript(
        string? jsonScript,
        ActionMap? actionCallbacks = null,
        GuardMap? guardCallbacks = null,
        ServiceMap? serviceCallbacks = null,
        DelayMap? delayCallbacks = null
    )
    {
        return ParseStateMachine(jsonScript, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sm"></param>
    /// <param name="jsonFilePath"></param>
    /// <param name="actionCallbacks"></param>
    /// <param name="guardCallbacks"></param>
    /// <returns></returns>
    public static StateMachine CreateFromFile(
        StateMachine sm,
        string jsonFilePath,
        ActionMap? actionCallbacks = null,
        GuardMap? guardCallbacks = null,
        ServiceMap? serviceCallbacks = null,
        DelayMap? delayCallbacks = null
        )
    {
        // Use secure file reading with validation
        var jsonScript = Security.SafeReadFile(jsonFilePath);
        return ParseStateMachine(sm, jsonScript, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sm"></param>
    /// <param name="jsonScript"></param>
    /// <param name="actionCallbacks"></param>
    /// <param name="guardCallbacks"></param>
    /// <returns></returns>
    public static StateMachine CreateFromScript(StateMachine sm, string jsonScript,
        ActionMap? actionCallbacks = null,
        GuardMap? guardCallbacks = null,
        ServiceMap? serviceCallbacks = null,
        DelayMap? delayCallbacks = null
        )
    {
        return ParseStateMachine(sm, jsonScript, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="state"></param>
    public void RegisterState(StateNode state)
    {
        if (StateMap == null)
            throw new InvalidOperationException("StateMap is not initialized");
        
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        
        if (string.IsNullOrWhiteSpace(state.Name))
            throw new ArgumentException("State name cannot be null or empty");
        
        StateMap[state.Name] = state;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public StateMachine Start()
    {
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Info, () => ">>> Start state machine");

        _stateLock.EnterWriteLock();
        try
        {
            if (machineState == MachineState.Running)
            {
                PerformanceOptimizations.LogOptimized(Logger.LogLevel.Warning, () => "State machine is already RUNNING!");
                return this;
            }
            
            // Initialize components based on thread safety setting
            if (EnableThreadSafety)
            {
                _sync = new StateMachineSync();
                _eventQueue = new EventQueue(this);
                transitionExecutor = new SafeTransitionExecutor(machineId, _sync);
            }
            else
            {
                transitionExecutor = new TransitionExecutor(machineId);
            }
            
            serviceInvoker = new ServiceInvoker(machineId);
            
            var list = GetEntryList(machineId!);
            string entry = list.ToCsvString(this, false, " -> ");

            PerformanceOptimizations.LogOptimized(Logger.LogLevel.Info, () => $">>> Start entry: {entry}");

            foreach (var stateName in list)
            {
                var state = GetState(stateName) as CompoundState;
                state?.EntryState();
            }

            machineState = MachineState.Running;
        }
        finally
        {
            _stateLock.ExitWriteLock();
        }
        return this;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="eventName"></param>
    public void Send(string eventName)
    {
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Debug, () => $">>> Send event: {eventName}");

        // Notify event received
        RaiseEventReceived(eventName, null);

        _stateLock.EnterReadLock();
        try
        {
            if (machineState != MachineState.Running)
            {
                PerformanceOptimizations.LogOptimized(Logger.LogLevel.Warning, () => $"State machine is not RUNNING!");
                return;
            }
        }
        finally
        {
            _stateLock.ExitReadLock();
        }

        // Direct synchronous processing for backward compatibility
        // EventQueue is only used when explicitly enabled
        Transit(eventName);
        //PrintCurrentStateTree();
        PrintCurrentStatesString();
    }
    
    /// <summary>
    /// Send event asynchronously with thread-safe processing
    /// </summary>
    public async Task SendAsync(string eventName)
    {
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Debug, () => $">>> Send event async: {eventName}");

        _stateLock.EnterReadLock();
        try
        {
            if (machineState != MachineState.Running)
            {
                PerformanceOptimizations.LogOptimized(Logger.LogLevel.Warning, () => $"State machine is not RUNNING!");
                return;
            }
        }
        finally
        {
            _stateLock.ExitReadLock();
        }

        if (_eventQueue != null)
        {
            await _eventQueue.SendAsync(eventName);
        }
        else
        {
            await Task.Run(() =>
            {
                Transit(eventName);
                //PrintCurrentStateTree();
                PrintCurrentStatesString();
            });
        }
    }
    
    /// <summary>
    /// Process event asynchronously (called by EventQueue)
    /// </summary>
    internal async Task ProcessEventAsync(string eventName)
    {
        await Task.Run(() =>
        {
            Transit(eventName);
            //PrintCurrentStateTree();
            PrintCurrentStatesString();
        });
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="eventName"></param>
    void Transit(string eventName)
    {
        // Use pooled list to reduce allocations
        var transitionList = PerformanceOptimizations.RentTransitionList();
        
        try
        {
            RootState?.BuildTransitionList(eventName, transitionList);


        //step 1:  build transition list

        if (Logger.CurrentLevel >= Logger.LogLevel.Debug)
        {
            PerformanceOptimizations.LogOptimized(Logger.LogLevel.Debug, () => "***** Transition list *****");
            foreach (var t in transitionList)
            {
                var key = PerformanceOptimizations.GetTransitionKey(t.transition?.SourceName, t.transition?.TargetName);
                PerformanceOptimizations.LogOptimized(Logger.LogLevel.Debug, () => $"Transition : {key}, event = {t.@event}");
            }
            PerformanceOptimizations.LogOptimized(Logger.LogLevel.Debug, () => "***************************");
        };

        //step 2:  perform transitions
        // For transitions from the same state with the same event (like guarded transitions),
        // only execute the first matching one
        
        // Use pooled HashSet to reduce allocations
        var executedTransitions = PerformanceOptimizations.RentTransitionHashSet();
        try
        {
            foreach (var (state, transition, @event) in transitionList)
            {
                var key = (transition?.SourceName, @event);
                
                // Skip if we've already executed a transition from this state for this event
                if (executedTransitions.Contains(key))
                {
                    Logger.Debug($"Skipping transition from {transition?.SourceName} on {@event} - already executed");
                    continue;
                }
                
                transitionExecutor.Execute(transition, @event);
                executedTransitions.Add(key);
            }
        }
        finally
        {
            // Return the HashSet to the pool
            PerformanceOptimizations.ReturnTransitionHashSet(executedTransitions);
        }
        }
        finally
        {
            // Return the list to the pool for reuse
            PerformanceOptimizations.ReturnTransitionList(transitionList);
        }
    }

    public List<string> GetEntryList(string target) // for Start
    {
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => ">>> - GetEntryList");


        var target_sub = GetTargetSubStateCollection(target);
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- target_sub: {target_sub.ToCsvString(this, false, " -> ")}");

        return target_sub.ToList();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="source"></param>
    /// <param name="target"></param>
    /// <returns></returns>
    public (List<string> exits, List<string> entrys) GetExitEntryList(string source, string target)
    {
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => ">>> - GetExitEntryList");

        var source_sub = GetSourceSubStateCollection(source).Reverse();
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- source_sub: {source_sub.ToCsvString(this, false, " -> ")}");

        var source_sup = GetSuperStateCollection(source);
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- source_sup: {source_sup?.ToCsvString(this, false, " -> ")}");

        var source_cat = source_sub.Concat(source_sup ?? Enumerable.Empty<string>());
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- source_cat: {source_cat.ToCsvString(this, false, " -> ")}");

        var target_sub = GetTargetSubStateCollection(target);
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- target_sub: {target_sub.ToCsvString(this, false, " -> ")}");

        var target_sup = GetSuperStateCollection(target)?.Reverse() ?? Enumerable.Empty<string>();
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- target_sup: {target_sup.ToCsvString(this, false, " -> ")}");

        var target_cat = target_sup.Concat(target_sub);
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- target_cat: {target_cat.ToCsvString(this, false, " -> ")}");

        // Optimized with HashSet for O(1) lookups instead of O(n) LINQ Except
        var targetSet = new HashSet<string>(target_cat);
        var sourceSet = new HashSet<string>(source_cat);
        
        // Use pooled lists to reduce allocations
        var source_exit = PerformanceOptimizations.RentStringList();
        var target_entry = PerformanceOptimizations.RentStringList();
        
        try
        {
            // Build exit path - states in source but not in target
            foreach (var state in source_cat)
            {
                if (!targetSet.Contains(state))
                    source_exit.Add(state);
            }
            
            // Build entry path - states in target but not in source
            foreach (var state in target_cat)
            {
                if (!sourceSet.Contains(state))
                    target_entry.Add(state);
            }

            PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- source_exit: {source_exit.ToCsvString(this, false, " -> ")}");
            PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- target_entry: {target_entry.ToCsvString(this, false, " -> ")}");

            // Create new lists to return (since we need to return ownership)
            var exitResult = new List<string>(source_exit);
            var entryResult = new List<string>(target_entry);
            
            return (exitResult, entryResult);
        }
        finally
        {
            // Return pooled lists
            PerformanceOptimizations.ReturnStringList(source_exit);
            PerformanceOptimizations.ReturnStringList(target_entry);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="stateName"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public StateNode GetState(string? stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName))
            throw new ArgumentNullException(nameof(stateName), "State name cannot be null or empty");
        
        // Try to get from cache first
        if (_stateCache.TryGetValue(stateName, out var cachedState) && cachedState != null)
        {
            return cachedState;
        }
        
        if (StateMap == null)
            throw new InvalidOperationException("StateMap is not initialized");
        
        if (!StateMap.TryGetValue(stateName, out var state) || state == null)
        {
            throw new ArgumentException($"State '{stateName}' not found in StateMap");
        }

        // Cache the result
        _stateCache.TryAdd(stateName, state);
        return state;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="stateName"></param>
    /// <returns></returns>
    public HistoryState? GetStateAsHistory(string stateName)
    {
        if (StateMap == null) 
            throw new InvalidOperationException("StateMap is not initialized");
        
        if (string.IsNullOrWhiteSpace(stateName))
            throw new ArgumentNullException(nameof(stateName), "State name cannot be null or empty");
        
        if (StateMap.TryGetValue(stateName, out var state))
            return state as HistoryState;
        
        return null;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="inConditionString"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private Func<bool> GetInConditionCallback(string inConditionString)
    {
        if (string.IsNullOrWhiteSpace(inConditionString))
            throw new ArgumentNullException(nameof(inConditionString));
        
        var parts = inConditionString.Split('.');
        if (parts.Length == 0)
            throw new ArgumentException("Invalid in-condition string format");
        
        string stateMachineId = parts[0];
        if (stateMachineId != machineId)
        {
            StateMachine? sm = GetInstance(stateMachineId);
            if (sm == null) 
                throw new InvalidOperationException($"State machine not found: {stateMachineId}");

            return () => IsInState(sm, inConditionString);
        }
        else
        {
            return () => IsInState(this, inConditionString);
        }
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="sm"></param>
    /// <param name="stateName"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public bool IsInState(StateMachine sm, string stateName)
    {
        if (sm == null)
            throw new ArgumentNullException(nameof(sm));
        
        if (string.IsNullOrWhiteSpace(stateName))
            throw new ArgumentNullException(nameof(stateName));
        
        var stateNode = GetState(stateName);
        if (stateNode is not CompoundState state)
            throw new InvalidOperationException($"State '{stateName}' is not a CompoundState");

        return state.IsActive;
    }

    /// <summary>
    /// GetActiveStateString
    /// </summary>
    /// <param name="leafOnly">true: leaf level state name only, false: full level state names</param>
    /// <param name="separator"></param>
    /// <returns></returns>
    public string GetActiveStateString(bool leafOnly = true, string separator = ";")
    {
        // Use pooled list to reduce allocations
        var strings = PerformanceOptimizations.RentStringList();
        try
        {
            RootState?.GetActiveSubStateNames(strings);
            return strings.ToCsvString(this, leafOnly, separator);
        }
        finally
        {
            // Return the list to the pool
            PerformanceOptimizations.ReturnStringList(strings);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="stateName"></param>
    /// <param name="singleBranchPath"></param>
    /// <returns></returns>
    public ICollection<string> GetSourceSubStateCollection(string? stateName = null, bool singleBranchPath = false)
    {
        CompoundState? state = null;

        if (stateName == null)
        {
            state = RootState;
        }
        else
        {
            var stateNode = GetState(stateName);
            state = stateNode as CompoundState;
            if (state == null)
                throw new InvalidOperationException($"State '{stateName}' is not a CompoundState");
        }

        ICollection<string> list = new List<string>();
        state?.GetSouceSubStateCollection(list, singleBranchPath);

        return list;
    }
    //
    // Transition algorithm
    //
    // 1. Find the full exit path to the root state = fullExitPath
    // 1.1 Find the single path from source to the the leaf level subtate = subExitPath
    // 1.1.1 If thers are parallel forks select first indexed path. 
    // 1.2 Find a single path to root state = supExitPath
    // 1.3 Reverse the super path sequence and then concaternate sub path to it = fullExitPath (topdown)
    // 2. Find the full entry path from the root path = fullEntryPath
    // 2.1 Find the single path to the root state from the target path = supEntryPath
    // 2.2 Find the single path to the leaf level state  = subEntryPath
    // 2.2.1 If thers are parallel forks select first indexed path. 
    // 2.3 Reverse super entry path and concaternate subEntryPath as reversed =  fullEntryPath
    // 3. Find the actual exit path by exclude operation (fullExitPath except fullEntryPath) = actualExitPath
    // 3.1 Revisit exit path along actualExitPath here if meet parallel fork,
    // 

    public (ICollection<string> exitSinglePath, ICollection<string> entrySinglePath) GetFullTransitionSinglePath(string? srcStateName, string? tgtStateName)
    {
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => ">>> - GetFullTransitionPath");

        //1.
        var subExitPath = GetSourceSubStateCollection(srcStateName, true);
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- subExitPath: {subExitPath?.ToCsvString(this, false, " -> ")}");

        var supExitPath = GetSuperStateCollection(srcStateName);
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- supExitPath: {(supExitPath ?? Enumerable.Empty<string>()).ToCsvString(this, false, " -> ")}");

        var fullExitPath = (supExitPath ?? Enumerable.Empty<string>()).Reverse().Concat(subExitPath);
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- fullExitPath: {fullExitPath.ToCsvString(this, false, " -> ")}");

        //2.
        var supEntryPath = GetSuperStateCollection(tgtStateName);
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- supEntryPath: {(supEntryPath ?? Enumerable.Empty<string>()).ToCsvString(this, false, " -> ")}");

        var subEntryPath = GetTargetSubStateCollection(tgtStateName, true);
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- subEntryPath: {subEntryPath?.ToCsvString(this, false, " -> ")}");

        var fullEntryPath = (supEntryPath ?? Enumerable.Empty<string>()).Reverse().Concat(subEntryPath ?? Enumerable.Empty<string>());
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- fullEntryPath: {fullEntryPath.ToCsvString(this, false, " -> ")}");


        // 3.
        // Special handling for self-transitions (external transitions to the same state)
        if (srcStateName == tgtStateName && !string.IsNullOrEmpty(srcStateName))
        {
            // For external self-transitions, we should exit and re-enter the same state
            var selfTransitionExitPath = new List<string> { srcStateName };
            var selfTransitionEntryPath = new List<string> { srcStateName };
            
            PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- Self-transition actualExitPath: {selfTransitionExitPath.ToCsvString(this, false, " -> ")}");
            PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- Self-transition actualEntryPath: {selfTransitionEntryPath.ToCsvString(this, false, " -> ")}");
            
            return (selfTransitionExitPath, selfTransitionEntryPath);
        }
        
        // Optimized with HashSet for O(1) lookups instead of O(n) LINQ Except
        var fullEntrySet = new HashSet<string>(fullEntryPath);
        var fullExitSet = new HashSet<string>(fullExitPath);
        
        // Use pooled lists to reduce allocations
        var actualExitPath = PerformanceOptimizations.RentStringList();
        var actualEntryPath = PerformanceOptimizations.RentStringList();
        
        try
        {
            // Build actual exit path - states to exit (in full exit path but not in full entry path)
            foreach (var state in fullExitPath)
            {
                if (!fullEntrySet.Contains(state))
                    actualExitPath.Add(state);
            }
            
            // Build actual entry path - states to enter (in full entry path but not in full exit path)
            foreach (var state in fullEntryPath)
            {
                if (!fullExitSet.Contains(state))
                    actualEntryPath.Add(state);
            }
            
            PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- actualExitPath: {actualExitPath.ToCsvString(this, false, " -> ")}");
            PerformanceOptimizations.LogOptimized(Logger.LogLevel.Trace, () => $">>> -- actualEntryPath: {actualEntryPath.ToCsvString(this, false, " -> ")}");

            // Create new lists to return (since we need to return ownership)
            var exitResult = new List<string>(actualExitPath);
            var entryResult = new List<string>(actualEntryPath);
            
            return (exitResult, entryResult);
        }
        finally
        {
            // Return pooled lists
            PerformanceOptimizations.ReturnStringList(actualExitPath);
            PerformanceOptimizations.ReturnStringList(actualEntryPath);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="topExitState"></param>
    public Task TransitUp(CompoundState? topExitState)
    {
        if (topExitState != null)
        {
            try
            {
                topExitState.ExitState(postAction: true, recursive: true);
            }
            catch (Exception ex)
            {
                // Store error context
                if(ContextMap is not null)
                {
                    ContextMap["_error"] = ex;
                    ContextMap["_lastError"] = ex;  // For backward compatibility
                    ContextMap["_errorType"] = ex.GetType().Name;
                    ContextMap["_errorMessage"] = ex.Message;
                }
                
                // Send onError event to trigger error transitions
                Send("onError");
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="topEntryState"></param>
    /// <param name="historyStateName"></param>
    public Task TransitDown(CompoundState? topEntryState, string? historyStateName = null)
    {
        var historyState = historyStateName != null ? GetState(historyStateName) as HistoryState : null;
        if (topEntryState != null)
        {
            try
            {
                topEntryState.EntryState(postAction: false, recursive: true, HistoryType.None, historyState);
            }
            catch (Exception ex)
            {
                // Store error context
                if(ContextMap is not null)
                {
                    ContextMap["_error"] = ex;
                    ContextMap["_lastError"] = ex;  // For backward compatibility
                    ContextMap["_errorType"] = ex.GetType().Name;
                    ContextMap["_errorMessage"] = ex.Message;
                }
                
                // Send onError event to trigger error transitions
                Send("onError");
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// GetTargetSubStateCollection
    /// 
    /// Note: The reason for handling the history state only here is that the history state 
    /// can only be activated when it is explicitly specified.
    /// </summary>
    /// <param name="statePath"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public ICollection<string> GetTargetSubStateCollection(string? statePath, bool singleBranchPath = false)
    {
        if (string.IsNullOrWhiteSpace(statePath))
            throw new ArgumentNullException(nameof(statePath));
        
        CompoundState? state = null;
        ICollection<string> list = new List<string>();
        
        var stateNode = GetState(statePath);

        if (stateNode is CompoundState realState)
        {
            state = realState;
            state?.GetTargetSubStateCollection(list, singleBranchPath);
        }
        else if (stateNode is HistoryState historyState)
        {
            if (historyState.Parent is not NormalState normalParent)
            {
                throw new InvalidOperationException("History state must be a child of NormalState");
            }
            
            state = normalParent.LastActiveState;
            state?.GetTargetSubStateCollection(list, singleBranchPath, historyState.HistoryType);
        }
        else
        {
            throw new InvalidOperationException($"State '{statePath}' must be CompoundState or HistoryState");
        }

        return list;
    }

    /// <summary>
    /// GetSuperStateCollection:
    /// Common to source and target.
    /// Absoultely source can not be history state. But for reuse purpose, it is implemented.
    /// </summary>
    /// <param name="statePath"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public ICollection<string>? GetSuperStateCollection(string? statePath)
    {
        if (statePath == null) 
            return null;

        CompoundState? state = null;
        var stateNode = GetState(statePath);

        if (stateNode is CompoundState realState)
        {
            state = realState;
        }
        else if (stateNode is HistoryState historyState)
        {
            if (historyState.Parent is not NormalState normalParent)
            {
                throw new InvalidOperationException("History state must be a child of NormalState");
            }
            state = normalParent.LastActiveState;
        }
        else
        {
            throw new InvalidOperationException($"State '{statePath}' must be CompoundState or HistoryState");
        }

        ICollection<string> list = new List<string>();
        state?.GetSuperStateCollection(list);

        return list;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="transition"></param>
    /// <param name="event"></param>
    public async Task TransitFull(Transition transition, string @event)
    {
        var fromState = transition.SourceName;
        string? toState = transition.TargetName;

        var path1 = GetFullTransitionSinglePath(fromState, toState);

        string? firstExit = path1.exitSinglePath.FirstOrDefault();
        string? firstEntry = path1.entrySinglePath.FirstOrDefault();

        bool guardPassed = transition.Guard == null || transition.Guard.PredicateFunc(this);
        if (transition.Guard != null)
        {
            // Notify guard evaluation
            RaiseGuardEvaluated(transition.Guard.Name, guardPassed);
        }

        if (guardPassed && (transition.InCondition == null || transition.InCondition()))
        {
            if (toState != null)
            {
                if (firstExit != null)
                    await TransitUp(firstExit.ToState(this) as CompoundState);
                
                if (transition.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        try
                        {
                            action.Action(this);
                        }
                        catch (Exception ex)
                        {
                            PerformanceOptimizations.LogOptimized(Logger.LogLevel.Error, () => $"Error executing action: {ex.Message}");
                        }
                    }
                }
                
                if (firstEntry != null)
                    await TransitDown(firstEntry.ToState(this) as CompoundState, toState);
            }
            else
            {
                // action only transition
                if (transition.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        try
                        {
                            action.Action(this);
                        }
                        catch (Exception ex)
                        {
                            PerformanceOptimizations.LogOptimized(Logger.LogLevel.Error, () => $"Error executing action: {ex.Message}");
                        }
                    }
                }
            }
        }
    }
    /// <summary>
    /// Only for test
    /// </summary>
    /// <param name="fromState"></param>
    /// <param name="toState"></param>
    public async Task TransitFull(string fromState, string toState)
    {
        var path1 = GetFullTransitionSinglePath(fromState, toState);

        string? firstExit = path1.exitSinglePath.FirstOrDefault();
        string? firstEntry = path1.entrySinglePath.FirstOrDefault();

        if (firstExit != null)
            await TransitUp(firstExit.ToState(this) as CompoundState);
        
        if (firstEntry != null)
            await TransitDown(firstEntry.ToState(this) as CompoundState, toState);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    public static void Log(string message)
    {
        Logger.Debug(message);
    }

    /// <summary>
    /// 
    /// </summary>
    public void PrintCurrentStatesString()
    {
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Info, () => "=== Current States ===");
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Info, () => GetActiveStateString());
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Info, () => "======================");
    }

    /// <summary>
    /// 
    /// </summary>
    public void PrintCurrentStateTree()
    {
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Info, () => "=== Current State Tree ===");
        RootState?.PrintActiveStateTree(0);
        PerformanceOptimizations.LogOptimized(Logger.LogLevel.Info, () => "==========================");
    }
    
    /// <summary>
    /// Stop the state machine execution
    /// </summary>
    public void Stop()
    {
        _stateLock.EnterWriteLock();
        try
        {
            if (machineState == MachineState.Stopped)
            {
                Logger.Debug("State machine is already stopped");
                return;
            }
            
            machineState = MachineState.Stopped;
            
            // Clean up all active states and their timers
            if (RootState != null)
            {
                CleanupStateTimers(RootState);
            }
            
            // Cancel any invoke services
            serviceInvoker?.CancelAllServices();
            
            Logger.Info("State machine stopped");
        }
        finally
        {
            _stateLock.ExitWriteLock();
        }
    }
    
    /// <summary>
    /// Recursively clean up timers in all states
    /// </summary>
    private void CleanupStateTimers(StateNode? state)
    {
        if (state == null) return;
        
        if (state is RealState realState)
        {
            try
            {
                realState.CleanupAfterTimer();
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }
        
        // Recursively clean up child states
        if (state is CompoundState compoundState && compoundState.SubStateNames != null)
        {
            foreach (var childName in compoundState.SubStateNames)
            {
                try
                {
                    var childState = GetState(childName);
                    if (childState != null)
                    {
                        CleanupStateTimers(childState);
                    }
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }
    }
    
    /// <summary>
    /// Pause the state machine
    /// </summary>
    public void Pause()
    {
        _stateLock.EnterWriteLock();
        try
        {
            if (machineState != MachineState.Running)
            {
                Logger.Warning("Can only pause a running state machine");
                return;
            }
            
            machineState = MachineState.Paused;
            Logger.Info("State machine paused");
        }
        finally
        {
            _stateLock.ExitWriteLock();
        }
    }
    
    /// <summary>
    /// Resume the state machine from paused state
    /// </summary>
    public void Resume()
    {
        _stateLock.EnterWriteLock();
        try
        {
            if (machineState != MachineState.Paused)
            {
                Logger.Warning("Can only resume a paused state machine");
                return;
            }
            
            machineState = MachineState.Running;
            Logger.Info("State machine resumed");
        }
        finally
        {
            _stateLock.ExitWriteLock();
        }
    }
    
    private bool _disposed = false;
    
    /// <summary>
    /// Dispose of state machine resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
        {
            // Dispose managed resources
            _eventQueue?.Dispose();
            _sync?.Dispose();
            _stateLock?.Dispose();
            
            // Remove from global instance map
            if (machineId != null)
            {
                _instanceMap.TryRemove(machineId, out _);
            }
        }
        
        _disposed = true;
    }
}