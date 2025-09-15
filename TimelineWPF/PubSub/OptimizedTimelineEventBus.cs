using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XStateNet.Distributed.Core;
using XStateNet.Distributed.EventBus.Optimized;

namespace TimelineWPF.PubSub
{
    /// <summary>
    /// Adapter that connects Timeline's ITimelineEventBus interface to the optimized XStateNet.Distributed event bus.
    /// This is a thin wrapper that translates between Timeline messages and the distributed event bus.
    /// </summary>
    public class OptimizedTimelineEventBus : ITimelineEventBus, IDisposable
    {
        private readonly OptimizedInMemoryEventBus _eventBus;
        private readonly Dictionary<ITimelineSubscriber, List<IDisposable>> _subscriptions;
        private readonly ILogger<OptimizedTimelineEventBus>? _logger;

        public int SubscriberCount => _subscriptions.Count;

        public OptimizedTimelineEventBus(ILogger<OptimizedTimelineEventBus>? logger = null)
        {
            _logger = logger;
            // Use the optimized event bus with default worker count (Environment.ProcessorCount)
            _eventBus = new OptimizedInMemoryEventBus(logger: null);
            _subscriptions = new Dictionary<ITimelineSubscriber, List<IDisposable>>();

            // Connect the event bus
            _ = _eventBus.ConnectAsync();
        }

        public void Subscribe(ITimelineSubscriber subscriber)
        {
            if (!_subscriptions.ContainsKey(subscriber))
            {
                _subscriptions[subscriber] = new List<IDisposable>();
            }

            // Subscribe to all events and forward to Timeline subscriber
            var subscription = _eventBus.SubscribeToAllAsync(evt =>
            {
                // The payload should be the Timeline message
                if (evt.Payload is ITimelineMessage message)
                {
                    subscriber.OnTimelineMessage(message);
                }
            }).GetAwaiter().GetResult();

            _subscriptions[subscriber].Add(subscription);
        }

        public void Subscribe(ITimelineSubscriber subscriber, params TimelineMessageType[] messageTypes)
        {
            if (!_subscriptions.ContainsKey(subscriber))
            {
                _subscriptions[subscriber] = new List<IDisposable>();
            }

            // Subscribe to all events and filter by message type
            var subscription = _eventBus.SubscribeToAllAsync(evt =>
            {
                if (evt.Payload is ITimelineMessage message)
                {
                    if (messageTypes.Contains(message.MessageType))
                    {
                        subscriber.OnTimelineMessage(message);
                    }
                }
            }).GetAwaiter().GetResult();

            _subscriptions[subscriber].Add(subscription);
        }

        public void SubscribeToMachine(ITimelineSubscriber subscriber, string machineName)
        {
            if (!_subscriptions.ContainsKey(subscriber))
            {
                _subscriptions[subscriber] = new List<IDisposable>();
            }

            // Subscribe to events from specific machine
            var subscription = _eventBus.SubscribeToMachineAsync(machineName, evt =>
            {
                if (evt.Payload is ITimelineMessage message)
                {
                    subscriber.OnTimelineMessage(message);
                }
            }).GetAwaiter().GetResult();

            _subscriptions[subscriber].Add(subscription);
        }

        public void Unsubscribe(ITimelineSubscriber subscriber)
        {
            if (_subscriptions.TryGetValue(subscriber, out var subs))
            {
                foreach (var sub in subs)
                {
                    sub.Dispose();
                }
                _subscriptions.Remove(subscriber);
            }
        }

        public void Publish(ITimelineMessage message)
        {
            // Fire and forget - publish asynchronously
            Task.Run(async () =>
            {
                try
                {
                    // Use the message type as event name and the message itself as payload
                    await _eventBus.PublishEventAsync(
                        message.MachineName,
                        message.MessageType.ToString(),
                        message);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error publishing timeline message");
                }
            });
        }

        public void PublishBatch(IEnumerable<ITimelineMessage> messages)
        {
            // Publish all messages asynchronously
            Task.Run(async () =>
            {
                var tasks = messages.Select(msg =>
                    _eventBus.PublishEventAsync(
                        msg.MachineName,
                        msg.MessageType.ToString(),
                        msg)
                ).ToList();

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error publishing batch of timeline messages");
                }
            });
        }

        public void ClearSubscriptions()
        {
            foreach (var subscriber in _subscriptions.Keys.ToList())
            {
                Unsubscribe(subscriber);
            }
        }

        public void Dispose()
        {
            ClearSubscriptions();
            _eventBus?.Dispose();
        }
    }
}