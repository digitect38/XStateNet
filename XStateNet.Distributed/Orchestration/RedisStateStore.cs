using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace XStateNet.Distributed.Orchestration
{
    /// <summary>
    /// Redis-backed implementation of distributed state storage for high availability
    /// </summary>
    public class RedisStateStore : IDistributedStateStore
    {
        private readonly IDistributedCache _cache;
        private readonly ILogger<RedisStateStore> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        private const string WORKFLOW_PREFIX = "xstate:workflow:";
        private const string SAGA_PREFIX = "xstate:saga:";
        private const string GROUP_PREFIX = "xstate:group:";
        private const string INDEX_WORKFLOWS = "xstate:index:workflows";
        private const string INDEX_SAGAS = "xstate:index:sagas";
        private const string INDEX_GROUPS = "xstate:index:groups";

        public RedisStateStore(IDistributedCache cache, ILogger<RedisStateStore> logger)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };
        }

        #region Workflow Management

        public async Task<bool> StoreWorkflowAsync(string workflowId, WorkflowState state)
        {
            try
            {
                state.UpdatedAt = DateTime.UtcNow;
                var key = WORKFLOW_PREFIX + workflowId;
                var json = JsonSerializer.Serialize(state, _jsonOptions);

                await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromHours(24)
                });

                // Add to index
                await AddToIndexAsync(INDEX_WORKFLOWS, workflowId);

                _logger.LogDebug("Stored workflow {WorkflowId} in Redis", workflowId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store workflow {WorkflowId}", workflowId);
                return false;
            }
        }

        public async Task<WorkflowState?> GetWorkflowAsync(string workflowId)
        {
            try
            {
                var key = WORKFLOW_PREFIX + workflowId;
                var json = await _cache.GetStringAsync(key);

                if (string.IsNullOrEmpty(json))
                    return null;

                return JsonSerializer.Deserialize<WorkflowState>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get workflow {WorkflowId}", workflowId);
                return null;
            }
        }

        public async Task<bool> RemoveWorkflowAsync(string workflowId)
        {
            try
            {
                var key = WORKFLOW_PREFIX + workflowId;
                await _cache.RemoveAsync(key);
                await RemoveFromIndexAsync(INDEX_WORKFLOWS, workflowId);

                _logger.LogDebug("Removed workflow {WorkflowId} from Redis", workflowId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove workflow {WorkflowId}", workflowId);
                return false;
            }
        }

        public async Task<IEnumerable<WorkflowState>> GetActiveWorkflowsAsync()
        {
            try
            {
                var workflowIds = await GetIndexAsync(INDEX_WORKFLOWS);
                var workflows = new List<WorkflowState>();

                foreach (var id in workflowIds)
                {
                    var workflow = await GetWorkflowAsync(id);
                    if (workflow != null &&
                        (workflow.Status == WorkflowStatus.Running ||
                         workflow.Status == WorkflowStatus.Paused))
                    {
                        workflows.Add(workflow);
                    }
                }

                return workflows;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get active workflows");
                return Enumerable.Empty<WorkflowState>();
            }
        }

        #endregion

        #region Saga Management

        public async Task<bool> StoreSagaAsync(string sagaId, SagaState state)
        {
            try
            {
                state.UpdatedAt = DateTime.UtcNow;
                var key = SAGA_PREFIX + sagaId;
                var json = JsonSerializer.Serialize(state, _jsonOptions);

                await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromHours(24)
                });

                await AddToIndexAsync(INDEX_SAGAS, sagaId);

                _logger.LogDebug("Stored saga {SagaId} in Redis", sagaId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store saga {SagaId}", sagaId);
                return false;
            }
        }

        public async Task<SagaState?> GetSagaAsync(string sagaId)
        {
            try
            {
                var key = SAGA_PREFIX + sagaId;
                var json = await _cache.GetStringAsync(key);

                if (string.IsNullOrEmpty(json))
                    return null;

                return JsonSerializer.Deserialize<SagaState>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get saga {SagaId}", sagaId);
                return null;
            }
        }

        public async Task<bool> RemoveSagaAsync(string sagaId)
        {
            try
            {
                var key = SAGA_PREFIX + sagaId;
                await _cache.RemoveAsync(key);
                await RemoveFromIndexAsync(INDEX_SAGAS, sagaId);

                _logger.LogDebug("Removed saga {SagaId} from Redis", sagaId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove saga {SagaId}", sagaId);
                return false;
            }
        }

        public async Task<IEnumerable<SagaState>> GetActiveSagasAsync()
        {
            try
            {
                var sagaIds = await GetIndexAsync(INDEX_SAGAS);
                var sagas = new List<SagaState>();

                foreach (var id in sagaIds)
                {
                    var saga = await GetSagaAsync(id);
                    if (saga != null &&
                        (saga.Status == SagaStatus.Running ||
                         saga.Status == SagaStatus.Compensating))
                    {
                        sagas.Add(saga);
                    }
                }

                return sagas;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get active sagas");
                return Enumerable.Empty<SagaState>();
            }
        }

        #endregion

        #region Group Management

        public async Task<bool> StoreGroupAsync(string groupId, GroupConfiguration config)
        {
            try
            {
                var key = GROUP_PREFIX + groupId;
                var json = JsonSerializer.Serialize(config, _jsonOptions);

                await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromHours(24)
                });

                await AddToIndexAsync(INDEX_GROUPS, groupId);

                _logger.LogDebug("Stored group {GroupId} in Redis", groupId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store group {GroupId}", groupId);
                return false;
            }
        }

        public async Task<GroupConfiguration?> GetGroupAsync(string groupId)
        {
            try
            {
                var key = GROUP_PREFIX + groupId;
                var json = await _cache.GetStringAsync(key);

                if (string.IsNullOrEmpty(json))
                    return null;

                return JsonSerializer.Deserialize<GroupConfiguration>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get group {GroupId}", groupId);
                return null;
            }
        }

        public async Task<bool> RemoveGroupAsync(string groupId)
        {
            try
            {
                var key = GROUP_PREFIX + groupId;
                await _cache.RemoveAsync(key);
                await RemoveFromIndexAsync(INDEX_GROUPS, groupId);

                _logger.LogDebug("Removed group {GroupId} from Redis", groupId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove group {GroupId}", groupId);
                return false;
            }
        }

        public async Task<IEnumerable<string>> GetGroupsAsync()
        {
            try
            {
                return await GetIndexAsync(INDEX_GROUPS);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get groups");
                return Enumerable.Empty<string>();
            }
        }

        #endregion

        #region Atomic Operations

        public async Task<bool> TryUpdateWorkflowAsync(string workflowId, Func<WorkflowState?, WorkflowState?> updateFunc)
        {
            const int maxRetries = 3;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var current = await GetWorkflowAsync(workflowId);
                    var updated = updateFunc(current);

                    if (updated == null)
                    {
                        if (current != null)
                            await RemoveWorkflowAsync(workflowId);
                        return true;
                    }

                    return await StoreWorkflowAsync(workflowId, updated);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Retry {Retry} failed for workflow {WorkflowId}", i + 1, workflowId);
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(100 * (i + 1)); // Exponential backoff
                }
            }

            return false;
        }

        public async Task<bool> TryUpdateSagaAsync(string sagaId, Func<SagaState?, SagaState?> updateFunc)
        {
            const int maxRetries = 3;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var current = await GetSagaAsync(sagaId);
                    var updated = updateFunc(current);

                    if (updated == null)
                    {
                        if (current != null)
                            await RemoveSagaAsync(sagaId);
                        return true;
                    }

                    return await StoreSagaAsync(sagaId, updated);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Retry {Retry} failed for saga {SagaId}", i + 1, sagaId);
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(100 * (i + 1)); // Exponential backoff
                }
            }

            return false;
        }

        #endregion

        #region Health and Maintenance

        public async Task<bool> PingAsync()
        {
            try
            {
                var testKey = "xstate:ping:" + Guid.NewGuid();
                await _cache.SetStringAsync(testKey, DateTime.UtcNow.ToString());
                await _cache.RemoveAsync(testKey);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis ping failed");
                return false;
            }
        }

        public async Task CleanupExpiredAsync(TimeSpan expiration)
        {
            try
            {
                var cutoff = DateTime.UtcNow - expiration;

                // Cleanup expired workflows
                var workflowIds = await GetIndexAsync(INDEX_WORKFLOWS);
                foreach (var id in workflowIds)
                {
                    var workflow = await GetWorkflowAsync(id);
                    if (workflow != null && workflow.UpdatedAt < cutoff &&
                        (workflow.Status == WorkflowStatus.Completed ||
                         workflow.Status == WorkflowStatus.Failed ||
                         workflow.Status == WorkflowStatus.Cancelled))
                    {
                        await RemoveWorkflowAsync(id);
                    }
                }

                // Cleanup expired sagas
                var sagaIds = await GetIndexAsync(INDEX_SAGAS);
                foreach (var id in sagaIds)
                {
                    var saga = await GetSagaAsync(id);
                    if (saga != null && saga.UpdatedAt < cutoff &&
                        (saga.Status == SagaStatus.Completed ||
                         saga.Status == SagaStatus.Failed))
                    {
                        await RemoveSagaAsync(id);
                    }
                }

                _logger.LogInformation("Cleanup completed. Removed expired items older than {Cutoff}", cutoff);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup failed");
            }
        }

        #endregion

        #region Private Helper Methods

        private async Task AddToIndexAsync(string indexKey, string value)
        {
            try
            {
                var existing = await _cache.GetStringAsync(indexKey);
                var index = string.IsNullOrEmpty(existing)
                    ? new HashSet<string>()
                    : JsonSerializer.Deserialize<HashSet<string>>(existing, _jsonOptions) ?? new HashSet<string>();

                index.Add(value);

                await _cache.SetStringAsync(indexKey, JsonSerializer.Serialize(index, _jsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to add {Value} to index {Index}", value, indexKey);
            }
        }

        private async Task RemoveFromIndexAsync(string indexKey, string value)
        {
            try
            {
                var existing = await _cache.GetStringAsync(indexKey);
                if (string.IsNullOrEmpty(existing))
                    return;

                var index = JsonSerializer.Deserialize<HashSet<string>>(existing, _jsonOptions) ?? new HashSet<string>();
                index.Remove(value);

                await _cache.SetStringAsync(indexKey, JsonSerializer.Serialize(index, _jsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove {Value} from index {Index}", value, indexKey);
            }
        }

        private async Task<IEnumerable<string>> GetIndexAsync(string indexKey)
        {
            try
            {
                var existing = await _cache.GetStringAsync(indexKey);
                if (string.IsNullOrEmpty(existing))
                    return Enumerable.Empty<string>();

                return JsonSerializer.Deserialize<HashSet<string>>(existing, _jsonOptions) ?? new HashSet<string>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get index {Index}", indexKey);
                return Enumerable.Empty<string>();
            }
        }

        #endregion
    }
}