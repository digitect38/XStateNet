using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;
using XStateNet.Semi.Standards;

namespace XStateNet.Semi.Schedulers;

/// <summary>
/// Enhanced CMP Tool Scheduler - Phase 1 Implementation
/// Integrates: E90 Substrate Tracking, E134 Data Collection, E39 Equipment Metrics
/// </summary>
public class EnhancedCMPToolScheduler
{
    private readonly string _toolId;
    private readonly string _instanceId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    // SEMI Standard Integration
    private readonly E134DataCollectionManager _dataCollectionManager;
    private readonly E39E116E10EquipmentMetricsMachine _metricsManager;
    private readonly E90SubstrateTrackingMachine _substrateTracker;
    private readonly Dictionary<string, SubstrateMachine> _substrateTracking = new();

    // Tool state
    private string? _currentJobId;
    private string? _currentWaferId;
    private double _slurryLevel = 100.0;
    private double _padWear = 0.0;
    private int _wafersProcessed = 0;
    private readonly int _wafersBetweenPM = 50;
    private DateTime _lastProcessStart = DateTime.UtcNow;
    private List<double> _cycleTimes = new();

    public string MachineId => $"{_toolId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public EnhancedCMPToolScheduler(string toolId, EventBusOrchestrator orchestrator)
    {
        _toolId = toolId;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
        _orchestrator = orchestrator;

        // Initialize SEMI standards
        _dataCollectionManager = new E134DataCollectionManager($"DCM_{toolId}", _orchestrator);
        _metricsManager = new E39E116E10EquipmentMetricsMachine($"METRICS_{toolId}", _orchestrator);
        _substrateTracker = new E90SubstrateTrackingMachine(toolId, _orchestrator);

        InitializeDataCollection();
        InitializeMetrics();

        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'idle',
            context: {
                slurryLevel: 100,
                padWear: 0,
                wafersProcessed: 0,
                utilizationPercent: 0
            },
            states: {
                idle: {
                    entry: ['logIdle', 'notifyMasterAvailable', 'collectIdleMetrics'],
                    on: {
                        PROCESS_JOB: {
                            target: 'checkingPrerequisites',
                            actions: ['storeJobInfo', 'createSubstrateTracking']
                        }
                    }
                },
                checkingPrerequisites: {
                    entry: ['logCheckingPrereqs', 'collectPrereqMetrics'],
                    always: [
                        {
                            target: 'loading',
                            cond: 'hasAdequateConsumables'
                        },
                        {
                            target: 'requestingConsumables'
                        }
                    ]
                },
                requestingConsumables: {
                    entry: ['requestConsumableRefill', 'collectConsumableMetrics'],
                    on: {
                        CONSUMABLES_REFILLED: 'checkingPrerequisites'
                    },
                    after: {
                        '3000': 'checkingPrerequisites'
                    }
                },
                loading: {
                    entry: ['logLoading', 'trackSubstrateLoading'],
                    after: {
                        '1000': 'processing'
                    }
                },
                processing: {
                    entry: ['logProcessing', 'trackSubstrateProcessing', 'recordProcessStart'],
                    invoke: {
                        src: 'cmpProcess',
                        onDone: {
                            target: 'unloading',
                            actions: ['updateConsumables', 'recordCycleTime']
                        },
                        onError: {
                            target: 'error',
                            actions: ['logProcessError', 'trackSubstrateError']
                        }
                    }
                },
                unloading: {
                    entry: ['logUnloading', 'incrementWaferCount', 'trackSubstrateUnloading'],
                    after: {
                        '800': 'reportingComplete'
                    }
                },
                reportingComplete: {
                    entry: ['reportCompletionToMaster', 'trackSubstrateComplete', 'collectCompletionMetrics'],
                    always: [
                        {
                            target: 'maintenance',
                            cond: 'isMaintenanceDue'
                        },
                        {
                            target: 'idle'
                        }
                    ]
                },
                maintenance: {
                    entry: ['logMaintenance', 'notifyMasterUnavailable', 'collectMaintenanceMetrics'],
                    invoke: {
                        src: 'performMaintenance',
                        onDone: {
                            target: 'idle',
                            actions: ['resetMaintenanceCounters']
                        }
                    }
                },
                error: {
                    entry: ['logError', 'reportErrorToMaster', 'collectErrorMetrics'],
                    on: {
                        RESET: 'idle'
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logIdle"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] 💤 Tool idle and ready"),

            ["notifyMasterAvailable"] = (ctx) =>
            {
                ctx.RequestSend("MASTER_SCHEDULER_001", "TOOL_AVAILABLE", new JObject
                {
                    ["toolId"] = _toolId,
                    ["slurryLevel"] = _slurryLevel,
                    ["padWear"] = _padWear,
                    ["wafersUntilPM"] = _wafersBetweenPM - _wafersProcessed
                });
            },

            ["collectIdleMetrics"] = async (ctx) =>
            {
                await _dataCollectionManager.CollectDataAsync("TOOL_STATE", new Dictionary<string, object>
                {
                    ["State"] = "IDLE",
                    ["Timestamp"] = DateTime.UtcNow,
                    ["WafersProcessed"] = _wafersProcessed
                });
            },

            ["storeJobInfo"] = (ctx) =>
            {
                _currentJobId = $"JOB_{DateTime.Now:HHmmss}";
                _currentWaferId = $"W{DateTime.Now:HHmmss}";
                Console.WriteLine($"[{_toolId}] 📥 Accepted job: {_currentJobId}, Wafer: {_currentWaferId}");
            },

            ["createSubstrateTracking"] = async (ctx) =>
            {
                if (_currentWaferId == null) return;

                // Register substrate with E90 Substrate Tracker
                var substrate = await _substrateTracker.RegisterSubstrateAsync(
                    _currentWaferId,
                    lotId: _currentJobId ?? "UNKNOWN");

                await _substrateTracker.UpdateLocationAsync(_currentWaferId, "LoadPort", SubstrateLocationType.LoadPort);

                _substrateTracking[_currentWaferId] = substrate;

                Console.WriteLine($"[{_toolId}] ✅ E90 Substrate tracking started for {_currentWaferId}");
            },

            ["logCheckingPrereqs"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] 🔍 Checking: Slurry={_slurryLevel:F1}%, Pad={_padWear:F1}%"),

            ["collectPrereqMetrics"] = async (ctx) =>
            {
                await _dataCollectionManager.CollectDataAsync("CONSUMABLES", new Dictionary<string, object>
                {
                    ["SlurryLevel"] = _slurryLevel,
                    ["PadWear"] = _padWear,
                    ["WafersUntilPM"] = _wafersBetweenPM - _wafersProcessed
                });

                // E39 metrics tracked via state machine events
            },

            ["requestConsumableRefill"] = (ctx) =>
            {
                Console.WriteLine($"[{_toolId}] ⚠️ Low consumables - auto-refill");
                _slurryLevel = 100.0;
            },

            ["collectConsumableMetrics"] = async (ctx) =>
            {
                await _dataCollectionManager.CollectDataAsync("CONSUMABLE_REFILL", new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTime.UtcNow,
                    ["SlurryLevelAfter"] = _slurryLevel
                });
            },

            ["logLoading"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] 🔄 Loading wafer {_currentWaferId}..."),

            ["trackSubstrateLoading"] = async (ctx) =>
            {
                if (_currentWaferId != null && _substrateTracking.ContainsKey(_currentWaferId))
                {
                    await _substrateTracker.UpdateLocationAsync(_currentWaferId, "Process Chamber", SubstrateLocationType.ProcessModule);
                }
            },

            ["logProcessing"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] 💎 CMP processing - Job: {_currentJobId}"),

            ["trackSubstrateProcessing"] = async (ctx) =>
            {
                if (_currentWaferId != null && _substrateTracking.ContainsKey(_currentWaferId))
                {
                    await _substrateTracker.StartProcessingAsync(_currentWaferId, "CMP_STANDARD_01");
                }
            },

            ["recordProcessStart"] = (ctx) =>
            {
                _lastProcessStart = DateTime.UtcNow;
            },

            ["updateConsumables"] = (ctx) =>
            {
                _slurryLevel -= Random.Shared.NextDouble() * 2 + 1;
                _padWear += Random.Shared.NextDouble() * 0.5 + 0.1;
                Console.WriteLine($"[{_toolId}] 📊 Consumables: Slurry={_slurryLevel:F1}%, Pad={_padWear:F1}%");
            },

            ["recordCycleTime"] = (ctx) =>
            {
                var cycleTime = (DateTime.UtcNow - _lastProcessStart).TotalSeconds;
                _cycleTimes.Add(cycleTime);

                // Keep last 100 cycle times
                if (_cycleTimes.Count > 100)
                    _cycleTimes.RemoveAt(0);
            },

            ["logUnloading"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] 🔄 Unloading wafer..."),

            ["incrementWaferCount"] = (ctx) =>
            {
                _wafersProcessed++;
                Console.WriteLine($"[{_toolId}] ✅ Wafer complete - Total: {_wafersProcessed}/{_wafersBetweenPM}");
            },

            ["trackSubstrateUnloading"] = async (ctx) =>
            {
                if (_currentWaferId != null && _substrateTracking.ContainsKey(_currentWaferId))
                {
                    await _substrateTracker.CompleteProcessingAsync(_currentWaferId, success: true);
                    await _substrateTracker.UpdateLocationAsync(_currentWaferId, "Unload Port", SubstrateLocationType.LoadPort);
                }
            },

            ["reportCompletionToMaster"] = (ctx) =>
            {
                var cycleTime = _cycleTimes.Any() ? _cycleTimes.Last() : 0;

                ctx.RequestSend("MASTER_SCHEDULER_001", "JOB_COMPLETED", new JObject
                {
                    ["toolId"] = _toolId,
                    ["jobId"] = _currentJobId,
                    ["waferId"] = _currentWaferId,
                    ["processingTime"] = cycleTime * 1000
                });
            },

            ["trackSubstrateComplete"] = async (ctx) =>
            {
                if (_currentWaferId != null && _substrateTracking.ContainsKey(_currentWaferId))
                {
                    await _substrateTracker.RemoveSubstrateAsync(_currentWaferId);
                    Console.WriteLine($"[{_toolId}] ✅ E90 Substrate {_currentWaferId} released");
                }
            },

            ["collectCompletionMetrics"] = async (ctx) =>
            {
                var avgCycleTime = _cycleTimes.Any() ? _cycleTimes.Average() : 0;

                await _dataCollectionManager.CollectDataAsync("WAFER_COMPLETION", new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTime.UtcNow,
                    ["WaferId"] = _currentWaferId ?? "UNKNOWN",
                    ["CycleTime"] = _cycleTimes.Any() ? _cycleTimes.Last() : 0,
                    ["AvgCycleTime"] = avgCycleTime,
                    ["TotalWafers"] = _wafersProcessed
                });

                // E39 metrics tracked via state machine events
            },

            ["logMaintenance"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] 🔧 PM started"),

            ["notifyMasterUnavailable"] = (ctx) =>
            {
                ctx.RequestSend("MASTER_SCHEDULER_001", "TOOL_STATUS_UPDATE", new JObject
                {
                    ["toolId"] = _toolId,
                    ["status"] = "MAINTENANCE",
                    ["estimatedDuration"] = 5000
                });
            },

            ["collectMaintenanceMetrics"] = async (ctx) =>
            {
                await _dataCollectionManager.CollectDataAsync("MAINTENANCE", new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTime.UtcNow,
                    ["WafersProcessedBeforePM"] = _wafersProcessed,
                    ["PadWearBeforePM"] = _padWear
                });
            },

            ["resetMaintenanceCounters"] = (ctx) =>
            {
                _wafersProcessed = 0;
                _padWear = 0;
                _slurryLevel = 100;
                Console.WriteLine($"[{_toolId}] ✅ PM complete - Counters reset");
            },

            ["logProcessError"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] ❌ Process error"),

            ["trackSubstrateError"] = async (ctx) =>
            {
                if (_currentWaferId != null && _substrateTracking.ContainsKey(_currentWaferId))
                {
                    await _substrateTracker.CompleteProcessingAsync(_currentWaferId, success: false);
                }
            },

            ["logError"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] ❌ Error state"),

            ["reportErrorToMaster"] = (ctx) =>
            {
                ctx.RequestSend("MASTER_SCHEDULER_001", "TOOL_ERROR", new JObject
                {
                    ["toolId"] = _toolId,
                    ["jobId"] = _currentJobId
                });
            },

            ["collectErrorMetrics"] = async (ctx) =>
            {
                await _dataCollectionManager.CollectDataAsync("TOOL_ERROR", new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTime.UtcNow,
                    ["JobId"] = _currentJobId ?? "UNKNOWN",
                    ["WaferId"] = _currentWaferId ?? "UNKNOWN"
                });
            }
        };

        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["hasAdequateConsumables"] = (sm) =>
            {
                return _slurryLevel > 10 && _padWear < 80;
            },

            ["isMaintenanceDue"] = (sm) =>
            {
                return _wafersProcessed >= _wafersBetweenPM || _padWear >= 80;
            }
        };

        var services = new Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>
        {
            ["cmpProcess"] = async (sm, ct) =>
            {
                Console.WriteLine($"[{_toolId}] [SERVICE] CMP process started");

                var phases = new[] { "RampUp", "Polishing", "RampDown", "Cleaning" };
                var phaseTimes = new[] { 500, 2000, 400, 500 };

                for (int i = 0; i < phases.Length; i++)
                {
                    if (ct.IsCancellationRequested) break;
                    Console.WriteLine($"[{_toolId}] [SERVICE] Phase: {phases[i]}");
                    await Task.Delay(phaseTimes[i], ct);
                }

                Console.WriteLine($"[{_toolId}] [SERVICE] CMP process complete");
                return new { status = "SUCCESS", duration = 3400 };
            },

            ["performMaintenance"] = async (sm, ct) =>
            {
                Console.WriteLine($"[{_toolId}] [SERVICE] PM: Replacing pad");
                await Task.Delay(3000, ct);
                Console.WriteLine($"[{_toolId}] [SERVICE] PM: Complete");
                return new { status = "PM_COMPLETE" };
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: guards,
            services: services,
            enableGuidIsolation: false
        );
    }

    private void InitializeDataCollection()
    {
        Task.Run(async () =>
        {
            await _dataCollectionManager.CreatePlanAsync(
                "TOOL_STATE",
                new[] { "State", "Timestamp", "WafersProcessed" },
                CollectionTrigger.StateChange);

            await _dataCollectionManager.CreatePlanAsync(
                "CONSUMABLES",
                new[] { "SlurryLevel", "PadWear", "WafersUntilPM" },
                CollectionTrigger.Event);

            await _dataCollectionManager.CreatePlanAsync(
                "WAFER_COMPLETION",
                new[] { "Timestamp", "WaferId", "CycleTime", "AvgCycleTime", "TotalWafers" },
                CollectionTrigger.Event);

            Console.WriteLine($"[{_toolId}] ✅ E134 Data Collection plans initialized");
        }).Wait();
    }

    private void InitializeMetrics()
    {
        Task.Run(async () =>
        {
            await _metricsManager.StartAsync();
            await _metricsManager.ScheduleAsync();

            Console.WriteLine($"[{_toolId}] ✅ E39 Equipment Metrics initialized");
        }).Wait();
    }

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public string GetCurrentState() => _machine.CurrentState;
    public int GetWafersProcessed() => _wafersProcessed;
    public double GetSlurryLevel() => _slurryLevel;
    public double GetPadWear() => _padWear;
    public string? GetCurrentWaferId() => _currentWaferId;
    public string? GetCurrentJobId() => _currentJobId;
    public bool HasWafer() => _currentWaferId != null;
    public double GetAvgCycleTime() => _cycleTimes.Any() ? _cycleTimes.Average() : 0;

    public IEnumerable<DataReport> GetReports(string planId, DateTime? since = null)
    {
        return _dataCollectionManager.GetReports(planId, since);
    }
}
