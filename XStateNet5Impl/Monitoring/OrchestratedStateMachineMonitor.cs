using System;
using System.Collections.Generic;
using System.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Monitoring
{
    /// <summary>
    /// Monitor for orchestrated state machines
    /// Monitors both orchestrator-level events and underlying machine events
    /// </summary>
    public class OrchestratedStateMachineMonitor : IStateMachineMonitor
    {
        private readonly string _machineId;
        private readonly StateMachine _underlyingMachine;
        private readonly EventBusOrchestrator _orchestrator;
        private bool _isMonitoring;

        // Events matching IStateMachineMonitor interface
        public event EventHandler<StateTransitionEventArgs>? StateTransitioned;
        public event EventHandler<StateMachineEventArgs>? EventReceived;
        public event EventHandler<ActionExecutedEventArgs>? ActionExecuted;
        public event EventHandler<GuardEvaluatedEventArgs>? GuardEvaluated;

        public string StateMachineId
        {
            get
            {
                // Remove # prefix if present
                return _machineId.StartsWith("#") ? _machineId.Substring(1) : _machineId;
            }
        }

        public OrchestratedStateMachineMonitor(
            string machineId,
            StateMachine underlyingMachine,
            EventBusOrchestrator orchestrator)
        {
            _machineId = machineId ?? throw new ArgumentNullException(nameof(machineId));
            _underlyingMachine = underlyingMachine ?? throw new ArgumentNullException(nameof(underlyingMachine));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;

            // Subscribe to underlying machine events (actions, transitions, guards)
            _underlyingMachine.OnTransition += OnStateMachineTransition;
            _underlyingMachine.OnEventReceived += OnStateMachineEventReceived;
            _underlyingMachine.OnActionExecuted += OnStateMachineActionExecuted;
            _underlyingMachine.OnGuardEvaluated += OnStateMachineGuardEvaluated;

            // Subscribe to orchestrator events (for event processing)
            _orchestrator.MachineEventProcessed += OnOrchestratorEventProcessed;
            _orchestrator.MachineEventFailed += OnOrchestratorEventFailed;

            _isMonitoring = true;
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            // Unsubscribe from underlying machine events
            _underlyingMachine.OnTransition -= OnStateMachineTransition;
            _underlyingMachine.OnEventReceived -= OnStateMachineEventReceived;
            _underlyingMachine.OnActionExecuted -= OnStateMachineActionExecuted;
            _underlyingMachine.OnGuardEvaluated -= OnStateMachineGuardEvaluated;

            // Unsubscribe from orchestrator events
            _orchestrator.MachineEventProcessed -= OnOrchestratorEventProcessed;
            _orchestrator.MachineEventFailed -= OnOrchestratorEventFailed;

            _isMonitoring = false;
        }

        public IEnumerable<string> GetCurrentStates()
        {
            // Get states and remove machine ID prefix
            return _underlyingMachine.GetSourceSubStateCollection(null)
                .Select(s =>
                {
                    var parts = s.Split('.');
                    return parts.Length > 1 ? parts.Last() : s;
                });
        }

        // Event handlers for underlying machine

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

        // Event handlers for orchestrator

        private void OnOrchestratorEventProcessed(object? sender, MachineEventProcessedEventArgs e)
        {
            // Only process events for this machine
            if (e.MachineId != _machineId) return;

            // Note: Transitions, actions, and guards are captured by underlying machine events
            // This event is useful for correlation and debugging
        }

        private void OnOrchestratorEventFailed(object? sender, MachineEventFailedEventArgs e)
        {
            // Only process events for this machine
            if (e.MachineId != _machineId) return;

            // Could raise a custom error event here if needed
        }
    }
}
