namespace XStateNet;

/// <summary>
/// 
/// </summary>
public class TransitionExecutor : StateObject
{
    public TransitionExecutor(string? machineId) : base(machineId) { }

    public async Task Execute(Transition? transition, string eventName)
    {
        await ExecuteCore(transition, eventName);
    }

    public virtual async Task ExecuteAsync(Transition? transition, string eventName)
    {
        await ExecuteCoreAsync(transition, eventName);
    }

    private async Task ExecuteMultipleTargets(Transition transition, string eventName)
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
                    // Notify action execution
                    StateMachine.RaiseActionExecuted(action.Name, transition.SourceName);
                    await action.Action(StateMachine);
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
                    await StateMachine.TransitUp(firstExit.ToState(StateMachine) as CompoundState);
                }

                Logger.Info($"Transit: [ {sourceInRegionName} --> {targetName} ] by {eventName}");

                // Entry
                if (firstEntry != null)
                {
                    await StateMachine.TransitDown(firstEntry.ToState(StateMachine) as CompoundState, targetName);
                }

                // Fire StateChanged event after transition is complete
                StateMachine.RaiseStateChanged();
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

    protected virtual async Task ExecuteCore(Transition? transition, string eventName)
    {
        if (transition == null) return;
        if (StateMachine == null)
            throw new InvalidOperationException("StateMachine is not initialized");

        Logger.Debug($">> transition on event {eventName} in state {transition.SourceName}");

        bool guardPassed = transition.Guard == null || transition.Guard.PredicateFunc(StateMachine);
        if (transition.Guard != null)
        {
            // Notify guard evaluation
            StateMachine.RaiseGuardEvaluated(transition.Guard.Name, guardPassed);
        }

        if (guardPassed && (transition.InCondition == null || transition.InCondition()))
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
                            // Notify action execution
                            StateMachine.RaiseActionExecuted(action.Name, sourceName);
                            await action.Action(StateMachine);
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
                StateMachine.RaiseTransition(sourceNode as CompoundState, sourceNode, eventName);

                // For internal transitions, still fire StateChanged with current state
                StateMachine.RaiseStateChanged();
                return;
            }

            // Handle multiple targets
            if (transition.HasMultipleTargets && transition.TargetNames != null)
            {
                await ExecuteMultipleTargets(transition, eventName);
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
                    await StateMachine.TransitUp(firstExit.ToState(StateMachine) as CompoundState);
                }

                Logger.Info($"Transit: [ {sourceName} --> {targetName} ] by {eventName}");

                // Transition
                var sourceNode = GetState(sourceName);
                CompoundState? source = sourceNode as CompoundState;
                StateNode? target = GetState(targetName);

                StateMachine.RaiseTransition(source, target, eventName);

                if (transition?.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        try
                        {
                            // Notify action execution
                            StateMachine.RaiseActionExecuted(action.Name, sourceName);
                            await action.Action(StateMachine);
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
                    await StateMachine.TransitDown(firstEntry.ToState(StateMachine) as CompoundState, targetName);
                }

                // Fire StateChanged event after transition is complete
                StateMachine.RaiseStateChanged();
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
                            // Notify action execution
                            StateMachine.RaiseActionExecuted(action.Name, sourceName);
                            await action.Action(StateMachine);
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

    protected virtual async Task ExecuteCoreAsync(Transition? transition, string eventName)
    {
        if (transition == null) return;
        if (StateMachine == null)
            throw new InvalidOperationException("StateMachine is not initialized");

        Logger.Debug($">> async transition on event {eventName} in state {transition.SourceName}");

        bool guardPassed = transition.Guard == null || transition.Guard.PredicateFunc(StateMachine);
        if (transition.Guard != null)
        {
            // Notify guard evaluation
            StateMachine.RaiseGuardEvaluated(transition.Guard.Name, guardPassed);
        }

        if (guardPassed && (transition.InCondition == null || transition.InCondition()))
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
                            // Notify action execution
                            StateMachine.RaiseActionExecuted(action.Name, sourceName);
                            await action.Action(StateMachine);
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
                StateMachine.RaiseTransition(sourceNode as CompoundState, sourceNode, eventName);

                // For internal transitions, still fire StateChanged with current state
                StateMachine.RaiseStateChanged();
                return;
            }

            // Handle multiple targets
            if (transition.HasMultipleTargets && transition.TargetNames != null)
            {
                await ExecuteMultipleTargetsAsync(transition, eventName);
                return;
            }

            if (targetName != null)
            {
                var (exitList, entryList) = StateMachine.GetFullTransitionSinglePath(sourceName, targetName);

                string? firstExit = exitList.FirstOrDefault();
                string? firstEntry = entryList.FirstOrDefault();

                // Exit - now properly await async operations
                if (firstExit != null)
                {
                    await StateMachine.TransitUp(firstExit.ToState(StateMachine) as CompoundState);
                }

                Logger.Info($"Transit: [ {sourceName} --> {targetName} ] by {eventName}");

                // Transition
                var sourceNode = GetState(sourceName);
                CompoundState? source = sourceNode as CompoundState;
                StateNode? target = GetState(targetName);

                StateMachine.RaiseTransition(source, target, eventName);

                if (transition?.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        try
                        {
                            // Notify action execution
                            StateMachine.RaiseActionExecuted(action.Name, sourceName);
                            await action.Action(StateMachine);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error executing transition action: {ex.Message}");
                        }
                    }
                }

                // Entry - now properly await async operations
                if (firstEntry != null)
                {
                    await StateMachine.TransitDown(firstEntry.ToState(StateMachine) as CompoundState, targetName);
                }

                // Fire StateChanged event after transition is complete
                StateMachine.RaiseStateChanged();
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
                            // Notify action execution
                            StateMachine.RaiseActionExecuted(action.Name, sourceName);
                            await action.Action(StateMachine);
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

    private async Task ExecuteMultipleTargetsAsync(Transition transition, string eventName)
    {
        if (transition.TargetNames == null || StateMachine == null) return;

        Logger.Info($"Executing async multiple target transition for event {eventName}");

        // Execute transition actions once before any state changes
        if (transition.Actions != null)
        {
            foreach (var action in transition.Actions)
            {
                try
                {
                    // Notify action execution
                    StateMachine.RaiseActionExecuted(action.Name, transition.SourceName);
                    await action.Action(StateMachine);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error executing transition action: {ex.Message}");
                }
            }
        }

        // Process each target - now with proper async/await for parallel states
        var tasks = new List<Task>();
        foreach (var targetName in transition.TargetNames)
        {
            if (string.IsNullOrWhiteSpace(targetName)) continue;

            tasks.Add(ProcessSingleTargetAsync(transition, targetName, eventName));
        }

        // Wait for all parallel transitions to complete
        await Task.WhenAll(tasks);

        // Fire StateChanged event after all transitions are complete
        StateMachine.RaiseStateChanged();
    }

    private async Task ProcessSingleTargetAsync(Transition transition, string targetName, string eventName)
    {
        try
        {
            // Find the source state for this target in the parallel regions
            var targetState = GetState(targetName);
            if (targetState == null)
            {
                Logger.Warning($"Target state '{targetName}' not found");
                return;
            }

            // Find the actual source state (parent of the target in parallel region)
            var targetParent = targetState.Parent;
            if (targetParent == null || StateMachine == null)
            {
                Logger.Warning($"Cannot determine source for target '{targetName}'");
                return;
            }

            // Find the active state in the same region as the target
            string? sourceName = null;
            if (targetParent.ActiveStateName != null)
            {
                sourceName = targetParent.ActiveStateName;
            }
            else if (targetParent is ParallelState parallelState)
            {
                // For parallel states, we need to find which child contains the target
                // Since parallel states have all regions active, we look for the active state in the region containing the target
                foreach (var regionName in parallelState.SubStateNames)
                {
                    var region = GetState(regionName) as CompoundState;
                    if (region?.ActiveStateName != null && IsStateInRegion(targetName, regionName))
                    {
                        sourceName = region.ActiveStateName;
                        break;
                    }
                }
            }

            if (sourceName == null)
            {
                Logger.Warning($"No active source state found for target '{targetName}'");
                return;
            }

            Logger.Debug($"Processing transition from '{sourceName}' to '{targetName}'");

            // Perform the transition for this target
            var (exitList, entryList) = StateMachine.GetFullTransitionSinglePath(sourceName, targetName);

            string? firstExit = exitList.FirstOrDefault();
            string? firstEntry = entryList.FirstOrDefault();

            // Exit - properly await async operations
            if (firstExit != null)
            {
                await StateMachine.TransitUp(firstExit.ToState(StateMachine) as CompoundState);
            }

            Logger.Info($"Transit: [ {sourceName} --> {targetName} ] by {eventName}");

            // Fire transition event
            var sourceNode = GetState(sourceName);
            CompoundState? source = sourceNode as CompoundState;
            StateNode? target = GetState(targetName);
            StateMachine.RaiseTransition(source, target, eventName);

            // Entry - properly await async operations
            if (firstEntry != null)
            {
                await StateMachine.TransitDown(firstEntry.ToState(StateMachine) as CompoundState, targetName);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error processing target '{targetName}': {ex.Message}");
        }
    }

    private bool IsStateInRegion(string stateName, string regionName)
    {
        var state = GetState(stateName);
        if (state == null) return false;

        // Walk up the parent chain to see if we reach the region
        var current = state.Parent;
        while (current != null)
        {
            if (current.Name == regionName) return true;
            current = current.Parent;
        }
        return false;
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


    public CompoundState? Source
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SourceName))
                throw new InvalidOperationException("SourceName cannot be null or empty");
            if (StateMachine == null)
                throw new InvalidOperationException("StateMachine is not initialized");

            var state = StateMachine.GetState(SourceName);
            if (state is not CompoundState compoundState)
                throw new InvalidOperationException($"Source state '{SourceName}' is not a CompoundState");

            return compoundState;
        }
    }
    // can not be null any case. Source never be history state
    public StateNode? Target
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(TargetName))
            {
                if (StateMachine == null)
                    throw new InvalidOperationException("StateMachine is not initialized");
                return StateMachine.GetState(TargetName);       // can be null if targetless transition. Target can be history state
            }
            else
            {
                return null;
            }
        }
    }

    public Transition(string? machineId) : base(machineId) { }
}

/// <summary>
/// 
/// </summary>
public class OnTransition : Transition
{
    public string? Event { get; set; }
    public OnTransition(string? machineId) : base(machineId) { }
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
    public AlwaysTransition(string? machineId) : base(machineId) { }
}

/// <summary>
/// 
/// </summary>
public class OnDoneTransition : Transition
{
    public OnDoneTransition(string? machineId) : base(machineId) { }
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
