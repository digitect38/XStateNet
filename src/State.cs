using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace XStateNet;

public abstract class State : AbstractState
{

    public ConcurrentDictionary<string, List<Transition>> OnTransitionMap { get; set; }

    public AfterTransition? AfterTransition { get; set; } // Added for after transitions
    public AlwaysTransition? AlwaysTransition { get; set; } // Added for always transitions

    public State? Parent => string.IsNullOrEmpty(ParentName) ? null : StateMachine.GetState(ParentName);

    public List<NamedAction> EntryActions { get; set; }
    public List<NamedAction> ExitActions { get; set; }
    public List<string> SubStateNames { get; set; } // state 의 current sub state 들..

    public string? LastActiveStateName { get; set; }
    public string? InitialStateName { get; set; }

    public State(string name, string? parentName, string stateMachineId) : base(name, parentName, stateMachineId)
    {
        SubStateNames = new List<string>();
        EntryActions = new List<NamedAction>();
        ExitActions = new List<NamedAction>();
        OnTransitionMap = new ConcurrentDictionary<string, List<Transition>>();
        AfterTransition = null;
        AlwaysTransition = null;
    }

    public virtual void InitializeCurrentStates()
    {
        StateMachine.AddCurrent(this);
    }

    public virtual void Start()
    {
        EntryActions?.ForEach(action => action.Action(StateMachine));     
    }
    public void Reset()
    {
        StateMachine.Send("RESET");
    }

    public abstract List<string> GetCurrentSubStateNames(List<string> list);

    public void GetSuperStateCollection(ICollection<State> collection)
    {
        collection.Add(this);
        var super = Parent;
        if (super != null && super.GetType() != typeof(ParallelState))
        {
            super.GetSuperStateCollection(collection);
        }
    }

    public void ScheduleAfterTransitionTimer()
    {
        var transition = AfterTransition;

        if (transition == null) return;

        /*
        if (StateMachine.TransitionTimers.ContainsKey(Name))
        {
            StateMachine.TransitionTimers[Name].Stop();
            StateMachine.TransitionTimers[Name].Dispose();
        }
          */

        var timer = new System.Timers.Timer();
        timer.Interval = transition.Delay;
        var now = DateTime.Now;

        timer.Elapsed += (sender, e) =>
        {
            Debug.WriteLine("");
            Debug.WriteLine($">>> Scheduled time has come {Name} in {transition.Delay} ms");
            Debug.WriteLine($">>> Timer elapsed (ms): {(e.SignalTime - now).TotalMilliseconds}");
            Debug.WriteLine("");
            timer.Stop();
            timer.Dispose();
            HandleAfterTransition(transition);
            //after.Value?.Transit();
        };
        timer.AutoReset = false;
        timer.Start();
        Debug.WriteLine("");
        Debug.WriteLine($">>> Scheduled after transition {Name} in {transition.Delay} ms");
        Debug.WriteLine("");

        //StateMachine.TransitionTimers[Name] = timer;

    }

    public void HandleAfterTransition(Transition transition)
    {
        if ((transition.Guard == null || transition.Guard.Func(StateMachine)) &&
            (transition.InCondition == null || transition.InCondition()))
        {
            var source = StateMachine.GetState(transition.SourceName);
            var target = StateMachine.GetState(transition.TargetName);

            source?.ExitState();
            transition.Actions?.ForEach(action => action.Action(StateMachine));
            target?.EntryState();
        }
    }

    private List<State> GetInitialStates(State state)
    {
        var initialStates = new List<State>();

        if (state.InitialStateName == null) return initialStates;

        var initialSubState = StateMachine.GetState(state.InitialStateName) as State;

        if (initialSubState == null) return initialStates;

        initialStates.Add(initialSubState);
        initialStates.AddRange(GetInitialStates(initialSubState));

        return initialStates;
    }

    public abstract void GetHistoryEntryList(List<AbstractState> entryList, string stateName, HistoryType historyType = HistoryType.None);
    public abstract void GetSouceSubStateCollection(ICollection<State> collection);
    public abstract void GetTargetSubStateCollection(ICollection<State> collection, HistoryType hist = HistoryType.None);
    public abstract List<State> GetLastActiveStates(HistoryType historyType = HistoryType.None);
    public abstract void PrintCurrentStateTree(int depth);  
}
