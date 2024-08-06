
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
// [ ] Bug fix for MOUSE_MOVE event handling of original Diagramming Framework script (to be resizing state)
// [ ] Precise analysis of transition path including complex (parallel, history) states
//     [v] Simple case analysis (No history, No Parallel)
//     [ ] Simple case implementation (No history, No Parallel)
//     [ ] History case analysis (Shallow, Deep)
//     [ ] History case implementation (Shallow, Deep)
//     [ ] Parallel case analysis
//     [ ] Parallel case implementation
// [ ] Implement and prove by unittest self transition 
/////////////////////////////////////////////////////////////////////////
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SharpState;

//using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
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
    public static StateMachine GetInstance(string Id)
    {
        return _instanceMap.TryGetValue(Id, out _) ? _instanceMap[Id] : null;
    }

    public string machineId { set; get; }
    public State RootState { set; get; }
    private ConcurrentDictionary<string, StateBase> StateMap { set; get; }
    private ConcurrentDictionary<string, State> ActiveStateMap { set; get; }
    public ConcurrentDictionary<string, object> ContextMap { get; private set; }
    //public ConcurrentDictionary<string, System.Timers.Timer> TransitionTimers { private set; get; }
    public ActionMap ActionMap { set; get; }
    public GuardMap GuardMap { set; get; }

    //
    // This state machine map is for interact each other in a process.
    // For multiple machine interact each other in the range of multiple process or multiple hosts in the network, we need implement messaging mechanism.
    //

    public static Dictionary<string, StateMachine> _instanceMap = new();

    // OnTransition delegate definition
    public delegate void TransitionHandler(State fromState, StateBase toState, string eventName);
    public TransitionHandler OnTransition;



    private MachineState machineState = MachineState.Stopped;

    public StateMachine()
    {
        StateMap = new ConcurrentDictionary<string, StateBase>();
        ActiveStateMap = new ConcurrentDictionary<string, State>();
        ContextMap = new ConcurrentDictionary<string, object>();
        //TransitionTimers = new ConcurrentDictionary<string, System.Timers.Timer>();
    }

    public static StateMachine CreateFromFile(string jsonFilePath, ActionMap actionCallbacks, GuardMap guardCallbacks)
    {
        var jsonScript = File.ReadAllText(jsonFilePath);
        return ParseStateMachine(jsonScript, actionCallbacks, guardCallbacks);
    }

    public static StateMachine CreateFromScript(string jsonScript, ActionMap actionCallbacks = null, GuardMap guardCallbacks = null)
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

    public void InitializeCurrentStates()
    {
        Console.WriteLine(">>> Initialize ...");
        ActiveStateMap.Clear();
        RootState.InitializeCurrentStates();
    }

    public void PrintCurrentStatesString()
    {
        Console.WriteLine("=== Current States ===");
        Console.WriteLine(GetCurrentState());
        Console.WriteLine("=======================");
    }

    public void PrintCurrentStateTree()
    {
        Console.WriteLine("=== Current State Tree ===");
        RootState.PrintCurrentStateTree(0);
        Console.WriteLine("==========================");
    }

    public static string GenerateKey(string stateName, string eventName) => eventName;// + "1234567"; // stateName + '_' + eventName;

    /*
    void BuildTransitionTable(string eventName, Dictionary<string, List<Transition>> transitionsToProcess)
    {
        foreach (var current in CurrentStateMap.Keys)
        {
            var state = StateMap[current];

            var key = GenerateKay(current, eventName);

            foreach (var item in state.TransitionMap)
            {
                transitionsToProcess.Add(item); // always transition!
            }
            
        }
    }
    */

    public StateMachine Start()
    {
        if (machineState == MachineState.Running)
        {
            Console.WriteLine("State machine is already RUNNING!");
            return this;
        }
        RootState.Start();
        machineState = MachineState.Running;
        return this;
    }

    public StateMachine Paused()
    {
        if (machineState == MachineState.Paused)
        {
            Console.WriteLine("State machine is already PAUSED!");
            return this;
        }

        if (machineState == MachineState.Running)
        {
            machineState = MachineState.Paused;
            RootState.Pause();
        }

        return this;
    }

    public StateMachine Stop()
    {
        if (machineState == MachineState.Stopped)
        {
            Console.WriteLine("State machine is already STOPPED!");
            return this;
        }
        machineState = MachineState.Stopped;
        RootState.Stop();
        return this;
    }

    public StateMachine Reset()
    {
        InitializeCurrentStates();
        return this;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="eventName"></param>
    public void Send(string eventName)
    {

        Console.WriteLine($">>> Send event: {eventName}");

        if (machineState != MachineState.Running)
        {
            Console.WriteLine($"State machine is not RUNNING!");
            return;
        }

        if (eventName == "RESET")
        {
            Reset();
            return;
        }

        Transit(eventName);

        PrintCurrentStateTree();
        PrintCurrentStatesString();
    }

    void Transit(string eventName)
    {
        List<State> beforeTransitionStateList = new List<State>();

        foreach (var current in this.ActiveStateMap)
        {
            if (current.Value.OnTransitionMap.ContainsKey(eventName))
            {
                beforeTransitionStateList.Add(current.Value);
            }

            if (current.Value.AlwaysTransition != null)
            {
                beforeTransitionStateList.Add(current.Value);
            }

            if (current.Value.AfterTransition != null)
            {
                beforeTransitionStateList.Add(current.Value);
            }
        }

        foreach (var current in beforeTransitionStateList)
        {
            if (current.OnTransitionMap.ContainsKey(eventName))
            {
                var onTransitionList = current.OnTransitionMap[eventName];

                foreach (var transition in onTransitionList)
                {
                    if ((transition.Guard == null || transition.Guard.Func(this)) && (transition.InCondition == null || transition.InCondition()))
                    {
                        var exitList = transition.GetExitList();
                        var (entryList, historyType) = transition.GetEntryList();

                        // Exit
                        exitList.ForEach(state =>
                        {
                            state.ExitState();
                        });

                        State fromState = transition.Source;
                        StateBase toState = transition.Target;

                        // Transition
                        OnTransition?.Invoke(fromState, toState, eventName);
                        transition.Actions?.ForEach(action => action.Action(this));

                        // Entry
                        entryList.ForEach(state =>
                        {
                            state.EntryState(historyType);
                        });

                        break;
                    }
                    else
                    {
                        Console.WriteLine($"Condition not met for transition on event {eventName} in state {ActiveStateMap.First().Value.Name}");
                    }
                }
            }
            else
            {

                {
                    var transition = current.AfterTransition;
                    if (transition != null && (transition.Guard == null || transition.Guard.Func(this)) && (transition.InCondition == null || transition.InCondition()))
                    {
                        var exitList = transition.GetExitList();
                        var (entryList, historyType) = transition.GetEntryList();

                        // Exit
                        exitList.ForEach(state =>
                        {
                            state.ExitState();
                        });

                        State fromState = transition.Source;
                        StateBase toState = transition.Target;

                        // Transition
                        OnTransition?.Invoke(fromState, toState, eventName);
                        transition.Actions?.ForEach(action => action.Action(this));

                        // Entry
                        entryList.ForEach(state =>
                        {
                            state.EntryState(historyType);
                        });

                    }
                    else
                    {
                        Console.WriteLine($"Condition not met for transition on event {eventName} in state {ActiveStateMap.First().Value.Name}");
                    }
                }
                {

                    var transition = current.AlwaysTransition;
                    if (transition != null && (transition.Guard == null || transition.Guard.Func(this)) && (transition.InCondition == null || transition.InCondition()))
                    {
                        var exitList = transition.GetExitList();
                        var (entryList, historyType) = transition.GetEntryList();

                        // Exit
                        exitList.ForEach(state =>
                        {
                            state.ExitState();
                        });

                        State fromState = transition.Source;
                        StateBase toState = transition.Target;

                        // Transition
                        OnTransition?.Invoke(fromState, toState, eventName);
                        transition.Actions?.ForEach(action => action.Action(this));

                        // Entry
                        entryList.ForEach(state =>
                        {
                            state.EntryState(historyType);
                        });

                    }
                    else
                    {
                        Console.WriteLine($"Condition not met for transition on event {eventName} in state {ActiveStateMap.First().Value.Name}");
                    }
                }
            }

        }
    }

    public void AddState(State state)
    {
        StateMap[state.Name] = state;
    }

    public State GetState(string stateName)
    {
        return StateMap[stateName] as State;
    }

    public HistoryState GetStateAsHistory(string stateName)
    {
        return StateMap[stateName] as HistoryState;
    }


    public void AddCurrent(State state)
    {
        lock (this)
        {
            ActiveStateMap[state.Name] = state;
        }
    }

    public void RemoveCurrent(State state)
    {
        lock (this)
        {
            ActiveStateMap.TryRemove(state.Name, out _);
        }
    }

    public void RemoveCurrent(string stateName)
    {
        lock (this)
        {
            ActiveStateMap.TryRemove(stateName, out _);
        }
    }

    public bool TestCurrent(State state)
    {
        lock (this)
        {
            return ActiveStateMap.ContainsKey(state.Name);
        }
    }

    public bool TestActive(string stateName)
    {
        lock (this)
        {
            return ActiveStateMap.ContainsKey(stateName);
        }
    }

    public bool TestInitial(string stateName)
    {
        lock (this)
        {
            var state = StateMap[stateName] as State;
            return state.Parent != null && state.Parent.InitialStateName == state.Name;
        }
    }

    public bool TestHistory(string stateName)
    {
        lock (this)
        {
            return StateMap[stateName].GetType() == typeof(HistoryState);
        }
    }

    //private Func<StateMachine, bool> GetInCondition(string stateName)
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

    public static bool IsInState(StateMachine sm, string stateName)
    {
        return sm.ActiveStateMap.ContainsKey(stateName);
    }

    public string GetCurrentState()
    {
        // select only  the states have no children
        return ActiveStateMap.Values.ToCsvString();
    }

    public ICollection<State> GetSourceSubStateCollection(string statePath = null)
    {
        State state = null;

        if (statePath == null)
        {
            state = RootState;
        }
        else
        {
            state = GetState(statePath) as State;
        }

        ICollection<State> list = new List<State>();
        state.GetSouceSubStateCollection(list);

        return list;
    }

    

    public ICollection<State> GetTargetSubStateCollection(string statePath = null)
    {
        State state = null;

        if (statePath == null)
        {
            state = RootState;
        }
        else
        {
            state = GetState(statePath) as State;
        }

        ICollection<State> list = new List<State>();
        state.GetTargetSubStateCollection(list);

        return list;
    }

    public ICollection<State> GetSuperStateCollection(string statePath)
    {
        State state = null;

        state = GetState(statePath) as State;

        ICollection<State> list = new List<State>();
        state.GetSuperStateCollection(list);

        return list;
    }

    /*
    public JObject StateTreeToJson()
    {
        var json = StateToJson(RootState, Id, isRoot: true);
        if (ContextMap != null && ContextMap.Count > 0)
        {
            var contextJson = new JObject();
            foreach (var kvp in ContextMap)
            {
                contextJson[kvp.Key] = JToken.FromObject(kvp.Value);
            }
            json["context"] = contextJson;
        }
        return json;
    }
    
    private JObject StateToJson(State state, string id = null, bool isRoot = false)
    {
        var stateJson = new JObject();

        if (isRoot && id != null)
        {
            stateJson["id"] = id;
        }

        if (state.IsParallel)
        {
            stateJson["type"] = "parallel";
        }

        if (state.HistoryType == HistoryType.Shallow)
        {
            stateJson["history"] = "shallow";
        }
        else if (state.HistoryType == HistoryType.Deep)
        {
            stateJson["history"] = "deep";
        }

        if (!string.IsNullOrEmpty(state.InitialStateName))
        {
            stateJson["initial"] = state.InitialStateName;
        }

        if (state.EntryActions != null && state.EntryActions.Any())
        {
            stateJson["entry"] = new JArray(state.EntryActions.Select(a => a.Name));
        }

        if (state.ExitActions != null && state.ExitActions.Any())
        {
            stateJson["exit"] = new JArray(state.ExitActions.Select(a => a.Name));
        }

        if (state.OnTransitionMap != null && state.OnTransitionMap.Any())
        {
            var transitions = new JObject();
            foreach (var transitionList in state.OnTransitionMap)
            {
                var transitionJson = new JObject
                {
                    ["target"] = transitionList.Value.TargetName
                };

                if (transitionList.Value.Actions != null && transitionList.Value.Actions.Any())
                {
                    transitionJson["actions"] = new JArray(transitionList.Value.Actions.Select(a => a.Name));
                }

                if (!string.IsNullOrEmpty(transitionList.Value.Guard?.Name))
                {
                    transitionJson["guard"] = transitionList.Value.Guard.Name;
                }

                if (transitionList.Value.InCondition != null)
                {
                    transitionJson["in"] = transitionList.Value.InCondition.Method.Name;
                }

                transitions[transitionList.Key] = transitionJson;
            }
            stateJson["on"] = transitions;
        }

        if (state.AlwaysTransition != null)
        {
            var alwaysJson = new JObject
            {
                ["target"] = state.AlwaysTransition.TargetName
            };

            if (state.AlwaysTransition.Guard != null)
            {
                alwaysJson["guard"] = state.AlwaysTransition.Guard.Name;
            }

            stateJson["always"] = alwaysJson;
        }

        if (state.SubStateNames != null && state.SubStateNames.Any())
        {
            var subStatesJson = new JObject();
            foreach (var subStateName in state.SubStateNames)
            {
                var subState = StateMap[subStateName];
                subStatesJson[subStateName] = StateToJson(subState);
            }
            stateJson["states"] = subStatesJson;
        }

        // Remove null properties
        foreach (var property in stateJson.Properties().ToList())
        {
            if (property.Value.Type == JTokenType.Null)
            {
                property.Remove();
            }
        }

        return stateJson;
    }

    public bool ValidateStateMachineJson(JObject originalJson, out string errorMsg)
    {
        var reconstructedJson = StateTreeToJson();
        return CompareTokens(originalJson, reconstructedJson, out errorMsg);
    }

    private bool CompareTokens(JToken original, JToken reconstructed, out string errorMsg)
    {
        errorMsg = null;

        if (original.Type != reconstructed.Type)
        {
            errorMsg = $"Type mismatch: Original({original.Type}) vs Reconstructed({reconstructed.Type}) at Path {original.Path}";
            return false;
        }

        if (original is JObject originalObj && reconstructed is JObject reconstructedObj)
        {
            foreach (var property in originalObj.Properties())
            {
                if (!reconstructedObj.TryGetValue(property.Name, out var reconstructedValue))
                {
                    errorMsg = $"Property {property.Name} missing in reconstructed JSON at Path {original.Path}";
                    return false;
                }
                if (!CompareTokens(property.Value, reconstructedValue, out errorMsg))
                {
                    return false;
                }
            }

            foreach (var property in reconstructedObj.Properties())
            {
                if (!originalObj.TryGetValue(property.Name, out _))
                {
                    errorMsg = $"Property {property.Name} present in reconstructed JSON but missing in original JSON at Path {reconstructed.Path}";
                    return false;
                }
            }
        }
        else if (original is JArray originalArray && reconstructed is JArray reconstructedArray)
        {
            if (originalArray.Count != reconstructedArray.Count)
            {
                errorMsg = $"Array length mismatch: Original({originalArray.Count}) vs Reconstructed({reconstructedArray.Count}) at Path {original.Path}";
                return false;
            }

            for (int i = 0; i < originalArray.Count; i++)
            {
                if (!CompareTokens(originalArray[i], reconstructedArray[i], out errorMsg))
                {
                    return false;
                }
            }
        }
        else if (!JToken.DeepEquals(original, reconstructed))
        {
            errorMsg = $"Value mismatch: Original({original}) vs Reconstructed({reconstructed}) at Path {original.Path}";
            return false;
        }

        return true;
    }

    public void PrintReconstructedJson()
    {
        var reconstructedJson = StateTreeToJson();
        Console.WriteLine(reconstructedJson.ToString(Newtonsoft.Json.Formatting.Indented));
    }
    */
}
