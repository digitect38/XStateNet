using Newtonsoft.Json.Linq;
namespace XStateNet;


public class ParallelState : RealState
{
    public ParallelState(string name, string? parentName, string stateMachineId) : base(name, parentName, stateMachineId)
    {
    }

    public override void Start()
    {
        base.Start();

        var tasks = SubStateNames.Select(subStateName => Task.Run(() =>
        {
            GetState(subStateName)?.Start();
        })).ToArray();

        Task.WaitAll(tasks);
    }


    public override void InitializeCurrentStates()
    {
        base.InitializeCurrentStates();

        foreach (string subStateName in SubStateNames)
        {
            GetState(subStateName)?.InitializeCurrentStates();
        }

        // Schedule after transitions for the initial state
        ScheduleAfterTransitionTimer();
    }

    public override void EntryState(HistoryType historyType = HistoryType.None)
    {
        foreach (string subStateName in SubStateNames)
        {
            GetState(subStateName)?.EntryState(historyType);
        }
    }
    public override void ExitState()
    {

        foreach (string subStateName in SubStateNames)
        {
            GetState(subStateName)?.ExitState();
        }
    }
    public override void GetHistoryEntryList(List<StateBase> entryList, string stateName, HistoryType historyType = HistoryType.None)
    {
        var state = GetState(stateName) as RealState;
        entryList.Add(state);

        state.SubStateNames.ForEach(
            subStateName =>
            {
                var subState = GetState(subStateName);
                GetHistoryEntryList(entryList, subStateName);
            }
        );
    }

    public override List<string> GetActiveSubStateNames(List<string> list)
    {
        foreach (var subStateName in SubStateNames)
        {
            list.Add(subStateName);

            var subState =  GetState(subStateName);

            if (subState != null)
            {
                subState.GetActiveSubStateNames(list);
            }
        }

        return list;
    }

    public override void GetSouceSubStateCollection(ICollection<RealState> collection)
    {
        foreach (string subState in SubStateNames)
        {
            collection.Add(GetState(subState));
            GetState(subState)?.GetSouceSubStateCollection(collection);
        }
    }

    public override void GetTargetSubStateCollection(ICollection<RealState> collection, HistoryType hist = HistoryType.None)
    {
        foreach (string subState in SubStateNames)
        {
            var state = GetState(subState);
            if (state != null)
            {
                collection.Add(state);
                GetState(subState)?.GetTargetSubStateCollection(collection);
            }
        }
    }
    private List<RealState> GetInitialStates(RealState state)
    {
        var initialStates = new List<RealState>();
        if (state.InitialStateName != null)
        {
            var initialSubState = GetState(state.InitialStateName) as RealState;
            if (initialSubState != null)
            {
                initialStates.Add(initialSubState);
                initialStates.AddRange(GetInitialStates(initialSubState));
            }
        }
        return initialStates;
    }

    public override List<RealState> GetLastActiveStates(HistoryType historyType = HistoryType.None)
    {
        var lastActiveStates = new List<RealState>();

        foreach (var subStateName in SubStateNames)
        {
            var subState = GetState(subStateName);
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
            GetState(currentStateName)?.PrintCurrentStateTree(depth + 1);
        }
    }
}

public class Parser_ParallelState : Parser_RealState
{
    public Parser_ParallelState(string machineId) : base(machineId) { }

    public override StateBase Parse(string stateName, string? parentName, JToken stateToken)
    {
        var state = new ParallelState(stateName, parentName, machineId)
        {
        };
        
        state.EntryActions = Parser_Action.ParseActions(state, "entry", StateMachine.ActionMap, stateToken);
        state.ExitActions = Parser_Action.ParseActions(state, "exit", StateMachine.ActionMap, stateToken);
        
        return state;
    }
}


