using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace XStateNet;

public class NormalState : State
{
    public bool IsInitial => Parent != null && Parent.InitialStateName == Name;
    public NormalState(string name, string? parentName, string stateMachineId) : base(name, parentName, stateMachineId)
    {
    }

    public override void InitializeCurrentStates()
    {
        base.InitializeCurrentStates();

        if (SubStateNames != null && InitialStateName != null)
        {
            StateMachine.GetState(InitialStateName)?.InitializeCurrentStates();
        }

        // Schedule after transitions for the initial state
        ScheduleAfterTransitionTimer();
    }

    public override void Start()
    {
        base.Start();

        if (InitialStateName != null)
        {
            StateMachine.GetState(InitialStateName)?.Start();
        }
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




    private List<State> GetInitialStates(State state)
    {
        var initialStates = new List<State>();
        var initialSubState = StateMachine.GetState(state.InitialStateName) as State;
        initialStates.Add(initialSubState);
        initialStates.AddRange(GetInitialStates(initialSubState));
        return initialStates;
    }

    public override List<State> GetLastActiveStates(HistoryType historyType = HistoryType.None)
    {
        var lastActiveStates = new List<State>();

        if (historyType == HistoryType.None)
        {
            if (InitialStateName != null)
            {
                var initialState = StateMachine.GetState(InitialStateName);
                
                if (initialState != null)
                {
                    lastActiveStates.Add(initialState);
                    lastActiveStates.AddRange(initialState.GetLastActiveStates());
                }
            }
        }
        else
        {
            if (LastActiveStateName != null)
            {
                var lastActiveState = StateMachine.GetState(LastActiveStateName);

                if (lastActiveState != null)
                {
                    lastActiveStates.Add(lastActiveState);
                    lastActiveStates.AddRange(lastActiveState.GetLastActiveStates(historyType));
                }
            }
        }
        return lastActiveStates;
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


    public override void PrintCurrentStateTree(int depth)
    {
        Helper.WriteLine(depth * 2, $"- {Name.Split('.').Last()}");

        foreach (var currentStateName in SubStateNames)
        {
            if (StateMachine.TestActive(currentStateName))
                StateMachine.GetState(currentStateName)?.PrintCurrentStateTree(depth + 1);
        }
    }

    public bool IsSibling(State target)
    {
        return Parent.SubStateNames.Contains(target.Name);
    }
    
    public override List<string> GetCurrentSubStateNames(List<string> list)
    {
        foreach (var subStateName in SubStateNames)
        {
            if (StateMachine.TestActive(subStateName))
            {
                list.Add(subStateName);
                var subState = StateMachine.GetState(subStateName);
                if(subState != null) 
                    subState.GetCurrentSubStateNames(list);
            }
        }
        return list;
    }
    public override void GetHistoryEntryList(List<AbstractState> entryList, string stateName, HistoryType historyType = HistoryType.None)
    {
        var state = StateMachine.GetState(stateName) as State;
        entryList.Add(state);

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

    public override void GetSouceSubStateCollection(ICollection<State> collection)
    {
        foreach (var subState in SubStateNames)
        {
            if (StateMachine.TestActive(subState))
            {
                var state = StateMachine.GetState(subState);
                if (state != null)
                {
                    collection.Add(state);
                    state.GetSouceSubStateCollection(collection);
                }
            }
        }
    }

    public override void GetTargetSubStateCollection(ICollection<State> collection, HistoryType hist = HistoryType.None)
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