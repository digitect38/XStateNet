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

        // If in parallel state, broadcast event to all regions
        if (_isParallelState)
        {
            _log.Debug($"[{_script.Id}] Broadcasting event '{evt.Type}' to {_regions.Count} regions");
            foreach (var region in _regions.Values)
            {
                region.Tell(evt);
            }
            return;
        }

        // OPTIMIZATION: Use cached state node - no dictionary lookup!
        if (_currentStateNode == null)
        {
            _log.Error($"[{_script.Id}] Current state '{_currentState}' node is null");
            return;
        }

        // Search for transition in current state and parent states hierarchy
        List<XStateTransition>? transitions = null;
        bool found = false;

        // First check current state
        if (_currentTransitions != null && _currentTransitions.TryGetValue(evt.Type, out transitions))
        {
            found = true;
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

        // Execute transition
        if (transition.Internal || string.IsNullOrEmpty(transition.Target))
        {
            // Internal transition - execute actions but don't change state
            _log.Debug($"[{_script.Id}] Internal transition in state '{_currentState}'");
            ExecuteActions(transition.Actions, evt?.Data);
        }
        else
        {
            // External transition - change state
            Transition(transition.Target, evt, transition.Actions);
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
    /// Resolves relative target paths to absolute paths based on current state.
    /// Examples:
    /// - Current: "A.A1", Target: "A2" -> "A.A2" (sibling)
    /// - Current: "A.A1", Target: "B" -> "B" (root state)
    /// - Current: "A.A1.A1a", Target: "A1b" -> "A.A1.A1b" (sibling)
    /// </summary>
    private string ResolveTargetPath(string targetState)
    {
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

        if (!_script.States.TryGetValue(_currentState, out var stateNode))
            return;

        if (stateNode.Invoke?.OnDone != null)
        {
            ProcessTransition(stateNode.Invoke.OnDone, new SendEvent("done", msg.Data));
        }
    }

    private void HandleServiceError(ServiceError msg)
    {
        _log.Error(msg.Error, $"[{_script.Id}] Service '{msg.ServiceId}' failed");

        if (!_script.States.TryGetValue(_currentState, out var stateNode))
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
            new DelayedTransition(delay, transition.Target),
            Self
        );

        _delayedTransitions[key] = cancelable;
    }

    private void HandleDelayedTransition(DelayedTransition msg)
    {
        _log.Debug($"[{_script.Id}] Delayed transition triggered after {msg.Delay}ms");

        var key = $"after-{msg.Delay}";
        _delayedTransitions.Remove(key);

        if (!_script.States.TryGetValue(_currentState, out var stateNode))
            return;

        if (stateNode.After != null && stateNode.After.TryGetValue(msg.Delay, out var transition))
        {
            ProcessTransition(transition, null);
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
                _log.Debug($"[{_script.Id}] Taking always transition to '{transition.Target}'");

                // Execute the transition
                if (transition.Internal || string.IsNullOrEmpty(transition.Target))
                {
                    // Internal transition - execute actions but don't change state
                    ExecuteActions(transition.Actions, null);
                }
                else
                {
                    // External transition - this will trigger CheckAlwaysTransitions again recursively
                    Transition(transition.Target, null, transition.Actions);
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

        return new StateSnapshot(
            currentState,
            _context.GetAll(),
            _isRunning
        );
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

        return new StateSnapshot(
            currentState,
            _context.GetAll(),
            _isRunning
        );
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
        if (_script.States.TryGetValue(_currentState, out var stateNode))
        {
            // For parallel states, check for an onDone transition
            // This would be a transition from the parallel state itself
            // In XState, this is usually specified at the state level
            // For now, we'll just log completion
            _log.Info($"[{_script.Id}] Parallel state '{_currentState}' reached completion");

            // Notify subscribers
            NotifyStateChanged(_currentState, $"{_currentState}.done", null);
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
            var regionActor = Context.ActorOf(
                Props.Create(() => new RegionActor(regionId, regionNode, _context)),
                $"region-{regionId}"
            );

            _regions[regionId] = regionActor;

            // Initialize region state (will be updated by RegionStateChanged messages)
            var initialState = regionNode.Initial ?? regionNode.States?.Keys.FirstOrDefault() ?? "unknown";
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
