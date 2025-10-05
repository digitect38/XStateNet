using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// SEMI E134 Data Collection Management (DCM)
/// Manages data collection plans, reports, and event-driven data collection
/// </summary>
public class E134DataCollectionManager
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ConcurrentDictionary<string, DataCollectionPlan> _plans = new();
    private readonly ConcurrentDictionary<string, List<DataReport>> _reports = new();

    public string MachineId => $"E134_DCM_MGR_{_equipmentId}";

    public E134DataCollectionManager(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Create a data collection plan
    /// </summary>
    public async Task<DataCollectionPlan> CreatePlanAsync(string planId, string[] dataItemIds, CollectionTrigger trigger)
    {
        if (_plans.ContainsKey(planId))
        {
            return _plans[planId];
        }

        var plan = new DataCollectionPlan(planId, dataItemIds, trigger, _equipmentId, _orchestrator);
        _plans[planId] = plan;
        _reports[planId] = new List<DataReport>();

        await plan.StartAsync();
        await plan.EnableAsync();

        return plan;
    }

    /// <summary>
    /// Get data collection plan
    /// </summary>
    public DataCollectionPlan? GetPlan(string planId)
    {
        return _plans.TryGetValue(planId, out var plan) ? plan : null;
    }

    /// <summary>
    /// Get all active plans
    /// </summary>
    public IEnumerable<DataCollectionPlan> GetActivePlans()
    {
        return _plans.Values.Where(p => p.IsEnabled);
    }

    /// <summary>
    /// Collect data from plan
    /// </summary>
    public async Task<DataReport> CollectDataAsync(string planId, Dictionary<string, object> collectedData)
    {
        var plan = GetPlan(planId);
        if (plan == null)
        {
            throw new InvalidOperationException($"Plan {planId} not found");
        }

        var report = new DataReport
        {
            PlanId = planId,
            Timestamp = DateTime.UtcNow,
            Data = collectedData
        };

        _reports[planId].Add(report);
        await plan.TriggerCollectionAsync(collectedData);

        return report;
    }

    /// <summary>
    /// Get collected reports for plan
    /// </summary>
    public IEnumerable<DataReport> GetReports(string planId, DateTime? since = null)
    {
        if (!_reports.TryGetValue(planId, out var reports))
        {
            return Enumerable.Empty<DataReport>();
        }

        if (since.HasValue)
        {
            return reports.Where(r => r.Timestamp >= since.Value);
        }

        return reports;
    }

    /// <summary>
    /// Remove a plan
    /// </summary>
    public async Task<bool> RemovePlanAsync(string planId)
    {
        if (_plans.TryRemove(planId, out var plan))
        {
            await plan.DisableAsync();
            _reports.TryRemove(planId, out _);
            return true;
        }
        return false;
    }
}

/// <summary>
/// Data collection trigger types
/// </summary>
public enum CollectionTrigger
{
    Event,           // Triggered by specific events
    Timer,           // Periodic collection
    StateChange,     // Triggered by state transitions
    Threshold,       // Triggered when values cross thresholds
    Manual          // Manually triggered
}

/// <summary>
/// Data report structure
/// </summary>
public class DataReport
{
    public string PlanId { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// Individual data collection plan state machine
/// </summary>
public class DataCollectionPlan
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly string _instanceId;
    private readonly CollectionTrigger _trigger;

    public string PlanId { get; }
    public string[] DataItemIds { get; }
    public bool IsEnabled { get; private set; }
    public int CollectionCount { get; private set; }
    public DateTime? LastCollectionTime { get; private set; }

    public string MachineId => $"E134_DCM_PLAN_{PlanId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public DataCollectionPlan(string planId, string[] dataItemIds, CollectionTrigger trigger, string equipmentId, EventBusOrchestrator orchestrator)
    {
        PlanId = planId;
        DataItemIds = dataItemIds;
        _trigger = trigger;
        _orchestrator = orchestrator;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'Disabled',
            context: {
                planId: '{{planId}}',
                dataItemIds: [],
                trigger: '{{trigger}}',
                collectionCount: 0,
                lastCollectionTime: null
            },
            states: {
                Disabled: {
                    entry: 'logDisabled',
                    on: {
                        ENABLE: {
                            target: 'Enabled',
                            actions: 'enableCollection'
                        }
                    }
                },
                Enabled: {
                    entry: 'logEnabled',
                    on: {
                        DISABLE: {
                            target: 'Disabled',
                            actions: 'disableCollection'
                        },
                        COLLECT: {
                            target: 'Collecting',
                            actions: 'startCollection'
                        },
                        PAUSE: 'Paused'
                    }
                },
                Collecting: {
                    entry: 'logCollecting',
                    on: {
                        COLLECTION_COMPLETE: {
                            target: 'Enabled',
                            actions: 'recordCollection'
                        },
                        COLLECTION_FAILED: {
                            target: 'Enabled',
                            actions: 'logCollectionError'
                        },
                        DISABLE: {
                            target: 'Disabled',
                            actions: 'disableCollection'
                        }
                    }
                },
                Paused: {
                    entry: 'logPaused',
                    on: {
                        RESUME: 'Enabled',
                        DISABLE: {
                            target: 'Disabled',
                            actions: 'disableCollection'
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
                IsEnabled = false;
                Console.WriteLine($"[{MachineId}] 🔴 Data collection disabled");
            },

            ["enableCollection"] = (ctx) =>
            {
                IsEnabled = true;
                Console.WriteLine($"[{MachineId}] 🟢 Data collection enabled - Trigger: {_trigger}");

                ctx.RequestSend("E39_EQUIPMENT_METRICS", "DCM_PLAN_ENABLED", new JObject
                {
                    ["planId"] = PlanId,
                    ["trigger"] = _trigger.ToString(),
                    ["dataItemCount"] = DataItemIds.Length
                });
            },

            ["disableCollection"] = (ctx) =>
            {
                IsEnabled = false;
                Console.WriteLine($"[{MachineId}] 🔴 Data collection disabled");

                ctx.RequestSend("E39_EQUIPMENT_METRICS", "DCM_PLAN_DISABLED", new JObject
                {
                    ["planId"] = PlanId
                });
            },

            ["logEnabled"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Ready to collect: {string.Join(", ", DataItemIds)}");
            },

            ["startCollection"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📊 Starting data collection...");
            },

            ["logCollecting"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📈 Collecting {DataItemIds.Length} data items");
            },

            ["recordCollection"] = (ctx) =>
            {
                CollectionCount++;
                LastCollectionTime = DateTime.UtcNow;

                Console.WriteLine($"[{MachineId}] ✅ Collection #{CollectionCount} complete at {LastCollectionTime}");

                ctx.RequestSend("E39_EQUIPMENT_METRICS", "DATA_COLLECTED", new JObject
                {
                    ["planId"] = PlanId,
                    ["collectionCount"] = CollectionCount,
                    ["timestamp"] = LastCollectionTime
                });

                // Notify E40 Process Jobs about data collection
                ctx.RequestSend("E40_PROCESSJOB_MGR", "DATA_REPORT_AVAILABLE", new JObject
                {
                    ["planId"] = PlanId,
                    ["dataItemIds"] = new JArray(DataItemIds),
                    ["timestamp"] = LastCollectionTime
                });
            },

            ["logCollectionError"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ❌ Collection failed");

                ctx.RequestSend("E39_EQUIPMENT_METRICS", "DATA_COLLECTION_FAILED", new JObject
                {
                    ["planId"] = PlanId,
                    ["timestamp"] = DateTime.UtcNow
                });
            },

            ["logPaused"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏸️ Data collection paused");
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

    public async Task<EventResult> TriggerCollectionAsync(Dictionary<string, object> data)
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "COLLECT", new JObject
        {
            ["data"] = JObject.FromObject(data)
        });

        // Simulate collection complete
        await Task.Delay(50);
        await _orchestrator.SendEventAsync("SYSTEM", MachineId, "COLLECTION_COMPLETE", null);

        return result;
    }

    public async Task<EventResult> PauseAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PAUSE", null);
    }

    public async Task<EventResult> ResumeAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "RESUME", null);
    }
}
