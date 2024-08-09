using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace XStateNet;

public abstract class RealState : StateBase
{

    public ConcurrentDictionary<string, List<Transition>> OnTransitionMap { get; set; }

    public AfterTransition? AfterTransition { get; set; } // Added for after transitions
    public AlwaysTransition? AlwaysTransition { get; set; } // Added for always transitions

    public List<NamedAction>? EntryActions { get; set; }
    public List<NamedAction>? ExitActions { get; set; }
    public List<string> SubStateNames { get; set; } // state 의 current sub state 들..

    public string? LastActiveStateName { get; set; }
    public string? InitialStateName { get; set; }

    public bool IsParallel => typeof(ParallelState) == this.GetType();

    public RealState? GetState(string stateName)
    {
        return StateMachine.GetState(stateName) as RealState;
    }

    public RealState(string name, string? parentName, string stateMachineId) : base(name, parentName, stateMachineId)
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

    public abstract List<string> GetActiveSubStateNames(List<string> list);

    public void GetSuperStateCollection(ICollection<RealState> collection)
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
            Console.WriteLine("");
            Console.WriteLine($">>> Scheduled time has come {Name} in {transition.Delay} ms");
            Console.WriteLine($">>> Timer elapsed (ms): {(e.SignalTime - now).TotalMilliseconds}");
            Console.WriteLine("");
            timer.Stop();
            timer.Dispose();
            HandleAfterTransition(transition);
            //after.Value?.Transit();
        };
        
        timer.AutoReset = false;
        timer.Start();

        Console.WriteLine("");
        Console.WriteLine($">>> Scheduled after transition {Name} in {transition.Delay} ms");
        Console.WriteLine("");

        //StateMachine.TransitionTimers[Name] = timer;

    }

    public void HandleAfterTransition(Transition transition)
    {
        if ((transition.Guard == null || transition.Guard.Func(StateMachine)) &&
            (transition.InCondition == null || transition.InCondition()))
        {
            var source = GetState(transition.SourceName);
            var target = GetState(transition.TargetName);

            source?.ExitState();
            transition.Actions?.ForEach(action => action.Action(StateMachine));
            target?.EntryState();
        }
    }

    private List<RealState> GetInitialStates(RealState state)
    {
        var initialStates = new List<RealState>();

        if (state.InitialStateName == null) return initialStates;

        var initialSubState = StateMachine.GetState(state.InitialStateName) as RealState;

        if (initialSubState == null) return initialStates;

        initialStates.Add(initialSubState);
        initialStates.AddRange(GetInitialStates(initialSubState));

        return initialStates;
    }

    public abstract void EntryState(HistoryType historyType = HistoryType.None);
    public abstract void ExitState();
    public abstract void GetHistoryEntryList(List<StateBase> entryList, string stateName, HistoryType historyType = HistoryType.None);
    public abstract void GetSouceSubStateCollection(ICollection<RealState> collection);
    public abstract void GetTargetSubStateCollection(ICollection<RealState> collection, HistoryType hist = HistoryType.None);
    public abstract List<RealState> GetLastActiveStates(HistoryType historyType = HistoryType.None);
    public abstract void PrintCurrentStateTree(int depth);  
}

public abstract class Parser_RealState : Parser_StateBase
{
    public Parser_RealState(string machineId) : base(machineId)  { }

}
