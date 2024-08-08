using System;
using System.Collections.Generic;
using System.Linq;

namespace XStateNet;

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
    public string? TargetName { get; set; }
    public NamedGuard? Guard { get; set; }
    public List<NamedAction>? Actions { get; set; }
    public Func<bool>? InCondition { get; set; }
    public StateMachine StateMachine => StateMachine.GetInstance(stateMachineId);

    public State Source => StateMachine.GetState(SourceName) as State;   // can not be null any case. Source never be history state
    public AbstractState? Target {
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
    /*
    public List<State> GetExitList()
    {
        if (Target != null)
            return GetExitEntryList(SourceName, TargetName);
        else
            return new List<State>();
    }
    */

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
    /*
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
    */


    /*
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
    */  

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

