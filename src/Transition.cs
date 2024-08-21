using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace XStateNet;

public enum TransitionType
{
    On,
    Always,
    After,
    OnDone
}

public class TransitionExecutor : StateObject
{
    public TransitionExecutor(string? machineId) : base(machineId) { }

    public async void Execute(Transition transition, string eventName)
    {
        if (transition == null) return;

        StateMachine.Log($">> transition on event {eventName} in state {transition.SourceName}");

        if ((transition.Guard == null || transition.Guard.Predicate(StateMachine))
            && (transition.InCondition == null || transition.InCondition()))
        {

            string sourceName = transition.SourceName;
            string? targetName = transition.TargetName;

            if (targetName != null)
            {
                var (exitList, entryList) = StateMachine.GetFullTransitionSinglePath(transition.SourceName, targetName);

                string? firstExit = exitList.First();
                string? firstEntry = entryList.First();

                // Exit

                await StateMachine.TransitUp(firstExit?.ToState(StateMachine) as RealState);

                StateMachine.Log($"Transit: [ {sourceName} --> {targetName} ] by {eventName}");

                // Transition
                RealState? source = GetState(sourceName) as RealState;
                StateBase? target = GetState(targetName);


                StateMachine.OnTransition?.Invoke(source, target, eventName);

                if (transition.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        action.Action(StateMachine);
                    }
                }

                // Entry

                await StateMachine.TransitDown(firstEntry?.ToState(StateMachine) as RealState, targetName);
            }
            else
            {
                // action only transition

                if (transition.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        action.Action(StateMachine);
                    }
                }
            }
        }
        else
        {
            StateMachine.Log($"Condition not met for transition on event {eventName}");
        }
    }
}

public abstract class Transition : StateObject
{

    public string? SourceName { get; set; }
    public string? TargetName { get; set; }
    public NamedGuard? Guard { get; set; }
    public List<NamedAction>? Actions { get; set; }
    public Func<bool>? InCondition { get; set; }


    public RealState? Source {
        get {
            if(SourceName == null) throw new Exception("SourceName is null");
            if(StateMachine == null) throw new Exception("StateMachine is null");
            return (RealState)StateMachine.GetState(SourceName); 
        }
    }  
    // can not be null any case. Source never be history state
    public StateBase? Target {
        get {
            if (TargetName != null)
            {
                if(StateMachine == null) throw new Exception("StateMachine is null");
                return StateMachine.GetState(TargetName);       // can be null if targetless transition. Target can be history state
                /*
                return TargetName.Contains(".hist") 
                    ? StateMachine.GetStateAsHistory(TargetName) 
                    : StateMachine.GetState(TargetName);       // can be null if targetless transition. Target can be history state
                */
            }
            else
            {
                return null;
            }
        }
    }
    
    public Transition(string? machineId) : base(machineId){}
}

public class OnTransition : Transition
{
    public string? Event { get; set; }
    public OnTransition(string? machineId) : base(machineId){}
}

public class AfterTransition : Transition
{
    public int Delay { get; set; }
    public AfterTransition(string? machineId) : base(machineId){}
}

public class AlwaysTransition : Transition
{
    public AlwaysTransition(string? machineId) : base(machineId){}
}

public class OnDoneTransition : Transition
{
    public OnDoneTransition(string? machineId) : base(machineId){}
}

// Need to understand this code
internal record struct NewStruct(string? targetName, HistoryType historyType)
{
    public static implicit operator (string? targetName, HistoryType historyType)(NewStruct value)
    {
        return (value.targetName, value.historyType);
    }

    public static implicit operator NewStruct((string? targetName, HistoryType historyType) value)
    {
        return new NewStruct(value.targetName, value.historyType);
    }
}