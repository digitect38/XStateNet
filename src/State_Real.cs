﻿using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Xml.Linq;

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
    public NamedService? Service { get; set; }

    public List<string> SubStateNames { get; set; }         // state 의 current sub state 들..

    public string? InitialStateName { get; set; }

    public string? ActiveStateName { get; set; }
    public RealState? ActiveState => ActiveStateName != null ? GetState(ActiveStateName!) : null;

    public bool IsParallel => typeof(ParallelState) == this.GetType();

    public new RealState? GetState(string stateName)
    {
        return StateMachine?.GetState(stateName) as RealState;
    }

    public RealState(string? name, string? parentName, string? stateMachineId) : base(name, parentName, stateMachineId)
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

    public virtual Task ExitState(bool postAction = true, bool recursive = false)
    {
        //StateMachine.Log(">>>- State_Real.ExitState: " + Name);

        IsActive = false;
        IsDone = false; // for next time

        if (Parent != null)
        {
            Parent.ActiveStateName = null;
        }

        if(StateMachine != null)
            ExitActions?.ForEach(action => action.Action(StateMachine));

        return Task.CompletedTask;
    }
        

    /// <summary>
    /// 
    /// </summary>
    /// <param name="historyType"></param>
    public virtual Task EntryState(bool postAction = false, bool recursive = false, HistoryType historyType = HistoryType.None, HistoryState? targetHistoryState = null)
    {
        //StateMachine.Log(">>>- State_Real.EntryState: " + Name);

        IsDone = false;

        if (StateMachine != null)
        {
            EntryActions?.ForEach(a => a.Action(StateMachine));
            //Service?.AsParallel().ForAll(s => s.Service(StateMachine));
            Service?.Service(StateMachine);
        }

        // Let define active state as all the entry actions performed successfuly.

        IsActive = true;

        if (Parent != null)
        {
            Parent.ActiveStateName = Name;
        }

        ScheduleAfterTransitionTimer();

        return Task.CompletedTask;
    }


    public void ScheduleAfterTransitionTimer()
    {
        if (AfterTransition == null) return;

        var timer = new System.Timers.Timer();
        timer.Interval = AfterTransition.Delay;
        var now = DateTime.Now;

        timer.Elapsed += (sender, e) =>
        {
            StateMachine.Log("");
            StateMachine.Log($">>> Scheduled time has come {Name} in {AfterTransition.Delay} ms");
            StateMachine.Log($">>> Timer elapsed (ms): {(e.SignalTime - now).TotalMilliseconds}");
            StateMachine.Log("");
            timer.Stop();
            timer.Dispose();
            StateMachine?.transitionExecutor.Execute(AfterTransition, $"after: {AfterTransition.Delay}");
        };
        
        timer.AutoReset = false;
        timer.Start();

        StateMachine.Log("");
        StateMachine.Log($">>> Scheduled after transition {Name} in {AfterTransition.Delay} ms");
        StateMachine.Log("");
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

        //
        // Question: Should we execute always transition whenever 'after' and 'onDone' transitions?
        // Clarify always transition rule!
        //

        if (AlwaysTransition != null)
            transitionList.Add((this, AlwaysTransition, "always"));

        /* After and onDone transition should be called other 
         * 
        if (AfterTransition != null)
            transitionList.Add((this, AfterTransition, "after"));
        onDone transition should be called by OnDone() methid
        if (OnDoneTransition != null)
            transitionList.Add((this, OnDoneTransition, "onDone"));
        */
    }

    public abstract void GetTargetSubStateCollection(ICollection<string> collection, bool singleBranchPath, HistoryType hist = HistoryType.None);
    public abstract void GetSouceSubStateCollection(ICollection<string> collection, bool singleBranchPath = false);
    public abstract void PrintActiveStateTree(int depth);  
}

public abstract class Parser_RealState : Parser_StateBase
{
    public Parser_RealState(string? machineId) : base(machineId)  { }
}
