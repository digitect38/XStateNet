using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace XStateNet.InterMachine
{
    /// <summary>
    /// Machine registrator/orchestrator for type-based discovery
    /// Allows machines to find and communicate with each other by type
    /// </summary>
    public interface IMachineRegistrator
    {
        /// <summary>
        /// Register a machine with its type
        /// </summary>
        Task<string> RegisterMachineAsync(string machineType, IStateMachine machine, Dictionary<string, object> metadata = null);

        /// <summary>
        /// Discover machines by type
        /// </summary>
        Task<IReadOnlyList<MachineInfo>> DiscoverByTypeAsync(string machineType);

        /// <summary>
        /// Discover all machines matching a pattern
        /// </summary>
        Task<IReadOnlyList<MachineInfo>> DiscoverByPatternAsync(string typePattern);

        /// <summary>
        /// Get a specific machine by ID
        /// </summary>
        Task<MachineInfo> GetMachineAsync(string machineId);

        /// <summary>
        /// Send message to all machines of a type
        /// </summary>
        Task BroadcastToTypeAsync(string machineType, string eventName, object data = null);

        /// <summary>
        /// Send message to a specific machine
        /// </summary>
        Task SendToMachineAsync(string fromMachineId, string toMachineId, string eventName, object data = null);

        /// <summary>
        /// Unregister a machine
        /// </summary>
        Task UnregisterMachineAsync(string machineId);

        /// <summary>
        /// Get all registered machine types
        /// </summary>
        Task<IReadOnlyList<string>> GetMachineTypesAsync();

        /// <summary>
        /// Health check for a machine
        /// </summary>
        Task<bool> IsHealthyAsync(string machineId);
    }

    /// <summary>
    /// Information about a registered machine
    /// </summary>
    public class MachineInfo
    {
        public string MachineId { get; set; }
        public string MachineType { get; set; }
        public string Status { get; set; } // "online", "offline", "busy", etc.
        public DateTime RegisteredAt { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
        public IStateMachine Machine { get; set; }
    }

    /// <summary>
    /// Self-made implementation of machine registrator
    /// </summary>
    public class LocalMachineRegistrator : IMachineRegistrator
    {
        private readonly ConcurrentDictionary<string, MachineInfo> _machines = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _typeIndex = new();
        private readonly InterMachineConnector _connector = new();
        private int _machineCounter = 0;

        public async Task<string> RegisterMachineAsync(string machineType, IStateMachine machine, Dictionary<string, object> metadata = null)
        {
            // Generate unique ID based on type
            var uniqueId = $"{machineType}_{System.Threading.Interlocked.Increment(ref _machineCounter)}_{Guid.NewGuid():N}";
            var machineId = uniqueId.Length > 50 ? uniqueId.Substring(0, 50) : uniqueId;

            var info = new MachineInfo
            {
                MachineId = machineId,
                MachineType = machineType,
                Status = "online",
                RegisteredAt = DateTime.UtcNow,
                LastHeartbeat = DateTime.UtcNow,
                Metadata = metadata ?? new Dictionary<string, object>(),
                Machine = machine
            };

            // Register with connector
            _connector.RegisterMachine(machineId, machine);

            // Add to registry
            _machines[machineId] = info;

            // Update type index
            _typeIndex.AddOrUpdate(machineType,
                new HashSet<string> { machineId },
                (_, set) =>
                {
                    set.Add(machineId);
                    return set;
                });

            // Auto-connect to other machines of same type
            await AutoConnectToTypeAsync(machineId, machineType);

            return await Task.FromResult(machineId);
        }

        private async Task AutoConnectToTypeAsync(string newMachineId, string machineType)
        {
            var existingMachines = await DiscoverByTypeAsync(machineType);
            foreach (var other in existingMachines.Where(m => m.MachineId != newMachineId))
            {
                try
                {
                    _connector.Connect(newMachineId, other.MachineId);
                }
                catch { /* Ignore connection failures */ }
            }
        }

        public async Task<IReadOnlyList<MachineInfo>> DiscoverByTypeAsync(string machineType)
        {
            if (_typeIndex.TryGetValue(machineType, out var machineIds))
            {
                var result = new List<MachineInfo>();
                foreach (var id in machineIds)
                {
                    if (_machines.TryGetValue(id, out var info) && info.Status == "online")
                    {
                        result.Add(info);
                    }
                }
                return await Task.FromResult(result);
            }
            return await Task.FromResult(new List<MachineInfo>());
        }

        public async Task<IReadOnlyList<MachineInfo>> DiscoverByPatternAsync(string typePattern)
        {
            var result = new List<MachineInfo>();
            var pattern = typePattern.Replace("*", ".*");
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var type in _typeIndex.Keys)
            {
                if (regex.IsMatch(type))
                {
                    var machines = await DiscoverByTypeAsync(type);
                    result.AddRange(machines);
                }
            }
            return result;
        }

        public async Task<MachineInfo> GetMachineAsync(string machineId)
        {
            _machines.TryGetValue(machineId, out var info);
            return await Task.FromResult(info);
        }

        public async Task BroadcastToTypeAsync(string machineType, string eventName, object data = null)
        {
            var machines = await DiscoverByTypeAsync(machineType);
            var tasks = machines.Select(m => Task.Run(async () =>
            {
                try
                {
                    await m.Machine.SendAsync(eventName, data);
                }
                catch { /* Ignore individual failures */ }
            }));
            await Task.WhenAll(tasks);
        }

        public async Task SendToMachineAsync(string fromMachineId, string toMachineId, string eventName, object data = null)
        {
            // Auto-connect if not already connected
            try
            {
                await _connector.SendAsync(fromMachineId, toMachineId, eventName, data);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("are not connected"))
            {
                // Auto-connect and retry
                _connector.Connect(fromMachineId, toMachineId);
                await _connector.SendAsync(fromMachineId, toMachineId, eventName, data);
            }
        }

        public async Task UnregisterMachineAsync(string machineId)
        {
            if (_machines.TryRemove(machineId, out var info))
            {
                info.Status = "offline";

                // Remove from type index
                if (_typeIndex.TryGetValue(info.MachineType, out var set))
                {
                    set.Remove(machineId);
                    if (set.Count == 0)
                    {
                        _typeIndex.TryRemove(info.MachineType, out _);
                    }
                }

                // Unregister from connector
                _connector.UnregisterMachine(machineId);
            }
            await Task.CompletedTask;
        }

        public async Task<IReadOnlyList<string>> GetMachineTypesAsync()
        {
            return await Task.FromResult(_typeIndex.Keys.ToList());
        }

        public async Task<bool> IsHealthyAsync(string machineId)
        {
            if (_machines.TryGetValue(machineId, out var info))
            {
                var timeSinceHeartbeat = DateTime.UtcNow - info.LastHeartbeat;
                return await Task.FromResult(timeSinceHeartbeat.TotalSeconds < 30 && info.Status == "online");
            }
            return await Task.FromResult(false);
        }

        public void UpdateHeartbeat(string machineId)
        {
            if (_machines.TryGetValue(machineId, out var info))
            {
                info.LastHeartbeat = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Kubernetes-based machine registrator using service discovery
    /// </summary>
    public class KubernetesMachineRegistrator : IMachineRegistrator
    {
        private readonly string _namespace;
        private readonly LocalMachineRegistrator _localRegistry = new();

        public KubernetesMachineRegistrator(string k8sNamespace = "default")
        {
            _namespace = k8sNamespace;
        }

        public async Task<string> RegisterMachineAsync(string machineType, IStateMachine machine, Dictionary<string, object> metadata = null)
        {
            // Add Kubernetes-specific metadata
            var k8sMetadata = metadata ?? new Dictionary<string, object>();
            k8sMetadata["k8s.namespace"] = _namespace;
            k8sMetadata["k8s.pod"] = Environment.GetEnvironmentVariable("HOSTNAME") ?? "local";
            k8sMetadata["k8s.service"] = machineType.ToLower().Replace("_", "-");

            // Register locally
            var machineId = await _localRegistry.RegisterMachineAsync(machineType, machine, k8sMetadata);

            // In real implementation, would register with Kubernetes API
            // This would create/update a Service or Endpoint
            await RegisterWithKubernetesAsync(machineId, machineType, k8sMetadata);

            return machineId;
        }

        private async Task RegisterWithKubernetesAsync(string machineId, string machineType, Dictionary<string, object> metadata)
        {
            // Placeholder for Kubernetes API integration
            // In production, this would:
            // 1. Connect to Kubernetes API
            // 2. Register as a service endpoint
            // 3. Add appropriate labels for discovery
            // 4. Set up health check endpoint

            // Example pseudo-code:
            // var client = new KubernetesClient();
            // await client.CreateOrUpdateEndpoint(new Endpoint
            // {
            //     Name = machineId,
            //     Labels = { ["type"] = machineType },
            //     Port = metadata["port"],
            //     TargetPort = metadata["targetPort"]
            // });

            await Task.CompletedTask;
        }

        public async Task<IReadOnlyList<MachineInfo>> DiscoverByTypeAsync(string machineType)
        {
            // In real implementation, would query Kubernetes API
            // Example: kubectl get endpoints -l type=machineType

            // For now, use local registry
            return await _localRegistry.DiscoverByTypeAsync(machineType);
        }

        public async Task<IReadOnlyList<MachineInfo>> DiscoverByPatternAsync(string typePattern)
        {
            // Would use Kubernetes label selectors
            return await _localRegistry.DiscoverByPatternAsync(typePattern);
        }

        public async Task<MachineInfo> GetMachineAsync(string machineId)
        {
            return await _localRegistry.GetMachineAsync(machineId);
        }

        public async Task BroadcastToTypeAsync(string machineType, string eventName, object data = null)
        {
            // Could use Kubernetes Service for load balancing
            await _localRegistry.BroadcastToTypeAsync(machineType, eventName, data);
        }

        public async Task SendToMachineAsync(string fromMachineId, string toMachineId, string eventName, object data = null)
        {
            await _localRegistry.SendToMachineAsync(fromMachineId, toMachineId, eventName, data);
        }

        public async Task UnregisterMachineAsync(string machineId)
        {
            // Would remove from Kubernetes registry
            await _localRegistry.UnregisterMachineAsync(machineId);
        }

        public async Task<IReadOnlyList<string>> GetMachineTypesAsync()
        {
            // Would query Kubernetes for all service types
            return await _localRegistry.GetMachineTypesAsync();
        }

        public async Task<bool> IsHealthyAsync(string machineId)
        {
            // Would check Kubernetes health/readiness probes
            return await _localRegistry.IsHealthyAsync(machineId);
        }
    }
}