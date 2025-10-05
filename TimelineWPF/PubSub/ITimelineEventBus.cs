namespace TimelineWPF.PubSub
{
    /// <summary>
    /// Event bus interface for Timeline pub/sub messaging
    /// </summary>
    public interface ITimelineEventBus
    {
        /// <summary>
        /// Subscribe to all timeline messages
        /// </summary>
        void Subscribe(ITimelineSubscriber subscriber);

        /// <summary>
        /// Subscribe to specific message types
        /// </summary>
        void Subscribe(ITimelineSubscriber subscriber, params TimelineMessageType[] messageTypes);

        /// <summary>
        /// Subscribe to messages from specific state machines
        /// </summary>
        void SubscribeToMachine(ITimelineSubscriber subscriber, string machineName);

        /// <summary>
        /// Unsubscribe from all messages
        /// </summary>
        void Unsubscribe(ITimelineSubscriber subscriber);

        /// <summary>
        /// Publish a timeline message to all subscribers
        /// </summary>
        void Publish(ITimelineMessage message);

        /// <summary>
        /// Publish multiple messages as a batch
        /// </summary>
        void PublishBatch(IEnumerable<ITimelineMessage> messages);

        /// <summary>
        /// Clear all subscriptions
        /// </summary>
        void ClearSubscriptions();

        /// <summary>
        /// Get current subscriber count
        /// </summary>
        int SubscriberCount { get; }
    }

    /// <summary>
    /// Interface for timeline message subscribers
    /// </summary>
    public interface ITimelineSubscriber
    {
        /// <summary>
        /// Handle received timeline message
        /// </summary>
        void OnTimelineMessage(ITimelineMessage message);

        /// <summary>
        /// Handle batch of timeline messages
        /// </summary>
        void OnTimelineMessageBatch(IEnumerable<ITimelineMessage> messages);
    }
}