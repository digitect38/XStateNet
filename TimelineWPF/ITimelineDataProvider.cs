using TimelineWPF.Models;

namespace TimelineWPF
{
    /// <summary>
    /// Interface for providing timeline data to the TimelineComponent
    /// </summary>
    public interface ITimelineDataProvider
    {
        /// <summary>
        /// Adds a new state machine to the timeline
        /// </summary>
        void AddStateMachine(string name, List<string> states, string initialState);

        /// <summary>
        /// Removes a state machine from the timeline
        /// </summary>
        void RemoveStateMachine(string name);

        /// <summary>
        /// Clears all state machines
        /// </summary>
        void ClearStateMachines();

        /// <summary>
        /// Adds a state transition for a specific state machine
        /// </summary>
        void AddStateTransition(string machineName, string fromState, string toState, double timestamp);

        /// <summary>
        /// Adds an event for a specific state machine
        /// </summary>
        void AddEvent(string machineName, string eventName, double timestamp);

        /// <summary>
        /// Adds an action for a specific state machine
        /// </summary>
        void AddAction(string machineName, string actionName, double timestamp);

        /// <summary>
        /// Clears all timeline data for a specific state machine
        /// </summary>
        void ClearTimelineData(string machineName);

        /// <summary>
        /// Clears all timeline data for all state machines
        /// </summary>
        void ClearAllTimelineData();

        /// <summary>
        /// Sets the current state for a state machine
        /// </summary>
        void SetCurrentState(string machineName, string state, double timestamp);

        /// <summary>
        /// Gets all registered state machines
        /// </summary>
        IEnumerable<StateMachineDefinition> GetStateMachines();
    }
}