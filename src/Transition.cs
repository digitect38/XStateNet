using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpState;

public enum TransitionType
{
    On,
    Always,
    After
}

public abstract class Transition
{
    public string stateMachineId;
    public string SourceName { get; set; }
    public string TargetName { get; set; }
    public NamedGuard Guard { get; set; }
    public List<NamedAction> Actions { get; set; }
    public Func<bool> InCondition { get; set; }
    public StateMachine StateMachine => StateMachine.GetInstance(stateMachineId);

    public State Source => SourceName != null ? StateMachine.GetState(SourceName) as State : null;   // can not be null any case. Source never be history state
    public StateBase Target {
        get {
            if (TargetName != null)
            {
                return TargetName.Contains(".hist") 
                    ? StateMachine.GetStateAsHistory(TargetName) 
                    : StateMachine.GetState(TargetName);       // can be null if targetless transition. Target can be history state
            }
            else
            {
                return null;
            }
        }
    }


    public List<State> GetExitList()
    {
        if (Target != null)
            return GetExitList(SourceName, TargetName);
        else
            return new List<State>();
    }

    (string, HistoryType) ConvertHistoryState(string targetName)
    {
        var historyType = HistoryType.None;
        if (targetName != null)
        {
            if (targetName?.Split('.').Last() == "hist")
            {
                var historyState = StateMachine.GetStateAsHistory(targetName);
                historyType = historyState.HistoryType;
                targetName = historyState.ParentName;
            }
        }
        return (targetName, historyType);
    }

    public (List<StateBase> stateList, HistoryType historyType) GetEntryList()
    {
        if (Target != null)
            return GetEntryList(SourceName, TargetName);
        else
            return (new List<StateBase>(), HistoryType.None);
    }

    List<State> GetExitList(string source, string target)
    {
        (target, HistoryType historyType) = ConvertHistoryState(target);
        var exitList = new List<State>();
        if (source == target) return exitList;

        var sourceState = StateMachine.GetState(source) as State;
        var targetState = StateMachine.GetState(target) as State;
        var commonAncestor = FindCommonAncestor(sourceState, targetState);

        // Traverse from source to common ancestor, including current child states
        var currentState = sourceState;
        while (currentState != null && currentState != commonAncestor)
        {
            exitList.Add(currentState);
            exitList.AddRange(GetActiveSubStates(currentState, includeSelf: false));
            currentState = currentState.Parent;
        }

        return exitList;
    }

    void GetHistoryEntryList(List<StateBase> entryList, string stateName, HistoryType historyType = HistoryType.None)
    {
        var state = StateMachine.GetState(stateName) as State;
        entryList.Add(state);

        if (state.IsParallel)
        {
            state.SubStateNames.ForEach(
                subStateName =>
                {
                    var subState = StateMachine.GetState(subStateName);
                    GetHistoryEntryList(entryList, subStateName);
                }
            );
        }
        if (historyType != HistoryType.None && state.LastActiveStateName != null)
        {
            if (historyType == HistoryType.Deep)
            {
                GetHistoryEntryList(entryList, state.LastActiveStateName, historyType);
            }
            else
            {
                GetHistoryEntryList(entryList, state.LastActiveStateName);
            }
        }
        else if (state.InitialStateName != null)
        {
            var subStateName = state.InitialStateName;
            GetHistoryEntryList(entryList, subStateName);
        }
    }

    (List<StateBase> stateList, HistoryType historytype) GetEntryList(string source, string target)
    {
        (target, HistoryType historyType) = ConvertHistoryState(target);

        var entryList = new List<StateBase>();
        if (source == target) return (entryList, historyType);

        var sourceState = StateMachine.GetState(source) as State;
        var targetState = StateMachine.GetState(target) as State;
        var commonAncestor = FindCommonAncestor(sourceState, targetState);

        // Traverse from common ancestor to target, including child states
        var targetPath = new Stack<State>();
        var currentState = targetState;
        while (currentState != null && currentState != commonAncestor)
        {
            targetPath.Push(currentState);
            currentState = currentState.Parent;
        }

        while (targetPath.Count > 0)
        {
            currentState = targetPath.Pop();


            entryList.Add(currentState);
            if (targetPath.Count == 0)
            {
                entryList.AddRange(GetLastActiveStates(currentState, historyType));
            }
        }

        return (entryList, historyType);
    }

    private State FindCommonAncestor(State state1, State state2)
    {
        var ancestors1 = new HashSet<State>();

        while (state1 != null)
        {
            ancestors1.Add(state1);
            if (state1.Parent == null) break;
            if (state1.Parent.IsParallel) break;
            state1 = state1.Parent;
        }

        while (state2 != null)
        {
            if (ancestors1.Contains(state2)) return state2;
            state2 = state2.Parent;
        }

        return null; // Shouldn't happen if both states are in the same state machine
    }

    private List<State> GetActiveSubStates(State state, bool includeSelf = true)
    {
        var activeSubStates = new List<State>();

        if (includeSelf)
        {
            activeSubStates.Add(state);
        }
        foreach (var subStateName in state.CurrentSubStateNames)
        {
            var subState = StateMachine.GetState(subStateName) as State;
            activeSubStates.Add(subState);
            activeSubStates.AddRange(GetActiveSubStates(subState, includeSelf: false));
        }
        return activeSubStates;
    }

    private List<State> GetInitialStates(State state)
    {
        var initialStates = new List<State>();
        var initialSubState = StateMachine.GetState(state.InitialStateName) as State;
        initialStates.Add(initialSubState);
        initialStates.AddRange(GetInitialStates(initialSubState));
        return initialStates;
    }

    private List<State> GetLastActiveStates(State state, HistoryType historyType = HistoryType.None)
    {
        var lastActiveStates = new List<State>();

        if (state.IsParallel)
        {
            foreach (var subStateName in state.SubStateNames)
            {
                var subState = StateMachine.GetState(subStateName);
                lastActiveStates.Add(subState);
                lastActiveStates.AddRange(GetLastActiveStates(subState, historyType));
            }
        }
        else
        {
            if (historyType == HistoryType.None)
            {
                if (state.InitialStateName != null)
                {
                    var initialState = StateMachine.GetState(state.InitialStateName);
                    lastActiveStates.Add(initialState);
                    lastActiveStates.AddRange(GetLastActiveStates(initialState));
                }
            }
            else
            {
                if (state.LastActiveStateName != null)
                {
                    var lastActiveState = StateMachine.GetState(state.LastActiveStateName);

                    lastActiveStates.Add(lastActiveState);
                    lastActiveStates.AddRange(GetLastActiveStates(lastActiveState, historyType));
                }
            }
        }
        return lastActiveStates;
    }


}

public class OnTransition : Transition
{
    public string Event { get; set; }
}

public class AfterTransition : Transition
{
    public int Delay;
}
public class AlwaysTransition : Transition
{
}

