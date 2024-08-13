using Newtonsoft.Json.Linq;
namespace XStateNet;


public class ParallelState : RealState
{
    public ParallelState(string name, string? parentName, string? stateMachineId) : base(name, parentName, stateMachineId)
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

    public override void BuildTransitionList(string eventName, List<(RealState state, Transition transition, string eventName)> transitionList)
    {
        // parent first evaluation (is not the order exit/entry sequence)
        base.BuildTransitionList(eventName, transitionList);

        // children next evaluation

#if true // serial way
        foreach (string subStateName in SubStateNames) {
            GetState(subStateName)?.BuildTransitionList(eventName, transitionList);
        }
#else   // parallel way
        SubStateNames.AsParallel().ForAll(
            subStateName => {
                GetState(subStateName)?.BuildTransitionList(eventName, transitionList);
            }
        );
#endif

    }

    public override void OnDone()
    {
        bool done = true;

        foreach(string subStateName in SubStateNames)
        {
            done = done && GetState(subStateName).IsDone;
        }

        if(done) IsDone = true;

        StateMachine.Send("onDone");
    }

    public override void EntryState(HistoryType historyType = HistoryType.None)
    {
        base.EntryState();

        SubStateNames.AsParallel().ForAll(
            subStateName =>
            {
                if(StateMachine == null) throw new Exception("StateMachine is null");
                var subState = StateMachine.GetState(subStateName) as RealState;
                subState?.EntryState(historyType);
            }
        );
    }
    public override void ExitState()
    {

        foreach (string subStateName in SubStateNames)
        {
            GetState(subStateName)?.ExitState();
        }
    }

    public override void GetActiveSubStateNames(List<string> list)
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
    }

    public override void GetSouceSubStateCollection(ICollection<string> collection)
    {
        foreach (string subState in SubStateNames)
        {
            collection.Add(subState);
            GetState(subState)?.GetSouceSubStateCollection(collection);
        }
    }

    public override void GetTargetSubStateCollection(ICollection<string> collection, HistoryType hist = HistoryType.None)
    {
        foreach (string subStateName in SubStateNames)
        {
            var state = GetState(subStateName);
            if (state != null)
            {
                collection.Add(subStateName);
                GetState(subStateName)?.GetTargetSubStateCollection(collection);
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
    
    public override void PrintActiveStateTree(int depth)
    {
        Helper.WriteLine(depth * 2, $"- {Name.Split('.').Last()}");

        foreach (var currentStateName in SubStateNames)
        {
            GetState(currentStateName)?.PrintActiveStateTree(depth + 1);
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
        
        if(StateMachine == null) throw new Exception("StateMachine is null");
        state.EntryActions = Parser_Action.ParseActions("entry", StateMachine.ActionMap, stateToken);
        state.ExitActions = Parser_Action.ParseActions("exit", StateMachine.ActionMap, stateToken);
        
        return state;
    }
}


