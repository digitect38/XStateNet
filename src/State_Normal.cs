using Newtonsoft.Json.Linq;
namespace XStateNet;

public class NormalState : RealState
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
            GetState(InitialStateName)?.InitializeCurrentStates();
        }

        // Schedule after transitions for the initial state
        // ScheduleAfterTransitionTimer();
    }

    public override void Start()
    {
        base.Start();

        if (InitialStateName != null)
        {
            GetState(InitialStateName)?.Start();
        }
        ScheduleAfterTransitionTimer();
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




    private List<RealState> GetInitialStates(RealState state)
    {
        var initialStates = new List<RealState>();
        var initialSubState = StateMachine.GetState(state.InitialStateName) as RealState;
        initialStates.Add(initialSubState);
        initialStates.AddRange(GetInitialStates(initialSubState));
        return initialStates;
    }

    public override List<RealState> GetLastActiveStates(HistoryType historyType = HistoryType.None)
    {
        var lastActiveStates = new List<RealState>();

        if (historyType == HistoryType.None)
        {
            if (InitialStateName != null)
            {
                var initialState = GetState(InitialStateName);
                
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
                var lastActiveState = GetState(LastActiveStateName);

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
        */
        ExitActions?.ForEach(action => action.Action(StateMachine));

        if (Parent != null && !IsParallel)
        {
            Parent.LastActiveStateName = Name;
        }
        
        Console.WriteLine(">>>- ExitState: " + Name);

        StateMachine.RemoveCurrent(Name);
    }


    public override void PrintCurrentStateTree(int depth)
    {
        Helper.WriteLine(depth * 2, $"- {Name.Split('.').Last()}");

        foreach (var currentStateName in SubStateNames)
        {
            if (StateMachine.TestActive(currentStateName))
                GetState(currentStateName)?.PrintCurrentStateTree(depth + 1);
        }
    }

    public bool IsSibling(RealState target)
    {
        return Parent.SubStateNames.Contains(target.Name);
    }
    
    public override List<string> GetActiveSubStateNames(List<string> list)
    {
        foreach (var subStateName in SubStateNames)
        {
            if (StateMachine.TestActive(subStateName))
            {
                list.Add(subStateName);
                var subState = GetState(subStateName);
                if(subState != null) 
                    subState.GetActiveSubStateNames(list);
            }
        }
        return list;
    }

    public override void GetHistoryEntryList(List<StateBase> entryList, string stateName, HistoryType historyType = HistoryType.None)
    {
        var state = StateMachine.GetState(stateName) as RealState;
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

    public override void GetSouceSubStateCollection(ICollection<RealState> collection)
    {
        foreach (var subStateName in SubStateNames)
        {
            if (StateMachine.TestActive(subStateName))
            {
                var state = GetState(subStateName);
                if (state != null)
                {
                    collection.Add(state);
                    state.GetSouceSubStateCollection(collection);
                }
            }
        }
    }

    public override void GetTargetSubStateCollection(ICollection<RealState> collection, HistoryType hist = HistoryType.None)
    {
        foreach (var subStateName in SubStateNames)
        {
            if (hist == HistoryType.Deep || StateMachine.TestHistory(subStateName))
            {
                var state = GetState(subStateName);
                if (state != null)
                {
                    collection.Add(state);
                }

                GetState(subStateName)?.GetTargetSubStateCollection(collection, hist);
            }
            else if (StateMachine.TestInitial(subStateName))
            {
                var state = GetState(subStateName);
                if (state != null)
                {
                    collection.Add(state);
                }

                GetState(subStateName)?.GetTargetSubStateCollection(collection);
            }
        }
    } 
}


public class Parser_NormalState : Parser_RealState
{
    public Parser_NormalState() { }

    public override StateBase Parse(string stateName, string? parentName, string machineId, JToken stateToken)
    {
        StateMachine stateMachine = StateMachine.GetInstance(machineId);

        var state = new NormalState(stateName, parentName, machineId)
        {
            InitialStateName = (stateToken["initial"] != null) ? stateName + "." + stateToken["initial"].ToString() : null,
        };

        state.InitialStateName = state.InitialStateName != null ? StateMachine.ResolveAbsolutePath(stateName, state.InitialStateName) : null;

        state.EntryActions = Parser_Action.ParseActions(state, "entry", stateMachine.ActionMap, stateToken);
        state.ExitActions = Parser_Action.ParseActions(state, "exit", stateMachine.ActionMap, stateToken);

        return state;
    }
}