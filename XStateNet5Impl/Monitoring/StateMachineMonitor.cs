using System;
using System.Collections.Generic;
using System.Linq;

namespace XStateNet.Monitoring
{
    /// <summary>
    /// Default implementation of IStateMachineMonitor
    /// </summary>
    public class StateMachineMonitor : IStateMachineMonitor
    {
        private readonly StateMachine _stateMachine;
        private bool _isMonitoring;

        public event EventHandler<StateTransitionEventArgs>? StateTransitioned;
        public event EventHandler<StateMachineEventArgs>? EventReceived;
        public event EventHandler<ActionExecutedEventArgs>? ActionExecuted;
        public event EventHandler<GuardEvaluatedEventArgs>? GuardEvaluated;

        public string StateMachineId
        {
            get
            {
                var id = _stateMachine.machineId ?? "unknown";
                // Remove the # prefix if present
                return id.StartsWith("#") ? id.Substring(1) : id;
            }
        }

        public StateMachineMonitor(StateMachine stateMachine)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;

            // Subscribe to state machine events
            _stateMachine.OnTransition += OnStateMachineTransition;
            _stateMachine.OnEventReceived += OnStateMachineEventReceived;
            _stateMachine.OnActionExecuted += OnStateMachineActionExecuted;
            _stateMachine.OnGuardEvaluated += OnStateMachineGuardEvaluated;

            _isMonitoring = true;
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            // Unsubscribe from state machine events
            _stateMachine.OnTransition -= OnStateMachineTransition;
            _stateMachine.OnEventReceived -= OnStateMachineEventReceived;
            _stateMachine.OnActionExecuted -= OnStateMachineActionExecuted;
            _stateMachine.OnGuardEvaluated -= OnStateMachineGuardEvaluated;

            _isMonitoring = false;
        }

        public IEnumerable<string> GetCurrentStates()
        {
            // Get states and remove machine ID prefix
            return _stateMachine.GetSourceSubStateCollection(null)
                .Select(s =>
                {
                    var parts = s.Split('.');
                    return parts.Length > 1 ? parts.Last() : s;
                });
        }

        private void OnStateMachineTransition(CompoundState? fromState, StateNode? toState, string eventName)
        {
            // Extract just the state name without machine ID prefix
            string ExtractStateName(string? fullName)
            {
                if (string.IsNullOrEmpty(fullName)) return "none";
                var parts = fullName.Split('.');
                return parts.Length > 1 ? parts.Last() : fullName;
            }

            StateTransitioned?.Invoke(this, new StateTransitionEventArgs
            {
                StateMachineId = StateMachineId,
                FromState = ExtractStateName(fromState?.Name),
                ToState = ExtractStateName(toState?.Name),
                TriggerEvent = eventName,
                Timestamp = DateTime.UtcNow
            });
        }

        private void OnStateMachineEventReceived(string eventName, object? eventData)
        {
            EventReceived?.Invoke(this, new StateMachineEventArgs
            {
                StateMachineId = StateMachineId,
                EventName = eventName,
                EventData = eventData,
                Timestamp = DateTime.UtcNow
            });
        }

        private void OnStateMachineActionExecuted(string actionName, string? stateName)
        {
            ActionExecuted?.Invoke(this, new ActionExecutedEventArgs
            {
                StateMachineId = StateMachineId,
                ActionName = actionName,
                StateName = stateName,
                Timestamp = DateTime.UtcNow
            });
        }

        private void OnStateMachineGuardEvaluated(string guardName, bool result)
        {
            GuardEvaluated?.Invoke(this, new GuardEvaluatedEventArgs
            {
                StateMachineId = StateMachineId,
                GuardName = guardName,
                Result = result,
                Timestamp = DateTime.UtcNow
            });
        }
    }
}