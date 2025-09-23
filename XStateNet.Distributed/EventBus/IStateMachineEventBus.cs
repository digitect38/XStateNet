using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XStateNet.Distributed.EventBus
{
    /// <summary>
    /// Interface for distributed state machine event bus
    /// </summary>
    public interface IStateMachineEventBus
    {
        // Publishing
        /// <summary>
        /// Publish a state change event
        /// </summary>
        Task PublishStateChangeAsync(string machineId, StateChangeEvent evt);
        
        /// <summary>
        /// Send an event to a specific state machine
        /// </summary>
        Task PublishEventAsync(string targetMachineId, string eventName, object? payload = null);
        
        /// <summary>
        /// Broadcast an event to all state machines
        /// </summary>
        Task BroadcastAsync(string eventName, object? payload = null, string? filter = null);
        
        /// <summary>
        /// Publish to a group of state machines
        /// </summary>
        Task PublishToGroupAsync(string groupName, string eventName, object? payload = null);
        
        // Subscribing
        /// <summary>
        /// Subscribe to events for a specific state machine
        /// </summary>
        Task<IDisposable> SubscribeToMachineAsync(string machineId, Action<StateMachineEvent> handler);
        
        /// <summary>
        /// Subscribe to state changes for a specific machine
        /// </summary>
        Task<IDisposable> SubscribeToStateChangesAsync(string machineId, Action<StateChangeEvent> handler);
        
        /// <summary>
        /// Subscribe to events matching a pattern
        /// </summary>
        Task<IDisposable> SubscribeToPatternAsync(string pattern, Action<StateMachineEvent> handler);
        
        /// <summary>
        /// Subscribe to all events
        /// </summary>
        Task<IDisposable> SubscribeToAllAsync(Action<StateMachineEvent> handler);
        
        /// <summary>
        /// Subscribe to a group
        /// </summary>
        Task<IDisposable> SubscribeToGroupAsync(string groupName, Action<StateMachineEvent> handler);
        
        // Request/Response pattern
        /// <summary>
        /// Send a request and wait for response
        /// </summary>
        Task<TResponse?> RequestAsync<TResponse>(string targetMachineId, string requestType, object? payload = null, TimeSpan? timeout = null);
        
        /// <summary>
        /// Register a request handler
        /// </summary>
        Task RegisterRequestHandlerAsync<TRequest, TResponse>(string requestType, Func<TRequest, Task<TResponse>> handler);
        
        // Connection management
        Task ConnectAsync();
        Task DisconnectAsync();
        bool IsConnected { get; }
        
        // Events
        event EventHandler<EventBusConnectedEventArgs>? Connected;
        event EventHandler<EventBusDisconnectedEventArgs>? Disconnected;
        event EventHandler<EventBusErrorEventArgs>? ErrorOccurred;
    }
    
    /// <summary>
    /// Base class for all state machine events
    /// </summary>
    public class StateMachineEvent
    {
        public string EventId { get; set; } = Guid.NewGuid().ToString();
        public string EventName { get; set; } = string.Empty;
        public string SourceMachineId { get; set; } = string.Empty;
        public string? TargetMachineId { get; set; }
        public object? Payload { get; set; }
        public ConcurrentDictionary<string, string> Headers { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int Version { get; set; } = 1;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
    }
    
    /// <summary>
    /// State change event
    /// </summary>
    public class StateChangeEvent : StateMachineEvent
    {
        public string? OldState { get; set; }
        public string NewState { get; set; } = string.Empty;
        public string? Transition { get; set; }
        public ConcurrentDictionary<string, object>? Context { get; set; }
        public TimeSpan? Duration { get; set; }
        
        public StateChangeEvent()
        {
            EventName = "StateChange";
        }
    }
    
    /// <summary>
    /// Event bus subscription
    /// </summary>
    public interface IEventBusSubscription : IDisposable
    {
        string SubscriptionId { get; }
        bool IsActive { get; }
        Task PauseAsync();
        Task ResumeAsync();
    }
    
    // Event args
    public class EventBusConnectedEventArgs : EventArgs
    {
        public string Endpoint { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    }
    
    public class EventBusDisconnectedEventArgs : EventArgs
    {
        public string Reason { get; set; } = string.Empty;
        public bool WillReconnect { get; set; }
        public TimeSpan? ReconnectDelay { get; set; }
    }
    
    public class EventBusErrorEventArgs : EventArgs
    {
        public Exception Exception { get; set; } = new();
        public string Context { get; set; } = string.Empty;
        public bool IsFatal { get; set; }
    }
}