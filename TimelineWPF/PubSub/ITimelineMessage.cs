namespace TimelineWPF.PubSub
{
    /// <summary>
    /// Base interface for all timeline messages
    /// </summary>
    public interface ITimelineMessage
    {
        /// <summary>
        /// Unique identifier for the message
        /// </summary>
        Guid MessageId { get; }

        /// <summary>
        /// Timestamp when the message was created (microseconds)
        /// </summary>
        double Timestamp { get; }

        /// <summary>
        /// Name of the state machine that published this message
        /// </summary>
        string MachineName { get; }

        /// <summary>
        /// Type of timeline message
        /// </summary>
        TimelineMessageType MessageType { get; }
    }

    /// <summary>
    /// Types of timeline messages
    /// </summary>
    public enum TimelineMessageType
    {
        StateTransition,
        Event,
        Action,
        MachineRegistered,
        MachineUnregistered,
        Error
    }
}