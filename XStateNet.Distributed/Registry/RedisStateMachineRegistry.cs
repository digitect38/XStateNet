using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;

namespace XStateNet.Distributed.Registry
{
    /// <summary>
    /// Redis-based implementation of distributed state machine registry
    /// </summary>
    public class RedisStateMachineRegistry : IStateMachineRegistry, IDisposable
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly ISubscriber _subscriber;
        private readonly ILogger<RedisStateMachineRegistry>? _logger;
        private readonly string _keyPrefix;
        private readonly TimeSpan _defaultHeartbeatTtl;
        private readonly Dictionary<string, List<Action<RegistryChangeEvent>>> _changeHandlers = new();
        private readonly object _handlersLock = new();
        
        // Redis keys
        private string MachinesKey => $"{_keyPrefix}:machines";
        private string HeartbeatKey(string machineId) => $"{_keyPrefix}:heartbeat:{machineId}";
        private string StatusKey(string machineId) => $"{_keyPrefix}:status:{machineId}";
        private string MetadataKey(string machineId) => $"{_keyPrefix}:metadata:{machineId}";
        private string GroupsKey => $"{_keyPrefix}:groups";
        private string GroupMembersKey(string groupName) => $"{_keyPrefix}:group:{groupName}";
        
        // Events
        public event EventHandler<StateMachineRegisteredEventArgs>? MachineRegistered;
        public event EventHandler<StateMachineUnregisteredEventArgs>? MachineUnregistered;
        public event EventHandler<StateMachineStatusChangedEventArgs>? StatusChanged;
        
        public RedisStateMachineRegistry(
            IConnectionMultiplexer redis,
            string keyPrefix = "xstatenet",
            TimeSpan? heartbeatTtl = null,
            ILogger<RedisStateMachineRegistry>? logger = null)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _db = _redis.GetDatabase();
            _subscriber = _redis.GetSubscriber();
            _keyPrefix = keyPrefix;
            _defaultHeartbeatTtl = heartbeatTtl ?? TimeSpan.FromSeconds(30);
            _logger = logger;
            
            SubscribeToRedisEvents();
        }
        
        public async Task<bool> RegisterAsync(string machineId, StateMachineInfo info)
        {
            try
            {
                if (string.IsNullOrEmpty(machineId))
                    throw new ArgumentException("Machine ID cannot be empty", nameof(machineId));
                
                info.MachineId = machineId;
                info.RegisteredAt = DateTime.UtcNow;
                info.LastHeartbeat = DateTime.UtcNow;
                
                var json = JsonSerializer.Serialize(info);
                
                // Store machine info
                var transaction = _db.CreateTransaction();
                
                // Add to machines hash
                var hashTask = transaction.HashSetAsync(MachinesKey, machineId, json);
                
                // Set initial heartbeat
                var heartbeatTask = transaction.StringSetAsync(
                    HeartbeatKey(machineId), 
                    DateTime.UtcNow.Ticks, 
                    _defaultHeartbeatTtl);
                
                // Set initial status
                var statusTask = transaction.StringSetAsync(
                    StatusKey(machineId), 
                    info.Status.ToString());
                
                // Store metadata
                if (info.Metadata?.Any() == true)
                {
                    var metadataJson = JsonSerializer.Serialize(info.Metadata);
                    _ = transaction.StringSetAsync(MetadataKey(machineId), metadataJson);
                }
                
                var committed = await transaction.ExecuteAsync();
                
                if (committed)
                {
                    // Publish registration event
                    await PublishEventAsync(new RegistryChangeEvent
                    {
                        Type = RegistryChangeType.Registered,
                        MachineId = machineId,
                        MachineInfo = info
                    });
                    
                    MachineRegistered?.Invoke(this, new StateMachineRegisteredEventArgs 
                    { 
                        MachineId = machineId, 
                        Info = info 
                    });
                    
                    _logger?.LogInformation("Registered state machine {MachineId} on node {NodeId}", 
                        machineId, info.NodeId);
                }
                
                return committed;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to register state machine {MachineId}", machineId);
                throw;
            }
        }
        
        public async Task<bool> UnregisterAsync(string machineId)
        {
            try
            {
                var info = await GetAsync(machineId);
                if (info == null)
                    return false;
                
                var transaction = _db.CreateTransaction();
                
                // Remove from machines hash
                _ = transaction.HashDeleteAsync(MachinesKey, machineId);
                
                // Remove heartbeat
                _ = transaction.KeyDeleteAsync(HeartbeatKey(machineId));
                
                // Remove status
                _ = transaction.KeyDeleteAsync(StatusKey(machineId));
                
                // Remove metadata
                _ = transaction.KeyDeleteAsync(MetadataKey(machineId));
                
                // Remove from any groups
                await RemoveFromAllGroupsAsync(machineId);
                
                var committed = await transaction.ExecuteAsync();
                
                if (committed)
                {
                    // Publish unregistration event
                    await PublishEventAsync(new RegistryChangeEvent
                    {
                        Type = RegistryChangeType.Unregistered,
                        MachineId = machineId
                    });
                    
                    MachineUnregistered?.Invoke(this, new StateMachineUnregisteredEventArgs 
                    { 
                        MachineId = machineId, 
                        Reason = "Explicit unregistration" 
                    });
                    
                    _logger?.LogInformation("Unregistered state machine {MachineId}", machineId);
                }
                
                return committed;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to unregister state machine {MachineId}", machineId);
                throw;
            }
        }
        
        public async Task<StateMachineInfo?> GetAsync(string machineId)
        {
            try
            {
                var json = await _db.HashGetAsync(MachinesKey, machineId);
                
                if (!json.HasValue)
                    return null;
                
                var info = JsonSerializer.Deserialize<StateMachineInfo>(json!);
                
                // Update with latest heartbeat
                var heartbeat = await _db.StringGetAsync(HeartbeatKey(machineId));
                if (heartbeat.HasValue && long.TryParse(heartbeat, out var ticks))
                {
                    info!.LastHeartbeat = new DateTime(ticks, DateTimeKind.Utc);
                }
                
                // Update with latest status
                var status = await _db.StringGetAsync(StatusKey(machineId));
                if (status.HasValue && Enum.TryParse<MachineStatus>(status, out var machineStatus))
                {
                    info!.Status = machineStatus;
                }
                
                return info;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to get state machine {MachineId}", machineId);
                throw;
            }
        }
        
        public async Task<IEnumerable<StateMachineInfo>> GetAllAsync()
        {
            try
            {
                var entries = await _db.HashGetAllAsync(MachinesKey);
                var machines = new List<StateMachineInfo>();
                
                foreach (var entry in entries)
                {
                    try
                    {
                        var info = JsonSerializer.Deserialize<StateMachineInfo>(entry.Value!);
                        if (info != null)
                        {
                            info.MachineId = entry.Name!;
                            
                            // Update with latest heartbeat
                            var heartbeat = await _db.StringGetAsync(HeartbeatKey(info.MachineId));
                            if (heartbeat.HasValue && long.TryParse(heartbeat, out var ticks))
                            {
                                info.LastHeartbeat = new DateTime(ticks, DateTimeKind.Utc);
                            }
                            
                            machines.Add(info);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to deserialize machine info for {MachineId}", entry.Name);
                    }
                }
                
                return machines;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to get all state machines");
                throw;
            }
        }
        
        public async Task<IEnumerable<StateMachineInfo>> GetActiveAsync(TimeSpan heartbeatThreshold)
        {
            var allMachines = await GetAllAsync();
            var threshold = DateTime.UtcNow - heartbeatThreshold;
            
            return allMachines.Where(m => m.LastHeartbeat > threshold);
        }
        
        public async Task UpdateHeartbeatAsync(string machineId)
        {
            try
            {
                var now = DateTime.UtcNow;
                await _db.StringSetAsync(
                    HeartbeatKey(machineId), 
                    now.Ticks, 
                    _defaultHeartbeatTtl);
                
                // Update last heartbeat in machine info
                var json = await _db.HashGetAsync(MachinesKey, machineId);
                if (json.HasValue)
                {
                    var info = JsonSerializer.Deserialize<StateMachineInfo>(json!);
                    if (info != null)
                    {
                        info.LastHeartbeat = now;
                        await _db.HashSetAsync(MachinesKey, machineId, JsonSerializer.Serialize(info));
                    }
                }
                
                _logger?.LogDebug("Updated heartbeat for state machine {MachineId}", machineId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to update heartbeat for state machine {MachineId}", machineId);
                throw;
            }
        }
        
        public async Task UpdateStatusAsync(string machineId, MachineStatus status, string? currentState = null)
        {
            try
            {
                var oldStatusStr = await _db.StringGetAsync(StatusKey(machineId));
                var oldStatus = oldStatusStr.HasValue && Enum.TryParse<MachineStatus>(oldStatusStr, out var s) 
                    ? s 
                    : MachineStatus.Stopped;
                
                await _db.StringSetAsync(StatusKey(machineId), status.ToString());
                
                // Update machine info
                var json = await _db.HashGetAsync(MachinesKey, machineId);
                if (json.HasValue)
                {
                    var info = JsonSerializer.Deserialize<StateMachineInfo>(json!);
                    if (info != null)
                    {
                        info.Status = status;
                        if (currentState != null)
                            info.CurrentState = currentState;
                        
                        await _db.HashSetAsync(MachinesKey, machineId, JsonSerializer.Serialize(info));
                    }
                }
                
                // Publish status change event
                await PublishEventAsync(new RegistryChangeEvent
                {
                    Type = RegistryChangeType.StatusChanged,
                    MachineId = machineId,
                    MachineInfo = await GetAsync(machineId)
                });
                
                StatusChanged?.Invoke(this, new StateMachineStatusChangedEventArgs
                {
                    MachineId = machineId,
                    OldStatus = oldStatus,
                    NewStatus = status,
                    CurrentState = currentState
                });
                
                _logger?.LogInformation("Updated status for state machine {MachineId}: {OldStatus} -> {NewStatus}", 
                    machineId, oldStatus, status);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to update status for state machine {MachineId}", machineId);
                throw;
            }
        }
        
        public async Task<IEnumerable<StateMachineInfo>> FindByPatternAsync(string pattern)
        {
            try
            {
                var allMachines = await GetAllAsync();
                
                // Support wildcards: * for any characters, ? for single character
                var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";
                
                var regexObj = new System.Text.RegularExpressions.Regex(regex, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                return allMachines.Where(m => 
                    regexObj.IsMatch(m.MachineId) || 
                    m.Tags.Any(t => regexObj.IsMatch(t.Value)));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to find state machines by pattern {Pattern}", pattern);
                throw;
            }
        }
        
        public async Task SubscribeToChangesAsync(Action<RegistryChangeEvent> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            
            var subscriberId = Guid.NewGuid().ToString();
            
            lock (_handlersLock)
            {
                if (!_changeHandlers.ContainsKey(subscriberId))
                    _changeHandlers[subscriberId] = new List<Action<RegistryChangeEvent>>();
                
                _changeHandlers[subscriberId].Add(handler);
            }
            
            await Task.CompletedTask;
        }
        
        private void SubscribeToRedisEvents()
        {
            // Subscribe to registry events channel
            _subscriber.Subscribe(RedisChannel.Literal($"{_keyPrefix}:events"), (channel, message) =>
            {
                try
                {
                    var evt = JsonSerializer.Deserialize<RegistryChangeEvent>(message!);
                    if (evt != null)
                    {
                        NotifyChangeHandlers(evt);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to process Redis event");
                }
            });
            
            // Subscribe to Redis keyspace notifications for expired keys (heartbeat timeout)
            _subscriber.Subscribe(RedisChannel.Literal("__keyevent@0__:expired"), async (channel, key) =>
            {
                var keyStr = key.ToString();
                if (keyStr.StartsWith($"{_keyPrefix}:heartbeat:"))
                {
                    var machineId = keyStr.Substring($"{_keyPrefix}:heartbeat:".Length);
                    _logger?.LogWarning("Heartbeat expired for state machine {MachineId}", machineId);
                    
                    // Update status to unhealthy
                    await UpdateStatusAsync(machineId, MachineStatus.Unhealthy);
                }
            });
        }
        
        private async Task PublishEventAsync(RegistryChangeEvent evt)
        {
            var json = JsonSerializer.Serialize(evt);
            await _subscriber.PublishAsync(RedisChannel.Literal($"{_keyPrefix}:events"), json);
        }
        
        private void NotifyChangeHandlers(RegistryChangeEvent evt)
        {
            List<Action<RegistryChangeEvent>> handlers;
            
            lock (_handlersLock)
            {
                handlers = _changeHandlers.Values.SelectMany(h => h).ToList();
            }
            
            foreach (var handler in handlers)
            {
                try
                {
                    handler(evt);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in change handler");
                }
            }
        }
        
        private async Task RemoveFromAllGroupsAsync(string machineId)
        {
            var groups = await _db.HashGetAllAsync(GroupsKey);
            
            foreach (var group in groups)
            {
                await _db.SetRemoveAsync(GroupMembersKey(group.Name!), machineId);
            }
        }
        
        public void Dispose()
        {
            _redis?.Dispose();
        }
    }
}