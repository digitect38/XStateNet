using Akka.Actor;
using Akka.Event;
using System.Text.Json;
using XStateNet2.Core.Engine;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Runtime;

namespace XStateNet2.Core.Actors;

/// <summary>
/// State machine actor - interprets and executes XState definitions
/// </summary>
public class StateMachineActor : ReceiveActor, IWithUnboundedStash
{
    private readonly XStateMachineScript _script;
    private readonly InterpreterContext _context;
    private readonly ILoggingAdapter _log;
    private readonly HashSet<IActorRef> _subscribers = new();
    private readonly Dictionary<string, ICancelable> _delayedTransitions = new();

    // Parallel state management
    private readonly Dictionary<string, IActorRef> _regions = new();
    private readonly HashSet<string> _completedRegions = new();
    private readonly Dictionary<string, string> _regionStates = new();

    // History state management - tracks last active child state for each parent
    private readonly Dictionary<string, string> _stateHistory = new();

    // Performance optimization: Array-based state lookup
    private readonly StateIndex _stateIndex;

    private string _currentState;
    private IActorRef? _currentService;
    private bool _isRunning;
    private bool _isParallelState;

    // Performance optimization: Cache current state info to avoid repeated lookups
    private int _currentStateIndex = -1;
    private XStateNode? _currentStateNode;
    private Dictionary<string, List<XStateTransition>>? _currentTransitions;

    public IStash Stash { get; set; } = null!;

    public StateMachineActor(XStateMachineScript script, InterpreterContext context)
    {
        _script = script ?? throw new ArgumentNullException(nameof(script));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _log = Context.GetLogger();
        _currentState = script.Initial;

        // Initialize optimized state index
        _stateIndex = new StateIndex(script.States);

        // Register self for internal messaging
        _context.RegisterActor(_script.Id, Self);

        Idle();
    }

    private void Idle()
    {
        Receive<StartMachine>(_ =>
        {
            _log.Info($"[{_script.Id}] Starting state machine");
            _isRunning = true;

            // Check if this is a root-level parallel machine
            if (_script.Type == "parallel")
            {
                _log.Info($"[{_script.Id}] Root machine is parallel");
                _currentState = ""; // Parallel machines don't have a single current state
                StartParallelRegions(new XStateNode
                {
                    Type = "parallel",
                    States = _script.States
                });
            }
            else
            {
                EnterState(_currentState, null);
            }

            Become(Running);
            Stash.UnstashAll();
        });

        ReceiveAny(_ => Stash.Stash());
    }

    private void Running()
    {
        Receive<SendEvent>(HandleEvent);
        ReceiveAsync<GetState>(async msg =>
        {
            var snapshot = await CreateStateSnapshotAsync();
            Sender.Tell(snapshot);
        });
        Receive<Subscribe>(_ =>
        {
            _subscribers.Add(Sender);
            _log.Debug($"[{_script.Id}] Subscriber added: {Sender.Path}");
        });
        Receive<Unsubscribe>(_ =>
        {
            _subscribers.Remove(Sender);
            _log.Debug($"[{_script.Id}] Subscriber removed: {Sender.Path}");
        });
        Receive<ServiceDone>(HandleServiceDone);
        Receive<ServiceError>(HandleServiceError);
        Receive<DelayedTransition>(HandleDelayedTransition);
        Receive<RegionCompleted>(HandleRegionCompleted);
        Receive<RegionStateChanged>(HandleRegionStateChanged);
        Receive<CrossRegionTransition>(HandleCrossRegionTransition);
        Receive<StopMachine>(_ =>
        {
            _log.Info($"[{_script.Id}] Stopping state machine");
            ExitState(_currentState, null);
            _isRunning = false;
            Become(Idle);
        });
    }

    #region Event Handling

    private void HandleEvent(SendEvent evt)
    {
        if (!_isRunning)
        {
            _log.Warning($"[{_script.Id}] Received event '{evt.Type}' but machine is not running");
            return;
        }

        _log.Debug($"[{_script.Id}] Event '{evt.Type}' in state '{_currentState}'");

        // If in parallel state, first check if there's a transition on the parallel state itself
        // This allows transitions that exit the parallel state (e.g., CANCEL, SUCCESS)
        if (_isParallelState)
        {
            // Check if the parallel state has transitions for this event
            // For root-level parallel states, check _script.On; for nested parallel states, check _currentStateNode.On
            bool hasParallelTransition = false;
            if (_currentStateNode?.On != null && _currentStateNode.On.ContainsKey(evt.Type))
            {
                hasParallelTransition = true;
            }
            else if (_currentStateNode == null && _script.On != null && _script.On.ContainsKey(evt.Type))
            {
                // Root-level parallel state - transitions are in _script.On
                hasParallelTransition = true;
            }

            // If parallel state has no transition for this event, broadcast to regions
            if (!hasParallelTransition)
            {
                _log.Debug($"[{_script.Id}] Broadcasting event '{evt.Type}' to {_regions.Count} regions");
                var eventWithRegions = new SendEventWithRegionStates(evt, _regionStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
                foreach (var region in _regions.Values)
                {
                    region.Tell(eventWithRegions);
                }
                return;
            }
            // Otherwise, fall through to handle the parallel state transition
            _log.Debug($"[{_script.Id}] Parallel state '{_currentState}' has transition for '{evt.Type}'");
        }

        // Search for transition in current state and parent states hierarchy
        List<XStateTransition>? transitions = null;
        bool found = false;

        // For root-level parallel states, _currentStateNode is null, so use _script.On directly
        if (_currentStateNode == null && _isParallelState)
        {
            // Root-level parallel state - check _script.On for transitions
            if (_script.On != null && _script.On.TryGetValue(evt.Type, out transitions))
            {
                found = true;
            }
        }
        // For normal states, use cached state node - no dictionary lookup!
        else if (_currentStateNode != null)
        {
            // First check current state
            if (_currentTransitions != null && _currentTransitions.TryGetValue(evt.Type, out transitions))
            {
                found = true;
            }
        }
        else
        {
            _log.Error($"[{_script.Id}] Current state '{_currentState}' node is null and not a parallel state");
            return;
        }

        // If not found and this is a nested state, check parent states
        if (!found && _currentState.Contains('.'))
        {
            var statePath = _currentState;
            while (statePath.Contains('.') && !found)
            {
                // Move up to parent state
                var lastDotIndex = statePath.LastIndexOf('.');
                statePath = statePath.Substring(0, lastDotIndex);

                // Navigate to parent state node
                XStateNode? parentNode = null;
                if (statePath.Contains('.'))
                {
                    // Nested parent
                    var pathParts = statePath.Split('.');
                    var currentStates = _script.States;

                    foreach (var part in pathParts)
                    {
                        if (currentStates != null && currentStates.TryGetValue(part, out var node))
                        {
                            parentNode = node;
                            currentStates = node.States;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else
                {
                    // Root parent
                    _script.States.TryGetValue(statePath, out parentNode);
                }

                // Check if parent has the transition
                if (parentNode?.On != null && parentNode.On.TryGetValue(evt.Type, out transitions))
                {
                    found = true;
                    _log.Debug($"[{_script.Id}] Found transition for '{evt.Type}' in parent state '{statePath}'");
                    break;
                }
            }
        }

        // If still not found, check root-level global transitions
        if (!found && _script.On != null && _script.On.TryGetValue(evt.Type, out transitions))
        {
            found = true;
            _log.Debug($"[{_script.Id}] Found global transition for '{evt.Type}' at root level");
        }

        if (found && transitions != null)
        {
            // Try each transition in order until one succeeds
            bool transitionTaken = false;
            foreach (var transition in transitions)
            {
                if (TryProcessTransition(transition, evt))
                {
                    transitionTaken = true;
                    break; // Take only the first matching transition
                }
            }

            if (!transitionTaken)
            {
                _log.Debug($"[{_script.Id}] No transition taken for event '{evt.Type}' in state '{_currentState}' (all guards failed)");
            }
        }
        else
        {
            _log.Debug($"[{_script.Id}] No transition for event '{evt.Type}' in state '{_currentState}'");
        }
    }

    /// <summary>
    /// Try to process a transition. Returns true if the transition was taken, false if guard failed.
    /// </summary>
    private bool TryProcessTransition(XStateTransition transition, SendEvent? evt)
    {
        // Evaluate guard
        if (!string.IsNullOrEmpty(transition.Cond))
        {
            if (!_context.HasGuard(transition.Cond))
            {
                _log.Error($"[{_script.Id}] Guard '{transition.Cond}' not found");
                return false;
            }

            if (!_context.EvaluateGuard(transition.Cond, evt?.Data))
            {
                _log.Debug($"[{_script.Id}] Guard '{transition.Cond}' failed");
                return false;
            }
        }

        // Evaluate in-state condition
        if (!string.IsNullOrEmpty(transition.In))
        {
            if (!IsInState(transition.In))
            {
                _log.Debug($"[{_script.Id}] In-state condition '{transition.In}' failed - not in that state");
                return false;
            }
        }

        // Execute transition
        if (transition.Internal || transition.Targets == null || transition.Targets.Count == 0)
        {
            // Internal transition - execute actions but don't change state
            _log.Debug($"[{_script.Id}] Internal transition in state '{_currentState}'");
            ExecuteActions(transition.Actions, evt?.Data);
        }
        else if (transition.Targets.Count == 1)
        {
            // Single target - standard transition
            Transition(transition.Targets[0], evt, transition.Actions);
        }
        else
        {
            // Multiple targets - transition multiple regions in parallel state
            _log.Info($"[{_script.Id}] Multiple target transition to {transition.Targets.Count} targets");
            TransitionMultipleTargets(transition.Targets, evt, transition.Actions);
        }

        return true;
    }

    private void ProcessTransition(XStateTransition transition, SendEvent? evt)
    {
        TryProcessTransition(transition, evt);
    }

    /// <summary>
    /// Resolves history state references (e.g., "A.hist") to actual target states.
    /// Returns the saved history state, or the default target if no history exists.
    /// </summary>
    private string ResolveHistoryState(string targetState)
    {
        // Check if this is a history state reference
        if (!targetState.Contains(".hist"))
        {
            return targetState;
        }

        _log.Debug($"[{_script.Id}] Resolving history state: {targetState}");

        // Parse the parent state and history state ID
        // Format can be "A.hist" or "A.B.hist"
        var lastDotIndex = targetState.LastIndexOf('.');
        var historyStateName = targetState.Substring(lastDotIndex + 1); // "hist"
        var parentStatePath = targetState.Substring(0, lastDotIndex); // "A" or "A.B"

        // Navigate to the parent state to get the history node
        XStateNode? parentNode = null;
        var pathParts = parentStatePath.Split('.');

        // Start from root states
        var currentStates = _script.States;

        // Navigate through nested states
        foreach (var part in pathParts)
        {
            if (currentStates != null && currentStates.TryGetValue(part, out var node))
            {
                parentNode = node;
                currentStates = node.States;
            }
            else
            {
                _log.Error($"[{_script.Id}] Parent state '{parentStatePath}' not found for history state");
                return targetState; // Return original if can't resolve
            }
        }

        if (parentNode?.States == null)
        {
            _log.Error($"[{_script.Id}] Parent state '{parentStatePath}' has no child states");
            return targetState;
        }

        // Get the history node
        if (!parentNode.States.TryGetValue(historyStateName, out var historyNode))
        {
            _log.Error($"[{_script.Id}] History state '{historyStateName}' not found in '{parentStatePath}'");
            return targetState;
        }

        if (historyNode.Type != "history")
        {
            _log.Error($"[{_script.Id}] State '{targetState}' is not a history state");
            return targetState;
        }

        // Check if we have saved history for this parent state
        if (_stateHistory.TryGetValue(parentStatePath, out var savedState))
        {
            // The saved state might be a relative reference (shallow history) or absolute path (deep history)
            // If it doesn't contain '.', it's relative and needs the parent path prepended
            if (!savedState.Contains('.'))
            {
                savedState = $"{parentStatePath}.{savedState}";
            }
            _log.Info($"[{_script.Id}] Restoring history state: {savedState} for parent '{parentStatePath}'");
            return savedState;
        }

        // No history exists - use default target
        if (!string.IsNullOrEmpty(historyNode.Target))
        {
            // The target might be a relative state ID, so we need to make it absolute
            var defaultTarget = historyNode.Target;
            if (!defaultTarget.Contains('.'))
            {
                // Relative reference - prepend parent path
                defaultTarget = $"{parentStatePath}.{defaultTarget}";
            }
            _log.Info($"[{_script.Id}] No history exists, using default target: {defaultTarget}");
            return defaultTarget;
        }

        // No default target - use parent's initial state
        if (!string.IsNullOrEmpty(parentNode.Initial))
        {
            var initialState = $"{parentStatePath}.{parentNode.Initial}";
            _log.Info($"[{_script.Id}] No history or default, using initial state: {initialState}");
            return initialState;
        }

        _log.Error($"[{_script.Id}] Cannot resolve history state '{targetState}' - no saved history, default, or initial state");
        return targetState;
    }

    private void Transition(string targetState, SendEvent? evt, List<object>? transitionActions)
    {
        // Resolve history state references before proceeding
        targetState = ResolveHistoryState(targetState);

        // Resolve relative target paths to absolute paths
        targetState = ResolveTargetPath(targetState);

        var previousState = _currentState;

        // _log.Info($"[{_script.Id}] Transition: {previousState} -> {targetState}");

        // Exit current state
        ExitState(previousState, evt);

        // Execute transition actions
        ExecuteActions(transitionActions, evt?.Data);

        // Update state
        _currentState = targetState;

        // Enter new state
        EnterState(_currentState, evt);

        // Notify subscribers
        NotifyStateChanged(previousState, _currentState, evt);
    }

    /// <summary>
    /// Handles transitions to multiple targets simultaneously.
    /// This is typically used in parallel states to transition multiple regions at once.
    /// </summary>
    private void TransitionMultipleTargets(List<string> targets, SendEvent? evt, List<object>? transitionActions)
    {
        _log.Info($"[{_script.Id}] Transitioning to {targets.Count} targets: {string.Join(", ", targets)}");

        // Execute transition actions once (not per target)
        ExecuteActions(transitionActions, evt?.Data);

        // Check if we're in a parallel state
        if (_isParallelState && _regions.Count > 0)
        {
            // Multiple targets in parallel state - transition each region
            foreach (var target in targets)
            {
                var resolvedTarget = ResolveTargetPath(target);

                // Parse the target to extract region ID and target state
                // Format: "region1.state1" or ".region1.state1"
                var targetPath = resolvedTarget.TrimStart('.');
                var pathParts = targetPath.Split('.', 2);

                if (pathParts.Length >= 1)
                {
                    var regionId = pathParts[0];
                    var regionTargetState = pathParts.Length > 1 ? pathParts[1] : null;

                    if (_regions.TryGetValue(regionId, out var regionActor))
                    {
                        // Send direct transition command to the specific region
                        if (!string.IsNullOrEmpty(regionTargetState))
                        {
                            _log.Debug($"[{_script.Id}] Sending direct transition to region '{regionId}' -> '{regionTargetState}'");
                            regionActor.Tell(new DirectTransition(regionTargetState));
                        }
                    }
                    else
                    {
                        _log.Warning($"[{_script.Id}] Region '{regionId}' not found for target '{target}'");
                    }
                }
            }
        }
        else
        {
            // Not in a parallel state or targets might cross state boundaries
            // For now, just transition to the first target (fallback behavior)
            _log.Warning($"[{_script.Id}] Multiple targets specified but not in parallel state - using first target only");
            if (targets.Count > 0)
            {
                Transition(targets[0], evt, null); // Actions already executed
            }
        }
    }

    /// <summary>
    /// Resolves relative target paths to absolute paths based on current state.
    /// Examples:
    /// - Current: "A.A1", Target: "A2" -> "A.A2" (sibling)
    /// - Current: "A.A1", Target: "B" -> "B" (root state)
    /// - Current: "A.A1.A1a", Target: "A1b" -> "A.A1.A1b" (sibling)
    /// - Target: ".idle" -> "idle" (relative to root, remove leading dot)
    /// </summary>
    private string ResolveTargetPath(string targetState)
    {
        // Handle absolute paths starting with '#' (e.g., "#atm.idle" means "idle" at root level)
        if (targetState.StartsWith("#"))
        {
            // Remove #machineId. prefix
            var parts = targetState.Split('.', 2);
            if (parts.Length > 1)
            {
                var resolvedState = parts[1]; // "#atm.idle" -> "idle"
                _log.Debug($"[{_script.Id}] Resolving absolute path '{targetState}' to '{resolvedState}'");
                return resolvedState;
            }
        }

        // Handle relative paths starting with '.' (e.g., ".idle" means "idle" at root level)
        if (targetState.StartsWith("."))
        {
            var resolvedState = targetState.Substring(1); // Remove leading dot
            _log.Debug($"[{_script.Id}] Resolving relative path '{targetState}' to '{resolvedState}'");
            return resolvedState;
        }

        // If target contains '.', it's likely an absolute path or multi-level
        // If it doesn't contain '.', it could be a sibling or root state

        // Check if target is a root state
        if (_script.States.ContainsKey(targetState))
        {
            // It's a root state, return as is
            return targetState;
        }

        // If current state is nested and target doesn't contain '.', assume it's a sibling
        if (_currentState.Contains('.') && !targetState.Contains('.'))
        {
            var lastDotIndex = _currentState.LastIndexOf('.');
            var parentPath = _currentState.Substring(0, lastDotIndex);
            var resolvedPath = $"{parentPath}.{targetState}";

            _log.Debug($"[{_script.Id}] Resolving relative path '{targetState}' from '{_currentState}' to '{resolvedPath}'");
            return resolvedPath;
        }

        // Return as is (absolute path or root state)
        return targetState;
    }

    #endregion

    #region State Entry/Exit

    private void EnterState(string state, SendEvent? evt)
    {
        _log.Debug($"[{_script.Id}] Entering state '{state}'");

        // Navigate to the state node - handle both root and nested states
        XStateNode? stateNode = null;
        if (state.Contains('.'))
        {
            // Nested state - navigate through the hierarchy
            var pathParts = state.Split('.');
            var currentStates = _script.States;

            foreach (var part in pathParts)
            {
                if (currentStates != null && currentStates.TryGetValue(part, out var node))
                {
                    stateNode = node;
                    currentStates = node.States;
                }
                else
                {
                    _log.Error($"[{_script.Id}] State '{state}' not found in hierarchy");
                    return;
                }
            }
        }
        else
        {
            // Root level state - use StateIndex for optimization
            _currentStateIndex = _stateIndex.GetStateIndex(state);
            stateNode = _stateIndex.GetStateByIndex(_currentStateIndex);
        }

        if (stateNode == null)
        {
            _log.Error($"[{_script.Id}] State '{state}' not found");
            return;
        }

        // Update cached info
        _currentStateNode = stateNode;
        _currentTransitions = stateNode.On; // Cache transitions directly

        // Check if this is a parallel state
        if (stateNode.Type == "parallel")
        {
            _log.Info($"[{_script.Id}] State '{state}' is a parallel state");

            // Execute entry actions
            ExecuteActions(stateNode.Entry, evt?.Data);

            // Start parallel regions
            StartParallelRegions(stateNode);

            return; // Parallel states don't have normal transitions
        }

        // Execute entry actions
        ExecuteActions(stateNode.Entry, evt?.Data);

        // If this state has an initial child state, automatically enter it
        if (!string.IsNullOrEmpty(stateNode.Initial))
        {
            var initialState = $"{state}.{stateNode.Initial}";
            _log.Debug($"[{_script.Id}] Auto-entering initial child state: {initialState}");

            // Update current state to the initial child
            _currentState = initialState;

            // Recursively enter the initial child state
            EnterState(initialState, evt);
            return; // Don't execute the rest of entry logic for compound states
        }

        // Start invoked service
        if (stateNode.Invoke != null)
        {
            StartService(stateNode.Invoke);
        }

        // Schedule delayed transitions (after)
        if (stateNode.After != null)
        {
            foreach (var (delay, transition) in stateNode.After)
            {
                ScheduleDelayedTransition(delay, transition);
            }
        }

        // Check always transitions (eventless transitions)
        CheckAlwaysTransitions(stateNode, 0);
    }

    private void ExitState(string state, SendEvent? evt)
    {
        _log.Debug($"[{_script.Id}] Exiting state '{state}'");

        // Get the state node - handle nested states properly
        XStateNode? stateNode = null;
        if (state.Contains('.'))
        {
            // Nested state - navigate through the hierarchy
            var pathParts = state.Split('.');
            var currentStates = _script.States;

            foreach (var part in pathParts)
            {
                if (currentStates != null && currentStates.TryGetValue(part, out var node))
                {
                    stateNode = node;
                    currentStates = node.States;
                }
                else
                {
                    _log.Error($"[{_script.Id}] State '{state}' not found in hierarchy");
                    return;
                }
            }
        }
        else
        {
            // Root level state
            if (!_script.States.TryGetValue(state, out stateNode))
            {
                _log.Error($"[{_script.Id}] State '{state}' not found");
                return;
            }
        }

        if (stateNode == null)
        {
            _log.Error($"[{_script.Id}] State node for '{state}' is null");
            return;
        }

        // Save history for parent state if it has a history node
        SaveStateHistory(state);

        // Stop parallel regions if this was a parallel state
        if (_isParallelState)
        {
            StopParallelRegions();
        }

        // Cancel delayed transitions
        CancelDelayedTransitions();

        // Stop invoked service
        if (_currentService != null)
        {
            Context.Stop(_currentService);
            _currentService = null;
        }

        // Execute exit actions
        ExecuteActions(stateNode.Exit, evt?.Data);
    }

    /// <summary>
    /// Saves the current state as history for all ancestor states that have history nodes.
    /// Handles both shallow (immediate child only) and deep (full nested path) history.
    /// </summary>
    private void SaveStateHistory(string state)
    {
        // Only save history for nested states (contains ".")
        if (!state.Contains('.'))
        {
            return;
        }

        // Check all ancestor states (not just immediate parent) for history nodes
        var statePath = state;
        while (statePath.Contains('.'))
        {
            // Get the parent state path and child name
            var lastDotIndex = statePath.LastIndexOf('.');
            var parentStatePath = statePath.Substring(0, lastDotIndex);
            var childStateName = statePath.Substring(lastDotIndex + 1);

            // Navigate to the parent state to check if it has a history node
            XStateNode? parentNode = null;
            Dictionary<string, XStateNode>? currentStates;

            if (parentStatePath.Contains('.'))
            {
                // Nested parent - navigate through hierarchy
                var pathParts = parentStatePath.Split('.');
                currentStates = _script.States;

                foreach (var part in pathParts)
                {
                    if (currentStates != null && currentStates.TryGetValue(part, out var node))
                    {
                        parentNode = node;
                        currentStates = node.States;
                    }
                    else
                    {
                        break; // Parent not found, continue to next ancestor
                    }
                }
            }
            else
            {
                // Root level parent
                if (_script.States.TryGetValue(parentStatePath, out parentNode))
                {
                    currentStates = parentNode.States;
                }
                else
                {
                    break; // Parent not found
                }
            }

            if (parentNode?.States != null)
            {
                // Check if parent has any history nodes
                var historyNodes = parentNode.States
                    .Where(kvp => kvp.Value.Type == "history")
                    .ToList();

                // Save history based on the type (shallow or deep)
                foreach (var (historyId, historyNode) in historyNodes)
                {
                    string stateToSave;

                    if (historyNode.History == "deep")
                    {
                        // Deep history: save the full nested path (the original state)
                        stateToSave = state;
                        _log.Debug($"[{_script.Id}] Saving deep history for '{parentStatePath}': {stateToSave}");
                    }
                    else
                    {
                        // Shallow history (default): save relative to this parent
                        // This should be the direct child of the parent
                        if (statePath.StartsWith(parentStatePath + "."))
                        {
                            stateToSave = statePath.Substring(parentStatePath.Length + 1);
                        }
                        else
                        {
                            stateToSave = childStateName;
                        }
                        _log.Debug($"[{_script.Id}] Saving shallow history for '{parentStatePath}': {stateToSave}");
                    }

                    _stateHistory[parentStatePath] = stateToSave;
                }
            }

            // Move up to next ancestor
            statePath = parentStatePath;
        }
    }

    #endregion

    #region Action Execution

    private void ExecuteActions(List<object>? actions, object? eventData)
    {
        if (actions == null || actions.Count == 0)
            return;

        foreach (var action in actions)
        {
            try
            {
                if (action is string actionName)
                {
                    // Registered action by name
                    _context.ExecuteAction(actionName, eventData);
                }
                else if (action is JsonElement jsonElement)
                {
                    // Check if it's a string or object
                    if (jsonElement.ValueKind == JsonValueKind.String)
                    {
                        // Action name as string
                        var name = jsonElement.GetString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            _context.ExecuteAction(name, eventData);
                        }
                    }
                    else if (jsonElement.ValueKind == JsonValueKind.Object)
                    {
                        // Inline action definition (assign, send, raise, etc.)
                        var actionDef = JsonSerializer.Deserialize<ActionDefinition>(jsonElement.GetRawText());
                        if (actionDef != null)
                        {
                            _context.ExecuteActionDefinition(actionDef, eventData, Self);
                        }
                    }
                }
                else if (action is ActionDefinition actionDef)
                {
                    // Direct ActionDefinition object
                    _context.ExecuteActionDefinition(actionDef, eventData, Self);
                }
                else
                {
                    _log.Warning($"[{_script.Id}] Unknown action type: {action.GetType()}");
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"[{_script.Id}] Error executing action: {action}");
            }
        }

        // Process any pending spawn requests after actions execute
        ProcessSpawnRequests();
    }

    private void ProcessSpawnRequests()
    {
        var pendingSpawns = _context.GetPendingSpawnRequests();
        foreach (var spawnId in pendingSpawns)
        {
            try
            {
                var request = _context.GetSpawnRequest(spawnId);
                if (request != null)
                {
                    // For now, we support spawning registered machines by name
                    // In the future, this could support inline machine definitions
                    _log.Info($"[{_script.Id}] Spawning actor: {spawnId}");

                    // Store spawn metadata in context
                    _context.Set($"_spawned_{spawnId}", true);

                    // Clear the spawn request
                    _context.ClearSpawnRequest(spawnId);
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"[{_script.Id}] Error processing spawn request: {spawnId}");
                _context.ClearSpawnRequest(spawnId);
            }
        }
    }

    #endregion

    #region Service Management

    private void StartService(XStateInvoke invoke)
    {
        if (!_context.HasService(invoke.Src))
        {
            _log.Error($"[{_script.Id}] Service '{invoke.Src}' not registered");
            return;
        }

        _log.Debug($"[{_script.Id}] Starting service '{invoke.Src}'");

        var serviceId = invoke.Id ?? invoke.Src;
        var serviceActor = Context.ActorOf(
            Props.Create(() => new ServiceActor(invoke.Src, _context)),
            $"service-{serviceId}-{Guid.NewGuid():N}"
        );

        _currentService = serviceActor;

        serviceActor.Tell(new StartService(serviceId));
    }

    private void HandleServiceDone(ServiceDone msg)
    {
        _log.Info($"[{_script.Id}] Service '{msg.ServiceId}' completed successfully");

        var stateNode = GetStateNode(_currentState);
        if (stateNode == null)
            return;

        if (stateNode.Invoke?.OnDone != null)
        {
            ProcessTransition(stateNode.Invoke.OnDone, new SendEvent("done", msg.Data));
        }
    }

    private void HandleServiceError(ServiceError msg)
    {
        _log.Error(msg.Error, $"[{_script.Id}] Service '{msg.ServiceId}' failed");

        var stateNode = GetStateNode(_currentState);
        if (stateNode == null)
            return;

        if (stateNode.Invoke?.OnError != null)
        {
            ProcessTransition(stateNode.Invoke.OnError, new SendEvent("error", msg.Error));
        }
    }

    #endregion

    #region Delayed Transitions

    private void ScheduleDelayedTransition(int delay, XStateTransition transition)
    {
        var key = $"after-{delay}";

        if (_delayedTransitions.ContainsKey(key))
        {
            _log.Warning($"[{_script.Id}] Delayed transition '{key}' already scheduled");
            return;
        }

        _log.Debug($"[{_script.Id}] Scheduling delayed transition after {delay}ms");

        var cancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(
            TimeSpan.FromMilliseconds(delay),
            Self,
            new DelayedTransition(delay, transition.Target), // Use legacy Target property for delayed transitions (single target only)
            Self
        );

        _delayedTransitions[key] = cancelable;
    }

    private void HandleDelayedTransition(DelayedTransition msg)
    {
        _log.Debug($"[{_script.Id}] Delayed transition triggered after {msg.Delay}ms");

        var key = $"after-{msg.Delay}";
        _delayedTransitions.Remove(key);

        var stateNode = GetStateNode(_currentState);
        if (stateNode == null)
            return;

        if (stateNode.After != null && stateNode.After.TryGetValue(msg.Delay, out var transition))
        {
            ProcessTransition(transition, null);
        }
    }

    /// <summary>
    /// Gets the state node for a given state path (handles both root and nested states)
    /// </summary>
    private XStateNode? GetStateNode(string statePath)
    {
        if (string.IsNullOrEmpty(statePath))
            return null;

        if (statePath.Contains('.'))
        {
            // Nested state - navigate through the hierarchy
            var pathParts = statePath.Split('.');
            var currentStates = _script.States;
            XStateNode? node = null;

            foreach (var part in pathParts)
            {
                if (currentStates != null && currentStates.TryGetValue(part, out node))
                {
                    currentStates = node.States;
                }
                else
                {
                    _log.Error($"[{_script.Id}] State '{statePath}' not found in hierarchy");
                    return null;
                }
            }

            return node;
        }
        else
        {
            // Root level state
            return _script.States.TryGetValue(statePath, out var node) ? node : null;
        }
    }

    private void CancelDelayedTransitions()
    {
        foreach (var cancelable in _delayedTransitions.Values)
        {
            cancelable.Cancel();
        }

        _delayedTransitions.Clear();
    }

    /// <summary>
    /// Check and process always (eventless) transitions
    /// These are evaluated immediately upon entering a state
    /// </summary>
    private void CheckAlwaysTransitions(XStateNode stateNode, int depth)
    {
        const int MAX_ALWAYS_DEPTH = 10; // Prevent infinite loops

        if (depth >= MAX_ALWAYS_DEPTH)
        {
            _log.Warning($"[{_script.Id}] Maximum always transition depth ({MAX_ALWAYS_DEPTH}) reached - potential infinite loop");
            return;
        }

        if (stateNode.Always == null || stateNode.Always.Count == 0)
            return;

        // Evaluate guards in order and take first matching transition
        foreach (var transition in stateNode.Always)
        {
            // Check if guard passes (or no guard specified)
            bool shouldTransition = true;

            if (!string.IsNullOrEmpty(transition.Cond))
            {
                if (!_context.HasGuard(transition.Cond))
                {
                    _log.Error($"[{_script.Id}] Guard '{transition.Cond}' not found in always transition");
                    continue; // Try next transition
                }

                shouldTransition = _context.EvaluateGuard(transition.Cond, null);
                _log.Debug($"[{_script.Id}] Always transition guard '{transition.Cond}' evaluated to {shouldTransition}");
            }

            if (shouldTransition)
            {
                var targetsDesc = transition.Targets != null ? string.Join(", ", transition.Targets) : "none";
                _log.Debug($"[{_script.Id}] Taking always transition to '{targetsDesc}'");

                // Execute the transition
                if (transition.Internal || transition.Targets == null || transition.Targets.Count == 0)
                {
                    // Internal transition - execute actions but don't change state
                    ExecuteActions(transition.Actions, null);
                }
                else if (transition.Targets.Count == 1)
                {
                    // Single target - External transition - this will trigger CheckAlwaysTransitions again recursively
                    Transition(transition.Targets[0], null, transition.Actions);
                }
                else
                {
                    // Multiple targets
                    TransitionMultipleTargets(transition.Targets, null, transition.Actions);
                }

                // Take only the first matching transition
                break;
            }
        }
    }

    #endregion

    #region State Management

    private StateSnapshot CreateStateSnapshot()
    {
        string currentState;

        if (_isParallelState && _regionStates.Count > 0)
        {
            // Aggregate region states: e.g., "light.red;pedestrian.cannotWalk"
            var regionPaths = _regionStates
                .OrderBy(kvp => kvp.Key) // Consistent ordering
                .Select(kvp => $"{kvp.Key}.{kvp.Value}");
            currentState = string.Join(";", regionPaths);
        }
        else
        {
            currentState = _currentState;
        }

        // Collect metadata from active state nodes
        var (meta, tags, output, description) = CollectMetadata(currentState);

        return new StateSnapshot(
            currentState,
            _context.GetAll(),
            _isRunning
        )
        {
            Meta = meta,
            Tags = tags,
            Output = output,
            Description = description
        };
    }

    private async Task<StateSnapshot> CreateStateSnapshotAsync()
    {
        string currentState;

        if (_isParallelState && _regions.Count > 0)
        {
            // Query each region actor directly for its current state
            var regionStateTasks = _regions.Select(async kvp =>
            {
                try
                {
                    var regionSnapshot = await kvp.Value.Ask<StateSnapshot>(
                        new GetState(),
                        TimeSpan.FromMilliseconds(500)
                    );
                    return (kvp.Key, regionSnapshot.CurrentState);
                }
                catch (Exception ex)
                {
                    _log.Warning($"[{_script.Id}] Failed to get state from region '{kvp.Key}': {ex.Message}");
                    // Fallback to cached state
                    return (kvp.Key, _regionStates.GetValueOrDefault(kvp.Key, "unknown"));
                }
            });

            var regionStatesArray = await Task.WhenAll(regionStateTasks);

            // Update cached region states
            foreach (var (regionId, state) in regionStatesArray)
            {
                _regionStates[regionId] = state;
            }

            // Aggregate region states: e.g., "region1.R1_S2;region2.R2_S2"
            var regionPaths = regionStatesArray
                .OrderBy(tuple => tuple.Item1) // Consistent ordering
                .Select(tuple => $"{tuple.Item1}.{tuple.Item2}");
            currentState = string.Join(";", regionPaths);
        }
        else
        {
            currentState = _currentState;
        }

        // Collect metadata from active state nodes
        var (meta, tags, output, description) = CollectMetadata(currentState);

        return new StateSnapshot(
            currentState,
            _context.GetAll(),
            _isRunning
        )
        {
            Meta = meta,
            Tags = tags,
            Output = output,
            Description = description
        };
    }

    /// <summary>
    /// Collects metadata (meta, tags, output, description) from active state nodes
    /// Supports both simple states ("idle") and parallel states ("region1.state1;region2.state2")
    /// </summary>
    private (Dictionary<string, Dictionary<string, object>>?, List<string>?, object?, string?) CollectMetadata(string currentStatePath)
    {
        Dictionary<string, Dictionary<string, object>>? allMeta = null;
        List<string>? allTags = null;
        object? output = null;
        string? description = null;

        if (string.IsNullOrEmpty(currentStatePath))
            return (null, null, null, null);

        // Parse state paths (handles both simple and parallel states)
        var statePaths = currentStatePath.Split(';');

        foreach (var statePath in statePaths)
        {
            if (string.IsNullOrEmpty(statePath))
                continue;

            // Traverse the state path hierarchy to collect metadata from all ancestor nodes
            var pathParts = statePath.Split('.');
            var currentPath = "";

            for (int i = 0; i < pathParts.Length; i++)
            {
                currentPath = i == 0 ? pathParts[i] : $"{currentPath}.{pathParts[i]}";
                var stateNode = GetStateNode(currentPath);

                if (stateNode != null)
                {
                    // Collect Meta
                    if (stateNode.Meta != null && stateNode.Meta.Count > 0)
                    {
                        allMeta ??= new Dictionary<string, Dictionary<string, object>>();
                        allMeta[$"{_script.Id}.{currentPath}"] = new Dictionary<string, object>(stateNode.Meta);
                    }

                    // Collect Tags
                    if (stateNode.Tags != null && stateNode.Tags.Count > 0)
                    {
                        allTags ??= new List<string>();
                        foreach (var tag in stateNode.Tags)
                        {
                            if (!allTags.Contains(tag))
                                allTags.Add(tag);
                        }
                    }

                    // Collect Output (only from final states, use the deepest one)
                    if (stateNode.Type == "final" && stateNode.Output != null)
                    {
                        output = stateNode.Output;
                    }

                    // Collect Description (use the current/leaf state's description)
                    if (i == pathParts.Length - 1 && !string.IsNullOrEmpty(stateNode.Description))
                    {
                        description = stateNode.Description;
                    }
                }
            }
        }

        return (allMeta, allTags, output, description);
    }

    private void NotifyStateChanged(string previousState, string currentState, SendEvent? evt)
    {
        var notification = new StateChanged(previousState, currentState, evt);

        foreach (var subscriber in _subscribers)
        {
            subscriber.Tell(notification);
        }

        // Also publish to event stream
        Context.System.EventStream.Publish(notification);
    }

    #endregion

    #region Parallel State Management

    private void HandleRegionCompleted(RegionCompleted msg)
    {
        _log.Info($"[{_script.Id}] Region '{msg.RegionId}' completed");
        _completedRegions.Add(msg.RegionId);

        // Check if all regions are completed
        if (_completedRegions.Count == _regions.Count)
        {
            _log.Info($"[{_script.Id}] All {_regions.Count} regions completed");
            OnAllRegionsCompleted();
        }
    }

    private void HandleRegionStateChanged(RegionStateChanged msg)
    {
        _log.Debug($"[{_script.Id}] Region '{msg.RegionId}' changed to state '{msg.State}'");
        _regionStates[msg.RegionId] = msg.State;
    }

    private void HandleCrossRegionTransition(CrossRegionTransition msg)
    {
        _log.Info($"[{_script.Id}] Cross-region transition to '{msg.TargetState}' triggered by event '{msg.TriggeringEvent.Type}'");

        // Stop all parallel regions
        StopParallelRegions();

        // Execute transition actions
        ExecuteActions(msg.Actions, msg.TriggeringEvent.Data);

        // Resolve and transition to target state
        var resolvedTarget = ResolveTargetPath(msg.TargetState);
        var previousState = _currentState;

        // Exit current parallel state
        ExitState(previousState, msg.TriggeringEvent);

        // Update state
        _currentState = resolvedTarget;

        // Enter new state
        EnterState(_currentState, msg.TriggeringEvent);

        // Notify subscribers
        NotifyStateChanged(previousState, _currentState, msg.TriggeringEvent);
    }

    private void OnAllRegionsCompleted()
    {
        _log.Info($"[{_script.Id}] Parallel state completed");

        // Stop all region actors
        foreach (var region in _regions.Values)
        {
            Context.Stop(region);
        }

        _regions.Clear();
        _completedRegions.Clear();
        _isParallelState = false;

        // Trigger onDone transition if configured
        var stateNode = GetStateNode(_currentState);
        if (stateNode != null)
        {
            // For parallel states, check for an onDone transition
            if (stateNode.OnDone != null)
            {
                _log.Info($"[{_script.Id}] Processing onDone transition for parallel state '{_currentState}'");
                ProcessTransition(stateNode.OnDone, new SendEvent("done", null));
            }
            else
            {
                _log.Info($"[{_script.Id}] Parallel state '{_currentState}' reached completion (no onDone transition)");
                // Notify subscribers
                NotifyStateChanged(_currentState, $"{_currentState}.done", null);
            }
        }
    }

    private void StartParallelRegions(XStateNode stateNode)
    {
        if (stateNode.States == null || stateNode.States.Count == 0)
        {
            _log.Warning($"[{_script.Id}] Parallel state '{_currentState}' has no regions");
            return;
        }

        _log.Info($"[{_script.Id}] Starting {stateNode.States.Count} parallel regions");

        // Create a region actor for each sub-state
        foreach (var (regionId, regionNode) in stateNode.States)
        {
            // Check if we have saved history for this region
            var historyKey = $"{_currentState}.{regionId}";
            string initialState;

            if (_stateHistory.TryGetValue(historyKey, out var savedState))
            {
                // Restore from history
                initialState = savedState;
                _log.Info($"[{_script.Id}] Restoring region '{regionId}' from history: {savedState}");
            }
            else
            {
                // Use the region's initial state
                initialState = regionNode.Initial ?? regionNode.States?.Keys.FirstOrDefault() ?? "unknown";
                _log.Debug($"[{_script.Id}] Starting region '{regionId}' with initial state: {initialState}");
            }

            // Use GUID to ensure unique actor names even when rapidly recreating parallel states
            var uniqueName = $"region-{regionId}-{Guid.NewGuid():N}";
            var regionActor = Context.ActorOf(
                Props.Create(() => new RegionActor(regionId, regionNode, _context, initialState)),
                uniqueName
            );

            _regions[regionId] = regionActor;

            // Initialize region state (will be updated by RegionStateChanged messages)
            _regionStates[regionId] = initialState;

            // Start the region
            regionActor.Tell(new StartMachine());

            _log.Debug($"[{_script.Id}] Started region '{regionId}'");
        }

        _isParallelState = true;
    }

    private void StopParallelRegions()
    {
        _log.Info($"[{_script.Id}] Stopping {_regions.Count} parallel regions");

        // Save region states as history before clearing them
        // This allows restoring the parallel state's previous region states
        if (_regionStates.Count > 0)
        {
            foreach (var (regionId, regionState) in _regionStates)
            {
                var historyKey = $"{_currentState}.{regionId}";
                _stateHistory[historyKey] = regionState;
                _log.Debug($"[{_script.Id}] Saved parallel region history: {historyKey} -> {regionState}");
            }
        }

        foreach (var (regionId, regionActor) in _regions)
        {
            regionActor.Tell(new StopMachine());
            Context.Stop(regionActor);
            _log.Debug($"[{_script.Id}] Stopped region '{regionId}'");
        }

        _regions.Clear();
        _completedRegions.Clear();
        _regionStates.Clear();
        _isParallelState = false;
    }

    /// <summary>
    /// Checks if the machine is currently in the specified state.
    /// Supports absolute state paths like "#machineId.state.substate"
    /// </summary>
    private bool IsInState(string statePath)
    {
        if (string.IsNullOrEmpty(statePath))
            return false;

        // Remove machine ID prefix if present (e.g., "#test.regionA.A2" -> "regionA.A2")
        var targetState = statePath;
        if (statePath.StartsWith("#"))
        {
            var parts = statePath.Split('.', 2);
            if (parts.Length > 1)
            {
                targetState = parts[1]; // Remove "#machineId" prefix
            }
        }

        // For parallel states, check if any region is in the target state
        if (_isParallelState)
        {
            // Check region states
            foreach (var (regionId, regionState) in _regionStates)
            {
                var fullRegionState = $"{regionId}.{regionState}";
                if (fullRegionState == targetState || fullRegionState.StartsWith(targetState + "."))
                {
                    _log.Debug($"[{_script.Id}] In-state check: region '{regionId}' is in state '{regionState}' (matches '{targetState}')");
                    return true;
                }
            }
            _log.Debug($"[{_script.Id}] In-state check: no region matches '{targetState}'");
            return false;
        }

        // For non-parallel states, check current state
        if (_currentState == targetState || _currentState.StartsWith(targetState + "."))
        {
            _log.Debug($"[{_script.Id}] In-state check: current state '{_currentState}' matches '{targetState}'");
            return true;
        }

        _log.Debug($"[{_script.Id}] In-state check: current state '{_currentState}' does not match '{targetState}'");
        return false;
    }

    #endregion

    protected override void PreStart()
    {
        _log.Info($"[{_script.Id}] State machine actor started");
    }

    protected override void PostStop()
    {
        CancelDelayedTransitions();
        _log.Info($"[{_script.Id}] State machine actor stopped");
    }
}
