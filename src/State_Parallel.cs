using XStateNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public class ParallelState : State
{
    public ParallelState(string name, string? parentName, string stateMachineId) : base(name, parentName, stateMachineId)
    {
    }

    public override void Start()
    {
        base.Start();

        var tasks = SubStateNames.Select(subStateName => Task.Run(() =>
        {
            StateMachine.GetState(subStateName)?.Start();
        })).ToArray();

        Task.WaitAll(tasks);
    }


    public override void InitializeCurrentStates()
    {
        base.InitializeCurrentStates();

        foreach (string subStateName in SubStateNames)
        {
            StateMachine.GetState(subStateName)?.InitializeCurrentStates();
        }

        // Schedule after transitions for the initial state
        ScheduleAfterTransitionTimer();
    }

    public override void EntryState(HistoryType historyType = HistoryType.None)
    {
        foreach (string subStateName in SubStateNames)
        {
            StateMachine.GetState(subStateName)?.EntryState(historyType);
        }
    }
    public override void ExitState()
    {

        foreach (string subStateName in SubStateNames)
        {
            StateMachine.GetState(subStateName)?.ExitState();
        }
    }
    public override void GetHistoryEntryList(List<AbstractState> entryList, string stateName, HistoryType historyType = HistoryType.None)
    {
        var state = StateMachine.GetState(stateName) as State;
        entryList.Add(state);

        state.SubStateNames.ForEach(
            subStateName =>
            {
                var subState = StateMachine.GetState(subStateName);
                GetHistoryEntryList(entryList, subStateName);
            }
        );
    }

    public override List<string> GetCurrentSubStateNames(List<string> list)
    {
        foreach (var subStateName in SubStateNames)
        {
            list.Add(subStateName);

            var subState = StateMachine.GetState(subStateName);

            if (subState != null)
                subState.GetCurrentSubStateNames(list);
        }

        return list;
    }

    public override void GetSouceSubStateCollection(ICollection<State> collection)
    {
        foreach (string subState in SubStateNames)
        {
            collection.Add(StateMachine.GetState(subState));
            StateMachine.GetState(subState)?.GetSouceSubStateCollection(collection);
        }
    }

    public override void GetTargetSubStateCollection(ICollection<State> collection, HistoryType hist = HistoryType.None)
    {
        foreach (string subState in SubStateNames)
        {
            var state = StateMachine.GetState(subState);
            if (state != null)
            {
                collection.Add(state);
                StateMachine.GetState(subState)?.GetTargetSubStateCollection(collection);
            }
        }
    }
    private List<State> GetInitialStates(State state)
    {
        var initialStates = new List<State>();
        if (state.InitialStateName != null)
        {
            var initialSubState = StateMachine.GetState(state.InitialStateName) as State;
            if (initialSubState != null)
            {
                initialStates.Add(initialSubState);
                initialStates.AddRange(GetInitialStates(initialSubState));
            }
        }
        return initialStates;
    }

    public override List<State> GetLastActiveStates(HistoryType historyType = HistoryType.None)
    {
        var lastActiveStates = new List<State>();

        foreach (var subStateName in SubStateNames)
        {
            var subState = StateMachine.GetState(subStateName);
            if (subState != null)
            {
                lastActiveStates.Add(subState);
                lastActiveStates.AddRange(subState.GetLastActiveStates(historyType));
            }
        }

        return lastActiveStates;
    }

    public override void PrintCurrentStateTree(int depth)
    {
        Helper.WriteLine(depth * 2, $"- {Name.Split('.').Last()}");

        foreach (var currentStateName in SubStateNames)
        {
            StateMachine.GetState(currentStateName)?.PrintCurrentStateTree(depth + 1);
        }
    }
}
