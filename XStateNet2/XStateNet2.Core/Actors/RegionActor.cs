using Akka.Actor;
using Akka.Event;
using System.Text.Json;
using XStateNet2.Core.Engine;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Runtime;

namespace XStateNet2.Core.Actors;

/// <summary>
/// Region actor - represents a single region in a parallel state
/// Similar to StateMachineActor but reports completion to parent
/// </summary>
public class RegionActor : ReceiveActor, IWithUnboundedStash
{
    private readonly string _regionId;
    private readonly XStateNode _regionNode;
    private readonly InterpreterContext _context;
    private readonly ILoggingAdapter _log;
    private readonly HashSet<IActorRef> _subscribers = new();
    private readonly Dictionary<string, ICancelable> _delayedTransitions = new();

    private string _currentState;
    private IActorRef? _currentService;
    private bool _isRunning;
    private bool _isCompleted;
    private Dictionary<string, string> _regionStates = new();

    public IStash Stash { get; set; } = null!;

    public RegionActor(string regionId, XStateNode regionNode, InterpreterContext context, string? initialState = null)
    {
        _regionId = regionId ?? throw new ArgumentNullException(nameof(regionId));
        _regionNode = regionNode ?? throw new ArgumentNullException(nameof(regionNode));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _log = Context.GetLogger();

        // Use provided initial state (for history restoration) or find the default initial state
        _currentState = initialState ?? _regionNode.Initial ?? _regionNode.States?.Keys.FirstOrDefault() ?? "unknown";

        Idle();
    }

    private void Idle()
    {
        Receive<StartMachine>(_ =>
        {
            _log.Info($"[Region:{_regionId}] Starting region");
            _isRunning = true;

            EnterState(_currentState, null);

            // Notify parent of initial state (including nested states)
            Context.Parent.Tell(new RegionStateChanged(_regionId, _currentState));

            Become(Running);
            Stash.UnstashAll();
        });

        ReceiveAny(_ => Stash.Stash());
    }

    private void Running()
    {
        Receive<SendEventWithRegionStates>(msg =>
        {
            _regionStates = msg.RegionStates;
            HandleEvent(msg.Event);
        });
        Receive<SendEvent>(HandleEvent);
        Receive<DirectTransition>(HandleDirectTransition);
        Receive<GetState>(_ => Sender.Tell(CreateStateSnapshot()));
        Receive<ServiceDone>(HandleServiceDone);
        Receive<ServiceError>(HandleServiceError);
        Receive<DelayedTransition>(HandleDelayedTransition);
        Receive<StopMachine>(_ =>
        {
            _log.Info($"[Region:{_regionId}] Stopping region");
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
            _log.Warning($"[Region:{_regionId}] Received event '{evt.Type}' but region is not running");
            return;
        }

        _log.Debug($"[Region:{_regionId}] Event '{evt.Type}' in state '{_currentState}'");

        // Find transition by walking up the state hierarchy
        // For nested states like "red.bright", check "bright" first, then "red"
        var statePath = _currentState.Split('.');
        List<XStateTransition>? transitions = null;

        // Start from the deepest nested state and walk up
        for (int depth = statePath.Length; depth > 0; depth--)
        {
            var stateNode = FindStateNode(statePath, depth);
            if (stateNode?.On != null && stateNode.On.TryGetValue(evt.Type, out transitions))
            {
                _log.Debug($"[Region:{_regionId}] Found transition at depth {depth}");
                break;
            }
        }

        if (transitions != null && transitions.Count > 0)
        {
            // Try each transition in order until one succeeds
            bool transitionTaken = false;
            foreach (var transition in transitions)
            {
                if (TryProcessTransition(transition, evt, statePath))
                {
                    transitionTaken = true;
                    break; // Take only the first matching transition
                }
            }

            if (!transitionTaken)
            {
                _log.Debug($"[Region:{_regionId}] No transition taken for event '{evt.Type}' in state '{_currentState}' (all guards failed)");
            }
        }
        else
        {
            _log.Debug($"[Region:{_regionId}] No transition for event '{evt.Type}' in state '{_currentState}'");
        }
    }

    /// <summary>
    /// Handle direct transition command from parent (for multiple targets feature)
    /// </summary>
    private void HandleDirectTransition(DirectTransition msg)
    {
        if (!_isRunning)
        {
            _log.Warning($"[Region:{_regionId}] Received direct transition but region is not running");
            return;
        }

        _log.Info($"[Region:{_regionId}] Direct transition: {_currentState} -> {msg.TargetState}");

        var previousState = _currentState;

        // Exit current state
        ExitState(previousState, null);

        // Update state
        _currentState = msg.TargetState;

        // Enter new state
        EnterState(_currentState, null);

        // Notify parent of state change
        Context.Parent.Tell(new RegionStateChanged(_regionId, _currentState));
    }

    /// <summary>
    /// Find state node at given depth in the state path
    /// </summary>
    private XStateNode? FindStateNode(string[] statePath, int depth)
    {
        XStateNode? current = _regionNode;

        for (int i = 0; i < depth && current != null; i++)
        {
            if (current.States == null || !current.States.TryGetValue(statePath[i], out var next))
                return null;
            current = next;
        }

        return current;
    }

    /// <summary>
    /// Try to process a transition. Returns true if the transition was taken, false if guard failed.
    /// </summary>
    private bool TryProcessTransition(XStateTransition transition, SendEvent? evt, string[] currentStatePath)
    {
        // Evaluate guard
        if (!string.IsNullOrEmpty(transition.Cond))
        {
            if (!_context.HasGuard(transition.Cond))
            {
                _log.Error($"[Region:{_regionId}] Guard '{transition.Cond}' not found");
                return false;
            }

            if (!_context.EvaluateGuard(transition.Cond, evt?.Data))
            {
                _log.Debug($"[Region:{_regionId}] Guard '{transition.Cond}' failed");
                return false;
            }
        }

        // Evaluate in-state condition
        if (!string.IsNullOrEmpty(transition.In))
        {
            if (!IsInState(transition.In))
            {
                _log.Debug($"[Region:{_regionId}] In-state condition '{transition.In}' failed - not in that state");
                return false;
            }
        }

        // Check if this is a cross-region transition (absolute path starting with #)
        if (!string.IsNullOrEmpty(transition.Target) && transition.Target.StartsWith("#"))
        {
            // This targets a state outside the region - notify parent
            _log.Debug($"[Region:{_regionId}] Cross-region transition to '{transition.Target}' - notifying parent");
            Context.Parent.Tell(new CrossRegionTransition(transition.Target, evt, transition.Actions));
            return true; // Let parent handle it
        }

        // Execute transition
        if (transition.Internal || string.IsNullOrEmpty(transition.Target))
        {
            // Internal transition or no-target action
            _log.Debug($"[Region:{_regionId}] Internal/no-target transition in state '{_currentState}'");
            ExecuteActions(transition.Actions, evt?.Data);
        }
        else
        {
            // External transition
            Transition(transition.Target, evt, transition.Actions);
        }

        return true;
    }

    private void ProcessTransition(XStateTransition transition, SendEvent? evt)
    {
        var currentStatePath = _currentState.Split('.');
        TryProcessTransition(transition, evt, currentStatePath);
    }

    private void Transition(string targetState, SendEvent? evt, List<object>? transitionActions)
    {
        var previousState = _currentState;

        _log.Info($"[Region:{_regionId}] Transition: {previousState} -> {targetState}");

        // Exit current state
        ExitState(previousState, evt);

        // Execute transition actions
        ExecuteActions(transitionActions, evt?.Data);

        // Update state
        _currentState = targetState;

        // Enter new state
        EnterState(_currentState, evt);

        // Notify parent of state change
        Context.Parent.Tell(new RegionStateChanged(_regionId, _currentState));

        // Check if reached final state
        CheckCompletion();
    }

    #endregion

    #region State Entry/Exit

    private void EnterState(string state, SendEvent? evt)
    {
        _log.Debug($"[Region:{_regionId}] Entering state '{state}'");

        if (_regionNode.States == null || !_regionNode.States.TryGetValue(state, out var stateNode))
        {
            _log.Error($"[Region:{_regionId}] State '{state}' not found");
            return;
        }

        // Execute entry actions
        ExecuteActions(stateNode.Entry, evt?.Data);

        // Handle nested states - if this state has an initial state, build the full path
        string fullStatePath = state;
        var currentNode = stateNode;
        while (currentNode != null && !string.IsNullOrEmpty(currentNode.Initial) && currentNode.States != null)
        {
            var nestedInitial = currentNode.Initial;
            fullStatePath = $"{fullStatePath}.{nestedInitial}";

            if (currentNode.States.TryGetValue(nestedInitial, out var nestedNode))
            {
                _log.Debug($"[Region:{_regionId}] Entering nested state '{nestedInitial}' (full path: '{fullStatePath}')");
                ExecuteActions(nestedNode.Entry, evt?.Data);
                currentNode = nestedNode;
            }
            else
            {
                _log.Warning($"[Region:{_regionId}] Nested initial state '{nestedInitial}' not found");
                break;
            }
        }

        // Update current state to the full path and notify parent
        _currentState = fullStatePath;

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

        // Check always transitions
        CheckAlwaysTransitions(stateNode);

        // Check if this is a final state
        if (stateNode.Type == "final")
        {
            _isCompleted = true;
            Context.Parent.Tell(new RegionCompleted(_regionId));
            _log.Info($"[Region:{_regionId}] Reached final state - region completed");
        }
    }

    private void ExitState(string state, SendEvent? evt)
    {
        _log.Debug($"[Region:{_regionId}] Exiting state '{state}'");

        // For nested states like "red.bright", exit in reverse order: bright, then red
        var statePath = state.Split('.');

        // Exit nested states from deepest to shallowest
        for (int depth = statePath.Length; depth > 0; depth--)
        {
            var stateNode = FindStateNode(statePath, depth);
            if (stateNode != null)
            {
                var stateName = string.Join(".", statePath.Take(depth));
                _log.Debug($"[Region:{_regionId}] Exiting nested state '{stateName}'");
                ExecuteActions(stateNode.Exit, evt?.Data);
            }
        }

        // Cancel delayed transitions
        CancelDelayedTransitions();

        // Stop invoked service
        if (_currentService != null)
        {
            Context.Stop(_currentService);
            _currentService = null;
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
                    _context.ExecuteAction(actionName, eventData);
                }
                else if (action is JsonElement jsonElement)
                {
                    if (jsonElement.ValueKind == JsonValueKind.String)
                    {
                        var name = jsonElement.GetString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            _context.ExecuteAction(name, eventData);
                        }
                    }
                    else if (jsonElement.ValueKind == JsonValueKind.Object)
                    {
                        var actionDef = JsonSerializer.Deserialize<ActionDefinition>(jsonElement.GetRawText());
                        if (actionDef != null)
                        {
                            _context.ExecuteActionDefinition(actionDef, eventData, Self);
                        }
                    }
                }
                else if (action is ActionDefinition actionDef)
                {
                    _context.ExecuteActionDefinition(actionDef, eventData, Self);
                }
                else
                {
                    _log.Warning($"[Region:{_regionId}] Unknown action type: {action.GetType()}");
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"[Region:{_regionId}] Error executing action: {action}");
            }
        }
    }

    #endregion

    #region Service Management

    private void StartService(XStateInvoke invoke)
    {
        if (!_context.HasService(invoke.Src))
        {
            _log.Error($"[Region:{_regionId}] Service '{invoke.Src}' not registered");
            return;
        }

        _log.Debug($"[Region:{_regionId}] Starting service '{invoke.Src}'");

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
        _log.Info($"[Region:{_regionId}] Service '{msg.ServiceId}' completed successfully");

        if (_regionNode.States == null || !_regionNode.States.TryGetValue(_currentState, out var stateNode))
            return;

        if (stateNode.Invoke?.OnDone != null)
        {
            ProcessTransition(stateNode.Invoke.OnDone, new SendEvent("done", msg.Data));
        }
    }

    private void HandleServiceError(ServiceError msg)
    {
        _log.Error(msg.Error, $"[Region:{_regionId}] Service '{msg.ServiceId}' failed");

        if (_regionNode.States == null || !_regionNode.States.TryGetValue(_currentState, out var stateNode))
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
            _log.Warning($"[Region:{_regionId}] Delayed transition '{key}' already scheduled");
            return;
        }

        _log.Debug($"[Region:{_regionId}] Scheduling delayed transition after {delay}ms");

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
        _log.Debug($"[Region:{_regionId}] Delayed transition triggered after {msg.Delay}ms");

        var key = $"after-{msg.Delay}";
        _delayedTransitions.Remove(key);

        if (_regionNode.States == null || !_regionNode.States.TryGetValue(_currentState, out var stateNode))
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

    private void CheckAlwaysTransitions(XStateNode stateNode)
    {
        if (stateNode.Always == null || stateNode.Always.Count == 0)
            return;

        foreach (var transition in stateNode.Always)
        {
            bool shouldTransition = true;

            if (!string.IsNullOrEmpty(transition.Cond))
            {
                if (!_context.HasGuard(transition.Cond))
                {
                    _log.Error($"[Region:{_regionId}] Guard '{transition.Cond}' not found in always transition");
                    continue;
                }

                shouldTransition = _context.EvaluateGuard(transition.Cond, null);
            }

            if (shouldTransition)
            {
                if (transition.Internal || string.IsNullOrEmpty(transition.Target))
                {
                    ExecuteActions(transition.Actions, null);
                }
                else
                {
                    Transition(transition.Target, null, transition.Actions);
                }

                break;
            }
        }
    }

    #endregion

    #region State Management

    private StateSnapshot CreateStateSnapshot()
    {
        return new StateSnapshot(
            _currentState,
            _context.GetAll(),
            _isRunning
        );
    }

    private void CheckCompletion()
    {
        if (_regionNode.States == null || !_regionNode.States.TryGetValue(_currentState, out var stateNode))
            return;

        if (stateNode.Type == "final" && !_isCompleted)
        {
            _isCompleted = true;
            Context.Parent.Tell(new RegionCompleted(_regionId));
            _log.Info($"[Region:{_regionId}] Reached final state - region completed");
        }
    }

    #endregion

    /// <summary>
    /// Checks if the machine is currently in the specified state.
    /// Supports absolute state paths like "#machineId.regionId.state"
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

        // Check if target state is in a different region
        if (targetState.Contains('.'))
        {
            var targetParts = targetState.Split('.', 2);
            var targetRegion = targetParts[0];
            var targetRegionState = targetParts.Length > 1 ? targetParts[1] : "";

            // Check if we have state information for the target region
            if (_regionStates.TryGetValue(targetRegion, out var regionState))
            {
                var matches = regionState == targetRegionState || regionState.StartsWith(targetRegionState + ".");
                _log.Debug($"[Region:{_regionId}] In-state check: region '{targetRegion}' is in state '{regionState}' (checking '{targetRegionState}') = {matches}");
                return matches;
            }

            _log.Debug($"[Region:{_regionId}] In-state check: no state information for region '{targetRegion}'");
            return false;
        }

        // For non-region states, check current state
        if (_currentState == targetState || _currentState.StartsWith(targetState + "."))
        {
            _log.Debug($"[Region:{_regionId}] In-state check: current state '{_currentState}' matches '{targetState}'");
            return true;
        }

        _log.Debug($"[Region:{_regionId}] In-state check: current state '{_currentState}' does not match '{targetState}'");
        return false;
    }

    protected override void PreStart()
    {
        _log.Info($"[Region:{_regionId}] Region actor started");
    }

    protected override void PostStop()
    {
        CancelDelayedTransitions();
        _log.Info($"[Region:{_regionId}] Region actor stopped");
    }
}
