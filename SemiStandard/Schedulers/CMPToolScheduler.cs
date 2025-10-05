using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Schedulers;

/// <summary>
/// CMP Tool Scheduler - Manages individual CMP tool operation
/// Implements: Resource management, PM scheduling, consumable tracking
/// </summary>
public class CMPToolScheduler
{
    private readonly string _toolId;
    private readonly string _instanceId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    // Tool state
    private string? _currentJobId;
    private string? _currentWaferId;
    private double _slurryLevel = 100.0;
    private double _padWear = 0.0;
    private int _wafersProcessed = 0;
    private readonly int _wafersBetweenPM = 50; // PM every 50 wafers for demo

    public string MachineId => $"{_toolId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public CMPToolScheduler(string toolId, EventBusOrchestrator orchestrator)
    {
        _toolId = toolId;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
        _orchestrator = orchestrator;

        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'idle',
            context: {
                slurryLevel: 100,
                padWear: 0,
                wafersProcessed: 0
            },
            states: {
                idle: {
                    entry: ['logIdle', 'notifyMasterAvailable'],
                    on: {
                        PROCESS_JOB: {
                            target: 'checkingPrerequisites',
                            actions: ['storeJobInfo']
                        }
                    }
                },
                checkingPrerequisites: {
                    entry: ['logCheckingPrereqs'],
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
                    entry: ['requestConsumableRefill'],
                    on: {
                        CONSUMABLES_REFILLED: 'checkingPrerequisites'
                    },
                    after: {
                        '3000': 'checkingPrerequisites'
                    }
                },
                loading: {
                    entry: ['logLoading'],
                    after: {
                        '1000': 'processing'
                    }
                },
                processing: {
                    entry: ['logProcessing'],
                    invoke: {
                        src: 'cmpProcess',
                        onDone: {
                            target: 'unloading',
                            actions: ['updateConsumables']
                        },
                        onError: {
                            target: 'error',
                            actions: ['logProcessError']
                        }
                    }
                },
                unloading: {
                    entry: ['logUnloading', 'incrementWaferCount'],
                    after: {
                        '800': 'reportingComplete'
                    }
                },
                reportingComplete: {
                    entry: ['reportCompletionToMaster'],
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
                    entry: ['logMaintenance', 'notifyMasterUnavailable'],
                    invoke: {
                        src: 'performMaintenance',
                        onDone: {
                            target: 'idle',
                            actions: ['resetMaintenanceCounters']
                        }
                    }
                },
                error: {
                    entry: ['logError', 'reportErrorToMaster'],
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

            ["storeJobInfo"] = (ctx) =>
            {
                _currentJobId = $"JOB_{DateTime.Now:HHmmss}";
                _currentWaferId = $"W{DateTime.Now:HHmmss}";
                Console.WriteLine($"[{_toolId}] 📥 Accepted job: {_currentJobId}");
            },

            ["logCheckingPrereqs"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] 🔍 Checking consumables: Slurry={_slurryLevel:F1}%, Pad Wear={_padWear:F1}%"),

            ["requestConsumableRefill"] = (ctx) =>
            {
                Console.WriteLine($"[{_toolId}] ⚠️ Low consumables - simulating auto-refill");
                // Simulate refill
                _slurryLevel = 100.0;
            },

            ["logLoading"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] 🔄 Loading wafer {_currentWaferId}..."),

            ["logProcessing"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] 💎 CMP processing started - Job: {_currentJobId}"),

            ["updateConsumables"] = (ctx) =>
            {
                _slurryLevel -= Random.Shared.NextDouble() * 2 + 1; // 1-3% per wafer
                _padWear += Random.Shared.NextDouble() * 0.5 + 0.1; // 0.1-0.6% per wafer
                Console.WriteLine($"[{_toolId}] 📊 Consumables: Slurry={_slurryLevel:F1}%, Pad Wear={_padWear:F1}%");
            },

            ["logUnloading"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] 🔄 Unloading wafer..."),

            ["incrementWaferCount"] = (ctx) =>
            {
                _wafersProcessed++;
                Console.WriteLine($"[{_toolId}] ✅ Wafer complete - Total: {_wafersProcessed}/{_wafersBetweenPM}");
            },

            ["reportCompletionToMaster"] = (ctx) =>
            {
                ctx.RequestSend("MASTER_SCHEDULER_001", "JOB_COMPLETED", new JObject
                {
                    ["toolId"] = _toolId,
                    ["jobId"] = _currentJobId,
                    ["waferId"] = _currentWaferId,
                    ["processingTime"] = 13500 // ms
                });
                Console.WriteLine($"[{_toolId}] 📤 Reported completion to master scheduler");
            },

            ["logMaintenance"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] 🔧 PM (Preventive Maintenance) started"),

            ["notifyMasterUnavailable"] = (ctx) =>
            {
                ctx.RequestSend("MASTER_SCHEDULER_001", "TOOL_STATUS_UPDATE", new JObject
                {
                    ["toolId"] = _toolId,
                    ["status"] = "MAINTENANCE",
                    ["estimatedDuration"] = 5000 // 5 seconds for demo
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
                Console.WriteLine($"[{_toolId}] ❌ Process error occurred"),

            ["logError"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] ❌ Error state - Tool unavailable"),

            ["reportErrorToMaster"] = (ctx) =>
            {
                ctx.RequestSend("MASTER_SCHEDULER_001", "TOOL_ERROR", new JObject
                {
                    ["toolId"] = _toolId,
                    ["jobId"] = _currentJobId,
                    ["errorType"] = "PROCESS_ERROR"
                });
            }
        };

        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["hasAdequateConsumables"] = (sm) =>
            {
                var adequate = _slurryLevel > 10 && _padWear < 80;
                Console.WriteLine($"[{_toolId}] [GUARD] ConsumablesOK={adequate}");
                return adequate;
            },

            ["isMaintenanceDue"] = (sm) =>
            {
                var due = _wafersProcessed >= _wafersBetweenPM || _padWear >= 80;
                if (due)
                {
                    Console.WriteLine($"[{_toolId}] [GUARD] MaintenanceDue={due} (Wafers: {_wafersProcessed}/{_wafersBetweenPM}, Pad: {_padWear:F1}%)");
                }
                return due;
            }
        };

        var services = new Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>
        {
            ["cmpProcess"] = async (sm, ct) =>
            {
                Console.WriteLine($"[{_toolId}] [SERVICE] CMP process started");

                var phases = new[] { "RampUp", "Polishing", "RampDown", "Cleaning" };
                var phaseTimes = new[] { 500, 2000, 400, 500 }; // Faster for demo

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
                Console.WriteLine($"[{_toolId}] [SERVICE] PM: Replacing pad and refilling slurry");
                await Task.Delay(3000, ct); // Simulate PM time
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
            enableGuidIsolation: false  // Already has GUID suffix in MachineId
        );
    }

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public string GetCurrentState()
    {
        return _machine.CurrentState;
    }

    public int GetWafersProcessed() => _wafersProcessed;
    public double GetSlurryLevel() => _slurryLevel;
    public double GetPadWear() => _padWear;
}