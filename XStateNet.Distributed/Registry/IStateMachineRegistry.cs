using System.Collections.Concurrent;

namespace XStateNet.Distributed.Registry
{
    /// <summary>
    /// Interface for distributed state machine registry
    /// </summary>
    public interface IStateMachineRegistry
    {
        /// <summary>
        /// Register a state machine in the distributed registry
        /// </summary>
        Task<bool> RegisterAsync(string machineId, StateMachineInfo info);

        /// <summary>
        /// Unregister a state machine from the distributed registry
        /// </summary>
        Task<bool> UnregisterAsync(string machineId);

        /// <summary>
        /// Get information about a specific state machine
        /// </summary>
        Task<StateMachineInfo?> GetAsync(string machineId);

        /// <summary>
        /// Get all registered state machines
        /// </summary>
        Task<IEnumerable<StateMachineInfo>> GetAllAsync();

        /// <summary>
        /// Get all active state machines (based on heartbeat)
        /// </summary>
        Task<IEnumerable<StateMachineInfo>> GetActiveAsync(TimeSpan heartbeatThreshold);

        /// <summary>
        /// Update heartbeat for a state machine
        /// </summary>
        Task UpdateHeartbeatAsync(string machineId);

        /// <summary>
        /// Update state machine status
        /// </summary>
        Task UpdateStatusAsync(string machineId, MachineStatus status, string? currentState = null);

        /// <summary>
        /// Find state machines by pattern
        /// </summary>
        Task<IEnumerable<StateMachineInfo>> FindByPatternAsync(string pattern);

        /// <summary>
        /// Subscribe to registry changes
        /// </summary>
        Task SubscribeToChangesAsync(Action<RegistryChangeEvent> handler);

        // Events
        event EventHandler<StateMachineRegisteredEventArgs>? MachineRegistered;
        event EventHandler<StateMachineUnregisteredEventArgs>? MachineUnregistered;
        event EventHandler<StateMachineStatusChangedEventArgs>? StatusChanged;
    }

    /// <summary>
    /// Information about a registered state machine
    /// </summary>
    public class StateMachineInfo
    {
        public string MachineId { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0.0";
        public ConcurrentDictionary<string, object> Metadata { get; set; } = new();
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
        public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
        public MachineStatus Status { get; set; } = MachineStatus.Stopped;
        public string? CurrentState { get; set; }
        public string? ParentMachineId { get; set; }
        public List<string> ChildMachineIds { get; set; } = new();
        public ConcurrentDictionary<string, string> Tags { get; set; } = new();
        public ResourceUsage? Resources { get; set; }
    }

    /// <summary>
    /// State machine status
    /// </summary>
    public enum MachineStatus
    {
        Stopped,
        Starting,
        Running,
        Paused,
        Stopping,
        Error,
        Unhealthy,
        Degraded
    }

    /// <summary>
    /// Resource usage information
    /// </summary>
    public class ResourceUsage
    {
        public double CpuPercent { get; set; }
        public long MemoryBytes { get; set; }
        public int ActiveConnections { get; set; }
        public long EventsProcessed { get; set; }
        public double EventsPerSecond { get; set; }
        public TimeSpan Uptime { get; set; }
    }

    /// <summary>
    /// Registry change event
    /// </summary>
    public class RegistryChangeEvent
    {
        public RegistryChangeType Type { get; set; }
        public string MachineId { get; set; } = string.Empty;
        public StateMachineInfo? MachineInfo { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public enum RegistryChangeType
    {
        Registered,
        Unregistered,
        StatusChanged,
        HeartbeatUpdated,
        MetadataUpdated
    }

    // Event args
    public class StateMachineRegisteredEventArgs : EventArgs
    {
        public string MachineId { get; set; } = string.Empty;
        public StateMachineInfo Info { get; set; } = new();
    }

    public class StateMachineUnregisteredEventArgs : EventArgs
    {
        public string MachineId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class StateMachineStatusChangedEventArgs : EventArgs
    {
        public string MachineId { get; set; } = string.Empty;
        public MachineStatus OldStatus { get; set; }
        public MachineStatus NewStatus { get; set; }
        public string? CurrentState { get; set; }
    }
}