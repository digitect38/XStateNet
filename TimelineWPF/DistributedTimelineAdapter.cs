using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XStateNet;
using XStateNet.Distributed.EventBus;
using XStateNet.Distributed.PubSub;
using XStateNet.Monitoring;

namespace TimelineWPF
{
    /// <summary>
    /// Enhanced adapter that combines local monitoring with distributed pub/sub
    /// Provides seamless timeline visualization for both local and remote state machines
    /// </summary>
    public class DistributedTimelineAdapter : IDisposable
    {
        private readonly ITimelineDataProvider _timelineProvider;
        private readonly IStateMachineEventBus? _eventBus;
        private readonly ILogger<DistributedTimelineAdapter>? _logger;
        private readonly RealTimeStateMachineAdapter _localAdapter;
        private readonly PubSubTimelineAdapter? _pubSubAdapter;
        private readonly HashSet<string> _localMachineIds = new();
        private readonly Stopwatch _globalStopwatch = new();
        private bool _isDistributed;

        /// <summary>
        /// Create adapter with optional distributed capabilities
        /// </summary>
        public DistributedTimelineAdapter(
            ITimelineDataProvider timelineProvider,
            IStateMachineEventBus? eventBus = null,
            ILogger<DistributedTimelineAdapter>? logger = null)
        {
            _timelineProvider = timelineProvider ?? throw new ArgumentNullException(nameof(timelineProvider));
            _eventBus = eventBus;
            _logger = logger;

            // Create local adapter
            _localAdapter = new RealTimeStateMachineAdapter(timelineProvider);

            // Create pub/sub adapter if event bus provided
            if (_eventBus != null)
            {
                _pubSubAdapter = new PubSubTimelineAdapter(timelineProvider, _eventBus);
                _isDistributed = true;
            }

            _globalStopwatch.Start();
        }

        /// <summary>
        /// Initialize the adapter
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isDistributed && _pubSubAdapter != null)
            {
                _logger?.LogInformation("Initializing distributed timeline adapter");
                await _pubSubAdapter.StartAsync();
            }
        }

        /// <summary>
        /// Register a local state machine with optional distributed publishing
        /// </summary>
        public async Task RegisterLocalMachineAsync(
            StateMachine stateMachine,
            string? displayName = null,
            bool enableDistribution = true)
        {
            if (stateMachine == null) throw new ArgumentNullException(nameof(stateMachine));

            var machineId = stateMachine.machineId ?? Guid.NewGuid().ToString();
            var normalizedId = machineId.StartsWith("#") ? machineId.Substring(1) : machineId;
            var name = displayName ?? normalizedId;

            _logger?.LogDebug("Registering local machine {MachineId} as {DisplayName}", normalizedId, name);

            // Track as local machine
            _localMachineIds.Add(normalizedId);

            // If distributed and enabled, register with pub/sub adapter
            if (_isDistributed && enableDistribution && _pubSubAdapter != null)
            {
                try
                {
                    // Register with pub/sub adapter which handles both local monitoring and distribution
                    await _pubSubAdapter.RegisterStateMachineAsync(stateMachine, name);
                    _logger?.LogInformation("Machine {MachineId} registered for distributed monitoring", normalizedId);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to enable distributed monitoring for {MachineId}", normalizedId);
                }
            }
            else
            {
                // Register with local adapter only
                _localAdapter.RegisterStateMachine(stateMachine, name);
                _logger?.LogInformation("Machine {MachineId} registered for local monitoring only", normalizedId);
            }
        }

        /// <summary>
        /// Subscribe to a remote state machine
        /// </summary>
        public async Task SubscribeToRemoteMachineAsync(string remoteMachineId, string? displayName = null)
        {
            if (!_isDistributed || _pubSubAdapter == null)
            {
                throw new InvalidOperationException("Distributed features require an event bus");
            }

            _logger?.LogInformation("Subscribing to remote machine {MachineId}", remoteMachineId);
            await _pubSubAdapter.SubscribeToRemoteMachineAsync(remoteMachineId, displayName);
        }

        /// <summary>
        /// Enable automatic discovery of remote machines
        /// </summary>
        public async Task EnableAutoDiscoveryAsync(string discoveryGroup = "xstatenet-discovery")
        {
            if (!_isDistributed || _eventBus == null)
            {
                throw new InvalidOperationException("Auto-discovery requires distributed mode");
            }

            _logger?.LogInformation("Enabling auto-discovery on group {Group}", discoveryGroup);

            // Subscribe to discovery events
            await _eventBus.SubscribeToGroupAsync(discoveryGroup, async evt =>
            {
                await HandleDiscoveryEvent(evt);
            });

            // Announce local machines
            await AnnounceLocalMachinesAsync(discoveryGroup);
        }

        /// <summary>
        /// Enable timeline synchronization across distributed instances
        /// </summary>
        public async Task EnableTimelineSyncAsync(string syncGroup = "timeline-sync")
        {
            if (!_isDistributed || _pubSubAdapter == null)
            {
                throw new InvalidOperationException("Timeline sync requires distributed mode");
            }

            _logger?.LogInformation("Enabling timeline synchronization on group {Group}", syncGroup);
            await _pubSubAdapter.EnableDistributedSyncAsync(syncGroup);
        }

        /// <summary>
        /// Send command to all connected timelines
        /// </summary>
        public async Task SendTimelineCommandAsync(string command, object? payload = null)
        {
            if (!_isDistributed || _pubSubAdapter == null)
            {
                _logger?.LogWarning("Cannot send timeline command in non-distributed mode");
                return;
            }

            await _pubSubAdapter.BroadcastLocalEventAsync($"TIMELINE_{command}", payload);
        }

        /// <summary>
        /// Get statistics about connected machines
        /// </summary>
        public TimelineStatistics GetStatistics()
        {
            var localMachines = _localMachineIds.Count;
            var remoteMachines = _pubSubAdapter != null ? GetRemoteMachineCount() : 0;

            return new TimelineStatistics
            {
                LocalMachines = localMachines,
                RemoteMachines = remoteMachines,
                TotalMachines = localMachines + remoteMachines,
                IsDistributed = _isDistributed,
                UptimeSeconds = _globalStopwatch.Elapsed.TotalSeconds
            };
        }

        #region Private Methods

        private async Task HandleDiscoveryEvent(StateMachineEvent evt)
        {
            switch (evt.EventName)
            {
                case "MACHINE_ANNOUNCE":
                    if (evt.Payload is MachineAnnouncement announcement)
                    {
                        _logger?.LogDebug("Discovered machine {MachineId} from {Source}",
                            announcement.MachineId, announcement.SourceHost);

                        // Auto-subscribe to discovered machine
                        if (_pubSubAdapter != null && !IsLocalMachine(announcement.MachineId))
                        {
                            await _pubSubAdapter.SubscribeToRemoteMachineAsync(
                                announcement.MachineId,
                                announcement.DisplayName);
                        }
                    }
                    break;

                case "MACHINE_QUERY":
                    // Respond with local machines
                    await AnnounceLocalMachinesAsync("xstatenet-discovery");
                    break;
            }
        }

        private async Task AnnounceLocalMachinesAsync(string group)
        {
            if (_eventBus == null) return;

            var machines = _timelineProvider.GetStateMachines();

            foreach (var machine in machines)
            {
                var announcement = new MachineAnnouncement
                {
                    MachineId = machine.Name,
                    DisplayName = machine.Name,
                    SourceHost = Environment.MachineName,
                    Timestamp = DateTime.UtcNow
                };

                await _eventBus.PublishToGroupAsync(group, "MACHINE_ANNOUNCE", announcement);
            }
        }

        private bool IsLocalMachine(string machineId)
        {
            return _localMachineIds.Contains(machineId);
        }

        private int GetRemoteMachineCount()
        {
            // Get count of remote machines from timeline
            var allMachines = _timelineProvider.GetStateMachines();
            var localCount = _localMachineIds.Count;
            return Math.Max(0, allMachines.Count() - localCount);
        }

        #endregion

        public void Dispose()
        {
            _logger?.LogInformation("Disposing distributed timeline adapter");

            // Clear local machine tracking
            _localMachineIds.Clear();

            // Dispose adapters
            _localAdapter?.Dispose();
            _pubSubAdapter?.Dispose();
            _globalStopwatch.Stop();
        }
    }

    /// <summary>
    /// Statistics about the timeline adapter
    /// </summary>
    public class TimelineStatistics
    {
        public int LocalMachines { get; set; }
        public int RemoteMachines { get; set; }
        public int TotalMachines { get; set; }
        public bool IsDistributed { get; set; }
        public double UptimeSeconds { get; set; }
    }

    /// <summary>
    /// Machine announcement for discovery
    /// </summary>
    public class MachineAnnouncement
    {
        public string MachineId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string SourceHost { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Extension methods for RealTimeStateMachineAdapter
    /// </summary>
    public static class RealTimeStateMachineAdapterExtensions
    {
        private static readonly Dictionary<RealTimeStateMachineAdapter, int> _machineCountCache = new();

        public static int GetRegisteredMachineCount(this RealTimeStateMachineAdapter adapter)
        {
            // This would need access to internal state - for now return cached value
            if (!_machineCountCache.TryGetValue(adapter, out var count))
            {
                // Estimate based on timeline provider
                count = 0;
            }
            return count;
        }

        public static void UpdateMachineCount(this RealTimeStateMachineAdapter adapter, int count)
        {
            _machineCountCache[adapter] = count;
        }
    }
}