using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using XStateNet.Distributed.EventBus;

namespace XStateNet.Distributed.PubSub
{
    /// <summary>
    /// Comprehensive event notification service with pub/sub architecture
    /// Supports state changes, actions, guards, transitions, and custom events
    /// </summary>
    public class EventNotificationService : IDisposable
    {
        private readonly IStateMachine _stateMachine;
        private readonly IStateMachineEventBus _eventBus;
        private readonly ILogger<EventNotificationService>? _logger;
        private readonly string _machineId;
        private readonly ConcurrentDictionary<string, List<Subscription>> _subscriptions = new();
        private readonly ConcurrentDictionary<string, object> _eventAggregators = new();
        private readonly SemaphoreSlim _publishSemaphore = new(1, 1);
        private bool _isStarted;
        private bool _disposed;
        private string? _previousState;

        // Event types
        public const string StateChangeEvent = "StateChange";
        public const string ActionExecutedEvent = "ActionExecuted";
        public const string GuardEvaluatedEvent = "GuardEvaluated";
        public const string TransitionEvent = "Transition";
        public const string ErrorEvent = "Error";
        public const string CustomEvent = "Custom";

        public EventNotificationService(
            IStateMachine stateMachine,
            IStateMachineEventBus eventBus,
            string? machineId = null,
            ILogger<EventNotificationService>? logger = null)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _machineId = machineId ?? stateMachine.machineId ?? Guid.NewGuid().ToString();
            _logger = logger;
        }

        /// <summary>
        /// Start monitoring and publishing state machine events
        /// </summary>
        public async Task StartAsync()
        {
            if (_isStarted) return;

            try
            {
                // Connect event bus
                await _eventBus.ConnectAsync();

                // Wire up state machine events
                WireUpStateMachineEvents();

                _isStarted = true;
                _logger?.LogInformation("EventNotificationService started for machine {MachineId}", _machineId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to start EventNotificationService");
                throw;
            }
        }

        /// <summary>
        /// Stop monitoring and publishing
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isStarted) return;

            try
            {
                UnwireStateMachineEvents();
                await _eventBus.DisconnectAsync();
                _isStarted = false;
                _logger?.LogInformation("EventNotificationService stopped for machine {MachineId}", _machineId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error stopping EventNotificationService");
            }
        }

        #region Publishing Methods

        /// <summary>
        /// Publish a state change event
        /// </summary>
        public async Task PublishStateChangeAsync(string? oldState, string newState, string? transition, ConcurrentDictionary<string, object>? context = null)
        {
            var evt = new StateChangeEvent
            {
                SourceMachineId = _machineId,
                OldState = oldState,
                NewState = newState,
                Transition = transition,
                Context = context,
                Timestamp = DateTime.UtcNow
            };

            await PublishEventAsync(StateChangeEvent, evt);
        }

        /// <summary>
        /// Publish an action executed event
        /// </summary>
        public async Task PublishActionExecutedAsync(string actionName, string? stateName, object? result = null)
        {
            var evt = new ActionExecutedNotification
            {
                SourceMachineId = _machineId,
                ActionName = actionName,
                StateName = stateName,
                Result = result,
                Timestamp = DateTime.UtcNow
            };

            await PublishEventAsync(ActionExecutedEvent, evt);
        }

        /// <summary>
        /// Publish a guard evaluated event
        /// </summary>
        public async Task PublishGuardEvaluatedAsync(string guardName, bool passed, string? stateName = null)
        {
            var evt = new GuardEvaluatedNotification
            {
                SourceMachineId = _machineId,
                GuardName = guardName,
                Passed = passed,
                StateName = stateName,
                Timestamp = DateTime.UtcNow
            };

            await PublishEventAsync(GuardEvaluatedEvent, evt);
        }

        /// <summary>
        /// Publish a transition event
        /// </summary>
        public async Task PublishTransitionAsync(string from, string to, string trigger)
        {
            var evt = new TransitionNotification
            {
                SourceMachineId = _machineId,
                FromState = from,
                ToState = to,
                Trigger = trigger,
                Timestamp = DateTime.UtcNow
            };

            await PublishEventAsync(TransitionEvent, evt);
        }

        /// <summary>
        /// Publish an error event
        /// </summary>
        public async Task PublishErrorAsync(Exception exception, string? context = null)
        {
            var evt = new ErrorNotification
            {
                SourceMachineId = _machineId,
                ErrorMessage = exception.Message,
                ErrorType = exception.GetType().Name,
                StackTrace = exception.StackTrace,
                Context = context,
                Timestamp = DateTime.UtcNow
            };

            await PublishEventAsync(ErrorEvent, evt);
        }

        /// <summary>
        /// Publish a custom event
        /// </summary>
        public async Task PublishCustomEventAsync(string eventName, object? payload = null, ConcurrentDictionary<string, string>? headers = null)
        {
            var evt = new StateMachineEvent
            {
                EventName = eventName,
                SourceMachineId = _machineId,
                Payload = payload,
                Headers = headers ?? new ConcurrentDictionary<string, string>(),
                Timestamp = DateTime.UtcNow
            };

            await PublishEventAsync(CustomEvent, evt);
        }

        /// <summary>
        /// Broadcast an event to all machines
        /// </summary>
        public async Task BroadcastAsync(string eventName, object? payload = null)
        {
            await _eventBus.BroadcastAsync(eventName, payload);
        }

        /// <summary>
        /// Send event to specific machine
        /// </summary>
        public async Task SendToMachineAsync(string targetMachineId, string eventName, object? payload = null)
        {
            await _eventBus.PublishEventAsync(targetMachineId, eventName, payload);
        }

        /// <summary>
        /// Publish to a group
        /// </summary>
        public async Task PublishToGroupAsync(string groupName, string eventName, object? payload = null)
        {
            await _eventBus.PublishToGroupAsync(groupName, eventName, payload);
        }

        #endregion

        #region Subscription Methods

        /// <summary>
        /// Subscribe to all events from this machine
        /// </summary>
        public async Task<IDisposable> SubscribeToAllAsync(Action<StateMachineEvent> handler)
        {
            return await _eventBus.SubscribeToMachineAsync(_machineId, handler);
        }

        /// <summary>
        /// Subscribe to state changes
        /// </summary>
        public async Task<IDisposable> SubscribeToStateChangesAsync(Action<StateChangeEvent> handler)
        {
            return await _eventBus.SubscribeToStateChangesAsync(_machineId, handler);
        }

        /// <summary>
        /// Subscribe to specific event type
        /// </summary>
        public async Task<IDisposable> SubscribeToEventTypeAsync(string eventType, Action<StateMachineEvent> handler)
        {
            var pattern = $"{_machineId}.{eventType}.*";
            return await _eventBus.SubscribeToPatternAsync(pattern, handler);
        }

        /// <summary>
        /// Subscribe to events from another machine
        /// </summary>
        public async Task<IDisposable> SubscribeToMachineAsync(string targetMachineId, Action<StateMachineEvent> handler)
        {
            return await _eventBus.SubscribeToMachineAsync(targetMachineId, handler);
        }

        /// <summary>
        /// Subscribe to a group
        /// </summary>
        public async Task<IDisposable> SubscribeToGroupAsync(string groupName, Action<StateMachineEvent> handler)
        {
            return await _eventBus.SubscribeToGroupAsync(groupName, handler);
        }

        /// <summary>
        /// Subscribe with filter
        /// </summary>
        public IDisposable SubscribeWithFilter(Func<StateMachineEvent, bool> filter, Action<StateMachineEvent> handler)
        {
            var subscription = new FilteredSubscription(filter, handler);
            AddLocalSubscription("*", subscription);
            return subscription;
        }

        #endregion

        #region Event Aggregation

        /// <summary>
        /// Create an event aggregator for batch processing
        /// </summary>
        public EventAggregator<T> CreateAggregator<T>(
            TimeSpan window,
            int maxBatchSize,
            Action<List<T>> batchHandler) where T : StateMachineEvent
        {
            var aggregator = new EventAggregator<T>(window, maxBatchSize, batchHandler);
            _eventAggregators[$"{typeof(T).Name}_{Guid.NewGuid()}"] = aggregator;
            return aggregator;
        }

        #endregion

        #region Request/Response Pattern

        /// <summary>
        /// Send request and wait for response
        /// </summary>
        public async Task<TResponse?> RequestAsync<TResponse>(
            string targetMachineId,
            string requestType,
            object? payload = null,
            TimeSpan? timeout = null)
        {
            return await _eventBus.RequestAsync<TResponse>(targetMachineId, requestType, payload, timeout);
        }

        /// <summary>
        /// Register request handler
        /// </summary>
        public async Task RegisterRequestHandlerAsync<TRequest, TResponse>(
            string requestType,
            Func<TRequest, Task<TResponse>> handler)
        {
            await _eventBus.RegisterRequestHandlerAsync(requestType, handler);
        }

        #endregion

        #region Private Methods

        private void WireUpStateMachineEvents()
        {
            // Wire up the StateChanged event from IStateMachine
            if (_stateMachine != null)
            {
                // Initialize previous state with current state (may be empty if not started)
                var currentState = _stateMachine.GetActiveStateNames();
                _previousState = !string.IsNullOrEmpty(currentState) ? currentState : null;

                _stateMachine.StateChanged += OnStateMachineStateChanged;
                _stateMachine.ErrorOccurred += OnStateMachineError;
            }
        }

        private void UnwireStateMachineEvents()
        {
            // Clean up event handlers
            if (_stateMachine != null)
            {
                _stateMachine.StateChanged -= OnStateMachineStateChanged;
                _stateMachine.ErrorOccurred -= OnStateMachineError;
            }
        }

        private void OnStateMachineStateChanged(string newState)
        {
            // Use synchronous publish to ensure deterministic behavior
            try
            {
                // Create the event synchronously
                var stateChangeEvent = new StateChangeEvent
                {
                    NewState = newState,
                    OldState = _previousState,
                    Timestamp = DateTime.UtcNow,
                    SourceMachineId = _machineId
                };

                // Update previous state before publishing to avoid race conditions
                var oldState = _previousState;
                _previousState = newState;

                // Fire async publish but don't wait (fire-and-forget with proper error handling)
                _eventBus.PublishStateChangeAsync(_machineId, stateChangeEvent).ContinueWith(task =>
                {
                    if (task.Exception != null)
                    {
                        _logger?.LogError(task.Exception, "Failed to publish state change event");
                    }
                    else
                    {
                        _logger?.LogDebug("Published state change from {OldState} to {NewState} for machine {MachineId}",
                            oldState, newState, _machineId);
                    }
                }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create state change event");
            }
        }


        private void OnStateMachineError(Exception error)
        {
            // Fire and forget async operation
            _ = Task.Run(async () =>
            {
                try
                {
                    await PublishErrorAsync(error);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to publish error event");
                }
            });
        }

        private void OnStateMachineTransition(CompoundState? source, StateNode? target, string eventName)
        {
            if (source != null && target != null)
            {
                // Fire and forget async operation
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await PublishTransitionAsync(source.Name ?? "", target.Name ?? "", eventName ?? "");

                        // Also publish state change
                        // Convert ContextMap to ConcurrentDictionary<string, object>
                        ConcurrentDictionary<string, object>? context = null;
                        if (_stateMachine.ContextMap != null)
                        {
                            context = new ConcurrentDictionary<string, object>();
                            foreach (var kvp in _stateMachine.ContextMap)
                            {
                                context[kvp.Key] = kvp.Value;
                            }
                        }

                        await PublishStateChangeAsync(
                            source.Name,
                            target.Name ?? "",
                            eventName,
                            context);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error publishing transition event");
                    }
                });
            }
        }

        private void OnStateMachineActionExecuted(string actionName, string? stateName)
        {
            // Fire and forget async operation
            _ = Task.Run(async () =>
            {
                try
                {
                    await PublishActionExecutedAsync(actionName ?? "", stateName);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error publishing action executed event");
                }
            });
        }

        private void OnStateMachineGuardEvaluated(string guardName, bool passed)
        {
            // Fire and forget async operation
            _ = Task.Run(async () =>
            {
                try
                {
                    await PublishGuardEvaluatedAsync(guardName ?? "", passed);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error publishing guard evaluated event");
                }
            });
        }

        private async Task PublishEventAsync(string eventType, object eventData)
        {
            await _publishSemaphore.WaitAsync();
            try
            {
                // Publish to event bus
                if (_eventBus.IsConnected)
                {
                    if (eventData is StateChangeEvent stateChange)
                    {
                        await _eventBus.PublishStateChangeAsync(_machineId, stateChange);
                    }
                    else
                    {
                        await _eventBus.PublishEventAsync(_machineId, eventType, eventData);
                    }
                }

                // Process local subscriptions
                ProcessLocalSubscriptions(eventType, eventData);
            }
            finally
            {
                _publishSemaphore.Release();
            }
        }

        private void ProcessLocalSubscriptions(string eventType, object eventData)
        {
            if (_subscriptions.TryGetValue(eventType, out var subs))
            {
                foreach (var subscription in subs.ToList())
                {
                    try
                    {
                        subscription.Handle(eventData);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error processing local subscription");
                    }
                }
            }

            // Process wildcard subscriptions
            if (_subscriptions.TryGetValue("*", out var wildcardSubs))
            {
                foreach (var subscription in wildcardSubs.ToList())
                {
                    try
                    {
                        subscription.Handle(eventData);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error processing wildcard subscription");
                    }
                }
            }
        }

        private void AddLocalSubscription(string pattern, Subscription subscription)
        {
            _subscriptions.AddOrUpdate(pattern,
                new List<Subscription> { subscription },
                (_, list) =>
                {
                    list.Add(subscription);
                    return list;
                });
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            StopAsync().GetAwaiter().GetResult();
            _publishSemaphore?.Dispose();
            _disposed = true;
        }
    }

    #region Supporting Classes

    public abstract class Subscription : IDisposable
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public abstract void Handle(object eventData);
        public virtual void Dispose() { }
    }

    public class FilteredSubscription : Subscription
    {
        private readonly Func<StateMachineEvent, bool> _filter;
        private readonly Action<StateMachineEvent> _handler;

        public FilteredSubscription(Func<StateMachineEvent, bool> filter, Action<StateMachineEvent> handler)
        {
            _filter = filter;
            _handler = handler;
        }

        public override void Handle(object eventData)
        {
            if (eventData is StateMachineEvent evt && _filter(evt))
            {
                _handler(evt);
            }
        }
    }

    public class EventAggregator<T> : IDisposable where T : StateMachineEvent
    {
        private readonly List<T> _buffer = new();
        private readonly TimeSpan _window;
        private readonly int _maxBatchSize;
        private readonly Action<List<T>> _batchHandler;
        private readonly Timer _timer;
        private readonly object _lock = new();

        public EventAggregator(TimeSpan window, int maxBatchSize, Action<List<T>> batchHandler)
        {
            _window = window;
            _maxBatchSize = maxBatchSize;
            _batchHandler = batchHandler;
            _timer = new Timer(Flush, null, window, window);
        }

        public void Add(T item)
        {
            lock (_lock)
            {
                _buffer.Add(item);
                if (_buffer.Count >= _maxBatchSize)
                {
                    Flush(null);
                }
            }
        }

        private void Flush(object? state)
        {
            List<T> toProcess;
            lock (_lock)
            {
                if (_buffer.Count == 0) return;
                toProcess = new List<T>(_buffer);
                _buffer.Clear();
            }
            _batchHandler(toProcess);
        }

        public void Dispose()
        {
            _timer?.Dispose();
            Flush(null);
        }
    }

    #endregion

    #region Event Notification Classes

    public class ActionExecutedNotification : StateMachineEvent
    {
        public string ActionName { get; set; } = string.Empty;
        public string? StateName { get; set; }
        public object? Result { get; set; }
    }

    public class GuardEvaluatedNotification : StateMachineEvent
    {
        public string GuardName { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string? StateName { get; set; }
    }

    public class TransitionNotification : StateMachineEvent
    {
        public string FromState { get; set; } = string.Empty;
        public string ToState { get; set; } = string.Empty;
        public string Trigger { get; set; } = string.Empty;
    }

    public class ErrorNotification : StateMachineEvent
    {
        public string ErrorMessage { get; set; } = string.Empty;
        public string ErrorType { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
        public string? Context { get; set; }
    }

    #endregion
}