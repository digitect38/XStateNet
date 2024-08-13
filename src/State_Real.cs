using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace XStateNet;

/// <summary>
/// Notes : 
/// Normal and Parallel states can be active or not. 
/// Normal state can have an initial state.
/// Parallel state can not have initial state. But have all as active states
/// Parallel state is defined here, as the state has multiple parallel sub states.
/// </summary>
public abstract class RealState : StateBase
{
    bool onDone = false;
    public bool IsInitial => Parent != null && Parent.InitialStateName == Name;
    public bool IsActive { set; get; }

    public ConcurrentDictionary<string, List<Transition>> OnTransitionMap { get; set; }

    public AfterTransition? AfterTransition { get; set; }   // Added for after transitions
    public AlwaysTransition? AlwaysTransition { get; set; } // Added for always transitions
    public OnDoneTransition? OnDoneTransition { get; set; } // Added for onDone transitions

    public List<NamedAction>? EntryActions { get; set; }
    public List<NamedAction>? ExitActions { get; set; }

    public List<string> SubStateNames { get; set; }         // state 의 current sub state 들..

    public string? InitialStateName { get; set; }
    
    public string? ActiveStateName { get; set; }
    public RealState? ActiveState => ActiveStateName != null ? GetState(ActiveStateName!) : null;

    public bool IsParallel => typeof(ParallelState) == this.GetType();

    public RealState? GetState(string stateName)
    {
        return StateMachine.GetState(stateName) as RealState;
    }

    public RealState(string name, string? parentName, string? stateMachineId) : base(name, parentName, stateMachineId)
    {
        SubStateNames = new List<string>();
        EntryActions = new List<NamedAction>();
        ExitActions = new List<NamedAction>();
        OnTransitionMap = new ConcurrentDictionary<string, List<Transition>>();
        AfterTransition = null;
        AlwaysTransition = null;
        OnDoneTransition = null;
    }

    public virtual void Start()
    {
        EntryState();
    }

    public abstract void GetActiveSubStateNames(List<string> list);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="collection"></param>
    public void GetSuperStateCollection(ICollection<string> collection)
    {
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
    public virtual void ExitState()
    {
        StateMachine.Log(">>>- State_Real.ExitState: " + Name);

        IsActive = false;

        if (Parent != null)
        {
            Parent.ActiveStateName = null;
        }

        ExitActions?.ForEach(action => action.Action(StateMachine));
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="historyType"></param>
    public virtual void EntryState(HistoryType historyType = HistoryType.None)
    {
        StateMachine.Log(">>>- State_Real.EntryState: " + Name);

        EntryActions?.ForEach(action => action.Action(StateMachine));

        // Let define active state as all the entry actions performed successfuly.

        IsActive = true;

        if (Parent != null)
        {
            Parent.ActiveStateName = Name;
        }

        ScheduleAfterTransitionTimer();
    }


    public void ScheduleAfterTransitionTimer()
    {
        var transition = AfterTransition;

        if (transition == null) return;

        var timer = new System.Timers.Timer();
        timer.Interval = transition.Delay;
        var now = DateTime.Now;

        timer.Elapsed += (sender, e) =>
        {
            StateMachine.Log("");
            StateMachine.Log($">>> Scheduled time has come {Name} in {transition.Delay} ms");
            StateMachine.Log($">>> Timer elapsed (ms): {(e.SignalTime - now).TotalMilliseconds}");
            StateMachine.Log("");
            timer.Stop();
            timer.Dispose();
            //HandleAfterTransition(transition);
            Transit(transition, $"after: {transition.Delay}");
            //after.Value?.Transit();
        };
        
        timer.AutoReset = false;
        timer.Start();

        StateMachine.Log("");
        StateMachine.Log($">>> Scheduled after transition {Name} in {transition.Delay} ms");
        StateMachine.Log("");
    }    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="state"></param>
    /// <param name="transition"></param>
    /// <param name="eventName"></param>
    public void Transit(Transition? transition, string eventName)
    {
        if (transition == null) return;

        StateMachine.Log($">> transition on event {eventName} in state {Name}");

        if ((transition.Guard == null || transition.Guard.Predicate(StateMachine))
            && (transition.InCondition == null || transition.InCondition()))
        {

            string sourceName = transition.SourceName;
            string? targetName = transition.TargetName;

            if (targetName != null)
            {
                var (exitList, entryList) = StateMachine.GetExitEntryList(transition.SourceName, targetName);

                // Exit
                foreach (var stateName in exitList)
                {
                    ((RealState)GetState(stateName)).ExitState();
                }

                StateMachine.Log($"Transit: [ {sourceName} --> {targetName} ] by {eventName}");

                // Transition
                RealState? source = GetState(sourceName) as RealState;
                StateBase? target = GetState(targetName);

                if (target is HistoryState)
                {
                    target = StateMachine.GetStateAsHistory(targetName);
                }

                StateMachine.OnTransition?.Invoke(source, target, eventName);

                if (transition.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        action.Action(StateMachine);
                    }
                }

                // Entry
                foreach (var stateName in entryList)
                {
                    var state = GetState(stateName);
                    
                    if (state != null)
                    {
                        state.EntryState();
                    }
                }
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

    // check if the state is self or ancestor of the this state
    public bool IsAncestorOf(StateBase state)
    {
        StateMachine.Log($"IsAncestorOf: {Name} -> {state.Name}");

        if (this == state) return true;

        if (this is RealState realState)
        {
            if(state.Parent != null)
                return realState.IsAncestorOf(state.Parent);
            else
                throw new Exception("Parent is null");
        }

        return false;
    }

    public virtual void BuildTransitionList(string eventName, List<(RealState state, Transition transition, string eventName)> transitionList)
    {
        //StateMachine.Log(">>>- State.Real.BuildTransitionList: " + Name);
        // self second
        OnTransitionMap.TryGetValue(eventName, out var transitions);

        if (transitions != null)
        {
            foreach (var transition in transitions)
            {
                if (transition.Guard == null || transition.Guard != null && transition.Guard.Predicate(StateMachine))
                {
                    transitionList.Add((this, transition, eventName));
                }
            }
        }

        if (AlwaysTransition != null)
            transitionList.Add((this, AlwaysTransition, "always"));

        if (AfterTransition != null)
            transitionList.Add((this, AfterTransition, "after"));

        if (OnDoneTransition != null)
            transitionList.Add((this, OnDoneTransition, "onDone"));
    }

    public abstract void GetTargetSubStateCollection(ICollection<string> collection, HistoryType hist = HistoryType.None);
    public abstract void GetSouceSubStateCollection(ICollection<string> collection);
    public abstract void PrintActiveStateTree(int depth);  
}

public abstract class Parser_RealState : Parser_StateBase
{
    public Parser_RealState(string machineId) : base(machineId)  { }
}
