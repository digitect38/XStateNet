using System;
using System.Collections.Generic;

namespace XStateNet.Monitoring
{
    /// <summary>
    /// Interface for monitoring state machine events in real-time
    /// </summary>
    public interface IStateMachineMonitor
    {
        /// <summary>
        /// Fired when a state transition occurs
        /// </summary>
        event EventHandler<StateTransitionEventArgs>? StateTransitioned;

        /// <summary>
        /// Fired when an event is received by the state machine
        /// </summary>
        event EventHandler<StateMachineEventArgs>? EventReceived;

        /// <summary>
        /// Fired when an action is executed
        /// </summary>
        event EventHandler<ActionExecutedEventArgs>? ActionExecuted;

        /// <summary>
        /// Fired when a guard is evaluated
        /// </summary>
        event EventHandler<GuardEvaluatedEventArgs>? GuardEvaluated;

        /// <summary>
        /// Start monitoring the state machine
        /// </summary>
        void StartMonitoring();

        /// <summary>
        /// Stop monitoring the state machine
        /// </summary>
        void StopMonitoring();

        /// <summary>
        /// Get the current state of the state machine
        /// </summary>
        IEnumerable<string> GetCurrentStates();

        /// <summary>
        /// Get the state machine ID being monitored
        /// </summary>
        string StateMachineId { get; }
    }

    /// <summary>
    /// Event args for state transitions
    /// </summary>
    public class StateTransitionEventArgs : EventArgs
    {
        public string StateMachineId { get; set; } = string.Empty;
        public string FromState { get; set; } = string.Empty;
        public string ToState { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string? TriggerEvent { get; set; }
    }

    /// <summary>
    /// Event args for state machine events
    /// </summary>
    public class StateMachineEventArgs : EventArgs
    {
        public string StateMachineId { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public object? EventData { get; set; }
    }

    /// <summary>
    /// Event args for action execution
    /// </summary>
    public class ActionExecutedEventArgs : EventArgs
    {
        public string StateMachineId { get; set; } = string.Empty;
        public string ActionName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string? StateName { get; set; }
    }

    /// <summary>
    /// Event args for guard evaluation
    /// </summary>
    public class GuardEvaluatedEventArgs : EventArgs
    {
        public string StateMachineId { get; set; } = string.Empty;
        public string GuardName { get; set; } = string.Empty;
        public bool Result { get; set; }
        public DateTime Timestamp { get; set; }
    }
}