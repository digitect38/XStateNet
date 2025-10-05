using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// E94 Control Job Machine - SEMI E94 Standard
/// Manages control job lifecycle coordinating carrier management, substrate tracking, and process execution
/// Refactored to use ExtendedPureStateMachineFactory with EventBusOrchestrator
/// </summary>
public class E94ControlJobManager
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ConcurrentDictionary<string, ControlJobMachine> _controlJobs = new();

    public string MachineId => $"E94_CONTROLJOB_MGR_{_equipmentId}";

    public E94ControlJobManager(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Create and register a control job
    /// </summary>
    public async Task<ControlJobMachine> CreateControlJobAsync(string jobId, List<string> carrierIds, string? recipeId = null)
    {
        if (_controlJobs.ContainsKey(jobId))
        {
            return _controlJobs[jobId];
        }

        var controlJob = new ControlJobMachine(jobId, carrierIds, recipeId, _equipmentId, _orchestrator);
        _controlJobs[jobId] = controlJob;

        await controlJob.StartAsync();

        return controlJob;
    }

    /// <summary>
    /// Get control job
    /// </summary>
    public ControlJobMachine? GetControlJob(string jobId)
    {
        return _controlJobs.TryGetValue(jobId, out var job) ? job : null;
    }

    /// <summary>
    /// Get all control jobs
    /// </summary>
    public IEnumerable<ControlJobMachine> GetAllControlJobs()
    {
        return _controlJobs.Values;
    }

    /// <summary>
    /// Get selected control job
    /// </summary>
    public ControlJobMachine? GetSelectedControlJob()
    {
        return _controlJobs.Values.FirstOrDefault(j => j.GetCurrentState().Contains("selected"));
    }

    /// <summary>
    /// Get executing control jobs
    /// </summary>
    public IEnumerable<ControlJobMachine> GetExecutingJobs()
    {
        return _controlJobs.Values.Where(j => j.GetCurrentState().Contains("executing"));
    }

    /// <summary>
    /// Delete control job
    /// </summary>
    public async Task<bool> DeleteControlJobAsync(string jobId)
    {
        if (_controlJobs.TryRemove(jobId, out var job))
        {
            await job.DeleteAsync();
            return true;
        }
        return false;
    }
}

/// <summary>
/// Individual control job state machine using orchestrator
/// </summary>
public class ControlJobMachine
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly string _instanceId;

    public string JobId { get; }
    public List<string> CarrierIds { get; }
    public string? RecipeId { get; set; }
    public DateTime CreatedTime { get; }
    public DateTime? StartedTime { get; set; }
    public DateTime? CompletedTime { get; set; }
    public List<string> ProcessedSubstrates { get; }
    public ConcurrentDictionary<string, object> Properties { get; }

    public string MachineId => $"E94_CONTROLJOB_{JobId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public ControlJobMachine(string jobId, List<string> carrierIds, string? recipeId, string equipmentId, EventBusOrchestrator orchestrator)
    {
        JobId = jobId;
        CarrierIds = carrierIds;
        RecipeId = recipeId;
        CreatedTime = DateTime.UtcNow;
        ProcessedSubstrates = new List<string>();
        Properties = new ConcurrentDictionary<string, object>();
        _orchestrator = orchestrator;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Inline XState JSON definition with unique ID per control job
        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'noJob',
            states: {
                noJob: {
                    entry: 'logNoJob',
                    on: {
                        CREATE: 'queued'
                    }
                },
                queued: {
                    entry: 'logQueued',
                    on: {
                        SELECT: 'selected',
                        DELETE: 'noJob',
                        ABORT: 'noJob'
                    }
                },
                selected: {
                    entry: 'logSelected',
                    on: {
                        START: 'executing',
                        DESELECT: 'queued',
                        DELETE: 'noJob',
                        ABORT: 'aborting'
                    }
                },
                executing: {
                    type: 'parallel',
                    entry: 'logExecuting',
                    states: {
                        processing: {
                            initial: 'waitingForStart',
                            states: {
                                waitingForStart: {
                                    entry: 'logWaitingForProcessStart',
                                    on: {
                                        PROCESS_START: 'active'
                                    }
                                },
                                active: {
                                    entry: 'logProcessActive',
                                    on: {
                                        PROCESS_COMPLETE: 'completed',
                                        PAUSE: 'paused',
                                        STOP: 'stopping'
                                    }
                                },
                                paused: {
                                    entry: 'logProcessPaused',
                                    on: {
                                        RESUME: 'active',
                                        STOP: 'stopping',
                                        ABORT: 'aborted'
                                    }
                                },
                                stopping: {
                                    entry: 'logProcessStopping',
                                    on: {
                                        STOPPED: 'stopped'
                                    }
                                },
                                stopped: {
                                    entry: 'logProcessStopped',
                                    type: 'final'
                                },
                                completed: {
                                    entry: 'logProcessCompleted',
                                    type: 'final'
                                },
                                aborted: {
                                    entry: 'logProcessAborted',
                                    type: 'final'
                                }
                            }
                        },
                        materialFlow: {
                            initial: 'waitingForMaterial',
                            states: {
                                waitingForMaterial: {
                                    entry: 'logWaitingForMaterial',
                                    on: {
                                        MATERIAL_IN: 'materialAtSource'
                                    }
                                },
                                materialAtSource: {
                                    entry: 'logMaterialAtSource',
                                    on: {
                                        MATERIAL_OUT: 'materialAtDestination'
                                    }
                                },
                                materialAtDestination: {
                                    entry: 'logMaterialAtDestination',
                                    on: {
                                        MATERIAL_PROCESSED: 'materialComplete'
                                    }
                                },
                                materialComplete: {
                                    entry: 'logMaterialComplete',
                                    type: 'final'
                                }
                            }
                        }
                    },
                    onDone: {
                        target: 'completed'
                    },
                    on: {
                        ABORT: 'aborting',
                        PAUSE: 'paused',
                        STOP: 'stopping'
                    }
                },
                paused: {
                    entry: 'logPaused',
                    on: {
                        RESUME: 'executing',
                        ABORT: 'aborting',
                        STOP: 'stopping'
                    }
                },
                stopping: {
                    entry: 'logStopping',
                    on: {
                        STOPPED: 'stopped'
                    }
                },
                stopped: {
                    entry: 'logStopped',
                    on: {
                        DELETE: 'noJob'
                    }
                },
                aborting: {
                    entry: 'logAborting',
                    on: {
                        ABORTED: 'aborted'
                    }
                },
                aborted: {
                    entry: 'logAborted',
                    on: {
                        DELETE: 'noJob'
                    }
                },
                completed: {
                    entry: 'logCompleted',
                    on: {
                        DELETE: 'noJob'
                    }
                }
            }
        }
        """;

        // Orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logNoJob"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📋 No control job");
            },

            ["logQueued"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📬 Control job queued: Recipe={RecipeId}, Carriers={string.Join(",", CarrierIds)}");

                ctx.RequestSend("E42_RECIPE_MGMT", "CONTROLJOB_QUEUED", new JObject
                {
                    ["controlJobId"] = JobId,
                    ["recipeId"] = RecipeId
                });

                ctx.RequestSend("E87_CARRIER_MGMT", "CONTROLJOB_QUEUED", new JObject
                {
                    ["controlJobId"] = JobId,
                    ["carrierIds"] = new JArray(CarrierIds)
                });
            },

            ["logSelected"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Control job selected");

                ctx.RequestSend("E42_RECIPE_MGMT", "CONTROLJOB_SELECTED", new JObject
                {
                    ["controlJobId"] = JobId,
                    ["recipeId"] = RecipeId
                });

                ctx.RequestSend("HOST_SYSTEM", "CONTROLJOB_SELECTED", new JObject
                {
                    ["controlJobId"] = JobId,
                    ["carrierIds"] = new JArray(CarrierIds)
                });
            },

            ["logExecuting"] = (ctx) =>
            {
                StartedTime = DateTime.UtcNow;
                Console.WriteLine($"[{MachineId}] ▶️ Control job executing at {StartedTime}");

                ctx.RequestSend("E40_PROCESS_JOB", "CONTROLJOB_STARTED", new JObject
                {
                    ["controlJobId"] = JobId,
                    ["recipeId"] = RecipeId,
                    ["carrierIds"] = new JArray(CarrierIds)
                });

                ctx.RequestSend("E39_EQUIPMENT_METRICS", "JOB_STARTED", new JObject
                {
                    ["controlJobId"] = JobId
                });
            },

            ["logWaitingForProcessStart"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏳ Waiting for process to start...");
            },

            ["logProcessActive"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Process active");

                ctx.RequestSend("E40_PROCESS_JOB", "PROCESS_ACTIVE", new JObject
                {
                    ["controlJobId"] = JobId
                });
            },

            ["logProcessPaused"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏸️ Process paused");

                ctx.RequestSend("E40_PROCESS_JOB", "PROCESS_PAUSED", new JObject
                {
                    ["controlJobId"] = JobId
                });
            },

            ["logProcessStopping"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🛑 Process stopping...");
            },

            ["logProcessStopped"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🛑 Process stopped");

                ctx.RequestSend("E40_PROCESS_JOB", "PROCESS_STOPPED", new JObject
                {
                    ["controlJobId"] = JobId
                });
            },

            ["logProcessCompleted"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Process completed");

                ctx.RequestSend("E40_PROCESS_JOB", "PROCESS_COMPLETED", new JObject
                {
                    ["controlJobId"] = JobId
                });
            },

            ["logProcessAborted"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⚠️ Process aborted");

                ctx.RequestSend("E40_PROCESS_JOB", "PROCESS_ABORTED", new JObject
                {
                    ["controlJobId"] = JobId
                });
            },

            ["logWaitingForMaterial"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📦 Waiting for material...");

                ctx.RequestSend("E87_CARRIER_MGMT", "REQUEST_MATERIAL", new JObject
                {
                    ["controlJobId"] = JobId,
                    ["carrierIds"] = new JArray(CarrierIds)
                });
            },

            ["logMaterialAtSource"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📦 Material at source");

                ctx.RequestSend("E90_SUBSTRATE_TRACKING", "MATERIAL_AT_SOURCE", new JObject
                {
                    ["controlJobId"] = JobId
                });
            },

            ["logMaterialAtDestination"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📦 Material at destination");

                ctx.RequestSend("E90_SUBSTRATE_TRACKING", "MATERIAL_AT_DESTINATION", new JObject
                {
                    ["controlJobId"] = JobId
                });
            },

            ["logMaterialComplete"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Material flow complete");

                ctx.RequestSend("E90_SUBSTRATE_TRACKING", "MATERIAL_COMPLETE", new JObject
                {
                    ["controlJobId"] = JobId
                });
            },

            ["logPaused"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏸️ Control job paused");

                ctx.RequestSend("HOST_SYSTEM", "CONTROLJOB_PAUSED", new JObject
                {
                    ["controlJobId"] = JobId
                });
            },

            ["logStopping"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🛑 Control job stopping...");
            },

            ["logStopped"] = (ctx) =>
            {
                CompletedTime = DateTime.UtcNow;
                var duration = CompletedTime - StartedTime;
                Console.WriteLine($"[{MachineId}] 🛑 Control job stopped. Duration: {duration?.TotalSeconds:F1}s");

                ctx.RequestSend("HOST_SYSTEM", "CONTROLJOB_STOPPED", new JObject
                {
                    ["controlJobId"] = JobId,
                    ["duration"] = duration?.TotalSeconds
                });

                ctx.RequestSend("E39_EQUIPMENT_METRICS", "JOB_STOPPED", new JObject
                {
                    ["controlJobId"] = JobId
                });
            },

            ["logAborting"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⚠️ Control job aborting...");
            },

            ["logAborted"] = (ctx) =>
            {
                CompletedTime = DateTime.UtcNow;
                Console.WriteLine($"[{MachineId}] ⚠️ Control job aborted at {CompletedTime}");

                ctx.RequestSend("HOST_SYSTEM", "CONTROLJOB_ABORTED", new JObject
                {
                    ["controlJobId"] = JobId
                });

                ctx.RequestSend("E39_EQUIPMENT_METRICS", "JOB_ABORTED", new JObject
                {
                    ["controlJobId"] = JobId
                });

                ctx.RequestSend("E90_SUBSTRATE_TRACKING", "JOB_ABORTED", new JObject
                {
                    ["controlJobId"] = JobId
                });
            },

            ["logCompleted"] = (ctx) =>
            {
                CompletedTime = DateTime.UtcNow;
                var duration = CompletedTime - StartedTime;
                Console.WriteLine($"[{MachineId}] ✅ Control job completed. Duration: {duration?.TotalSeconds:F1}s");

                ctx.RequestSend("HOST_SYSTEM", "CONTROLJOB_COMPLETED", new JObject
                {
                    ["controlJobId"] = JobId,
                    ["processedSubstrates"] = ProcessedSubstrates.Count,
                    ["duration"] = duration?.TotalSeconds
                });

                ctx.RequestSend("E39_EQUIPMENT_METRICS", "JOB_COMPLETED", new JObject
                {
                    ["controlJobId"] = JobId,
                    ["processedSubstrates"] = ProcessedSubstrates.Count
                });
            }
        };

        // Guards
        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["canSelect"] = (sm) => true,
            ["canStart"] = (sm) => !string.IsNullOrEmpty(RecipeId),
            ["canAbort"] = (sm) => true
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: guards,
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

    // Public API methods
    public async Task<EventResult> CreateAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "CREATE", null);
        return result;
    }

    public async Task<EventResult> SelectAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SELECT", null);
        return result;
    }

    public async Task<EventResult> DeselectAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DESELECT", null);
        return result;
    }

    public async Task<EventResult> StartExecutionAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "START", null);
        return result;
    }

    public async Task<EventResult> ProcessStartAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESS_START", null);
        return result;
    }

    public async Task<EventResult> ProcessCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESS_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> MaterialInAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "MATERIAL_IN", null);
        return result;
    }

    public async Task<EventResult> MaterialOutAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "MATERIAL_OUT", null);
        return result;
    }

    public async Task<EventResult> MaterialProcessedAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "MATERIAL_PROCESSED", null);
        return result;
    }

    public async Task<EventResult> PauseAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PAUSE", null);
        return result;
    }

    public async Task<EventResult> ResumeAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "RESUME", null);
        return result;
    }

    public async Task<EventResult> StopAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "STOP", null);
        return result;
    }

    public async Task<EventResult> StoppedAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "STOPPED", null);
        return result;
    }

    public async Task<EventResult> AbortAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ABORT", null);
        return result;
    }

    public async Task<EventResult> AbortedAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ABORTED", null);
        return result;
    }

    public async Task<EventResult> DeleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DELETE", null);
        return result;
    }

    public void AddProcessedSubstrate(string substrateId)
    {
        ProcessedSubstrates.Add(substrateId);
    }
}
