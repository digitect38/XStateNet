using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Distributed.EventBus;
using XStateNet.Distributed.PubSub;

namespace TimelineWPF
{
    /// <summary>
    /// Adapter that connects the pub/sub EventNotificationService to TimelineWPF
    /// Enables distributed state machine visualization across multiple processes/machines
    /// </summary>
    public class PubSubTimelineAdapter : IDisposable
    {
        private readonly ITimelineDataProvider _timelineProvider;
        private readonly IStateMachineEventBus _eventBus;
        private readonly ConcurrentDictionary<string, EventNotificationService> _notificationServices = new();
        private readonly ConcurrentDictionary<string, IDisposable> _subscriptions = new();
        private readonly ConcurrentDictionary<string, string> _machineDisplayNames = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastEventTimes = new();
        private readonly Stopwatch _stopwatch = new();
        private readonly object _lockObj = new();
        private bool _isStarted;

        public PubSubTimelineAdapter(ITimelineDataProvider timelineProvider, IStateMachineEventBus eventBus)
        {
            _timelineProvider = timelineProvider ?? throw new ArgumentNullException(nameof(timelineProvider));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _stopwatch.Start();
        }

        /// <summary>
        /// Start the adapter and connect to the event bus
        /// </summary>
        public async Task StartAsync()
        {
            if (_isStarted) return;

            await _eventBus.ConnectAsync();
            await SubscribeToGlobalEvents();
            _isStarted = true;
        }

        /// <summary>
        /// Stop the adapter and disconnect from the event bus
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isStarted) return;

            UnsubscribeAll();
            await _eventBus.DisconnectAsync();
            _isStarted = false;
        }

        /// <summary>
        /// Register a local state machine for pub/sub monitoring
        /// </summary>
        public async Task RegisterStateMachineAsync(StateMachine stateMachine, string? displayName = null)
        {
            if (stateMachine == null) throw new ArgumentNullException(nameof(stateMachine));

            await EnsureStarted();

            lock (_lockObj)
            {
                var machineId = stateMachine.machineId ?? Guid.NewGuid().ToString();
                var normalizedId = machineId.StartsWith("#") ? machineId.Substring(1) : machineId;
                var name = displayName ?? normalizedId;

                // Create EventNotificationService for this machine
                var notificationService = new EventNotificationService(
                    stateMachine,
                    _eventBus,
                    normalizedId
                );

                // Store the service
                _notificationServices[normalizedId] = notificationService;
                _machineDisplayNames[normalizedId] = name;

                // Add to timeline
                var currentStates = stateMachine.GetSourceSubStateCollection(null).ToList();
                var initialState = currentStates.FirstOrDefault() ?? "unknown";
                _timelineProvider.AddStateMachine(name, currentStates, initialState);

                // Add initial state
                var timestamp = GetCurrentTimestamp();
                var cleanInitialState = initialState.Contains(".")
                    ? initialState.Split('.').Last()
                    : initialState;
                _timelineProvider.AddStateTransition(name, null, cleanInitialState, timestamp);
            }

            // Start the notification service
            var service = _notificationServices.Values.Last();
            await service.StartAsync();
        }

        /// <summary>
        /// Subscribe to a remote state machine via pub/sub
        /// </summary>
        public async Task SubscribeToRemoteMachineAsync(string machineId, string? displayName = null)
        {
            await EnsureStarted();

            var normalizedId = machineId.StartsWith("#") ? machineId.Substring(1) : machineId;
            var name = displayName ?? normalizedId;

            lock (_lockObj)
            {
                _machineDisplayNames[normalizedId] = name;

                // Add placeholder in timeline for remote machine
                _timelineProvider.AddStateMachine(name, new List<string> { "unknown" }, "unknown");
            }

            // Subscribe to state changes
            var stateChangeSub = await _eventBus.SubscribeToStateChangesAsync(normalizedId, evt =>
            {
                OnRemoteStateChange(normalizedId, evt);
            });
            AddSubscription($"state_{normalizedId}", stateChangeSub);

            // Subscribe to all events from this machine
            var machineSub = await _eventBus.SubscribeToMachineAsync(normalizedId, evt =>
            {
                OnRemoteMachineEvent(normalizedId, evt);
            });
            AddSubscription($"machine_{normalizedId}", machineSub);
        }

        /// <summary>
        /// Enable cross-machine synchronization
        /// </summary>
        public async Task EnableDistributedSyncAsync(string groupName = "timeline-sync")
        {
            await EnsureStarted();

            // Subscribe to group events for timeline synchronization
            var groupSub = await _eventBus.SubscribeToGroupAsync(groupName, evt =>
            {
                OnGroupSyncEvent(evt);
            });
            AddSubscription($"group_{groupName}", groupSub);
        }

        /// <summary>
        /// Broadcast a local event to all connected timelines
        /// </summary>
        public async Task BroadcastLocalEventAsync(string eventName, object? payload = null)
        {
            if (!_isStarted) return;

            await _eventBus.BroadcastAsync(eventName, payload);
        }

        #region Private Methods

        private async Task EnsureStarted()
        {
            if (!_isStarted)
            {
                await StartAsync();
            }
        }

        private async Task SubscribeToGlobalEvents()
        {
            // Subscribe to all state changes across all machines
            var allStatesSub = await _eventBus.SubscribeToPatternAsync("state.*", evt =>
            {
                if (evt is StateChangeEvent stateChange)
                {
                    OnAnyStateChange(stateChange);
                }
            });
            AddSubscription("all_states", allStatesSub);

            // Subscribe to all broadcast events
            var broadcastSub = await _eventBus.SubscribeToAllAsync(evt =>
            {
                if (evt.EventName?.StartsWith("TIMELINE_") == true)
                {
                    OnTimelineCommand(evt);
                }
            });
            AddSubscription("broadcasts", broadcastSub);
        }

        private void OnRemoteStateChange(string machineId, StateChangeEvent evt)
        {
            lock (_lockObj)
            {
                if (!_machineDisplayNames.TryGetValue(machineId, out var displayName))
                    return;

                var timestamp = GetCurrentTimestamp();

                // Update timeline with state transition
                _timelineProvider.AddStateTransition(
                    displayName,
                    evt.OldState ?? "unknown",
                    evt.NewState ?? "unknown",
                    timestamp
                );

                // Track last event time for synchronization
                _lastEventTimes[machineId] = DateTime.UtcNow;
            }
        }

        private void OnRemoteMachineEvent(string machineId, StateMachineEvent evt)
        {
            lock (_lockObj)
            {
                if (!_machineDisplayNames.TryGetValue(machineId, out var displayName))
                    return;

                var timestamp = GetCurrentTimestamp();

                // Handle different event types based on the payload
                if (evt.Payload is ActionExecutedNotification action)
                {
                    _timelineProvider.AddAction(displayName, action.ActionName, timestamp);
                }
                else if (evt.Payload is GuardEvaluatedNotification guard)
                {
                    // Optionally add guard evaluations to timeline
                    if (guard.Passed)
                    {
                        _timelineProvider.AddEvent(displayName, $"Guard:{guard.GuardName}", timestamp);
                    }
                }
                else if (evt.Payload is TransitionNotification transition)
                {
                    _timelineProvider.AddEvent(displayName, transition.Trigger, timestamp);
                }
                else if (evt.Payload is ErrorNotification error)
                {
                    _timelineProvider.AddEvent(displayName, $"ERROR:{error.ErrorMessage}", timestamp);
                }
                else
                {
                    // Generic event
                    _timelineProvider.AddEvent(displayName, evt.EventName ?? "unknown", timestamp);
                }

                _lastEventTimes[machineId] = DateTime.UtcNow;
            }
        }

        private void OnAnyStateChange(StateChangeEvent evt)
        {
            // Handle state changes from any machine
            var machineId = evt.SourceMachineId;
            if (string.IsNullOrEmpty(machineId)) return;

            // Skip if we're already handling this machine locally
            if (_notificationServices.ContainsKey(machineId)) return;

            // Auto-discover new machines
            lock (_lockObj)
            {
                if (!_machineDisplayNames.ContainsKey(machineId))
                {
                    // Auto-register discovered machine
                    var displayName = $"Remote:{machineId}";
                    _machineDisplayNames[machineId] = displayName;

                    // Add to timeline
                    _timelineProvider.AddStateMachine(displayName, new List<string> { evt.NewState ?? "unknown" }, evt.NewState ?? "unknown");
                }
            }

            OnRemoteStateChange(machineId, evt);
        }

        private void OnGroupSyncEvent(StateMachineEvent evt)
        {
            // Handle timeline synchronization events
            if (evt.EventName == "TIMELINE_SYNC_REQUEST")
            {
                // Respond with local timeline state
                Task.Run(async () =>
                {
                    var localMachines = GetLocalMachineStates();
                    await _eventBus.PublishToGroupAsync("timeline-sync", "TIMELINE_SYNC_RESPONSE", localMachines);
                });
            }
            else if (evt.EventName == "TIMELINE_SYNC_RESPONSE" && evt.Payload is ConcurrentDictionary<string, object> states)
            {
                // Update timeline with remote states
                SyncRemoteStates(states);
            }
        }

        private void OnTimelineCommand(StateMachineEvent evt)
        {
            // Handle timeline-specific broadcast commands
            switch (evt.EventName)
            {
                case "TIMELINE_CLEAR":
                    _timelineProvider.ClearStateMachines();
                    break;

                case "TIMELINE_PAUSE":
                    // Implement pause logic if needed
                    break;

                case "TIMELINE_RESUME":
                    // Implement resume logic if needed
                    break;
            }
        }

        private ConcurrentDictionary<string, object> GetLocalMachineStates()
        {
            lock (_lockObj)
            {
                var states = new ConcurrentDictionary<string, object>();

                foreach (var kvp in _machineDisplayNames)
                {
                    var machines = _timelineProvider.GetStateMachines();
                    var machine = machines.FirstOrDefault(m => m.Name == kvp.Value);

                    if (machine != null)
                    {
                        states[kvp.Key] = new
                        {
                            DisplayName = kvp.Value,
                            CurrentState = machine.InitialState,
                            LastUpdate = _lastEventTimes.TryGetValue(kvp.Key, out var time) ? time : DateTime.MinValue
                        };
                    }
                }

                return states;
            }
        }

        private void SyncRemoteStates(ConcurrentDictionary<string, object> remoteStates)
        {
            lock (_lockObj)
            {
                // Update timeline with remote state information
                // This could be extended to handle more complex synchronization scenarios
                foreach (var kvp in remoteStates)
                {
                    // Process remote state updates
                    // Implementation depends on specific synchronization requirements
                }
            }
        }

        private double GetCurrentTimestamp()
        {
            // Return microseconds since start
            return _stopwatch.Elapsed.TotalMilliseconds * 1000;
        }

        private void AddSubscription(string key, IDisposable subscription)
        {
            lock (_lockObj)
            {
                // Dispose existing subscription if any
                if (_subscriptions.TryGetValue(key, out var existing))
                {
                    existing.Dispose();
                }
                _subscriptions[key] = subscription;
            }
        }

        private void UnsubscribeAll()
        {
            lock (_lockObj)
            {
                foreach (var subscription in _subscriptions.Values)
                {
                    subscription?.Dispose();
                }
                _subscriptions.Clear();

                foreach (var service in _notificationServices.Values)
                {
                    service?.Dispose();
                }
                _notificationServices.Clear();
            }
        }

        #endregion

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            UnsubscribeAll();
            _stopwatch.Stop();
        }
    }
}