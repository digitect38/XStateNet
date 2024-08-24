
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

namespace XStateNet;

using ActionMap = ConcurrentDictionary<string, List<NamedAction>>;
using GuardMap = ConcurrentDictionary<string, NamedGuard>;
using ServiceMap = ConcurrentDictionary<string, NamedService>;

enum MachineState
{
    Running,
    Paused,
    Stopped
}

public partial class StateMachine
{
    public static StateMachine? GetInstance(string Id)
    {
        return _instanceMap.TryGetValue(Id, out _) ? _instanceMap[Id] : null;
    }

    public string? machineId { set; get; }
    public CompoundState? RootState { set; get; }
    private ConcurrentDictionary<string, StateNode>? StateMap { set; get; }
    public ConcurrentDictionary<string, object>? ContextMap { get; private set; } // use object because context can have various types of data
    
    public ActionMap? ActionMap { set; get; }
    public GuardMap? GuardMap { set; get; }
    public ServiceMap? ServiceMap { set; get; }

    public TransitionExecutor transitionExecutor { private set; get; }

    //
    // This state machine map is for interact each other in a process.
    // For multiple machine interact each other in the range of multiple process or multiple hosts in the network, we need implement messaging mechanism.
    //

    public static Dictionary<string, StateMachine> _instanceMap = new();

    // OnTransition delegate definition
    public delegate void TransitionHandler(CompoundState? fromState, StateNode? toState, string eventName);
    public TransitionHandler? OnTransition;
    private MachineState machineState = MachineState.Stopped;

    /// <summary>
    /// 
    /// </summary>
    public StateMachine()
    {
        StateMap = new ConcurrentDictionary<string, StateNode>();
        ContextMap = new ConcurrentDictionary<string, object>();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="jsonFilePath"></param>
    /// <param name="actionCallbacks"></param>
    /// <param name="guardCallbacks"></param>
    /// <returns></returns>
    public static StateMachine CreateFromFile(string jsonFilePath, ActionMap? actionCallbacks = null, GuardMap? guardCallbacks = null, ServiceMap? serviceCallbacks = null)
    {
        var jsonScript = File.ReadAllText(jsonFilePath);
        return ParseStateMachine(jsonScript, actionCallbacks, guardCallbacks, serviceCallbacks);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="jsonScript"></param>
    /// <param name="actionCallbacks"></param>
    /// <param name="guardCallbacks"></param>
    /// <returns></returns>
    public static StateMachine CreateFromScript(string? jsonScript, ActionMap? actionCallbacks = null, GuardMap? guardCallbacks = null, ServiceMap? serviceCallbacks = null)
    {
        return ParseStateMachine(jsonScript, actionCallbacks, guardCallbacks, serviceCallbacks);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sm"></param>
    /// <param name="jsonFilePath"></param>
    /// <param name="actionCallbacks"></param>
    /// <param name="guardCallbacks"></param>
    /// <returns></returns>
    public static StateMachine CreateFromFile(StateMachine sm, string jsonFilePath, ActionMap? actionCallbacks = null, GuardMap? guardCallbacks = null, ServiceMap? serviceCallbacks = null)
    {
        var jsonScript = File.ReadAllText(jsonFilePath);
        return ParseStateMachine(sm, jsonScript, actionCallbacks, guardCallbacks, serviceCallbacks);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sm"></param>
    /// <param name="jsonScript"></param>
    /// <param name="actionCallbacks"></param>
    /// <param name="guardCallbacks"></param>
    /// <returns></returns>
    public static StateMachine CreateFromScript(StateMachine sm, string jsonScript, ActionMap? actionCallbacks = null, GuardMap? guardCallbacks = null, ServiceMap? serviceCallbacks = null)
    {
        return ParseStateMachine(sm, jsonScript, actionCallbacks, guardCallbacks, serviceCallbacks);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="state"></param>
    public void RegisterState(StateNode state)
    {
        if (StateMap != null)
        {
            StateMap[state.Name] = state;
        }
        else
        {
            throw new Exception("StateMap is not initialized");
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public StateMachine Start()
    {
        StateMachine.Log(">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");
        StateMachine.Log(">>> Start state machine");
        StateMachine.Log(">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");

        if (machineState == MachineState.Running)
        {
            StateMachine.Log("State machine is already RUNNING!");
            return this;
        }
#if false
        RootState?.Start();
#else
        transitionExecutor = new TransitionExecutor(machineId);
    var list = GetEntryList(machineId);
        string entry = list.ToCsvString(this, false, " -> ");
        
        Log($">>> Start entry: {entry}");

        foreach (var stateName in list)
        {            
            var state = GetState(stateName) as CompoundState;
            state?.EntryState();
        }      
#endif   

        machineState = MachineState.Running;
        return this;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="eventName"></param>
    public void Send(string eventName)
    {

        StateMachine.Log($">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");
        StateMachine.Log($">>> Send event: {eventName}");
        StateMachine.Log($">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");

        if (machineState != MachineState.Running)
        {
            StateMachine.Log($"State machine is not RUNNING!");
            return;
        }

        Transit(eventName);

        PrintCurrentStateTree();
        PrintCurrentStatesString();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="eventName"></param>
    void Transit(string eventName)
    {
        var transitionList = new List<(CompoundState state, Transition transition, string @event)>();

        RootState?.BuildTransitionList(eventName, transitionList);


        //step 1:  build transition list

        StateMachine.Log("***** Transition list *****");
        foreach (var t in transitionList)
        {
            StateMachine.Log($"Transition : (source = {t.transition?.SourceName}, target =  {t.transition?.TargetName}, event =  {t.@event}");
        }
        StateMachine.Log("***************************");

        //step 2:  perform transitions

        foreach (var (state, transition, @event) in transitionList)
        {
            transitionExecutor.Execute(transition, @event);
        }
    }

    public List<string> GetEntryList(string target) // for Start
    {
        StateMachine.Log(">>> - GetEntryList");


        var target_sub = GetTargetSubStateCollection(target);
        StateMachine.Log($">>> -- target_sub: {target_sub.ToCsvString(this, false, " -> ")}");
                
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
        StateMachine.Log(">>> - GetExitEntryList");

        var source_sub = GetSourceSubStateCollection(source).Reverse();
        StateMachine.Log($">>> -- source_sub: {source_sub.ToCsvString(this, false, " -> ")}");

        var source_sup = GetSuperStateCollection(source);
        StateMachine.Log($">>> -- source_sup: {source_sup.ToCsvString(this, false, " -> ")}");

        var source_cat = source_sub.Concat(source_sup);
        StateMachine.Log($">>> -- source_cat: {source_cat.ToCsvString(this, false, " -> ")}");

        var target_sub = GetTargetSubStateCollection(target);
        StateMachine.Log($">>> -- target_sub: {target_sub.ToCsvString(this, false, " -> ")}");

        var target_sup = GetSuperStateCollection(target).Reverse();
        StateMachine.Log($">>> -- target_sup: {target_sup.ToCsvString(this, false, " -> ")}");

        var target_cat = target_sup.Concat(target_sub);
        StateMachine.Log($">>> -- target_cat: {target_cat.ToCsvString(this, false, " -> ")}");

        var source_exit = source_cat.Except(target_cat);    // exclude common ancestors from source
        var target_entry = target_cat.Except(source_cat);   // exclude common ancestors from source

        StateMachine.Log($">>> -- source_exit: {source_exit.ToCsvString(this, false, " -> ")}");
        StateMachine.Log($">>> -- target_entry: {target_entry.ToCsvString(this, false, " -> ")}");

        return (source_exit.ToList(), target_entry.ToList());
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="stateName"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public StateNode GetState(string? stateName)
    {
        StateNode? state = null;

        if(stateName == null) throw new Exception("State name is null!");
        StateMap?.TryGetValue(stateName, out state);

        if (state == null)
        {
            throw new Exception($"State name {stateName} is not found in the StateMap");
        }

        return state;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="stateName"></param>
    /// <returns></returns>
    public HistoryState? GetStateAsHistory(string stateName)
    {
        if (StateMap == null) throw new Exception("StateMap is not initialized");
        return StateMap[stateName] as HistoryState;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="inConditionString"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private Func<bool> GetInConditionCallback(string inConditionString)
    {
        string stateMachineId = inConditionString.Split('.')[0];
        if (stateMachineId != machineId)
        {

            StateMachine? sm = GetInstance(stateMachineId);
            if (sm == null) throw new Exception($"State machine is not found with id, {stateMachineId}");

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
        var state = (GetState(stateName) as CompoundState);
        if(state == null) throw new Exception($"State is not found with name, {stateName}");

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
        List<string> strings = new();
        RootState?.GetActiveSubStateNames(strings);
        return strings.ToCsvString(this, leafOnly, separator);
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
            state = GetState(stateName) as CompoundState;
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

    public (ICollection<string> exitSinglePath, ICollection<string> entrySinglePath)  GetFullTransitionSinglePath(string? srcStateName, string? tgtStateName)
    {
        StateMachine.Log(">>> - GetFullTransitionPath");
                
        //1.
        var subExitPath = GetSourceSubStateCollection(srcStateName, true);
        StateMachine.Log($">>> -- subExitPath: {subExitPath.ToCsvString(this, false, " -> ")}");

        var supExitPath = GetSuperStateCollection(srcStateName);
        StateMachine.Log($">>> -- supExitPath: {supExitPath.ToCsvString(this, false, " -> ")}");

        var fullExitPath = supExitPath.Reverse().Concat(subExitPath);
        StateMachine.Log($">>> -- fullExitPath: {fullExitPath.ToCsvString(this, false, " -> ")}");

        //2.
        var supEntryPath = GetSuperStateCollection(tgtStateName);
        StateMachine.Log($">>> -- supEntryPath: {supEntryPath.ToCsvString(this, false, " -> ")}");
        
        var subEntryPath = GetTargetSubStateCollection(tgtStateName, true);
        StateMachine.Log($">>> -- subEntryPath: {subEntryPath.ToCsvString(this, false, " -> ")}");

        var fullEntryPath = supEntryPath.Reverse().Concat(subEntryPath);
        StateMachine.Log($">>> -- fullEntryPath: {fullEntryPath.ToCsvString(this, false, " -> ")}");


        // 3.
        var actualExitPath = fullExitPath.Except(fullEntryPath);    // top down
        StateMachine.Log($">>> -- actualExitPath: {actualExitPath.ToCsvString(this, false, " -> ")}");

        var actualEntryPath = fullEntryPath.Except(fullExitPath);   // top down
        StateMachine.Log($">>> -- actualEntryPath: {actualEntryPath.ToCsvString(this, false, " -> ")}");

        return (actualExitPath.ToList(), actualEntryPath.ToList());
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="topExitState"></param>
    public Task TransitUp(CompoundState? topExitState)
    {
        if(topExitState != null)
            topExitState.ExitState(postAction: true, recursive: true);
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
            topEntryState.EntryState(postAction: false, recursive: true, HistoryType.None, historyState);
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
        CompoundState? state = null;

        ICollection<string> list = new List<string>();

        if (GetState(statePath) is CompoundState realState)
        {
            state = realState;

            state?.GetTargetSubStateCollection(list, singleBranchPath);
        }
        else if (GetState(statePath) is HistoryState historyState) 
        {
            if (historyState.Parent is NormalState)
            {
                state = ((NormalState)historyState.Parent).LastActiveState;

                state?.GetTargetSubStateCollection(list, singleBranchPath, historyState.HistoryType);
            }
            else
            {
                throw new Exception("History state should be child of Normal state");
            }
        }
        else
        {
            throw new Exception("State should be RealState or HistoryState type");
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
        CompoundState? state = null;

        if (statePath == null) return null;

        state = GetState(statePath) as CompoundState;

        if (statePath == null)
        {
            state = RootState;
        }
        else
        {
            if (GetState(statePath) is CompoundState realState)
            {
                state = realState;
            }
            else if (GetState(statePath) is HistoryState historyState)
            {
                if (historyState.Parent is NormalState)
                {
                    state = ((NormalState)historyState.Parent).LastActiveState;
                }
                else
                {
                    throw new Exception("History state should be child of Normal state");
                }
            }
            else
            {
                throw new Exception("State should be RealState or HistoryState type");
            }
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
    public async void TransitFull(Transition transition, string @event)
    {
        var fromState = transition.SourceName;
        string? toState = transition.TargetName;

        var path1 = GetFullTransitionSinglePath(fromState, toState);

        string? firstExit = path1.exitSinglePath.First();
        string? firstEntry = path1.entrySinglePath.First();

        if ((transition.Guard == null || transition.Guard.Predicate(this))
            && (transition.InCondition == null || transition.InCondition()))
        {
            if (toState != null)
            {
                await TransitUp(firstExit?.ToState(this) as CompoundState);
                transition.Actions?.ForEach(action => action.Action(this));
                await TransitDown(firstEntry?.ToState(this) as CompoundState, toState);
            }

            else
            {
                // action only transition

                if (transition.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        action.Action(this);
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
    public async void TransitFull(string fromState, string toState)
    {

        var path1 = GetFullTransitionSinglePath(fromState, toState);

        string? firstExit = path1.exitSinglePath.First();
        string? firstEntry = path1.entrySinglePath.First();

        await TransitUp(firstExit?.ToState(this) as CompoundState);
        await TransitDown(firstEntry?.ToState(this) as CompoundState, toState);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    public static void Log(string message)
    {
        Console.WriteLine(message);
    }

    /// <summary>
    /// 
    /// </summary>
    public void PrintCurrentStatesString()
    {
        StateMachine.Log("=== Current States ===");
        StateMachine.Log(GetActiveStateString());
        StateMachine.Log("======================");
    }

    /// <summary>
    /// 
    /// </summary>
    public void PrintCurrentStateTree()
    {
        StateMachine.Log("=== Current State Tree ===");
        RootState?.PrintActiveStateTree(0);
        StateMachine.Log("==========================");
    }
}
