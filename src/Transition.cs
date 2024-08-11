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

    public RealState Source => (RealState)StateMachine.GetState(SourceName);   // can not be null any case. Source never be history state
    public StateBase? Target {
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

    (string?, HistoryType) ConvertHistoryToNormalState(string targetName)
    {
        var historyType = HistoryType.None;

        if (targetName != null)
        {
            if (targetName?.Split('.').Last() == "hist")
            {
                var historyState = StateMachine.GetStateAsHistory(targetName);
                
                if (historyState == null) throw new Exception($"History state {targetName} not found");
                historyType = historyState.HistoryType;

                if(historyState.ParentName == null) throw new Exception($"History state {targetName} has no parent");
                targetName = historyState.ParentName;
            }
            return new NewStruct(targetName, historyType);
        }
        else
        {
            throw new Exception("TargetName is null");
        }
    }
    
}

public class OnTransition : Transition
{
    public string Event { get; set; }
}

public class AfterTransition : Transition
{
    public int Delay { get; set; }
}
public class AlwaysTransition : Transition
{
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