using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// E157 Module Process Tracking Machine - SEMI E157 Standard
/// Tracks material processing through modules with step-level granularity
/// Refactored to use ExtendedPureStateMachineFactory with EventBusOrchestrator
/// </summary>
public class E157ModuleProcessTrackingMachine
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ConcurrentDictionary<string, ModuleTracker> _modules = new();

    public string MachineId => $"E157_MODULE_TRACKING_{_equipmentId}";

    public E157ModuleProcessTrackingMachine(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Register a module for tracking
    /// </summary>
    public async Task<ModuleTracker> RegisterModuleAsync(string moduleId)
    {
        if (_modules.ContainsKey(moduleId))
        {
            return _modules[moduleId];
        }

        var module = new ModuleTracker(moduleId, _equipmentId, _orchestrator);
        _modules[moduleId] = module;

        await module.StartAsync();

        return module;
    }

    /// <summary>
    /// Get module tracker
    /// </summary>
    public ModuleTracker? GetModule(string moduleId)
    {
        return _modules.TryGetValue(moduleId, out var module) ? module : null;
    }

    /// <summary>
    /// Get all modules
    /// </summary>
    public IEnumerable<ModuleTracker> GetAllModules()
    {
        return _modules.Values;
    }

    /// <summary>
    /// Get modules processing material
    /// </summary>
    public IEnumerable<ModuleTracker> GetActiveModules()
    {
        return _modules.Values.Where(m => m.CurrentMaterialId != null);
    }
}

/// <summary>
/// Individual module tracker with hierarchical state machine
/// </summary>
public class ModuleTracker
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly string _instanceId;
    private readonly List<ProcessStep> _processHistory = new();

    public string ModuleId { get; }
    public string? CurrentMaterialId { get; set; }
    public string? CurrentRecipeStep { get; set; }
    public DateTime? ProcessStartTime { get; set; }
    public int StepCount { get; set; }
    public int ErrorCount { get; set; }

    public string MachineId => $"E157_MODULE_{ModuleId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public ModuleTracker(string moduleId, string equipmentId, EventBusOrchestrator orchestrator)
    {
        ModuleId = moduleId;
        _orchestrator = orchestrator;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Inline XState JSON definition with hierarchical states
        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'Idle',
            context: {
                moduleId: '',
                materialId: null,
                processStartTime: null,
                stepCount: 0,
                errorCount: 0
            },
            states: {
                Idle: {
                    entry: 'logIdle',
                    on: {
                        MATERIAL_ARRIVE: {
                            target: 'MaterialArrived',
                            actions: 'recordMaterialArrival'
                        }
                    }
                },
                MaterialArrived: {
                    entry: 'logMaterialArrived',
                    on: {
                        START_PRE_PROCESS: {
                            target: 'PreProcessing',
                            actions: 'startPreProcess'
                        },
                        SKIP_PRE_PROCESS: {
                            target: 'Processing',
                            actions: 'startMainProcess'
                        },
                        MATERIAL_REMOVE: {
                            target: 'Idle',
                            actions: 'clearMaterial'
                        }
                    }
                },
                PreProcessing: {
                    entry: 'logPreProcessing',
                    on: {
                        PRE_PROCESS_COMPLETE: {
                            target: 'Processing',
                            actions: ['recordPreProcessResults', 'startMainProcess']
                        },
                        PRE_PROCESS_ERROR: {
                            target: 'Error',
                            actions: ['recordError', 'notifyError']
                        },
                        ABORT: {
                            target: 'Idle',
                            actions: 'abortAndClear'
                        }
                    }
                },
                Processing: {
                    entry: 'logProcessing',
                    on: {
                        PROCESS_COMPLETE: {
                            target: 'PostProcessing',
                            actions: 'recordProcessResults'
                        },
                        SKIP_POST_PROCESS: {
                            target: 'MaterialComplete',
                            actions: ['recordProcessResults', 'finalizeMaterialProcessing']
                        },
                        PROCESS_ERROR: {
                            target: 'Error',
                            actions: ['recordError', 'notifyError']
                        },
                        ABORT: {
                            target: 'Idle',
                            actions: 'abortAndClear'
                        }
                    }
                },
                PostProcessing: {
                    entry: 'logPostProcessing',
                    on: {
                        POST_PROCESS_COMPLETE: {
                            target: 'MaterialComplete',
                            actions: ['recordPostProcessResults', 'finalizeMaterialProcessing']
                        },
                        POST_PROCESS_ERROR: {
                            target: 'Error',
                            actions: ['recordError', 'notifyError']
                        },
                        ABORT: {
                            target: 'Idle',
                            actions: 'abortAndClear'
                        }
                    }
                },
                MaterialComplete: {
                    entry: 'logMaterialComplete',
                    on: {
                        MATERIAL_REMOVE: {
                            target: 'Idle',
                            actions: 'clearMaterial'
                        },
                        NEXT_MATERIAL: {
                            target: 'MaterialArrived',
                            actions: 'recordMaterialArrival'
                        }
                    }
                },
                Error: {
                    entry: 'logError',
                    on: {
                        ERROR_CLEAR: {
                            target: 'Idle',
                            actions: 'clearMaterial'
                        },
                        ABORT: {
                            target: 'Idle',
                            actions: 'abortAndClear'
                        }
                    }
                },
                Maintenance: {
                    entry: 'startMaintenance',
                    on: {
                        MAINTENANCE_COMPLETE: {
                            target: 'Idle',
                            actions: 'endMaintenance'
                        }
                    }
                }
            }
        }
        """;

        // Orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logIdle"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 💤 Module idle and ready");
            },

            ["recordMaterialArrival"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📦 Material {CurrentMaterialId} arrived for step '{CurrentRecipeStep}'");

                ctx.RequestSend("E90_TRACKING", "MATERIAL_AT_MODULE", new JObject
                {
                    ["moduleId"] = ModuleId,
                    ["materialId"] = CurrentMaterialId,
                    ["recipeStep"] = CurrentRecipeStep
                });
            },

            ["logMaterialArrived"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Material ready for processing");
            },

            ["startPreProcess"] = (ctx) =>
            {
                var step = new ProcessStep
                {
                    MaterialId = CurrentMaterialId ?? "",
                    StepName = "PreProcess",
                    StartTime = DateTime.UtcNow
                };
                _processHistory.Add(step);
                StepCount++;

                Console.WriteLine($"[{MachineId}] 🔧 Pre-process started");
            },

            ["logPreProcessing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⚙️ Pre-processing...");
            },

            ["startMainProcess"] = (ctx) =>
            {
                ProcessStartTime = DateTime.UtcNow;
                var step = new ProcessStep
                {
                    MaterialId = CurrentMaterialId ?? "",
                    StepName = CurrentRecipeStep ?? "MainProcess",
                    StartTime = ProcessStartTime.Value
                };
                _processHistory.Add(step);
                StepCount++;

                Console.WriteLine($"[{MachineId}] 🚀 Main process '{CurrentRecipeStep}' started");

                ctx.RequestSend("E40_PROCESS_JOB", "MODULE_PROCESSING_STARTED", new JObject
                {
                    ["moduleId"] = ModuleId,
                    ["materialId"] = CurrentMaterialId,
                    ["recipeStep"] = CurrentRecipeStep
                });
            },

            ["logProcessing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⚙️ Processing material {CurrentMaterialId}");
            },

            ["recordPreProcessResults"] = (ctx) =>
            {
                var lastStep = _processHistory.LastOrDefault();
                if (lastStep != null)
                {
                    lastStep.EndTime = DateTime.UtcNow;
                    Console.WriteLine($"[{MachineId}] ✅ Pre-process complete ({(lastStep.EndTime.Value - lastStep.StartTime).TotalSeconds:F1}s)");
                }
            },

            ["recordProcessResults"] = (ctx) =>
            {
                var lastStep = _processHistory.LastOrDefault();
                if (lastStep != null)
                {
                    lastStep.EndTime = DateTime.UtcNow;
                    Console.WriteLine($"[{MachineId}] ✅ Process complete ({(lastStep.EndTime.Value - lastStep.StartTime).TotalSeconds:F1}s)");
                }

                ctx.RequestSend("E40_PROCESS_JOB", "MODULE_PROCESSING_COMPLETE", new JObject
                {
                    ["moduleId"] = ModuleId,
                    ["materialId"] = CurrentMaterialId
                });
            },

            ["logPostProcessing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⚙️ Post-processing...");
            },

            ["recordPostProcessResults"] = (ctx) =>
            {
                var lastStep = _processHistory.LastOrDefault();
                if (lastStep != null)
                {
                    lastStep.EndTime = DateTime.UtcNow;
                    Console.WriteLine($"[{MachineId}] ✅ Post-process complete ({(lastStep.EndTime.Value - lastStep.StartTime).TotalSeconds:F1}s)");
                }
            },

            ["finalizeMaterialProcessing"] = (ctx) =>
            {
                var totalTime = _processHistory
                    .Where(s => s.MaterialId == CurrentMaterialId && s.EndTime.HasValue)
                    .Sum(s => (s.EndTime!.Value - s.StartTime).TotalSeconds);

                Console.WriteLine($"[{MachineId}] 🎯 Material {CurrentMaterialId} complete - Total: {totalTime:F1}s, Steps: {StepCount}");

                ctx.RequestSend("E90_TRACKING", "MATERIAL_PROCESSING_COMPLETE", new JObject
                {
                    ["moduleId"] = ModuleId,
                    ["materialId"] = CurrentMaterialId,
                    ["totalTime"] = totalTime,
                    ["stepCount"] = StepCount
                });
            },

            ["logMaterialComplete"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Material processing complete, ready for removal");
            },

            ["clearMaterial"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🗑️ Material {CurrentMaterialId} cleared");
                CurrentMaterialId = null;
                CurrentRecipeStep = null;
                StepCount = 0;
            },

            ["recordError"] = (ctx) =>
            {
                var lastStep = _processHistory.LastOrDefault();
                if (lastStep != null)
                {
                    lastStep.EndTime = DateTime.UtcNow;
                    Console.WriteLine($"[{MachineId}] ❌ Error in step '{lastStep.StepName}'");
                }
            },

            ["notifyError"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🚨 Error notification sent");

                ctx.RequestSend("E40_PROCESS_JOB", "MODULE_ERROR", new JObject
                {
                    ["moduleId"] = ModuleId,
                    ["materialId"] = CurrentMaterialId,
                    ["errorCount"] = ErrorCount
                });
            },

            ["logError"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⚠️ Module in error state");
            },

            ["abortAndClear"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🛑 Aborting and clearing material {CurrentMaterialId}");
                CurrentMaterialId = null;
                CurrentRecipeStep = null;
                StepCount = 0;
            },

            ["startMaintenance"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔧 Starting maintenance");
            },

            ["endMaintenance"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Maintenance complete");
            }
        };

        // Guards
        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["materialPresent"] = (sm) => CurrentMaterialId != null,
            ["noMaterialPresent"] = (sm) => CurrentMaterialId == null
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: guards
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

    // Public API methods
    public async Task<EventResult> MaterialArriveAsync(string materialId, string recipeStep)
    {
        CurrentMaterialId = materialId;
        CurrentRecipeStep = recipeStep;
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "MATERIAL_ARRIVE", null);
        return result;
    }

    public async Task<EventResult> StartPreProcessAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "START_PRE_PROCESS", null);
        return result;
    }

    public async Task<EventResult> SkipPreProcessAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SKIP_PRE_PROCESS", null);
        return result;
    }

    public async Task<EventResult> PreProcessCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PRE_PROCESS_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> ProcessCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESS_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> SkipPostProcessAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SKIP_POST_PROCESS", null);
        return result;
    }

    public async Task<EventResult> PostProcessCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "POST_PROCESS_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> MaterialRemoveAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "MATERIAL_REMOVE", null);
        return result;
    }

    public async Task<EventResult> NextMaterialAsync(string materialId, string recipeStep)
    {
        CurrentMaterialId = materialId;
        CurrentRecipeStep = recipeStep;
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "NEXT_MATERIAL", null);
        return result;
    }

    public async Task<EventResult> ReportErrorAsync(string errorType)
    {
        ErrorCount++;
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, errorType, null);
        return result;
    }

    public async Task<EventResult> ClearErrorAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ERROR_CLEAR", null);
        return result;
    }

    public async Task<EventResult> AbortAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ABORT", null);
        return result;
    }

    public async Task<EventResult> StartMaintenanceAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "MAINTENANCE_START", null);
        return result;
    }

    public async Task<EventResult> MaintenanceCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "MAINTENANCE_COMPLETE", null);
        return result;
    }

    public ProcessReport GenerateProcessReport(string materialId)
    {
        var steps = _processHistory.Where(s => s.MaterialId == materialId).ToList();

        return new ProcessReport
        {
            ModuleId = ModuleId,
            MaterialId = materialId,
            Steps = steps,
            TotalProcessTime = steps
                .Where(s => s.EndTime.HasValue)
                .Sum(s => (s.EndTime!.Value - s.StartTime).TotalSeconds),
            Success = steps.All(s => string.IsNullOrEmpty(s.ErrorCode))
        };
    }

    public IEnumerable<ProcessStep> GetMaterialHistory(string materialId)
    {
        return _processHistory.Where(s => s.MaterialId == materialId);
    }
}

// Data classes
public class ProcessStep
{
    public string MaterialId { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public ConcurrentDictionary<string, object> Parameters { get; set; } = new();
    public ConcurrentDictionary<string, object> Results { get; set; } = new();
    public string? ErrorCode { get; set; }
}

public class ProcessReport
{
    public string ModuleId { get; set; } = string.Empty;
    public string MaterialId { get; set; } = string.Empty;
    public List<ProcessStep> Steps { get; set; } = new();
    public double TotalProcessTime { get; set; }
    public bool Success { get; set; }
}
