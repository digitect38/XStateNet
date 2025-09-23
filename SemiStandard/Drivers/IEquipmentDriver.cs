using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XStateNet.Semi.Secs;

namespace XStateNet.Semi.Drivers
{
    /// <summary>
    /// Interface for equipment-specific driver implementations
    /// </summary>
    public interface IEquipmentDriver
    {
        /// <summary>
        /// Equipment model information
        /// </summary>
        string ModelName { get; }
        string SoftwareRevision { get; }
        string Manufacturer { get; }
        
        /// <summary>
        /// Connection state
        /// </summary>
        bool IsConnected { get; }
        EquipmentState State { get; }
        
        /// <summary>
        /// Initialize the equipment driver
        /// </summary>
        Task InitializeAsync(EquipmentConfiguration config, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Connect to the equipment
        /// </summary>
        Task ConnectAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Disconnect from the equipment
        /// </summary>
        Task DisconnectAsync();
        
        /// <summary>
        /// Execute a remote command
        /// </summary>
        Task<CommandResult> ExecuteCommandAsync(string command, ConcurrentDictionary<string, object> parameters, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Read equipment constant value
        /// </summary>
        Task<T> ReadConstantAsync<T>(uint ecid, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Write equipment constant value
        /// </summary>
        Task<bool> WriteConstantAsync(uint ecid, object value, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Read status variable value
        /// </summary>
        Task<T> ReadVariableAsync<T>(uint svid, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Load a process program/recipe
        /// </summary>
        Task<bool> LoadRecipeAsync(string ppid, byte[] ppbody, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Delete a process program/recipe
        /// </summary>
        Task<bool> DeleteRecipeAsync(string ppid, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Start process execution
        /// </summary>
        Task<bool> StartProcessAsync(string ppid, ConcurrentDictionary<string, object>? parameters = null, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Stop process execution
        /// </summary>
        Task<bool> StopProcessAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Pause process execution
        /// </summary>
        Task<bool> PauseProcessAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Resume process execution
        /// </summary>
        Task<bool> ResumeProcessAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Report an alarm
        /// </summary>
        Task ReportAlarmAsync(uint alarmId, AlarmState state, string? text = null);
        
        /// <summary>
        /// Report an event
        /// </summary>
        Task ReportEventAsync(uint eventId, ConcurrentDictionary<string, object>? data = null);
        
        /// <summary>
        /// Equipment state changed event
        /// </summary>
        event EventHandler<EquipmentState>? StateChanged;
        
        /// <summary>
        /// Alarm occurred event
        /// </summary>
        event EventHandler<AlarmEventArgs>? AlarmOccurred;
        
        /// <summary>
        /// Event reported event
        /// </summary>
        event EventHandler<EventReportArgs>? EventReported;
        
        /// <summary>
        /// Variable changed event
        /// </summary>
        event EventHandler<VariableChangedArgs>? VariableChanged;
    }
    
    /// <summary>
    /// Equipment states following SEMI E58 model
    /// </summary>
    public enum EquipmentState
    {
        Unknown,
        Offline,
        OfflineEquipment,
        OfflineAttemptOnline,
        OfflineHost,
        OnlineLocal,
        OnlineRemote,
        Initializing,
        Idle,
        Setup,
        Ready,
        Executing,
        Pause,
        Error,
        Maintenance
    }
    
    /// <summary>
    /// Alarm states
    /// </summary>
    public enum AlarmState
    {
        Set,
        Clear
    }
    
    /// <summary>
    /// Equipment configuration
    /// </summary>
    public class EquipmentConfiguration
    {
        public string EquipmentId { get; set; } = string.Empty;
        public string IpAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 5000;
        public bool IsActive { get; set; } = false; // Active (host) or Passive (equipment)
        public int T3Timeout { get; set; } = 45000; // Reply timeout
        public int T5Timeout { get; set; } = 10000; // Connect separation timeout
        public int T6Timeout { get; set; } = 5000;  // Control transaction timeout
        public int T7Timeout { get; set; } = 10000; // Not selected timeout
        public int T8Timeout { get; set; } = 5000;  // Network intercharacter timeout
        public int RetryCount { get; set; } = 3;
        public int RetryDelay { get; set; } = 1000;
        public ConcurrentDictionary<string, object> CustomSettings { get; set; } = new();
    }
    
    /// <summary>
    /// Command execution result
    /// </summary>
    public class CommandResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public ConcurrentDictionary<string, object>? ReturnData { get; set; }
        public int ErrorCode { get; set; }
    }
    
    /// <summary>
    /// Alarm event arguments
    /// </summary>
    public class AlarmEventArgs : EventArgs
    {
        public uint AlarmId { get; set; }
        public AlarmState State { get; set; }
        public string? Text { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    /// <summary>
    /// Event report arguments
    /// </summary>
    public class EventReportArgs : EventArgs
    {
        public uint EventId { get; set; }
        public ConcurrentDictionary<string, object>? Data { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    /// <summary>
    /// Variable changed arguments
    /// </summary>
    public class VariableChangedArgs : EventArgs
    {
        public uint VariableId { get; set; }
        public object? OldValue { get; set; }
        public object? NewValue { get; set; }
        public DateTime Timestamp { get; set; }
    }
}