using Newtonsoft.Json.Linq;
using System.Diagnostics;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// SEMI E148 Time Synchronization Standard
/// Manages time synchronization between host and equipment using NTP-like protocol
/// Ensures all timestamps are synchronized across the system
/// </summary>
public class E148TimeSynchronizationManager
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private TimeSynchronizationMachine? _machine;

    // Time synchronization state
    private TimeSpan _timeOffset = TimeSpan.Zero;
    private DateTime _lastSyncTime = DateTime.MinValue;
    private double _clockDriftRate = 0.0; // ppm (parts per million)
    private readonly List<SyncSample> _syncHistory = new();

    public string MachineId => $"E148_TIME_SYNC_{_equipmentId}";

    // Synchronization quality metrics
    public TimeSpan TimeOffset => _timeOffset;
    public double ClockDriftRate => _clockDriftRate;
    public DateTime LastSyncTime => _lastSyncTime;
    public bool IsSynchronized => (DateTime.UtcNow - _lastSyncTime).TotalMinutes < 5;

    public E148TimeSynchronizationManager(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Initialize time synchronization machine
    /// </summary>
    public async Task InitializeAsync()
    {
        _machine = new TimeSynchronizationMachine(_equipmentId, _orchestrator, this);
        await _machine.StartAsync();
        await _machine.EnableAsync();
    }

    /// <summary>
    /// Get synchronized time (UTC)
    /// </summary>
    public DateTime GetSynchronizedTime()
    {
        if (!IsSynchronized)
        {
            Console.WriteLine($"[{MachineId}] ⚠️ WARNING: Time not synchronized, using local time");
            return DateTime.UtcNow;
        }

        // Apply offset and drift correction
        var localTime = DateTime.UtcNow;
        var timeSinceSync = localTime - _lastSyncTime;
        var driftCorrection = TimeSpan.FromSeconds(timeSinceSync.TotalSeconds * _clockDriftRate / 1_000_000);

        return localTime + _timeOffset + driftCorrection;
    }

    /// <summary>
    /// Perform time synchronization with host
    /// </summary>
    public async Task<SyncResult> SynchronizeAsync(DateTime hostTime)
    {
        var t0 = DateTime.UtcNow; // Request sent
        var t1 = hostTime;        // Host timestamp
        var t2 = DateTime.UtcNow; // Response received

        // Calculate round-trip delay
        var roundTripDelay = t2 - t0;

        // Calculate time offset (assume symmetric network delay)
        var offset = (t1 - t0) - (roundTripDelay / 2);

        // Update synchronization state
        _timeOffset = offset;
        _lastSyncTime = DateTime.UtcNow;

        // Record sample for drift calculation
        var sample = new SyncSample
        {
            Timestamp = DateTime.UtcNow,
            Offset = offset,
            RoundTripDelay = roundTripDelay
        };
        _syncHistory.Add(sample);

        // Keep only last 100 samples
        if (_syncHistory.Count > 100)
        {
            _syncHistory.RemoveAt(0);
        }

        // Calculate clock drift if we have enough history
        if (_syncHistory.Count >= 2)
        {
            CalculateClockDrift();
        }

        var result = new SyncResult
        {
            Success = true,
            Offset = offset,
            RoundTripDelay = roundTripDelay,
            ClockDrift = _clockDriftRate,
            Timestamp = DateTime.UtcNow
        };

        await _machine!.SyncCompleteAsync(result);

        return result;
    }

    /// <summary>
    /// Calculate clock drift rate from sync history
    /// </summary>
    private void CalculateClockDrift()
    {
        if (_syncHistory.Count < 2) return;

        // Linear regression on offset vs time
        var samples = _syncHistory.TakeLast(20).ToList();
        var n = samples.Count;

        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumX2 = 0.0;

        var baseTime = samples[0].Timestamp;

        for (int i = 0; i < n; i++)
        {
            var x = (samples[i].Timestamp - baseTime).TotalSeconds;
            var y = samples[i].Offset.TotalMilliseconds;

            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        // Calculate slope (drift rate in ms/s = ppm/1000)
        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        _clockDriftRate = slope * 1000; // Convert to ppm
    }

    /// <summary>
    /// Get synchronization status
    /// </summary>
    public SyncStatus GetStatus()
    {
        return new SyncStatus
        {
            IsSynchronized = IsSynchronized,
            TimeOffset = _timeOffset,
            ClockDrift = _clockDriftRate,
            LastSyncTime = _lastSyncTime,
            SyncAccuracy = _syncHistory.Count > 0 ? _syncHistory.Last().RoundTripDelay : TimeSpan.Zero,
            SampleCount = _syncHistory.Count
        };
    }
}

/// <summary>
/// Time synchronization state machine
/// </summary>
public class TimeSynchronizationMachine
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E148TimeSynchronizationManager _manager;

    public string MachineId { get; }

    public TimeSynchronizationMachine(string equipmentId, EventBusOrchestrator orchestrator, E148TimeSynchronizationManager manager)
    {
        _orchestrator = orchestrator;
        _manager = manager;
        MachineId = $"E148_TIME_SYNC_{equipmentId}";

        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'Disabled',
            context: {
                syncCount: 0,
                lastSyncTime: null,
                offset: 0,
                drift: 0
            },
            states: {
                Disabled: {
                    entry: 'logDisabled',
                    on: {
                        ENABLE: {
                            target: 'Enabled',
                            actions: 'enableSync'
                        }
                    }
                },
                Enabled: {
                    entry: 'logEnabled',
                    on: {
                        DISABLE: {
                            target: 'Disabled',
                            actions: 'disableSync'
                        },
                        SYNC_REQUEST: {
                            target: 'Synchronizing',
                            actions: 'startSync'
                        },
                        PERIODIC_SYNC: {
                            target: 'Synchronizing',
                            actions: 'startPeriodicSync'
                        }
                    }
                },
                Synchronizing: {
                    entry: 'logSynchronizing',
                    on: {
                        SYNC_COMPLETE: {
                            target: 'Enabled',
                            actions: 'recordSync'
                        },
                        SYNC_FAILED: {
                            target: 'Enabled',
                            actions: 'logSyncError'
                        },
                        DISABLE: {
                            target: 'Disabled',
                            actions: 'disableSync'
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logDisabled"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔴 Time synchronization disabled");
            },

            ["enableSync"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🟢 Time synchronization enabled");

                ctx.RequestSend("E39_EQUIPMENT_METRICS", "TIME_SYNC_ENABLED", new JObject
                {
                    ["equipmentId"] = equipmentId
                });
            },

            ["disableSync"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔴 Time synchronization disabled");
            },

            ["logEnabled"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Ready for time synchronization");
            },

            ["startSync"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Starting time synchronization...");
            },

            ["startPeriodicSync"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏰ Periodic time synchronization triggered");
            },

            ["logSynchronizing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🕐 Synchronizing with host time server...");
            },

            ["recordSync"] = (ctx) =>
            {
                var status = _manager.GetStatus();
                Console.WriteLine($"[{MachineId}] ✅ Sync complete - Offset: {status.TimeOffset.TotalMilliseconds:F2}ms, Drift: {status.ClockDrift:F2} ppm");

                ctx.RequestSend("E39_EQUIPMENT_METRICS", "TIME_SYNCHRONIZED", new JObject
                {
                    ["equipmentId"] = equipmentId,
                    ["offset"] = status.TimeOffset.TotalMilliseconds,
                    ["drift"] = status.ClockDrift,
                    ["accuracy"] = status.SyncAccuracy.TotalMilliseconds,
                    ["timestamp"] = _manager.GetSynchronizedTime()
                });

                // Notify all other machines about time sync
                ctx.RequestSend("E40_PROCESSJOB_MGR", "TIME_SYNCHRONIZED", new JObject
                {
                    ["timestamp"] = _manager.GetSynchronizedTime()
                });

                ctx.RequestSend("E134_DCM_MGR", "TIME_SYNCHRONIZED", new JObject
                {
                    ["timestamp"] = _manager.GetSynchronizedTime()
                });
            },

            ["logSyncError"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ❌ Time synchronization failed");

                ctx.RequestSend("E39_EQUIPMENT_METRICS", "TIME_SYNC_FAILED", new JObject
                {
                    ["equipmentId"] = equipmentId,
                    ["timestamp"] = DateTime.UtcNow
                });
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            enableGuidIsolation: false
        );
    }

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public async Task<EventResult> EnableAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ENABLE", null);
    }

    public async Task<EventResult> DisableAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DISABLE", null);
    }

    public async Task<EventResult> RequestSyncAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SYNC_REQUEST", null);
    }

    public async Task<EventResult> SyncCompleteAsync(SyncResult result)
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SYNC_COMPLETE", JObject.FromObject(result));
    }

    public async Task<EventResult> SyncFailedAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SYNC_FAILED", null);
    }
}

/// <summary>
/// Time synchronization sample
/// </summary>
public class SyncSample
{
    public DateTime Timestamp { get; set; }
    public TimeSpan Offset { get; set; }
    public TimeSpan RoundTripDelay { get; set; }
}

/// <summary>
/// Synchronization result
/// </summary>
public class SyncResult
{
    public bool Success { get; set; }
    public TimeSpan Offset { get; set; }
    public TimeSpan RoundTripDelay { get; set; }
    public double ClockDrift { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Synchronization status
/// </summary>
public class SyncStatus
{
    public bool IsSynchronized { get; set; }
    public TimeSpan TimeOffset { get; set; }
    public double ClockDrift { get; set; }
    public DateTime LastSyncTime { get; set; }
    public TimeSpan SyncAccuracy { get; set; }
    public int SampleCount { get; set; }
}
