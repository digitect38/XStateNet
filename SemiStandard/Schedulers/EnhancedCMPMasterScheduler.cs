using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;
using XStateNet.Semi.Standards;

namespace XStateNet.Semi.Schedulers;

/// <summary>
/// Enhanced CMP Master Scheduler - Phase 1 Implementation
/// Integrates: E40 Process Jobs, E134 Data Collection, E39 Equipment Metrics
/// </summary>
public class EnhancedCMPMasterScheduler
{
    private readonly string _schedulerId;
    private readonly string _instanceId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    // SEMI Standard Integration
    private readonly E134DataCollectionManager _dataCollectionManager;
    private readonly E39E116E10EquipmentMetricsMachine _metricsManager;
    private readonly Dictionary<string, E40ProcessJobMachine> _processJobs = new();

    // Scheduling policies
    private readonly int _maxWip;
    private readonly Queue<EnhancedJobRequest> _highPriorityQueue = new();
    private readonly Queue<EnhancedJobRequest> _normalPriorityQueue = new();
    private readonly Queue<EnhancedJobRequest> _lowPriorityQueue = new();

    // Tool tracking
    private readonly Dictionary<string, EnhancedToolStatus> _toolStatus = new();
    private int _currentWip = 0;
    private int _totalJobsProcessed = 0;
    private DateTime _startTime = DateTime.UtcNow;

    public string MachineId => $"MASTER_SCHEDULER_{_schedulerId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public EnhancedCMPMasterScheduler(
        string schedulerId,
        EventBusOrchestrator orchestrator,
        int maxWip = 25)
    {
        _schedulerId = schedulerId;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
        _orchestrator = orchestrator;
        _maxWip = maxWip;

        // Initialize SEMI standards
        _dataCollectionManager = new E134DataCollectionManager($"DCM_{schedulerId}", _orchestrator);
        _metricsManager = new E39E116E10EquipmentMetricsMachine($"METRICS_{schedulerId}", _orchestrator);

        InitializeDataCollection();
        InitializeMetrics();

        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'idle',
            context: {
                wipCount: 0,
                maxWip: {{_maxWip}},
                pendingJobs: 0,
                totalJobsProcessed: 0,
                utilizationPercent: 0
            },
            states: {
                idle: {
                    entry: ['logIdle', 'collectIdleMetrics'],
                    on: {
                        JOB_ARRIVED: {
                            target: 'evaluating',
                            actions: ['createProcessJob', 'collectJobMetrics']
                        },
                        TOOL_STATUS_UPDATE: {
                            actions: ['updateToolStatus']
                        },
                        TOOL_AVAILABLE: {
                            target: 'evaluating'
                        },
                        JOB_COMPLETED: {
                            target: 'evaluating',
                            actions: ['completeProcessJob', 'collectCompletionMetrics']
                        }
                    }
                },
                evaluating: {
                    entry: ['logEvaluating', 'collectEvaluationMetrics'],
                    always: [
                        {
                            target: 'dispatching',
                            cond: 'hasAvailableToolAndCapacity'
                        },
                        {
                            target: 'waiting'
                        }
                    ]
                },
                dispatching: {
                    entry: ['logDispatching', 'dispatchProcessJob', 'collectDispatchMetrics'],
                    after: {
                        '100': 'idle'
                    }
                },
                waiting: {
                    entry: ['logWaiting', 'collectWaitingMetrics'],
                    on: {
                        TOOL_AVAILABLE: {
                            target: 'evaluating'
                        },
                        JOB_COMPLETED: {
                            target: 'evaluating',
                            actions: ['completeProcessJob', 'collectCompletionMetrics']
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logIdle"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 💤 Scheduler idle - WIP: {_currentWip}/{_maxWip}"),

            ["collectIdleMetrics"] = async (ctx) =>
            {
                await CollectSchedulerMetrics();
            },

            ["createProcessJob"] = async (ctx) =>
            {
                var jobId = $"PJ_{DateTime.Now:HHmmssff}";
                var waferId = $"W{DateTime.Now:HHmmss}";

                // Create E40 Process Job
                var processJob = new E40ProcessJobMachine(jobId, _orchestrator);
                await processJob.StartAsync();

                // Configure process job
                await processJob.SetMaterialLocationsAsync(new[] { waferId });
                await processJob.SetRecipeAsync("CMP_STANDARD_01");

                _processJobs[jobId] = processJob;

                var enhancedJob = new EnhancedJobRequest
                {
                    JobId = jobId,
                    ProcessJobMachine = processJob,
                    WaferId = waferId,
                    Priority = JobPriority.Normal,
                    RecipeId = "CMP_STANDARD_01",
                    RequestTime = DateTime.UtcNow
                };

                AddJobToQueue(enhancedJob);
                Console.WriteLine($"[{MachineId}] 📥 E40 Process Job created: {jobId}, Queue: {GetQueueSize()}");
            },

            ["collectJobMetrics"] = async (ctx) =>
            {
                await _dataCollectionManager.CollectDataAsync("JOB_ARRIVAL", new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTime.UtcNow,
                    ["QueueLength"] = GetQueueSize(),
                    ["CurrentWIP"] = _currentWip
                });
            },

            ["updateToolStatus"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Tool status updated");
            },

            ["logEvaluating"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔍 Evaluating dispatch conditions..."),

            ["collectEvaluationMetrics"] = async (ctx) =>
            {
                var utilization = CalculateUtilization();
                await _metricsManager.UpdateMetricAsync("UTILIZATION", utilization);
            },

            ["logDispatching"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🚀 Dispatching job..."),

            ["dispatchProcessJob"] = async (ctx) =>
            {
                var job = GetNextJob();
                if (job == null)
                {
                    Console.WriteLine($"[{MachineId}] ⚠️ No jobs in queue");
                    return;
                }

                var bestTool = SelectBestTool(job);
                if (bestTool == null)
                {
                    Console.WriteLine($"[{MachineId}] ⚠️ No available tool");
                    AddJobToQueue(job);
                    return;
                }

                // Start E40 Process Job
                await job.ProcessJobMachine.QueueAsync();
                await job.ProcessJobMachine.SetProcessingToolAsync(bestTool.ToolId);

                // Dispatch to tool
                ctx.RequestSend(bestTool.ToolId, "PROCESS_JOB", new JObject
                {
                    ["jobId"] = job.JobId,
                    ["waferId"] = job.WaferId,
                    ["recipeId"] = job.RecipeId,
                    ["priority"] = job.Priority.ToString()
                });

                _currentWip++;
                bestTool.IsAvailable = false;
                bestTool.CurrentJobId = job.JobId;

                Console.WriteLine($"[{MachineId}] ✅ E40 Job {job.JobId} dispatched to {bestTool.ToolId}");
            },

            ["collectDispatchMetrics"] = async (ctx) =>
            {
                await _dataCollectionManager.CollectDataAsync("JOB_DISPATCH", new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTime.UtcNow,
                    ["WIP"] = _currentWip,
                    ["QueueLength"] = GetQueueSize()
                });
            },

            ["logWaiting"] = (ctx) =>
            {
                var pending = GetQueueSize();
                Console.WriteLine($"[{MachineId}] ⏳ Waiting - {pending} jobs queued");
            },

            ["collectWaitingMetrics"] = async (ctx) =>
            {
                await _dataCollectionManager.CollectDataAsync("SCHEDULER_WAITING", new Dictionary<string, object>
                {
                    ["QueueLength"] = GetQueueSize(),
                    ["AvailableTools"] = _toolStatus.Values.Count(t => t.IsAvailable)
                });
            },

            ["completeProcessJob"] = async (ctx) =>
            {
                _currentWip--;
                _totalJobsProcessed++;

                // Mark tool as available
                var completedTool = _toolStatus.Values.FirstOrDefault(t => !t.IsAvailable);
                if (completedTool != null)
                {
                    completedTool.IsAvailable = true;
                    var jobId = completedTool.CurrentJobId;
                    completedTool.CurrentJobId = null;
                    completedTool.ProcessedWafers++;

                    // Complete E40 Process Job
                    if (jobId != null && _processJobs.TryGetValue(jobId, out var processJob))
                    {
                        await processJob.CompleteAsync();
                        Console.WriteLine($"[{MachineId}] ✅ E40 Job {jobId} completed");
                    }
                }

                Console.WriteLine($"[{MachineId}] 📊 WIP: {_currentWip}/{_maxWip}, Total: {_totalJobsProcessed}");
            },

            ["collectCompletionMetrics"] = async (ctx) =>
            {
                var cycleTime = CalculateAverageCycleTime();
                var throughput = CalculateThroughput();

                await _dataCollectionManager.CollectDataAsync("JOB_COMPLETION", new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTime.UtcNow,
                    ["TotalJobs"] = _totalJobsProcessed,
                    ["CurrentWIP"] = _currentWip,
                    ["AvgCycleTime"] = cycleTime,
                    ["ThroughputWPH"] = throughput
                });

                await _metricsManager.UpdateMetricAsync("THROUGHPUT", throughput);
                await _metricsManager.UpdateMetricAsync("AVG_CYCLE_TIME", cycleTime);
            }
        };

        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["hasAvailableToolAndCapacity"] = (sm) =>
            {
                var hasCapacity = _currentWip < _maxWip;
                var hasAvailableTool = _toolStatus.Values.Any(t => t.IsAvailable);
                var hasJobs = GetQueueSize() > 0;

                return hasCapacity && hasAvailableTool && hasJobs;
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: guards,
            enableGuidIsolation: false
        );
    }

    /// <summary>
    /// Initialize E134 Data Collection plans
    /// </summary>
    private void InitializeDataCollection()
    {
        // Create collection plans
        Task.Run(async () =>
        {
            await _dataCollectionManager.CreatePlanAsync(
                "JOB_ARRIVAL",
                new[] { "Timestamp", "QueueLength", "CurrentWIP" },
                CollectionTrigger.Event);

            await _dataCollectionManager.CreatePlanAsync(
                "JOB_DISPATCH",
                new[] { "Timestamp", "WIP", "QueueLength" },
                CollectionTrigger.Event);

            await _dataCollectionManager.CreatePlanAsync(
                "JOB_COMPLETION",
                new[] { "Timestamp", "TotalJobs", "CurrentWIP", "AvgCycleTime", "ThroughputWPH" },
                CollectionTrigger.Event);

            await _dataCollectionManager.CreatePlanAsync(
                "SCHEDULER_METRICS",
                new[] { "Utilization", "QueueLength", "WIP", "Throughput" },
                CollectionTrigger.Timer);

            Console.WriteLine($"[{MachineId}] ✅ E134 Data Collection plans initialized");
        }).Wait();
    }

    /// <summary>
    /// Initialize E39 Equipment Metrics
    /// </summary>
    private void InitializeMetrics()
    {
        Task.Run(async () =>
        {
            await _metricsManager.StartAsync();

            // Define metrics
            _metricsManager.DefineMetric("UTILIZATION", 0, 100, "%");
            _metricsManager.DefineMetric("THROUGHPUT", 0, 1000, "wafers/hour");
            _metricsManager.DefineMetric("AVG_CYCLE_TIME", 0, 600, "seconds");
            _metricsManager.DefineMetric("QUEUE_LENGTH", 0, 100, "jobs");

            Console.WriteLine($"[{MachineId}] ✅ E39 Equipment Metrics initialized");
        }).Wait();
    }

    /// <summary>
    /// Collect scheduler performance metrics
    /// </summary>
    private async Task CollectSchedulerMetrics()
    {
        var utilization = CalculateUtilization();
        var throughput = CalculateThroughput();

        await _dataCollectionManager.CollectDataAsync("SCHEDULER_METRICS", new Dictionary<string, object>
        {
            ["Utilization"] = utilization,
            ["QueueLength"] = GetQueueSize(),
            ["WIP"] = _currentWip,
            ["Throughput"] = throughput
        });
    }

    /// <summary>
    /// Calculate scheduler utilization
    /// </summary>
    private double CalculateUtilization()
    {
        if (_maxWip == 0) return 0;
        return (_currentWip / (double)_maxWip) * 100.0;
    }

    /// <summary>
    /// Calculate throughput (wafers per hour)
    /// </summary>
    private double CalculateThroughput()
    {
        var elapsedHours = (DateTime.UtcNow - _startTime).TotalHours;
        if (elapsedHours == 0) return 0;
        return _totalJobsProcessed / elapsedHours;
    }

    /// <summary>
    /// Calculate average cycle time
    /// </summary>
    private double CalculateAverageCycleTime()
    {
        // Simplified - in production, track actual cycle times
        return 45.0; // seconds
    }

    /// <summary>
    /// Register tool with scheduler
    /// </summary>
    public void RegisterTool(string toolId, string toolType, Dictionary<string, object>? capabilities = null)
    {
        _toolStatus[toolId] = new EnhancedToolStatus
        {
            ToolId = toolId,
            ToolType = toolType,
            IsAvailable = true,
            Capabilities = capabilities ?? new Dictionary<string, object>(),
            LastMaintenanceDate = DateTime.UtcNow.AddDays(-5),
            ProcessedWafers = 0,
            SlurryLevel = 100.0,
            PadWear = 0.0
        };

        Console.WriteLine($"[{MachineId}] 🔧 Registered tool: {toolId}");
    }

    private void AddJobToQueue(EnhancedJobRequest job)
    {
        switch (job.Priority)
        {
            case JobPriority.High:
            case JobPriority.Critical:
                _highPriorityQueue.Enqueue(job);
                break;
            case JobPriority.Normal:
                _normalPriorityQueue.Enqueue(job);
                break;
            case JobPriority.Low:
                _lowPriorityQueue.Enqueue(job);
                break;
        }
    }

    private EnhancedJobRequest? GetNextJob()
    {
        if (_highPriorityQueue.Count > 0)
            return _highPriorityQueue.Dequeue();

        if (_normalPriorityQueue.Count > 0)
            return _normalPriorityQueue.Dequeue();

        if (_lowPriorityQueue.Count > 0)
            return _lowPriorityQueue.Dequeue();

        return null;
    }

    private int GetQueueSize()
    {
        return _highPriorityQueue.Count + _normalPriorityQueue.Count + _lowPriorityQueue.Count;
    }

    private EnhancedToolStatus? SelectBestTool(EnhancedJobRequest job)
    {
        var availableTools = _toolStatus.Values
            .Where(t => t.IsAvailable)
            .Where(t => t.ToolType == "CMP")
            .ToList();

        if (!availableTools.Any())
            return null;

        var scoredTools = availableTools.Select(tool => new
        {
            Tool = tool,
            Score = CalculateToolScore(tool, job)
        })
        .OrderByDescending(x => x.Score)
        .ToList();

        var selected = scoredTools.First();
        Console.WriteLine($"[{MachineId}] 🎯 Selected {selected.Tool.ToolId} (Score: {selected.Score:F1})");

        return selected.Tool;
    }

    private double CalculateToolScore(EnhancedToolStatus tool, EnhancedJobRequest job)
    {
        double score = 100.0;

        var daysSinceLastPM = (DateTime.UtcNow - tool.LastMaintenanceDate).TotalDays;
        score += (30 - daysSinceLastPM) * 2;

        score += (1000 - tool.ProcessedWafers) / 10.0;
        score += tool.SlurryLevel / 2;
        score += (100 - tool.PadWear) / 2;

        if (tool.Capabilities.ContainsKey("recipes"))
        {
            score += 50;
        }

        return score;
    }

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public string GetCurrentState() => _machine.CurrentState;
    public int GetCurrentWip() => _currentWip;
    public int GetQueueLength() => GetQueueSize();
    public int GetTotalJobsProcessed() => _totalJobsProcessed;
    public double GetUtilization() => CalculateUtilization();
    public double GetThroughput() => CalculateThroughput();

    /// <summary>
    /// Get data collection reports
    /// </summary>
    public IEnumerable<DataReport> GetReports(string planId, DateTime? since = null)
    {
        return _dataCollectionManager.GetReports(planId, since);
    }
}

/// <summary>
/// Enhanced job request with E40 Process Job integration
/// </summary>
public class EnhancedJobRequest
{
    public string JobId { get; set; } = "";
    public E40ProcessJobMachine ProcessJobMachine { get; set; } = null!;
    public string WaferId { get; set; } = "";
    public string RecipeId { get; set; } = "";
    public JobPriority Priority { get; set; }
    public DateTime RequestTime { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Job priority levels
/// </summary>
public enum JobPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Enhanced tool status with additional metrics
/// </summary>
public class EnhancedToolStatus
{
    public string ToolId { get; set; } = "";
    public string ToolType { get; set; } = "";
    public bool IsAvailable { get; set; }
    public string? CurrentJobId { get; set; }
    public Dictionary<string, object> Capabilities { get; set; } = new();
    public DateTime LastMaintenanceDate { get; set; }
    public int ProcessedWafers { get; set; }
    public double SlurryLevel { get; set; } = 100.0;
    public double PadWear { get; set; } = 0.0;
}
