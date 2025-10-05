using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// E40 Process Job Machine - SEMI E40 Standard
/// Manages process job lifecycle from creation through processing to completion
/// Refactored to use ExtendedPureStateMachineFactory with EventBusOrchestrator
/// </summary>
public class E40ProcessJobManager
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ConcurrentDictionary<string, ProcessJobMachine> _processJobs = new();

    public string MachineId => $"E40_PROCESSJOB_MGR_{_equipmentId}";

    public E40ProcessJobManager(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Create and register a process job
    /// </summary>
    public async Task<ProcessJobMachine> CreateProcessJobAsync(string processJobId, string recipeId, List<string> materialIds)
    {
        if (_processJobs.ContainsKey(processJobId))
        {
            return _processJobs[processJobId];
        }

        var processJob = new ProcessJobMachine(processJobId, recipeId, materialIds, _equipmentId, _orchestrator);
        _processJobs[processJobId] = processJob;

        await processJob.StartAsync();

        return processJob;
    }

    /// <summary>
    /// Get process job
    /// </summary>
    public ProcessJobMachine? GetProcessJob(string processJobId)
    {
        return _processJobs.TryGetValue(processJobId, out var job) ? job : null;
    }

    /// <summary>
    /// Get all process jobs
    /// </summary>
    public IEnumerable<ProcessJobMachine> GetAllProcessJobs()
    {
        return _processJobs.Values;
    }

    /// <summary>
    /// Get processing jobs
    /// </summary>
    public IEnumerable<ProcessJobMachine> GetProcessingJobs()
    {
        return _processJobs.Values.Where(j => j.GetCurrentState().Contains("Processing"));
    }

    /// <summary>
    /// Remove completed job
    /// </summary>
    public async Task<bool> RemoveProcessJobAsync(string processJobId)
    {
        if (_processJobs.TryRemove(processJobId, out var job))
        {
            await job.RemoveAsync();
            return true;
        }
        return false;
    }
}

/// <summary>
/// Individual process job state machine using orchestrator
/// </summary>
public class ProcessJobMachine
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly string _instanceId;

    public string ProcessJobId { get; }
    public string RecipeId { get; set; }
    public List<string> MaterialIds { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? ErrorCode { get; set; }
    public ConcurrentDictionary<string, object> RecipeParameters { get; set; }
    public ConcurrentDictionary<string, object> Properties { get; }

    public string MachineId => $"E40_PROCESSJOB_{ProcessJobId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public ProcessJobMachine(string processJobId, string recipeId, List<string> materialIds, string equipmentId, EventBusOrchestrator orchestrator)
    {
        ProcessJobId = processJobId;
        RecipeId = recipeId;
        MaterialIds = materialIds;
        RecipeParameters = new ConcurrentDictionary<string, object>();
        Properties = new ConcurrentDictionary<string, object>();
        _orchestrator = orchestrator;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Inline XState JSON definition with unique ID per process job
        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'NoState',
            context: {
                processJobId: '{{processJobId}}',
                recipeId: '{{recipeId}}',
                materialIds: [],
                startTime: null,
                endTime: null,
                errorCode: null
            },
            states: {
                NoState: {
                    entry: 'logNoState',
                    on: {
                        CREATE: {
                            target: 'Queued',
                            actions: 'assignProcessJobData'
                        }
                    }
                },
                Queued: {
                    entry: 'logQueued',
                    on: {
                        SETUP: 'SettingUp',
                        ABORT: 'Aborting',
                        REMOVE: 'NoState'
                    }
                },
                SettingUp: {
                    entry: 'logSettingUp',
                    on: {
                        SETUP_COMPLETE: 'WaitingForStart',
                        SETUP_FAILED: 'Queued',
                        ABORT: 'Aborting'
                    }
                },
                WaitingForStart: {
                    entry: 'logWaitingForStart',
                    on: {
                        START: {
                            target: 'Processing',
                            actions: 'recordStartTime'
                        },
                        PAUSE_REQUEST: 'Pausing',
                        STOP: 'Stopping',
                        ABORT: 'Aborting'
                    }
                },
                Processing: {
                    entry: 'logProcessing',
                    on: {
                        PROCESSING_COMPLETE: {
                            target: 'ProcessingComplete',
                            actions: 'recordEndTime'
                        },
                        PAUSE_REQUEST: 'Pausing',
                        STOP: 'Stopping',
                        ABORT: 'Aborting',
                        ERROR: {
                            target: 'Aborting',
                            actions: 'recordError'
                        }
                    }
                },
                ProcessingComplete: {
                    entry: 'logProcessingComplete',
                    on: {
                        REMOVE: 'NoState',
                        RESTART: 'Queued'
                    }
                },
                Pausing: {
                    entry: 'logPausing',
                    on: {
                        PAUSE_COMPLETE: 'Paused',
                        PAUSE_FAILED: 'Processing'
                    }
                },
                Paused: {
                    entry: 'logPaused',
                    on: {
                        RESUME: 'Processing',
                        STOP: 'Stopping',
                        ABORT: 'Aborting'
                    }
                },
                Stopping: {
                    entry: 'logStopping',
                    on: {
                        STOP_COMPLETE: {
                            target: 'Stopped',
                            actions: 'recordEndTime'
                        }
                    }
                },
                Aborting: {
                    entry: 'logAborting',
                    on: {
                        ABORT_COMPLETE: {
                            target: 'Aborted',
                            actions: 'recordEndTime'
                        }
                    }
                },
                Stopped: {
                    entry: 'logStopped',
                    on: {
                        REMOVE: 'NoState',
                        RESTART: 'Queued'
                    }
                },
                Aborted: {
                    entry: 'logAborted',
                    on: {
                        REMOVE: 'NoState'
                    }
                }
            }
        }
        """;

        // Orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logNoState"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📋 No process job state");
            },

            ["assignProcessJobData"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📝 Assigning process job data: Recipe={RecipeId}, Materials={string.Join(",", MaterialIds)}");

                ctx.RequestSend("E42_RECIPE_MGMT", "PROCESSJOB_CREATED", new JObject
                {
                    ["processJobId"] = ProcessJobId,
                    ["recipeId"] = RecipeId
                });
            },

            ["logQueued"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📬 Process job queued with recipe {RecipeId}");

                ctx.RequestSend("E94_CONTROL_JOB", "PROCESSJOB_QUEUED", new JObject
                {
                    ["processJobId"] = ProcessJobId,
                    ["recipeId"] = RecipeId,
                    ["materialCount"] = MaterialIds.Count
                });
            },

            ["logSettingUp"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔧 Setting up process job...");

                ctx.RequestSend("E87_CARRIER_MGMT", "REQUEST_MATERIALS", new JObject
                {
                    ["processJobId"] = ProcessJobId,
                    ["materialIds"] = new JArray(MaterialIds)
                });
            },

            ["logWaitingForStart"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏳ Waiting for start command");

                ctx.RequestSend("E94_CONTROL_JOB", "READY_TO_START", new JObject
                {
                    ["processJobId"] = ProcessJobId
                });
            },

            ["recordStartTime"] = (ctx) =>
            {
                StartTime = DateTime.UtcNow;
                Console.WriteLine($"[{MachineId}] ▶️ Started at {StartTime}");
            },

            ["logProcessing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Processing with recipe {RecipeId}");

                ctx.RequestSend("E90_SUBSTRATE_TRACKING", "PROCESSING_STARTED", new JObject
                {
                    ["processJobId"] = ProcessJobId,
                    ["recipeId"] = RecipeId,
                    ["materialIds"] = new JArray(MaterialIds),
                    ["startTime"] = StartTime
                });

                ctx.RequestSend("E42_RECIPE_MGMT", "RECIPE_PROCESSING", new JObject
                {
                    ["processJobId"] = ProcessJobId,
                    ["recipeId"] = RecipeId
                });
            },

            ["recordEndTime"] = (ctx) =>
            {
                EndTime = DateTime.UtcNow;
                var duration = EndTime - StartTime;
                Console.WriteLine($"[{MachineId}] ⏱️ End time recorded: {EndTime}, Duration: {duration?.TotalSeconds:F1}s");
            },

            ["logProcessingComplete"] = (ctx) =>
            {
                var duration = EndTime - StartTime;
                Console.WriteLine($"[{MachineId}] ✅ Processing complete. Duration: {duration?.TotalSeconds:F1} seconds");

                ctx.RequestSend("E90_SUBSTRATE_TRACKING", "PROCESSING_COMPLETE", new JObject
                {
                    ["processJobId"] = ProcessJobId,
                    ["recipeId"] = RecipeId,
                    ["materialIds"] = new JArray(MaterialIds),
                    ["endTime"] = EndTime,
                    ["duration"] = duration?.TotalSeconds
                });

                ctx.RequestSend("E94_CONTROL_JOB", "PROCESSJOB_COMPLETE", new JObject
                {
                    ["processJobId"] = ProcessJobId,
                    ["status"] = "Complete"
                });
            },

            ["logPausing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏸️ Pausing process job...");
            },

            ["logPaused"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏸️ Process job paused");

                ctx.RequestSend("E94_CONTROL_JOB", "PROCESSJOB_PAUSED", new JObject
                {
                    ["processJobId"] = ProcessJobId
                });
            },

            ["logStopping"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🛑 Stopping process job...");
            },

            ["logStopped"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🛑 Process job stopped");

                ctx.RequestSend("E94_CONTROL_JOB", "PROCESSJOB_STOPPED", new JObject
                {
                    ["processJobId"] = ProcessJobId
                });
            },

            ["logAborting"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⚠️ Aborting process job...");
            },

            ["logAborted"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⚠️ Process job aborted");

                ctx.RequestSend("E94_CONTROL_JOB", "PROCESSJOB_ABORTED", new JObject
                {
                    ["processJobId"] = ProcessJobId,
                    ["errorCode"] = ErrorCode
                });

                ctx.RequestSend("E90_SUBSTRATE_TRACKING", "PROCESSING_ABORTED", new JObject
                {
                    ["processJobId"] = ProcessJobId,
                    ["materialIds"] = new JArray(MaterialIds)
                });
            },

            ["recordError"] = (ctx) =>
            {
                // Error code should be set via ErrorAsync method before triggering ERROR event
                ErrorCode = ErrorCode ?? "UNKNOWN_ERROR";
                Console.WriteLine($"[{MachineId}] ❌ Error recorded: {ErrorCode}");

                ctx.RequestSend("E39_EQUIPMENT_METRICS", "ERROR_OCCURRED", new JObject
                {
                    ["processJobId"] = ProcessJobId,
                    ["errorCode"] = ErrorCode
                });
            }
        };

        // Guards
        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["canAbort"] = (sm) => true,
            ["canPause"] = (sm) => true,
            ["canResume"] = (sm) => true
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

    public async Task<EventResult> SetupAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SETUP", null);
        return result;
    }

    public async Task<EventResult> SetupCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SETUP_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> SetupFailedAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SETUP_FAILED", null);
        return result;
    }

    public async Task<EventResult> StartProcessingAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "START", null);
        return result;
    }

    public async Task<EventResult> ProcessingCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESSING_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> PauseRequestAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PAUSE_REQUEST", null);
        return result;
    }

    public async Task<EventResult> PauseCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PAUSE_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> PauseFailedAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PAUSE_FAILED", null);
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

    public async Task<EventResult> StopCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "STOP_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> AbortAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ABORT", null);
        return result;
    }

    public async Task<EventResult> AbortCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ABORT_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> ErrorAsync(string errorCode)
    {
        ErrorCode = errorCode; // Set error code before triggering ERROR event
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ERROR", null);
        return result;
    }

    public async Task<EventResult> RemoveAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "REMOVE", null);
        return result;
    }

    public async Task<EventResult> RestartAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "RESTART", null);
        return result;
    }
}
