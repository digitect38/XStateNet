using System.Collections.Concurrent;
using System.Diagnostics;
using XStateNet;
using XStateNet.Monitoring;

namespace TimelineWPF
{
    /// <summary>
    /// Adapter that connects XStateNet state machines to TimelineWPF in real-time
    /// </summary>
    public class RealTimeStateMachineAdapter : IDisposable
    {
        private readonly ITimelineDataProvider _timelineProvider;
        private readonly ConcurrentDictionary<string, IStateMachineMonitor> _monitors = new();
        private readonly ConcurrentDictionary<string, string> _machineDisplayNames = new();
        private readonly Stopwatch _stopwatch = new();
        private readonly object _lockObj = new();
        private double? _startTime = null;

        public event EventHandler? ViewModelUpdated;

        public RealTimeStateMachineAdapter(ITimelineDataProvider timelineProvider)
        {
            _timelineProvider = timelineProvider ?? throw new ArgumentNullException(nameof(timelineProvider));
            _stopwatch.Start();
        }

        /// <summary>
        /// Register a state machine for real-time monitoring
        /// </summary>
        public void RegisterStateMachine(StateMachine stateMachine, string? displayName = null)
        {
            if (stateMachine == null) throw new ArgumentNullException(nameof(stateMachine));

            lock (_lockObj)
            {
                var machineId = stateMachine.machineId ?? Guid.NewGuid().ToString();
                var normalizedId = machineId.StartsWith("#") ? machineId.Substring(1) : machineId;
                var name = displayName ?? normalizedId;

                // Remove existing monitor if present
                if (_monitors.ContainsKey(normalizedId))
                {
                    UnregisterStateMachine(normalizedId);
                }

                // Create monitor
                var monitor = new StateMachineMonitor(stateMachine);

                // Subscribe to events
                monitor.StateTransitioned += OnStateTransitioned;
                monitor.EventReceived += OnEventReceived;
                monitor.ActionExecuted += OnActionExecuted;

                // Add state machine to timeline
                var currentStates = stateMachine.GetSourceSubStateCollection(null).ToList();
                var initialState = currentStates.FirstOrDefault() ?? "unknown";

                // Collect all possible states from the state machine
                // For now, use current states - in production, you'd traverse the entire state tree
                _timelineProvider.AddStateMachine(name, currentStates, initialState);

                // Start monitoring
                monitor.StartMonitoring();

                // Add initial state as first state item
                var initialTimestamp = GetCurrentTimestamp();
                var cleanInitialState = initialState.Contains(".")
                    ? initialState.Split('.').Last()
                    : initialState;
                _timelineProvider.AddStateTransition(name, null!, cleanInitialState, initialTimestamp);

                // Use the monitor's ID (which removes # prefix) for consistency
                var monitorId = monitor.StateMachineId;
                _monitors[monitorId] = monitor;
                _machineDisplayNames[monitorId] = name;

                // Notify that the view model has been updated with a new state machine
                ViewModelUpdated?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Unregister a state machine from monitoring
        /// </summary>
        public void UnregisterStateMachine(string machineId)
        {
            lock (_lockObj)
            {
                // Normalize the ID (remove # if present)
                var normalizedId = machineId.StartsWith("#") ? machineId.Substring(1) : machineId;

                if (_monitors.TryGetValue(normalizedId, out var monitor))
                {
                    monitor.StopMonitoring();

                    // Unsubscribe from events
                    monitor.StateTransitioned -= OnStateTransitioned;
                    monitor.EventReceived -= OnEventReceived;
                    monitor.ActionExecuted -= OnActionExecuted;

                    if (!_monitors.TryRemove(normalizedId, out _))
                    {
                        Debug.WriteLine($"[TIMELINE ADAPTER] WARNING: Failed to remove monitor for ID: {normalizedId}");
                    }

                    // Remove from timeline using the display name
                    if (_machineDisplayNames.TryGetValue(normalizedId, out var displayName))
                    {
                        _timelineProvider.RemoveStateMachine(displayName);
                        if (!_machineDisplayNames.TryRemove(normalizedId, out _))
                        {
                            Debug.WriteLine($"[TIMELINE ADAPTER] WARNING: Failed to remove display name for ID: {normalizedId}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Clear all registered state machines
        /// </summary>
        public void Clear()
        {
            lock (_lockObj)
            {
                foreach (var monitor in _monitors.Values)
                {
                    monitor.StopMonitoring();
                    monitor.StateTransitioned -= OnStateTransitioned;
                    monitor.EventReceived -= OnEventReceived;
                    monitor.ActionExecuted -= OnActionExecuted;
                }
                _monitors.Clear();
                _machineDisplayNames.Clear();
                _timelineProvider.ClearStateMachines();
            }
        }

        private double GetCurrentTimestamp()
        {
            // Return microseconds since start
            var currentTime = _stopwatch.Elapsed.TotalMilliseconds * 1000;

            // Initialize start time on first call
            if (_startTime == null)
            {
                _startTime = currentTime;
                Console.WriteLine($"[TIMELINE ADAPTER] Setting start time to {_startTime}");
                return 0; // First event starts at time 0
            }

            var relativeTime = currentTime - _startTime.Value;
            Console.WriteLine($"[TIMELINE ADAPTER] Current: {currentTime}, Start: {_startTime}, Relative: {relativeTime}");
            return relativeTime;
        }

        private void OnStateTransitioned(object? sender, StateTransitionEventArgs e)
        {
            Console.WriteLine($"[TIMELINE] OnStateTransitioned called: {e.StateMachineId} from {e.FromState} to {e.ToState}");

            if (sender is IStateMachineMonitor monitor && _machineDisplayNames.TryGetValue(monitor.StateMachineId, out var displayName))
            {
                var timestamp = GetCurrentTimestamp();
                Console.WriteLine($"[TIMELINE] Adding state transition to timeline: {displayName} {e.FromState} -> {e.ToState} at {timestamp}");
                _timelineProvider.AddStateTransition(
                    displayName,
                    e.FromState ?? string.Empty,
                    e.ToState,
                    timestamp
                );
                Console.WriteLine($"[TIMELINE] State transition added successfully");
                ViewModelUpdated?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Console.WriteLine($"[TIMELINE] WARNING: Could not find display name for machine ID: {(sender as IStateMachineMonitor)?.StateMachineId}");
            }
        }

        private void OnEventReceived(object? sender, StateMachineEventArgs e)
        {
            Console.WriteLine($"[TIMELINE] OnEventReceived called: {e.StateMachineId} event {e.EventName}");

            if (sender is IStateMachineMonitor monitor && _machineDisplayNames.TryGetValue(monitor.StateMachineId, out var displayName))
            {
                var timestamp = GetCurrentTimestamp();
                Console.WriteLine($"[TIMELINE] Adding event to timeline: {displayName} {e.EventName} at {timestamp}");
                _timelineProvider.AddEvent(
                    displayName,
                    e.EventName,
                    timestamp
                );
                Console.WriteLine($"[TIMELINE] Event added successfully");
                ViewModelUpdated?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Console.WriteLine($"[TIMELINE] WARNING: Could not find display name for event machine ID: {(sender as IStateMachineMonitor)?.StateMachineId}");
            }
        }

        private void OnActionExecuted(object? sender, ActionExecutedEventArgs e)
        {
            Console.WriteLine($"[TIMELINE] OnActionExecuted called: {e.StateMachineId} action {e.ActionName}");

            if (sender is IStateMachineMonitor monitor && _machineDisplayNames.TryGetValue(monitor.StateMachineId, out var displayName))
            {
                var timestamp = GetCurrentTimestamp();
                Console.WriteLine($"[TIMELINE] Adding action to timeline: {displayName} {e.ActionName} at {timestamp}");
                _timelineProvider.AddAction(
                    displayName,
                    e.ActionName,
                    timestamp
                );
                Console.WriteLine($"[TIMELINE] Action added successfully");
                ViewModelUpdated?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Console.WriteLine($"[TIMELINE] WARNING: Could not find display name for action machine ID: {(sender as IStateMachineMonitor)?.StateMachineId}");
            }
        }

        public void Dispose()
        {
            Clear();
            _stopwatch.Stop();
        }
    }
}