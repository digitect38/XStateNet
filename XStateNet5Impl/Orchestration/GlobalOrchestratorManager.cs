using System;
using System.Collections.Concurrent;
using System.Threading;

namespace XStateNet.Orchestration
{
    /// <summary>
    /// Global singleton orchestrator with channel group token isolation.
    /// Supports both production and testing scenarios with auto-growth.
    /// Thread-safe and designed for high concurrency.
    /// </summary>
    public sealed class GlobalOrchestratorManager
    {
        private static readonly Lazy<GlobalOrchestratorManager> _instance =
            new(() => new GlobalOrchestratorManager(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static GlobalOrchestratorManager Instance => _instance.Value;

        private readonly EventBusOrchestrator _orchestrator;
        private readonly ConcurrentDictionary<string, ChannelGroupToken> _channelGroups;
        private int _nextGroupId = 0;

        private GlobalOrchestratorManager()
        {
            var config = new OrchestratorConfig
            {
                PoolSize = 16,              // Initial size
                EnableMetrics = true,       // Track performance
                EnableLogging = false       // Disabled by default
            };

            _orchestrator = new EventBusOrchestrator(config);
            _channelGroups = new ConcurrentDictionary<string, ChannelGroupToken>();
        }

        /// <summary>
        /// Get the global orchestrator instance.
        /// Use with channel group tokens for isolation.
        /// </summary>
        public EventBusOrchestrator Orchestrator => _orchestrator;

        /// <summary>
        /// Create a new channel group for isolated execution context.
        /// Use this in tests or for logical grouping of machines in production.
        /// </summary>
        /// <param name="groupName">Optional name for debugging. Auto-generated if null.</param>
        /// <returns>Token representing the channel group</returns>
        public ChannelGroupToken CreateChannelGroup(string? groupName = null)
        {
            var groupId = Interlocked.Increment(ref _nextGroupId);
            var name = groupName ?? $"ChannelGroup_{groupId}";

            var token = new ChannelGroupToken(
                groupId: groupId,
                name: name,
                createdAt: DateTime.UtcNow
            );

            _channelGroups.TryAdd(token.Id, token);
            return token;
        }

        /// <summary>
        /// Release a channel group and cleanup associated resources.
        /// Call this when a test completes or a production context is done.
        /// </summary>
        public void ReleaseChannelGroup(ChannelGroupToken token)
        {
            if (token == null) return;

            if (_channelGroups.TryRemove(token.Id, out var removedToken))
            {
                // Unregister all machines in this group
                _orchestrator.UnregisterMachinesInGroup(token.GroupId);
                removedToken.MarkReleased();
            }
        }

        /// <summary>
        /// Create a machine ID scoped to a channel group.
        /// This ensures isolation between different execution contexts.
        /// </summary>
        public string CreateScopedMachineId(ChannelGroupToken token, string baseName)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            if (token.IsReleased)
                throw new InvalidOperationException($"Channel group {token.Name} has been released");

            // Format: #baseName_groupId_shortGuid (consistent with GUID isolation format)
            // Use underscore separator instead of # to be consistent with guidIsolate format
            var shortGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
            return $"#{baseName}_{token.GroupId}_{shortGuid}";
        }

        /// <summary>
        /// Get metrics for the global orchestrator.
        /// </summary>
        public OrchestratorMetrics GetMetrics()
        {
            return _orchestrator.Metrics;
        }

        /// <summary>
        /// Get active channel group count.
        /// </summary>
        public int ActiveChannelGroupCount => _channelGroups.Count;

        /// <summary>
        /// Force cleanup of all resources (test-only, don't use in production).
        /// </summary>
        public void ForceCleanup()
        {
            foreach (var token in _channelGroups.Values)
            {
                ReleaseChannelGroup(token);
            }
            _channelGroups.Clear();
        }

        // Prevent external instantiation
        static GlobalOrchestratorManager() { }
    }

    /// <summary>
    /// Token representing a channel group for isolated execution.
    /// Each test or production context gets its own token.
    /// </summary>
    public sealed class ChannelGroupToken : IDisposable
    {
        public string Id { get; }
        public int GroupId { get; }
        public string Name { get; }
        public DateTime CreatedAt { get; }
        public bool IsReleased { get; private set; }

        internal ChannelGroupToken(int groupId, string name, DateTime createdAt)
        {
            GroupId = groupId;
            Name = name;
            CreatedAt = createdAt;
            Id = $"token_{groupId}_{createdAt.Ticks}";
        }

        internal void MarkReleased()
        {
            IsReleased = true;
        }

        public void Dispose()
        {
            if (!IsReleased)
            {
                GlobalOrchestratorManager.Instance.ReleaseChannelGroup(this);
            }
        }

        public override string ToString() => $"{Name} (Group {GroupId})";
    }
}
