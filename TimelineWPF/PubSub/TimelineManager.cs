using System;
using System.Collections.Generic;
using TimelineWPF.ViewModels;
using XStateNet;

namespace TimelineWPF.PubSub
{
    /// <summary>
    /// Manager class that coordinates Timeline pub/sub system
    /// </summary>
    public class TimelineManager : IDisposable
    {
        private readonly ITimelineEventBus _eventBus;
        private readonly StateMachinePublisher _publisher;
        private readonly List<ITimelineSubscriber> _subscribers;
        private static TimelineManager? _instance;
        private static readonly object _lock = new object();
        private static bool _useOptimizedEventBus = false;

        /// <summary>
        /// Configure whether to use the optimized event bus
        /// Must be called before accessing Instance
        /// </summary>
        public static void ConfigureOptimizedEventBus(bool useOptimized)
        {
            lock (_lock)
            {
                if (_instance != null)
                {
                    throw new InvalidOperationException("Cannot change event bus configuration after TimelineManager has been initialized");
                }
                _useOptimizedEventBus = useOptimized;
            }
        }

        /// <summary>
        /// Singleton instance of TimelineManager
        /// </summary>
        public static TimelineManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new TimelineManager(_useOptimizedEventBus);
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Gets the event bus for direct access
        /// </summary>
        public ITimelineEventBus EventBus => _eventBus;

        /// <summary>
        /// Gets the state machine publisher
        /// </summary>
        public StateMachinePublisher Publisher => _publisher;

        private TimelineManager() : this(false)
        {
        }

        private TimelineManager(bool useOptimizedEventBus)
        {
            _eventBus = useOptimizedEventBus
                ? new OptimizedTimelineEventBus()
                : new TimelineEventBus(enableAsyncPublishing: false);
            _publisher = new StateMachinePublisher(_eventBus);
            _subscribers = new List<ITimelineSubscriber>();
        }

        /// <summary>
        /// Register a state machine for timeline monitoring
        /// </summary>
        public void RegisterStateMachine(string name, StateMachine machine, List<string>? states = null)
        {
            _publisher.RegisterStateMachine(name, machine, states);
        }

        /// <summary>
        /// Unregister a state machine from timeline monitoring
        /// </summary>
        public void UnregisterStateMachine(string name)
        {
            _publisher.UnregisterStateMachine(name);
        }

        /// <summary>
        /// Subscribe a MainViewModel to timeline events
        /// </summary>
        public void SubscribeViewModel(MainViewModel viewModel)
        {
            if (!_subscribers.Contains(viewModel))
            {
                _subscribers.Add(viewModel);
                _eventBus.Subscribe(viewModel);
            }
        }

        /// <summary>
        /// Subscribe any ITimelineSubscriber to timeline events
        /// </summary>
        public void Subscribe(ITimelineSubscriber subscriber)
        {
            if (!_subscribers.Contains(subscriber))
            {
                _subscribers.Add(subscriber);
                _eventBus.Subscribe(subscriber);
            }
        }

        /// <summary>
        /// Unsubscribe from timeline events
        /// </summary>
        public void Unsubscribe(ITimelineSubscriber subscriber)
        {
            if (_subscribers.Contains(subscriber))
            {
                _subscribers.Remove(subscriber);
                _eventBus.Unsubscribe(subscriber);
            }
        }

        /// <summary>
        /// Publish a custom timeline message
        /// </summary>
        public void PublishMessage(ITimelineMessage message)
        {
            _eventBus.Publish(message);
        }

        /// <summary>
        /// Convenience method to publish a state transition
        /// </summary>
        public void PublishStateTransition(string machineName, string fromState, string toState, string? triggerEvent = null)
        {
            _publisher.PublishStateTransition(machineName, fromState, toState, triggerEvent);
        }

        /// <summary>
        /// Convenience method to publish an event
        /// </summary>
        public void PublishEvent(string machineName, string eventName, object? eventData = null)
        {
            _publisher.PublishEvent(machineName, eventName, eventData);
        }

        /// <summary>
        /// Convenience method to publish an action
        /// </summary>
        public void PublishAction(string machineName, string actionName, string? stateName = null)
        {
            _publisher.PublishAction(machineName, actionName, stateName);
        }

        /// <summary>
        /// Clear all registered state machines and subscribers
        /// </summary>
        public void Clear()
        {
            _publisher.Clear();
            _eventBus.ClearSubscriptions();
            _subscribers.Clear();
        }

        /// <summary>
        /// Reset the singleton instance
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _instance?.Dispose();
                _instance = null;
            }
        }

        public void Dispose()
        {
            Clear();
            if (_eventBus is IDisposable disposableEventBus)
            {
                disposableEventBus.Dispose();
            }
        }
    }
}