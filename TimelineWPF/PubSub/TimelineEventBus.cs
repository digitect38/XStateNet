using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TimelineWPF.PubSub
{
    /// <summary>
    /// Thread-safe event bus implementation for Timeline pub/sub messaging
    /// </summary>
    public class TimelineEventBus : ITimelineEventBus
    {
        private readonly ConcurrentDictionary<ITimelineSubscriber, SubscriptionInfo> _subscribers;
        private readonly ReaderWriterLockSlim _lock;
        private readonly bool _enableAsyncPublishing;

        public int SubscriberCount => _subscribers.Count;

        public TimelineEventBus(bool enableAsyncPublishing = false)
        {
            _subscribers = new ConcurrentDictionary<ITimelineSubscriber, SubscriptionInfo>();
            _lock = new ReaderWriterLockSlim();
            _enableAsyncPublishing = enableAsyncPublishing;
        }

        public void Subscribe(ITimelineSubscriber subscriber)
        {
            _subscribers.TryAdd(subscriber, new SubscriptionInfo
            {
                SubscribeToAll = true,
                MessageTypes = new HashSet<TimelineMessageType>(),
                MachineNames = new HashSet<string>()
            });
        }

        public void Subscribe(ITimelineSubscriber subscriber, params TimelineMessageType[] messageTypes)
        {
            _subscribers.AddOrUpdate(subscriber,
                new SubscriptionInfo
                {
                    SubscribeToAll = false,
                    MessageTypes = new HashSet<TimelineMessageType>(messageTypes),
                    MachineNames = new HashSet<string>()
                },
                (key, existing) =>
                {
                    foreach (var type in messageTypes)
                        existing.MessageTypes.Add(type);
                    return existing;
                });
        }

        public void SubscribeToMachine(ITimelineSubscriber subscriber, string machineName)
        {
            _subscribers.AddOrUpdate(subscriber,
                new SubscriptionInfo
                {
                    SubscribeToAll = false,
                    MessageTypes = new HashSet<TimelineMessageType>(),
                    MachineNames = new HashSet<string> { machineName }
                },
                (key, existing) =>
                {
                    existing.MachineNames.Add(machineName);
                    return existing;
                });
        }

        public void Unsubscribe(ITimelineSubscriber subscriber)
        {
            _subscribers.TryRemove(subscriber, out _);
        }

        public void Publish(ITimelineMessage message)
        {
            if (message == null) return;

            var relevantSubscribers = GetRelevantSubscribers(message);

            if (_enableAsyncPublishing)
            {
                Task.Run(() => NotifySubscribers(relevantSubscribers, message));
            }
            else
            {
                NotifySubscribers(relevantSubscribers, message);
            }
        }

        public void PublishBatch(IEnumerable<ITimelineMessage> messages)
        {
            if (messages == null || !messages.Any()) return;

            var messageList = messages.ToList();
            var subscribersToNotify = new HashSet<ITimelineSubscriber>();

            foreach (var message in messageList)
            {
                foreach (var subscriber in GetRelevantSubscribers(message))
                {
                    subscribersToNotify.Add(subscriber);
                }
            }

            if (_enableAsyncPublishing)
            {
                Task.Run(() => NotifySubscribersBatch(subscribersToNotify, messageList));
            }
            else
            {
                NotifySubscribersBatch(subscribersToNotify, messageList);
            }
        }

        public void ClearSubscriptions()
        {
            _subscribers.Clear();
        }

        private IEnumerable<ITimelineSubscriber> GetRelevantSubscribers(ITimelineMessage message)
        {
            _lock.EnterReadLock();
            try
            {
                return _subscribers
                    .Where(kvp => ShouldNotify(kvp.Value, message))
                    .Select(kvp => kvp.Key)
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private bool ShouldNotify(SubscriptionInfo info, ITimelineMessage message)
        {
            if (info.SubscribeToAll)
                return true;

            if (info.MessageTypes.Contains(message.MessageType))
                return true;

            if (info.MachineNames.Contains(message.MachineName))
                return true;

            return false;
        }

        private void NotifySubscribers(IEnumerable<ITimelineSubscriber> subscribers, ITimelineMessage message)
        {
            foreach (var subscriber in subscribers)
            {
                try
                {
                    subscriber.OnTimelineMessage(message);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error notifying subscriber: {ex.Message}");
                }
            }
        }

        private void NotifySubscribersBatch(IEnumerable<ITimelineSubscriber> subscribers, List<ITimelineMessage> messages)
        {
            foreach (var subscriber in subscribers)
            {
                try
                {
                    subscriber.OnTimelineMessageBatch(messages);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error notifying subscriber batch: {ex.Message}");
                }
            }
        }

        private class SubscriptionInfo
        {
            public bool SubscribeToAll { get; set; }
            public HashSet<TimelineMessageType> MessageTypes { get; set; } = new HashSet<TimelineMessageType>();
            public HashSet<string> MachineNames { get; set; } = new HashSet<string>();
        }

        public void Dispose()
        {
            _lock?.Dispose();
        }
    }
}