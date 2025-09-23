using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XStateNet.Distributed.Registry;
using XStateNet.Distributed.EventBus;

namespace XStateNet.Distributed.Orchestration
{
    /// <summary>
    /// Interface for state machine orchestrator
    /// </summary>
    public interface IStateMachineOrchestrator
    {
        // Lifecycle Management
        /// <summary>
        /// Deploy a new state machine instance
        /// </summary>
        Task<DeploymentResult> DeployStateMachineAsync(StateMachineDefinition definition, DeploymentOptions? options = null);
        
        /// <summary>
        /// Scale state machine instances
        /// </summary>
        Task<ScaleResult> ScaleStateMachineAsync(string machineId, int targetInstances);
        
        /// <summary>
        /// Migrate a state machine to another node
        /// </summary>
        Task<MigrationResult> MigrateStateMachineAsync(string machineId, string targetNodeId);
        
        /// <summary>
        /// Shutdown a state machine
        /// </summary>
        Task<bool> ShutdownStateMachineAsync(string machineId, bool graceful = true, TimeSpan? timeout = null);
        
        /// <summary>
        /// Restart a state machine
        /// </summary>
        Task<bool> RestartStateMachineAsync(string machineId);
        
        // Monitoring & Health
        /// <summary>
        /// Check health of a state machine
        /// </summary>
        Task<HealthCheckResult> CheckHealthAsync(string machineId);
        
        /// <summary>
        /// Get metrics for a state machine
        /// </summary>
        Task<MetricsSnapshot> GetMetricsAsync(string machineId, TimeSpan? window = null);
        
        /// <summary>
        /// Get active alerts
        /// </summary>
        Task<IEnumerable<Alert>> GetActiveAlertsAsync(string? machineId = null);
        
        /// <summary>
        /// Get system overview
        /// </summary>
        Task<SystemOverview> GetSystemOverviewAsync();
        
        // Coordination & Workflows
        /// <summary>
        /// Execute a workflow across multiple state machines
        /// </summary>
        Task<WorkflowResult> ExecuteWorkflowAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Create a state machine group
        /// </summary>
        Task<string> CreateStateMachineGroupAsync(string groupName, GroupOptions options, params string[] machineIds);
        
        /// <summary>
        /// Send event to a group
        /// </summary>
        Task SendGroupEventAsync(string groupName, string eventName, object? payload = null);
        
        /// <summary>
        /// Coordinate state machines in a saga pattern
        /// </summary>
        Task<SagaResult> ExecuteSagaAsync(SagaDefinition saga, CancellationToken cancellationToken = default);
        
        // Discovery & Routing
        /// <summary>
        /// Discover state machines by capability
        /// </summary>
        Task<IEnumerable<string>> DiscoverByCapabilityAsync(string capability);
        
        /// <summary>
        /// Route event to appropriate state machine
        /// </summary>
        Task<RoutingResult> RouteEventAsync(RoutingRequest request);
        
        // Configuration
        /// <summary>
        /// Update state machine configuration
        /// </summary>
        Task<bool> UpdateConfigurationAsync(string machineId, ConcurrentDictionary<string, object> configuration);
        
        /// <summary>
        /// Get configuration
        /// </summary>
        Task<ConcurrentDictionary<string, object>> GetConfigurationAsync(string machineId);
    }
    
    // Data models
    public class StateMachineDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string JsonScript { get; set; } = string.Empty;
        public ConcurrentDictionary<string, object> Configuration { get; set; } = new();
        public List<string> RequiredCapabilities { get; set; } = new();
        public ResourceRequirements? ResourceRequirements { get; set; }
        public ConcurrentDictionary<string, string> Labels { get; set; } = new();
    }
    
    public class DeploymentOptions
    {
        public string? TargetNodeId { get; set; }
        public int InitialInstances { get; set; } = 1;
        public bool AutoStart { get; set; } = true;
        public bool EnableMonitoring { get; set; } = true;
        public ConcurrentDictionary<string, string> EnvironmentVariables { get; set; } = new();
        public HealthCheckOptions? HealthCheck { get; set; }
        public RetryPolicy? RetryPolicy { get; set; }
    }
    
    public class DeploymentResult
    {
        public bool Success { get; set; }
        public string MachineId { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public TimeSpan DeploymentTime { get; set; }
    }
    
    public class ScaleResult
    {
        public bool Success { get; set; }
        public int PreviousInstances { get; set; }
        public int CurrentInstances { get; set; }
        public List<string> NewInstanceIds { get; set; } = new();
        public List<string> RemovedInstanceIds { get; set; } = new();
    }
    
    public class MigrationResult
    {
        public bool Success { get; set; }
        public string SourceNodeId { get; set; } = string.Empty;
        public string TargetNodeId { get; set; } = string.Empty;
        public TimeSpan MigrationTime { get; set; }
        public bool StatePreserved { get; set; }
    }
    
    public class HealthCheckResult
    {
        public string MachineId { get; set; } = string.Empty;
        public HealthStatus Status { get; set; }
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
        public List<HealthCheckItem> Items { get; set; } = new();
        public string? Message { get; set; }
    }
    
    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy,
        Unknown,
        NotFound
    }
    
    public class HealthCheckItem
    {
        public string Name { get; set; } = string.Empty;
        public bool IsHealthy { get; set; }
        public string? Message { get; set; }
        public ConcurrentDictionary<string, object> Data { get; set; } = new();
    }
    
    public class MetricsSnapshot
    {
        public string MachineId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public TimeSpan Window { get; set; }
        public long TotalEvents { get; set; }
        public double EventsPerSecond { get; set; }
        public long StateTransitions { get; set; }
        public double AverageTransitionTime { get; set; }
        public ConcurrentDictionary<string, long> StateVisitCounts { get; set; } = new();
        public ConcurrentDictionary<string, double> StateAverageDuration { get; set; } = new();
        public long ErrorCount { get; set; }
        public double ErrorRate { get; set; }
        public ResourceUsage? ResourceUsage { get; set; }
    }
    
    public class Alert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string MachineId { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
        public ConcurrentDictionary<string, object> Context { get; set; } = new();
    }
    
    public enum AlertSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }
    
    public class SystemOverview
    {
        public int TotalMachines { get; set; }
        public int ActiveMachines { get; set; }
        public int HealthyMachines { get; set; }
        public int DegradedMachines { get; set; }
        public int UnhealthyMachines { get; set; }
        public int ActiveWorkflows { get; set; }
        public int ActiveSagas { get; set; }
        public long TotalEventsProcessed { get; set; }
        public double SystemEventRate { get; set; }
        public ConcurrentDictionary<string, int> MachinesByNode { get; set; } = new();
        public List<Alert> RecentAlerts { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
    
    public class WorkflowDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<WorkflowStep> Steps { get; set; } = new();
        public ConcurrentDictionary<string, object> Context { get; set; } = new();
        public TimeSpan? Timeout { get; set; }
        public WorkflowCompensation? Compensation { get; set; }
    }
    
    public class WorkflowStep
    {
        public string StepId { get; set; } = string.Empty;
        public string MachineId { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public object? Payload { get; set; }
        public string? WaitForState { get; set; }
        public TimeSpan? Timeout { get; set; }
        public List<string> DependsOn { get; set; } = new();
    }
    
    public class WorkflowResult
    {
        public bool Success { get; set; }
        public string WorkflowId { get; set; } = string.Empty;
        public TimeSpan ExecutionTime { get; set; }
        public ConcurrentDictionary<string, StepResult> StepResults { get; set; } = new();
        public object? FinalOutput { get; set; }
        public string? ErrorMessage { get; set; }
    }
    
    public class StepResult
    {
        public string StepId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public object? Output { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan ExecutionTime { get; set; }
    }
    
    public class GroupOptions
    {
        public GroupCoordinationType CoordinationType { get; set; } = GroupCoordinationType.Broadcast;
        public LoadBalancingStrategy? LoadBalancing { get; set; }
        public bool PersistGroup { get; set; } = true;
        public ConcurrentDictionary<string, string> Metadata { get; set; } = new();
    }
    
    public enum GroupCoordinationType
    {
        Broadcast,
        RoundRobin,
        Random,
        LeastLoaded,
        Primary
    }
    
    public enum LoadBalancingStrategy
    {
        RoundRobin,
        Random,
        LeastConnections,
        WeightedRoundRobin,
        ConsistentHash
    }
    
    public class ResourceRequirements
    {
        public int MinCpu { get; set; }
        public int MinMemoryMB { get; set; }
        public int MaxCpu { get; set; }
        public int MaxMemoryMB { get; set; }
    }
    
    public class HealthCheckOptions
    {
        public string Endpoint { get; set; } = "/health";
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
        public int UnhealthyThreshold { get; set; } = 3;
        public int HealthyThreshold { get; set; } = 2;
    }
    
    public class RetryPolicy
    {
        public int MaxRetries { get; set; } = 3;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
        public double BackoffMultiplier { get; set; } = 2.0;
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(1);
    }
    
    public class SagaDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<SagaDefinitionStep> Steps { get; set; } = new();
        public TimeSpan? Timeout { get; set; }
    }
    
    public class SagaDefinitionStep
    {
        public string StepId { get; set; } = string.Empty;
        public string MachineId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string CompensationAction { get; set; } = string.Empty;
        public object? Payload { get; set; }
    }
    
    public class SagaResult
    {
        public bool Success { get; set; }
        public string SagaId { get; set; } = string.Empty;
        public List<string> CompletedSteps { get; set; } = new();
        public List<string> CompensatedSteps { get; set; } = new();
        public string? FailedStep { get; set; }
        public string? ErrorMessage { get; set; }
    }
    
    public class WorkflowCompensation
    {
        public CompensationStrategy Strategy { get; set; } = CompensationStrategy.Saga;
        public List<CompensationStep> Steps { get; set; } = new();
    }
    
    public enum CompensationStrategy
    {
        Saga,
        Retry,
        Ignore,
        Manual
    }
    
    public class CompensationStep
    {
        public string StepId { get; set; } = string.Empty;
        public string MachineId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public object? Payload { get; set; }
    }
    
    public class RoutingRequest
    {
        public string EventName { get; set; } = string.Empty;
        public object? Payload { get; set; }
        public RoutingStrategy Strategy { get; set; } = RoutingStrategy.Capability;
        public ConcurrentDictionary<string, string> Requirements { get; set; } = new();
    }
    
    public enum RoutingStrategy
    {
        Direct,
        Capability,
        LoadBalanced,
        Broadcast,
        ContentBased
    }
    
    public class RoutingResult
    {
        public bool Success { get; set; }
        public List<string> TargetMachineIds { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }
}