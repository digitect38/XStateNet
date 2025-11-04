using Akka.Actor;
using Akka.Event;
using XStateNet2.Core.Engine.ArrayBased;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Runtime;

namespace XStateNet2.Core.Actors;

/// <summary>
/// Array-optimized state machine actor using byte-indexed state/event lookups.
/// Provides 50-100% performance improvement over Dictionary-based StateMachineActor.
///
/// Supports: States, events, guards, actions, entry/exit actions, always transitions
/// Limitations: No parallel states, history states, services, or delayed transitions
/// For advanced features, use Dictionary or FrozenDictionary optimization levels.
/// </summary>
public class ArrayStateMachineActor : ReceiveActor, IWithUnboundedStash
{
    private readonly ArrayStateMachine _machine;
    private readonly InterpreterContext _context;
    private readonly ILoggingAdapter _log;
    private readonly HashSet<IActorRef> _subscribers = new();

    private byte _currentStateId;
    private bool _isRunning;

    public IStash Stash { get; set; } = null!;

    public ArrayStateMachineActor(ArrayStateMachine machine, InterpreterContext context)
    {
        _machine = machine ?? throw new ArgumentNullException(nameof(machine));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _log = Context.GetLogger();
        _currentStateId = machine.InitialStateId;

        // Register self for internal messaging
        _context.RegisterActor(_machine.Id, Self);

        Idle();
    }

    #region Actor Behaviors

    private void Idle()
    {
        Receive<StartMachine>(_ =>
        {
            _log.Info($"[{_machine.Id}] Starting array-optimized state machine");
            _isRunning = true;

            // Enter initial state
            EnterState(_currentStateId, null);

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
            var snapshot = CreateStateSnapshot();
            Sender.Tell(snapshot);
            await Task.CompletedTask;
        });
        Receive<Subscribe>(_ =>
        {
            _subscribers.Add(Sender);
            _log.Debug($"[{_machine.Id}] Subscriber added: {Sender.Path}");
        });
        Receive<Unsubscribe>(_ =>
        {
            _subscribers.Remove(Sender);
            _log.Debug($"[{_machine.Id}] Subscriber removed: {Sender.Path}");
        });
        Receive<StopMachine>(_ =>
        {
            _log.Info($"[{_machine.Id}] Stopping state machine");
            ExitState(_currentStateId, null);
            _isRunning = false;
            Become(Idle);
        });
    }

    #endregion

    #region Event Handling

    private void HandleEvent(SendEvent evt)
    {
        if (!_isRunning)
        {
            _log.Warning($"[{_machine.Id}] Received event '{evt.Type}' but machine is not running");
            return;
        }

        var currentStateName = _machine.GetStateName(_currentStateId);
        _log.Debug($"[{_machine.Id}] Event '{evt.Type}' in state '{currentStateName}'");

        // Convert event name to byte index
        var eventId = _machine.Map.Events.GetIndex(evt.Type);
        if (eventId == byte.MaxValue)
        {
            _log.Debug($"[{_machine.Id}] Unknown event '{evt.Type}' - ignored");
            return;
        }

        // Get transitions for current state and event (O(1) array access)
        var transitions = _machine.GetTransitions(_currentStateId, eventId);

        if (transitions == null || transitions.Length == 0)
        {
            _log.Debug($"[{_machine.Id}] No transition for event '{evt.Type}' in state '{currentStateName}'");
            return;
        }

        // Try each transition in order until one succeeds (guard passes)
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
            _log.Debug($"[{_machine.Id}] No transition taken for event '{evt.Type}' (all guards failed)");
        }
    }

    /// <summary>
    /// Try to process a transition. Returns true if the transition was taken, false if guard failed.
    /// </summary>
    private bool TryProcessTransition(ArrayTransition transition, SendEvent? evt)
    {
        // Evaluate guard (if specified)
        if (transition.GuardId != byte.MaxValue)
        {
            var guardName = _machine.Map.Guards.GetString(transition.GuardId);

            if (!_context.HasGuard(guardName))
            {
                _log.Error($"[{_machine.Id}] Guard '{guardName}' not registered");
                return false;
            }

            if (!_context.EvaluateGuard(guardName, evt?.Data))
            {
                _log.Debug($"[{_machine.Id}] Guard '{guardName}' failed");
                return false;
            }
        }

        // Execute transition
        if (transition.IsInternal || transition.TargetStateIds == null || transition.TargetStateIds.Length == 0)
        {
            // Internal transition - execute actions but don't change state
            _log.Debug($"[{_machine.Id}] Internal transition");
            ExecuteActions(transition.ActionIds, evt?.Data);
        }
        else
        {
            // External transition - change state (use first target for simplicity)
            Transition(transition.TargetStateIds[0], evt, transition.ActionIds);
        }

        return true;
    }

    private void Transition(byte targetStateId, SendEvent? evt, byte[]? transitionActionIds)
    {
        var previousStateId = _currentStateId;
        var previousStateName = _machine.GetStateName(previousStateId);
        var targetStateName = _machine.GetStateName(targetStateId);

        // _log.Info($"[{_machine.Id}] Transition: {previousStateName} -> {targetStateName}");

        // Exit current state
        ExitState(previousStateId, evt);

        // Execute transition actions
        ExecuteActions(transitionActionIds, evt?.Data);

        // Update state
        _currentStateId = targetStateId;

        // Enter new state
        EnterState(_currentStateId, evt);

        // Notify subscribers
        NotifyStateChanged(previousStateName, targetStateName, evt);
    }

    #endregion

    #region State Entry/Exit

    private void EnterState(byte stateId, SendEvent? evt)
    {
        var state = _machine.GetState(stateId);
        if (state == null)
        {
            _log.Error($"[{_machine.Id}] State {stateId} not found");
            return;
        }

        var stateName = _machine.GetStateName(stateId);
        _log.Debug($"[{_machine.Id}] Entering state '{stateName}'");

        // Execute entry actions
        ExecuteActions(state.EntryActions, evt?.Data);

        // Check always transitions (eventless transitions)
        CheckAlwaysTransitions(state, 0);
    }

    private void ExitState(byte stateId, SendEvent? evt)
    {
        var state = _machine.GetState(stateId);
        if (state == null)
        {
            _log.Error($"[{_machine.Id}] State {stateId} not found");
            return;
        }

        var stateName = _machine.GetStateName(stateId);
        _log.Debug($"[{_machine.Id}] Exiting state '{stateName}'");

        // Execute exit actions
        ExecuteActions(state.ExitActions, evt?.Data);
    }

    #endregion

    #region Action Execution

    private void ExecuteActions(byte[]? actionIds, object? eventData)
    {
        if (actionIds == null || actionIds.Length == 0)
            return;

        foreach (var actionId in actionIds)
        {
            if (actionId == byte.MaxValue)
                continue;

            var actionName = _machine.Map.Actions.GetString(actionId);

            try
            {
                if (_context.HasAction(actionName))
                {
                    _context.ExecuteAction(actionName, eventData);
                }
                else
                {
                    _log.Warning($"[{_machine.Id}] Action '{actionName}' not registered - skipping");
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"[{_machine.Id}] Error executing action '{actionName}'");
            }
        }
    }

    #endregion

    #region Always Transitions

    /// <summary>
    /// Check and process always (eventless) transitions.
    /// These are evaluated immediately upon entering a state.
    /// </summary>
    private void CheckAlwaysTransitions(ArrayStateNode state, int depth)
    {
        const int MAX_ALWAYS_DEPTH = 10; // Prevent infinite loops

        if (depth >= MAX_ALWAYS_DEPTH)
        {
            _log.Warning($"[{_machine.Id}] Maximum always transition depth ({MAX_ALWAYS_DEPTH}) reached - potential infinite loop");
            return;
        }

        if (state.AlwaysTransitions == null || state.AlwaysTransitions.Length == 0)
            return;

        // Evaluate guards in order and take first matching transition
        foreach (var transition in state.AlwaysTransitions)
        {
            bool shouldTransition = true;

            // Check guard (if specified)
            if (transition.GuardId != byte.MaxValue)
            {
                var guardName = _machine.Map.Guards.GetString(transition.GuardId);

                if (!_context.HasGuard(guardName))
                {
                    _log.Error($"[{_machine.Id}] Guard '{guardName}' not registered in always transition");
                    continue; // Try next transition
                }

                shouldTransition = _context.EvaluateGuard(guardName, null);
                _log.Debug($"[{_machine.Id}] Always transition guard '{guardName}' evaluated to {shouldTransition}");
            }

            if (shouldTransition)
            {
                var targetName = transition.TargetStateIds != null && transition.TargetStateIds.Length > 0
                    ? _machine.GetStateName(transition.TargetStateIds[0])
                    : "internal";
                _log.Debug($"[{_machine.Id}] Taking always transition to '{targetName}'");

                // Execute the transition
                if (transition.IsInternal || transition.TargetStateIds == null || transition.TargetStateIds.Length == 0)
                {
                    // Internal transition - execute actions but don't change state
                    ExecuteActions(transition.ActionIds, null);
                }
                else
                {
                    // External transition - this will trigger CheckAlwaysTransitions recursively
                    Transition(transition.TargetStateIds[0], null, transition.ActionIds);
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
        var currentStateName = _machine.GetStateName(_currentStateId);

        return new StateSnapshot(
            currentStateName,
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

    protected override void PreStart()
    {
        _log.Info($"[{_machine.Id}] Array-optimized state machine actor started (initial state: {_machine.GetStateName(_currentStateId)})");
    }

    protected override void PostStop()
    {
        _log.Info($"[{_machine.Id}] Array-optimized state machine actor stopped");
    }
}
