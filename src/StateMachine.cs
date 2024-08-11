
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

    public string machineId { set; get; }
    public RealState RootState { set; get; }
    private ConcurrentDictionary<string, StateBase> StateMap { set; get; }
    public ConcurrentDictionary<string, object> ContextMap { get; private set; } // use object because context can have various types of data
    public ActionMap? ActionMap { set; get; }
    public GuardMap? GuardMap { set; get; }

    //
    // This state machine map is for interact each other in a process.
    // For multiple machine interact each other in the range of multiple process or multiple hosts in the network, we need implement messaging mechanism.
    //

    public static Dictionary<string, StateMachine> _instanceMap = new();

    // OnTransition delegate definition
    public delegate void TransitionHandler(RealState fromState, StateBase toState, string eventName);
    public TransitionHandler OnTransition;



    private MachineState machineState = MachineState.Stopped;

    public StateMachine()
    {
        StateMap = new ConcurrentDictionary<string, StateBase>();
        ContextMap = new ConcurrentDictionary<string, object>();
    }

    public static StateMachine CreateFromFile(string jsonFilePath, ActionMap actionCallbacks, GuardMap guardCallbacks)
    {
        var jsonScript = File.ReadAllText(jsonFilePath);
        return ParseStateMachine(jsonScript, actionCallbacks, guardCallbacks);
    }

    public static StateMachine CreateFromScript(string jsonScript, ActionMap? actionCallbacks = null, GuardMap? guardCallbacks = null)
    {
        return ParseStateMachine(jsonScript, actionCallbacks, guardCallbacks);
    }

    public static StateMachine CreateFromFile(StateMachine sm, string jsonFilePath, ActionMap actionCallbacks, GuardMap guardCallbacks)
    {
        var jsonScript = File.ReadAllText(jsonFilePath);
        return ParseStateMachine(sm, jsonScript, actionCallbacks, guardCallbacks);
    }

    public static StateMachine CreateFromScript(StateMachine sm, string jsonScript, ActionMap actionCallbacks, GuardMap guardCallbacks)
    {
        return ParseStateMachine(sm, jsonScript, actionCallbacks, guardCallbacks);
    }

    public void RegisterState(StateBase state) => StateMap[state.Name] = state;
        
    public void PrintCurrentStatesString()
    {
        StateMachine.Log("=== Current States ===");
        StateMachine.Log(GetActiveStateString());
        StateMachine.Log("=======================");
    }

    public void PrintCurrentStateTree()
    {
        StateMachine.Log("=== Current State Tree ===");
        RootState.PrintActiveStateTree(0);
        StateMachine.Log("==========================");
    }

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
        RootState.Start();
        machineState = MachineState.Running;
        return this;
    }

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

    void Transit(string eventName)
    {
        var transitionList = new List<(RealState state, Transition transition, string @event)>();

        RootState.BuildTransitionList(eventName, transitionList);


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
            Transit(state, transition, @event);
        }
    }

    void Transit(RealState state, Transition? transition, string eventName)
    {
        if (transition == null) return;

        StateMachine.Log($">> transition on event {eventName} in state {state.Name}");

        if ((transition.Guard == null || transition.Guard.Predicate(this))
            && (transition.InCondition == null || transition.InCondition()))
        {

            string sourceName = transition.SourceName;
            string? targetName = transition.TargetName;

            if (targetName != null)
            {
                var (exitList, entryList) = GetExitEntryList(transition.SourceName, targetName);

                // Exit
                foreach (var stateName in exitList)
                {
                    ((RealState)GetState(stateName)).ExitState();
                }

                StateMachine.Log($"Transit: [ {sourceName} --> {targetName} ] by {eventName}");

                // Transition
                RealState? source = GetState(sourceName) as RealState;
                StateBase? target = GetState(targetName) is HistoryState ? GetStateAsHistory(targetName) : GetState(targetName);


                if (GetState(targetName) is HistoryState)
                {
                    target = GetState(targetName) is HistoryState ? GetStateAsHistory(targetName) : GetState(targetName);
                }

                OnTransition?.Invoke(source, target, eventName);

                if (transition.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        action.Action(this);
                    }
                }

                // Entry
                foreach (var stateName in entryList)
                {
                    ((RealState)GetState(stateName)).EntryState();
                }
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
        else
        {
            StateMachine.Log($"Condition not met for transition on event {eventName}");
        }
    }

    // check if the state is targe or ancestor of the this state
    bool IsAncestorOf(StateBase self, StateBase state)
    {
        if (self == state) return true;

        if (self is RealState realState)
        {
            return realState.IsAncestorOf(state);
        }

        return false;
    }


    (List<string> exits, List<string> entrys) GetExitEntryList(string source, string target)
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

    public void AddState(RealState state)
    {
        StateMap[state.Name] = state;
    }

    public StateBase GetState(string stateName)
    {
        StateBase? state;

        StateMap.TryGetValue(stateName, out state);

        if (state == null)
        {
            throw new Exception($"State name {stateName} is not found in the StateMap");
        }

        return state;
    }

    public HistoryState? GetStateAsHistory(string stateName)
    {
        return StateMap[stateName] as HistoryState;
    }
    
    public bool TestInitial(string stateName)
    {
        lock (this)
        {
            var state = StateMap[stateName] as RealState;
            return state?.Parent != null && state.Parent.InitialStateName == state.Name;
        }
    }

    public bool TestHistory(string stateName)
    {
        lock (this)
        {
            return StateMap[stateName].GetType() == typeof(HistoryState);
        }
    }

    private Func<bool> GetInConditionCallback(string inConditionString)
    {
        string stateMachineId = inConditionString.Split('.')[0];
        if (stateMachineId != machineId)
        {

            StateMachine sm = StateMachine.GetInstance(stateMachineId);
            return () => IsInState(sm, inConditionString);
        }
        else
        {
            return () => IsInState(this, inConditionString);
        }
    }

    public bool IsInState(StateMachine sm, string stateName)
    {
        var state = (GetState(stateName) as RealState);
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
        RootState.GetActiveSubStateNames(strings);
        return strings.ToCsvString(this, leafOnly, separator);
    }

    public ICollection<string> GetSourceSubStateCollection(string? statePath = null)
    {
        RealState? state = null;

        if (statePath == null)
        {
            state = RootState;
        }
        else
        {
            state = GetState(statePath) as RealState;
        }

        ICollection<string> list = new List<string>();
        state?.GetSouceSubStateCollection(list);

        return list;
    }

    public ICollection<string> GetTargetSubStateCollection(string? statePath = null)
    {
        RealState? state = null;

        ICollection<string> list = new List<string>();

        if (statePath == null)
        {
            state = RootState;
        }
        else
        {
            if (GetState(statePath) is RealState realState)
            {
                state = realState;

                state?.GetTargetSubStateCollection(list);
            }
            else if (GetState(statePath) is HistoryState historyState)
            {
                if (historyState.Parent is NormalState)
                {
                    state = ((NormalState)historyState.Parent).LastActiveState;

                    state?.GetTargetSubStateCollection(list, historyState.HistoryType);
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
    public ICollection<string> GetSuperStateCollection(string statePath)
    {
        RealState? state = null;

        state = GetState(statePath) as RealState;

        if (statePath == null)
        {
            state = RootState;
        }
        else
        {
            if (GetState(statePath) is RealState realState)
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

    public static void Log(string message)
    {
        Console.WriteLine(message);
    }
}
