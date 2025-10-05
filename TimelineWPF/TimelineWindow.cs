using System.Windows;
using XStateNet;

namespace TimelineWPF
{
    /// <summary>
    /// A window that hosts the Timeline component for visualizing state machine transitions
    /// </summary>
    public class TimelineWindow : Window, IDisposable
    {
        private TimelineComponent _timelineComponent;
        private RealTimeStateMachineAdapter? _realTimeAdapter;

        /// <summary>
        /// Gets the timeline data provider for managing state machines dynamically
        /// </summary>
        public ITimelineDataProvider? DataProvider => _timelineComponent?.DataProvider;

        /// <summary>
        /// Gets the real-time state machine adapter for monitoring XStateNet state machines
        /// </summary>
        public RealTimeStateMachineAdapter RealTimeAdapter
        {
            get
            {
                if (_realTimeAdapter == null && DataProvider != null)
                {
                    _realTimeAdapter = new RealTimeStateMachineAdapter(DataProvider);
                }
                return _realTimeAdapter!;
            }
        }

        public TimelineWindow()
        {
            Title = "XStateNet Timeline Viewer";
            Width = 1200;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _timelineComponent = new TimelineComponent();
            Content = _timelineComponent;
        }

        /// <summary>
        /// Shows the timeline window as a modeless dialog
        /// </summary>
        public void ShowTimeline()
        {
            Show();
        }

        /// <summary>
        /// Shows the timeline window as a modal dialog
        /// </summary>
        public bool? ShowTimelineDialog()
        {
            return ShowDialog();
        }

        #region Convenience Methods for State Machine Management

        /// <summary>
        /// Adds a new state machine to the timeline
        /// </summary>
        public void AddStateMachine(string name, List<string> states, string initialState)
        {
            DataProvider?.AddStateMachine(name, states, initialState);
        }

        /// <summary>
        /// Removes a state machine from the timeline
        /// </summary>
        public void RemoveStateMachine(string name)
        {
            DataProvider?.RemoveStateMachine(name);
        }

        /// <summary>
        /// Clears all state machines
        /// </summary>
        public void ClearStateMachines()
        {
            DataProvider?.ClearStateMachines();
        }

        /// <summary>
        /// Adds a state transition for a specific state machine
        /// </summary>
        public void AddStateTransition(string machineName, string fromState, string toState, double timestamp)
        {
            DataProvider?.AddStateTransition(machineName, fromState, toState, timestamp);
        }

        /// <summary>
        /// Adds an event for a specific state machine
        /// </summary>
        public void AddEvent(string machineName, string eventName, double timestamp)
        {
            DataProvider?.AddEvent(machineName, eventName, timestamp);
        }

        /// <summary>
        /// Adds an action for a specific state machine
        /// </summary>
        public void AddAction(string machineName, string actionName, double timestamp)
        {
            DataProvider?.AddAction(machineName, actionName, timestamp);
        }

        /// <summary>
        /// Clears all timeline data for a specific state machine
        /// </summary>
        public void ClearTimelineData(string machineName)
        {
            DataProvider?.ClearTimelineData(machineName);
        }

        /// <summary>
        /// Clears all timeline data for all state machines
        /// </summary>
        public void ClearAllTimelineData()
        {
            DataProvider?.ClearAllTimelineData();
        }

        /// <summary>
        /// Sets the current state for a state machine
        /// </summary>
        public void SetCurrentState(string machineName, string state, double timestamp)
        {
            DataProvider?.SetCurrentState(machineName, state, timestamp);
        }

        #endregion

        #region Real-time Monitoring Methods

        /// <summary>
        /// Register a XStateNet state machine for real-time monitoring
        /// </summary>
        public void RegisterStateMachine(StateMachine stateMachine, string? displayName = null)
        {
            RealTimeAdapter.RegisterStateMachine(stateMachine, displayName);
        }

        /// <summary>
        /// Unregister a state machine from real-time monitoring
        /// </summary>
        public void UnregisterStateMachine(string machineId)
        {
            _realTimeAdapter?.UnregisterStateMachine(machineId);
        }

        #endregion

        public void Dispose()
        {
            _realTimeAdapter?.Dispose();
        }
    }
}