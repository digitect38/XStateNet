using System.Linq;

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
    
    private void ExecuteMultipleTargets(Transition transition, string eventName)
    {
        if (transition.TargetNames == null || StateMachine == null) return;
        
        Logger.Info($"Executing multiple target transition for event {eventName}");
        
        // Execute transition actions once before any state changes
        if (transition.Actions != null)
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
        
        // Process each target
        foreach (var targetName in transition.TargetNames)
        {
            if (string.IsNullOrWhiteSpace(targetName)) continue;
            
            try
            {
                // Find the source state for this target in the parallel regions
                var targetState = GetState(targetName);
                if (targetState == null)
                {
                    Logger.Warning($"Target state '{targetName}' not found");
                    continue;
                }
                
                // Find the current active state in the same region as the target
                var targetRegion = GetParentRegion(targetState);
                if (targetRegion == null)
                {
                    Logger.Warning($"Could not find parent region for target state '{targetName}'");
                    continue;
                }
                
                // Get the current active state names in this region
                // Optimized: Single loop instead of multiple LINQ iterations
                var activeStateNamesInRegion = new List<string>();
                foreach (var name in StateMachine.GetSourceSubStateCollection(null))
                {
                    var state = StateMachine.GetState(name);
                    if (state is CompoundState cs && IsInRegion(cs, targetRegion) && state.Name != null)
                    {
                        activeStateNamesInRegion.Add(state.Name);
                    }
                }
                
                if (activeStateNamesInRegion.Count == 0)
                {
                    Logger.Warning($"No active state found in region containing '{targetName}'");
                    continue;
                }
                
                var sourceInRegionName = activeStateNamesInRegion.First();
                
                // Execute the transition for this specific region
                var (exitList, entryList) = StateMachine.GetFullTransitionSinglePath(sourceInRegionName, targetName);
                
                string? firstExit = exitList.FirstOrDefault();
                string? firstEntry = entryList.FirstOrDefault();
                
                // Exit
                if (firstExit != null)
                {
                    StateMachine.TransitUp(firstExit.ToState(StateMachine) as CompoundState).GetAwaiter().GetResult();
                }
                
                Logger.Info($"Transit: [ {sourceInRegionName} --> {targetName} ] by {eventName}");
                
                // Entry
                if (firstEntry != null)
                {
                    StateMachine.TransitDown(firstEntry.ToState(StateMachine) as CompoundState, targetName).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error transitioning to target '{targetName}': {ex.Message}");
            }
        }
    }
    
    private StateNode? GetParentRegion(StateNode state)
    {
        var current = state;
        while (current != null)
        {
            var parent = current.Parent;
            if (parent != null && parent.IsParallel)
            {
                return current;
            }
            current = current.Parent;
        }
        return null;
    }
    
    private bool IsInRegion(CompoundState state, StateNode region)
    {
        var current = state;
        while (current != null)
        {
            if (current == region) return true;
            current = current.Parent;
        }
        return false;
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

            string? sourceName = transition.SourceName;
            string? targetName = transition.TargetName;

            if (string.IsNullOrWhiteSpace(sourceName)) 
                throw new InvalidOperationException("Source state name cannot be null or empty");

            // Handle internal transitions - execute actions without changing state
            if (transition.IsInternal)
            {
                Logger.Info($"Internal transition on event {eventName} in state {sourceName}");
                
                // Execute transition actions without state change
                if (transition?.Actions != null && transition.Actions.Count > 0)
                {
                    Logger.Debug($"Executing {transition.Actions.Count} actions for internal transition");
                    foreach (var action in transition.Actions)
                    {
                        try
                        {
                            Logger.Debug($"Executing action: {action.Name}");
                            action.Action(StateMachine);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error executing internal transition action: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Logger.Debug($"No actions to execute for internal transition (Actions null: {transition?.Actions == null}, Count: {transition?.Actions?.Count ?? 0})");
                }
                
                // Fire OnTransition event even for internal transitions
                var sourceNode = GetState(sourceName);
                StateMachine.OnTransition?.Invoke(sourceNode as CompoundState, sourceNode, eventName);
                return;
            }

            // Handle multiple targets
            if (transition.HasMultipleTargets && transition.TargetNames != null)
            {
                ExecuteMultipleTargets(transition, eventName);
                return;
            }

            if (targetName != null)
            {
                var (exitList, entryList) = StateMachine.GetFullTransitionSinglePath(sourceName, targetName);

                string? firstExit = exitList.FirstOrDefault();
                string? firstEntry = entryList.FirstOrDefault();

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
    public List<string>? TargetNames { get; set; } // Support for multiple targets
    public NamedGuard? Guard { get; set; }
    public List<NamedAction>? Actions { get; set; }
    public Func<bool>? InCondition { get; set; }
    public bool IsInternal { get; set; } // Internal transition flag
    
    public bool HasMultipleTargets => TargetNames != null && TargetNames.Count > 0;


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