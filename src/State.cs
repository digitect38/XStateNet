using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;


namespace SharpState;


public abstract class StateBase
{    
    public string Name { get; set; }
    public string? ParentName { get; set; }
    public StateMachine StateMachine => StateMachine.GetInstance(stateMachineId);

    public string stateMachineId;

    public StateBase(string name, string? parentName, string stateMachineId)
    {
        Name = name;
        ParentName = parentName;
        this.stateMachineId = stateMachineId;
    }

    public abstract void EntryState(HistoryType historyType = HistoryType.None);
    public abstract void ExitState();
}

public class State : StateBase
{

    public State(string name, string? parentName, string stateMachineId) : base(name, parentName, stateMachineId)    
    {
        
        SubStateNames = new List<string>();
        EntryActions = new List<NamedAction>();
        ExitActions = new List<NamedAction>();
        OnTransitionMap = new ConcurrentDictionary<string, List<Transition>>();
        //AfterTransitionMap = new ConcurrentDictionary<string, List<Transition>>();
        AfterTransition = null;
        AlwaysTransition = null;
    }

    public ConcurrentDictionary<string, List<Transition>> OnTransitionMap { get; set; }
    //public ConcurrentDictionary<string, List<Transition>> AfterTransitionMap { get; set; }
    public AfterTransition? AfterTransition { get; set; } // Added for after transitions

    public AlwaysTransition? AlwaysTransition { get; set; } // Added for always transitions

    public State? Parent => string.IsNullOrEmpty(ParentName) ? null : StateMachine.GetState(ParentName);


    public List<NamedAction> EntryActions { get; set; }
    public List<NamedAction> ExitActions { get; set; }
    public List<string> SubStateNames { get; set; } // state 의 current sub state 들..

    public bool IsParallel { get; set; }
    public bool IsInitial => Parent != null && Parent.InitialStateName == Name;
    public bool IsHistory => throw new Exception("Not implemented yet");
    //public HistoryType HistoryType { get; set; }
    public string? LastActiveStateName { get; set; }
    public string? InitialStateName { get; set; }

    //private System.Timers.Timer transitionTimer;

    public void InitializeCurrentStates()
    {
        StateMachine.AddCurrent(this);

        if (IsParallel)
        {
            foreach (string subStateName in SubStateNames)
            {
                StateMachine.GetState(subStateName)?.InitializeCurrentStates();
            }
        }
        else if (SubStateNames != null && InitialStateName != null)
        {
            StateMachine.GetState(InitialStateName)?.InitializeCurrentStates();
        }

        // Schedule after transitions for the initial state
        ScheduleAfterTransitionTimer();
    }

    public void Start()
    {
        EntryActions?.ForEach(action => action.Action(StateMachine));

        if (IsParallel)
        {
            var tasks = SubStateNames.Select(subStateName => Task.Run(() =>
            {
                StateMachine.GetState(subStateName)?.Start();
            })).ToArray();

            Task.WaitAll(tasks);
        }
        else
        {
            if (InitialStateName != null)
            {
                StateMachine.GetState(InitialStateName)?.Start();
            }
        }
    }

    public void Reset()
    {
        StateMachine.Send("RESET");
    }

    public void Pause()
    {
        // todo: implement
    }

    public void Stop()
    {
        // todo: implement
    }

    public override void EntryState(HistoryType historyType = HistoryType.None)
    {
        StateMachine.AddCurrent(this);

        Console.WriteLine(">>>- EntryState: " + Name);

        EntryActions?.ForEach(action => action.Action(StateMachine));
        /*
        if (IsParallel)
        {
            SubStateNames.AsParallel().ForAll(
                subStateName =>
                {
                    var subState = StateMachine.GetState(subStateName);
                    subState?.EntryState();
                }
            );
        }
        else if (historyType != HistoryType.None && LastActiveStateName != null)
        {
            var lastActivestate = StateMachine.GetState(LastActiveStateName);

            if (historyType == HistoryType.Deep)
            {
                lastActivestate?.EntryState(historyType);
            }
            else
            {
                lastActivestate?.EntryState();
            }
        }
        else if (InitialStateName != null)
        {
            var subStateName = InitialStateName;
            var subState = StateMachine.GetState(subStateName);
            subState?.EntryState();
        }
        */
        ScheduleAfterTransitionTimer();
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
            //ExitState(transition.SourceName);
            var source = StateMachine.GetState(transition.SourceName);
            var target = StateMachine.GetState(transition.TargetName);

            source?.ExitState();
            transition.Actions?.ForEach(action => action.Action(StateMachine));
            target?.EntryState();
        }
    }
    

    public override void ExitState()
    {
        /*
        SubStateNames.ForEach(subStateName =>
        {
            if (StateMachine.TestActive(subStateName))
            {
                var subState = StateMachine.GetState(subStateName);
                subState?.ExitState();
            }
        });

        ExitActions?.ForEach(action => action.Action(StateMachine));

        if (Parent != null && !IsParallel)
        {
            Parent.LastActiveStateName = Name;
        }
        */
        Console.WriteLine(">>>- ExitState: " + Name); 

        StateMachine.RemoveCurrent(Name);
    }


    public void PrintCurrentStateTree(int depth)
    {
        Helper.WriteLine(depth * 2, $"- {Name.Split('.').Last()}");

        if (IsParallel)
        {
            foreach (var currentStateName in SubStateNames)
            {
                StateMachine.GetState(currentStateName)?.PrintCurrentStateTree(depth + 1);
            }
        }
        else
        {
            foreach (var currentStateName in SubStateNames)
            {
                if (StateMachine.TestActive(currentStateName))
                    StateMachine.GetState(currentStateName)?.PrintCurrentStateTree(depth + 1);
            }
        }
    }

    public bool IsSibling(State target)
    {
        return Parent.SubStateNames.Contains(target.Name);
    }

    public List<string> CurrentSubStateNames => GetCurrentSubStateNames(this, new List<string>());

    List<string> GetCurrentSubStateNames(State? state, List<string> list)
    {
        if (state.IsParallel)
        {
            foreach (var subState in state.SubStateNames)
            {
                list.Add(subState);
                GetCurrentSubStateNames(StateMachine.GetState(subState) as State, list);
            }
        }
        else
        {
            foreach (var subState in state.SubStateNames)
            {
                if (StateMachine.TestActive(subState))
                {
                    list.Add(subState);
                    GetCurrentSubStateNames(StateMachine.GetState(subState) as State, list);
                }
            }
        }
        return list;
    }

    public void GetSouceSubStateCollection(ICollection<State> collection)
    {
        if (IsParallel)
        {
            foreach (string subState in SubStateNames)
            {
                collection.Add(StateMachine.GetState(subState));
                StateMachine.GetState(subState)?.GetSouceSubStateCollection(collection);
            }
        }
        else
        {
            foreach (var subState in SubStateNames)
            {
                if (StateMachine.TestActive(subState))
                {
                    collection.Add(StateMachine.GetState(subState));
                    StateMachine.GetState(subState).GetSouceSubStateCollection(collection);
                }
            }
        }
    }

    public void GetTargetSubStateCollection(ICollection<State> collection, HistoryType hist = HistoryType.None)
    {
        if (IsParallel)
        {
            foreach (string subState in SubStateNames)
            {
                collection.Add(StateMachine.GetState(subState));
                StateMachine.GetState(subState)?.GetTargetSubStateCollection(collection);
            }
        }
        else
        {
            foreach (var subState in SubStateNames)
            {
                if (hist == HistoryType.Deep || StateMachine.TestHistory(subState))
                {
                    collection.Add(StateMachine.GetState(subState));
                    StateMachine.GetState(subState)?.GetTargetSubStateCollection(collection, hist);
                }
                else if (StateMachine.TestInitial(subState))
                {
                    collection.Add(StateMachine.GetState(subState));
                    StateMachine.GetState(subState)?.GetTargetSubStateCollection(collection);
                }
            }
        }
    }

    /// <summary>
    /// Collect states upto parallel child or root state. Note result includes self
    /// </summary>
    /// <param name="collection"></param>
    public void GetSuperStateCollection(ICollection<State> collection)
    {
        collection.Add(this);
        var super = Parent;
        if(super != null && !super.IsParallel)
        {
            super.GetSuperStateCollection(collection);    
        }
    }
}

public class HistoryState : StateBase
{
    State? lastActivState ;
    public HistoryType HistoryType { set; get; }
    public HistoryState(string name, string? parentName, string stateMachineId, HistoryType historyType) 
        : base(name, parentName, stateMachineId)
    {
        HistoryType = historyType;
    }
    
    void RememberLastActiveState(State state)
    {
        lastActivState = state;
    }
    public override void EntryState(HistoryType historyType = HistoryType.None)
    {
        if (historyType == HistoryType.Deep)
        {
            lastActivState?.EntryState(historyType);
        }
        else
        {
            lastActivState?.EntryState();
        }
    }

    public override void ExitState()
    {
    }
}