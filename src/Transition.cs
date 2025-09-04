namespace XStateNet;

/// <summary>
/// 
/// </summary>
public class TransitionExecutor : StateObject
{
    public TransitionExecutor(string? machineId) : base(machineId) { }

    public void Execute(Transition? transition, string eventName)
    {
        ExecuteCore(transition, eventName);
    }
    
    protected virtual void ExecuteCore(Transition? transition, string eventName)
    {
        if (transition == null) return;
        if (StateMachine == null) 
            throw new InvalidOperationException("StateMachine is not initialized");

        Logger.Debug($">> transition on event {eventName} in state {transition.SourceName}");

        if ((transition.Guard == null || transition.Guard.PredicateFunc(StateMachine))
            && (transition.InCondition == null || transition.InCondition()))
        {

            string? sourceName = transition?.SourceName;
            string? targetName = transition?.TargetName;

            if (string.IsNullOrWhiteSpace(sourceName)) 
                throw new InvalidOperationException("Source state name cannot be null or empty");

            if (targetName != null)
            {
                var (exitList, entryList) = StateMachine.GetFullTransitionSinglePath(sourceName, targetName);

                string? firstExit = exitList.First();
                string? firstEntry = entryList.First();

                // Exit
                if (firstExit != null)
                {
                    StateMachine.TransitUp(firstExit.ToState(StateMachine) as CompoundState).GetAwaiter().GetResult();
                }

                Logger.Info($"Transit: [ {sourceName} --> {targetName} ] by {eventName}");

                // Transition
                var sourceNode = GetState(sourceName);
                CompoundState? source = sourceNode as CompoundState;
                StateNode? target = GetState(targetName);

                StateMachine.OnTransition?.Invoke(source, target, eventName);

                if (transition?.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        try
                        {
                            action.Action(StateMachine);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error executing transition action: {ex.Message}");
                        }
                    }
                }

                // Entry
                if (firstEntry != null)
                {
                    StateMachine.TransitDown(firstEntry.ToState(StateMachine) as CompoundState, targetName).GetAwaiter().GetResult();
                }
            }
            else
            {
                // action only transition

                if (transition?.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        try
                        {
                            action.Action(StateMachine);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error executing action: {ex.Message}");
                        }
                    }
                }
            }
        }
        else
        {
            Logger.Debug($"Condition not met for transition on event {eventName}");
        }
    }
}

/// <summary>
/// 
/// </summary>
public abstract class Transition : StateObject
{

    public string? SourceName { get; set; }
    public string? TargetName { get; set; }
    public NamedGuard? Guard { get; set; }
    public List<NamedAction>? Actions { get; set; }
    public Func<bool>? InCondition { get; set; }


    public CompoundState? Source {
        get {
            if(string.IsNullOrWhiteSpace(SourceName)) 
                throw new InvalidOperationException("SourceName cannot be null or empty");
            if(StateMachine == null) 
                throw new InvalidOperationException("StateMachine is not initialized");
            
            var state = StateMachine.GetState(SourceName);
            if (state is not CompoundState compoundState)
                throw new InvalidOperationException($"Source state '{SourceName}' is not a CompoundState");
            
            return compoundState;
        }
    }  
    // can not be null any case. Source never be history state
    public StateNode? Target {
        get {
            if (!string.IsNullOrWhiteSpace(TargetName))
            {
                if(StateMachine == null) 
                    throw new InvalidOperationException("StateMachine is not initialized");
                return StateMachine.GetState(TargetName);       // can be null if targetless transition. Target can be history state
            }
            else
            {
                return null;
            }
        }
    }
    
    public Transition(string? machineId) : base(machineId){}
}

/// <summary>
/// 
/// </summary>
public class OnTransition : Transition
{
    public string? Event { get; set; }
    public OnTransition(string? machineId) : base(machineId){}
}

/// <summary>
/// 
/// </summary>
public class AfterTransition : Transition
{
    public string? Delay { get; set; }
    public AfterTransition(string? machineId) : base(machineId) { }
}

/// <summary>
/// 
/// </summary>
public class AlwaysTransition : Transition
{
    public AlwaysTransition(string? machineId) : base(machineId){}
}

/// <summary>
/// 
/// </summary>
public class OnDoneTransition : Transition
{
    public OnDoneTransition(string? machineId) : base(machineId){}
}

/// <summary>
/// 
/// </summary>
/// <param name="targetName"></param>
/// <param name="historyType"></param>
// Need to understand this code
internal record struct NewStruct(string? targetName, HistoryType historyType)
{
    public static implicit operator (string? targetName, HistoryType historyType)(NewStruct value)
    {
        return (value.targetName, value.historyType);
    }

    public static implicit operator NewStruct((string? targetName, HistoryType historyType) value)
    {
        return new NewStruct(value.targetName, value.historyType);
    }
}