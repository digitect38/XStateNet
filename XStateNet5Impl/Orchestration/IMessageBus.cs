namespace XStateNet.Orchestration
{
    /// <summary>
    /// Unified message bus abstraction providing location transparency
    /// Supports InProc, InterProc, and InterNode communication patterns
    /// </summary>
    public interface IMessageBus : IDisposable
    {
        /// <summary>
        /// Send an event to a target machine
        /// </summary>
        Task SendEventAsync(string sourceMachineId, string targetMachineId, string eventName, object? payload = null);

        /// <summary>
        /// Subscribe to events for a specific machine
        /// </summary>
        Task<IDisposable> SubscribeAsync(string machineId, Func<MachineEvent, Task> handler);

        /// <summary>
        /// Connect to the message bus (for remote transports)
        /// </summary>
        Task ConnectAsync();

        /// <summary>
        /// Disconnect from the message bus
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Publish an event to all subscribers (broadcast)
        /// </summary>
        Task PublishAsync(string eventName, object? payload = null);
    }

    /// <summary>
    /// Message event data
    /// </summary>
    public class MachineEvent
    {
        public string SourceMachineId { get; set; } = string.Empty;
        public string TargetMachineId { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public object? Payload { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Transport types for location transparency
    /// </summary>
    public enum TransportType
    {
        /// <summary>In-process orchestrator (fastest, local only)</summary>
        InProcess,

        /// <summary>Inter-process using named pipes or IPC</summary>
        InterProcess,

        /// <summary>Inter-node using TCP/RabbitMQ/Redis (distributed)</summary>
        InterNode
    }
}
