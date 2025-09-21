using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XStateNet.Distributed.Orchestration
{
    /// <summary>
    /// Interface for distributed state storage to externalize orchestrator state
    /// </summary>
    public interface IDistributedStateStore
    {
        // Workflow state management
        Task<bool> StoreWorkflowAsync(string workflowId, WorkflowState state);
        Task<WorkflowState?> GetWorkflowAsync(string workflowId);
        Task<bool> RemoveWorkflowAsync(string workflowId);
        Task<IEnumerable<WorkflowState>> GetActiveWorkflowsAsync();

        // Saga state management
        Task<bool> StoreSagaAsync(string sagaId, SagaState state);
        Task<SagaState?> GetSagaAsync(string sagaId);
        Task<bool> RemoveSagaAsync(string sagaId);
        Task<IEnumerable<SagaState>> GetActiveSagasAsync();

        // Group state management
        Task<bool> StoreGroupAsync(string groupId, GroupConfiguration config);
        Task<GroupConfiguration?> GetGroupAsync(string groupId);
        Task<bool> RemoveGroupAsync(string groupId);
        Task<IEnumerable<string>> GetGroupsAsync();

        // Atomic operations
        Task<bool> TryUpdateWorkflowAsync(string workflowId, Func<WorkflowState?, WorkflowState?> updateFunc);
        Task<bool> TryUpdateSagaAsync(string sagaId, Func<SagaState?, SagaState?> updateFunc);

        // Health and maintenance
        Task<bool> PingAsync();
        Task CleanupExpiredAsync(TimeSpan expiration);
    }

    /// <summary>
    /// Represents the state of a workflow in distributed storage
    /// </summary>
    public class WorkflowState
    {
        public string WorkflowId { get; set; } = string.Empty;
        public string Definition { get; set; } = string.Empty;
        public Dictionary<string, object?> Context { get; set; } = new();
        public string CurrentStep { get; set; } = string.Empty;
        public WorkflowStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int RetryCount { get; set; }
        public string? LastError { get; set; }
    }

    /// <summary>
    /// Represents the state of a saga in distributed storage
    /// </summary>
    public class SagaState
    {
        public string SagaId { get; set; } = string.Empty;
        public List<SagaStep> Steps { get; set; } = new();
        public int CurrentStepIndex { get; set; }
        public SagaStatus Status { get; set; }
        public Dictionary<string, object?> Context { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<CompensationRecord> CompensationLog { get; set; } = new();
    }

    public class SagaStep
    {
        public string Name { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string? CompensationAction { get; set; }
        public StepStatus Status { get; set; }
        public DateTime? ExecutedAt { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class CompensationRecord
    {
        public string StepName { get; set; } = string.Empty;
        public DateTime CompensatedAt { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class GroupConfiguration
    {
        public string GroupId { get; set; } = string.Empty;
        public List<string> MachineIds { get; set; } = new();
        public GroupBehavior Behavior { get; set; }
        public DateTime CreatedAt { get; set; }
        public Dictionary<string, object?> Metadata { get; set; } = new();
    }

    public enum WorkflowStatus
    {
        Pending,
        Running,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    public enum SagaStatus
    {
        Pending,
        Running,
        Compensating,
        Completed,
        Failed,
        CompensationFailed
    }

    public enum StepStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Compensated
    }

    public enum GroupBehavior
    {
        Sequential,
        Parallel,
        RoundRobin,
        LoadBalanced
    }
}