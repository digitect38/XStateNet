using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;

namespace XStateNet.Distributed.Core
{
    /// <summary>
    /// Core abstraction for state machine communication transport
    /// </summary>
    public interface IStateMachineTransport : IDisposable
    {
        /// <summary>
        /// Unique identifier for this transport instance
        /// </summary>
        string TransportId { get; }

        /// <summary>
        /// Type of transport (InMemory, ZeroMQ, RabbitMQ, etc.)
        /// </summary>
        TransportType Type { get; }

        /// <summary>
        /// Connection status
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Connect to the transport
        /// </summary>
        Task ConnectAsync(string address, CancellationToken cancellationToken = default);

        /// <summary>
        /// Disconnect from the transport
        /// </summary>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Send a message to a specific target
        /// </summary>
        Task<bool> SendAsync(StateMachineMessage message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Receive a single message (blocking with timeout)
        /// </summary>
        Task<StateMachineMessage?> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

        /// <summary>
        /// Subscribe to messages matching a pattern
        /// </summary>
        IAsyncEnumerable<StateMachineMessage> SubscribeAsync(string pattern, CancellationToken cancellationToken = default);

        /// <summary>
        /// Request-Response pattern
        /// </summary>
        Task<TResponse?> RequestAsync<TRequest, TResponse>(
            string target,
            TRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class;

        /// <summary>
        /// Discover other state machines
        /// </summary>
        Task<IEnumerable<StateMachineEndpoint>> DiscoverAsync(
            string query = "*",
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Register this state machine for discovery
        /// </summary>
        Task RegisterAsync(StateMachineEndpoint endpoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// Transport health check
        /// </summary>
        Task<TransportHealth> GetHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Event raised when connection status changes
        /// </summary>
        event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        /// <summary>
        /// Event raised when a message is received
        /// </summary>
        event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    }

    /// <summary>
    /// Transport types
    /// </summary>
    public enum TransportType
    {
        InMemory,      // Same process, different threads
        ZeroMQ,        // Brokerless, inter-process/computer
        RabbitMQ,      // Broker-based, inter-computer
        Hybrid         // Auto-select based on location
    }

    /// <summary>
    /// Message format for state machine communication
    /// </summary>
    [MessagePackObject]
    public class StateMachineMessage
    {
        [Key(0)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Key(1)]
        public string From { get; set; } = string.Empty;
        
        [Key(2)]
        public string To { get; set; } = string.Empty;
        
        [Key(3)]
        public string EventName { get; set; } = string.Empty;
        
        [Key(4)]
        public byte[]? Payload { get; set; }
        
        [Key(5)]
        public string? PayloadType { get; set; }
        
        [Key(6)]
        public Dictionary<string, string> Headers { get; set; } = new();
        
        [Key(7)]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        [Key(8)]
        public int Priority { get; set; } = 0;
        
        [Key(9)]
        public string? CorrelationId { get; set; }
        
        [Key(10)]
        public string? ReplyTo { get; set; }
        
        [Key(11)]
        public TimeSpan? Expiry { get; set; }
    }

    /// <summary>
    /// Represents a state machine endpoint
    /// </summary>
    [MessagePackObject]
    public class StateMachineEndpoint
    {
        [Key(0)]
        public string Id { get; set; } = string.Empty;
        
        [Key(1)]
        public string Address { get; set; } = string.Empty;
        
        [Key(2)]
        public MachineLocation Location { get; set; }
        
        [Key(3)]
        public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
        
        [Key(4)]
        public Dictionary<string, string> Metadata { get; set; } = new();
        
        [Key(5)]
        public string[]? SupportedEvents { get; set; }
        
        [Key(6)]
        public string? Version { get; set; }
    }

    /// <summary>
    /// Location of a state machine
    /// </summary>
    public enum MachineLocation
    {
        SameThread,    // Same thread context
        SameProcess,   // Different thread, same process
        SameMachine,   // Different process, same machine
        Remote         // Different machine
    }

    /// <summary>
    /// Transport health status
    /// </summary>
    public class TransportHealth
    {
        public bool IsHealthy { get; set; }
        public TimeSpan Latency { get; set; }
        public long MessagesSent { get; set; }
        public long MessagesReceived { get; set; }
        public long ErrorCount { get; set; }
        public DateTime LastActivity { get; set; }
        public Dictionary<string, object> Diagnostics { get; set; } = new();
    }

    /// <summary>
    /// Connection status changed event args
    /// </summary>
    public class ConnectionStatusChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; set; }
        public string? Reason { get; set; }
        public Exception? Exception { get; set; }
    }

    /// <summary>
    /// Message received event args
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        public StateMachineMessage Message { get; set; } = null!;
        public bool Handled { get; set; }
    }
}