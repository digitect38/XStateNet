using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Schedulers;

/// <summary>
/// Master CMP Scheduler - Orchestrates job distribution across CMP tools
/// Implements: Priority scheduling, load balancing, WIP control
/// </summary>
public class CMPMasterScheduler
{
    private readonly string _schedulerId;
    private readonly string _instanceId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    // Scheduling policies
    private readonly int _maxWip;
    private readonly Queue<JobRequest> _highPriorityQueue = new();
    private readonly Queue<JobRequest> _normalPriorityQueue = new();
    private readonly Queue<JobRequest> _lowPriorityQueue = new();

    // Tool tracking
    private readonly Dictionary<string, ToolStatus> _toolStatus = new();
    private int _currentWip = 0;

    public string MachineId => $"MASTER_SCHEDULER_{_schedulerId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public CMPMasterScheduler(string schedulerId, EventBusOrchestrator orchestrator, int maxWip = 25)
    {
        _schedulerId = schedulerId;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
        _orchestrator = orchestrator;
        _maxWip = maxWip;

        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'idle',
            context: {
                wipCount: 0,
                maxWip: 25,
                pendingJobs: 0
            },
            states: {
                idle: {
                    entry: ['logIdle'],
                    on: {
                        JOB_ARRIVED: {
                            target: 'evaluating',
                            actions: ['addJobToQueue']
                        },
                        TOOL_STATUS_UPDATE: {
                            actions: ['updateToolStatus']
                        },
                        TOOL_AVAILABLE: {
                            target: 'evaluating'
                        },
                        JOB_COMPLETED: {
                            target: 'evaluating',
                            actions: ['decrementWip']
                        }
                    }
                },
                evaluating: {
                    entry: ['logEvaluating'],
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
                    entry: ['logDispatching', 'dispatchJob'],
                    after: {
                        '100': 'idle'
                    }
                },
                waiting: {
                    entry: ['logWaiting'],
                    on: {
                        TOOL_AVAILABLE: {
                            target: 'evaluating'
                        },
                        JOB_COMPLETED: {
                            target: 'evaluating',
                            actions: ['decrementWip']
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

            ["addJobToQueue"] = (ctx) =>
            {
                // In production, extract from event data
                var job = new JobRequest
                {
                    JobId = $"JOB_{DateTime.Now:HHmmssff}",
                    WaferId = $"W{DateTime.Now:HHmmss}",
                    Priority = Priority.Normal,
                    RecipeId = "CMP_STANDARD_01",
                    RequestTime = DateTime.UtcNow
                };

                AddJobToQueue(job);
                Console.WriteLine($"[{MachineId}] 📥 Job added: {job.JobId}, Priority: {job.Priority}, Queue size: {GetQueueSize()}");
            },

            ["updateToolStatus"] = (ctx) =>
            {
                // In production, extract tool ID and status from event data
                Console.WriteLine($"[{MachineId}] 🔄 Tool status updated");
            },

            ["logEvaluating"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔍 Evaluating dispatch conditions..."),

            ["logDispatching"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🚀 Dispatching job..."),

            ["dispatchJob"] = (ctx) =>
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
                    Console.WriteLine($"[{MachineId}] ⚠️ No available tool for {job.JobId}");
                    // Put job back in queue
                    AddJobToQueue(job);
                    return;
                }

                // Dispatch to tool scheduler
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

                Console.WriteLine($"[{MachineId}] ✅ Dispatched {job.JobId} to {bestTool.ToolId}");
                Console.WriteLine($"[{MachineId}] 📊 WIP: {_currentWip}/{_maxWip}, Queue: {GetQueueSize()}");
            },

            ["logWaiting"] = (ctx) =>
            {
                var pending = GetQueueSize();
                Console.WriteLine($"[{MachineId}] ⏳ Waiting - {pending} jobs queued, WIP at capacity or no tools available");
            },

            ["decrementWip"] = (ctx) =>
            {
                _currentWip--;

                // Mark tool as available again
                // In production, extract tool ID from event data
                var completedTool = _toolStatus.Values.FirstOrDefault(t => !t.IsAvailable);
                if (completedTool != null)
                {
                    completedTool.IsAvailable = true;
                    completedTool.CurrentJobId = null;
                    completedTool.ProcessedWafers++;
                }

                Console.WriteLine($"[{MachineId}] ✅ Job completed - WIP: {_currentWip}/{_maxWip}");
            }
        };

        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["hasAvailableToolAndCapacity"] = (sm) =>
            {
                var hasCapacity = _currentWip < _maxWip;
                var hasAvailableTool = _toolStatus.Values.Any(t => t.IsAvailable);
                var hasJobs = GetQueueSize() > 0;

                var canDispatch = hasCapacity && hasAvailableTool && hasJobs;

                Console.WriteLine($"[{MachineId}] [GUARD] CanDispatch={canDispatch} (Capacity={hasCapacity}, AvailTool={hasAvailableTool}, Jobs={hasJobs})");

                return canDispatch;
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: guards,
            enableGuidIsolation: false // Already has GUID suffix in MachineId property
        );
    }

    /// <summary>
    /// Add tool to scheduler's tracking
    /// </summary>
    public void RegisterTool(string toolId, string toolType, Dictionary<string, object>? capabilities = null)
    {
        _toolStatus[toolId] = new ToolStatus
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

        Console.WriteLine($"[{MachineId}] 🔧 Registered tool: {toolId} ({toolType})");
    }

    /// <summary>
    /// Priority-based job queue management
    /// </summary>
    private void AddJobToQueue(JobRequest job)
    {
        switch (job.Priority)
        {
            case Priority.High:
            case Priority.Critical:
                _highPriorityQueue.Enqueue(job);
                break;
            case Priority.Normal:
                _normalPriorityQueue.Enqueue(job);
                break;
            case Priority.Low:
                _lowPriorityQueue.Enqueue(job);
                break;
        }
    }

    /// <summary>
    /// Get next job from priority queues
    /// </summary>
    private JobRequest? GetNextJob()
    {
        if (_highPriorityQueue.Count > 0)
            return _highPriorityQueue.Dequeue();

        if (_normalPriorityQueue.Count > 0)
            return _normalPriorityQueue.Dequeue();

        if (_lowPriorityQueue.Count > 0)
            return _lowPriorityQueue.Dequeue();

        return null;
    }

    /// <summary>
    /// Get total queue size
    /// </summary>
    private int GetQueueSize()
    {
        return _highPriorityQueue.Count + _normalPriorityQueue.Count + _lowPriorityQueue.Count;
    }

    /// <summary>
    /// Select best tool using multiple criteria:
    /// 1. Recipe compatibility
    /// 2. PM status (wafers until PM)
    /// 3. Consumables status
    /// 4. Load balancing
    /// </summary>
    private ToolStatus? SelectBestTool(JobRequest job)
    {
        var availableTools = _toolStatus.Values
            .Where(t => t.IsAvailable)
            .Where(t => t.ToolType == "CMP") // Recipe compatibility check
            .ToList();

        if (!availableTools.Any())
            return null;

        // Score each tool
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

    /// <summary>
    /// Calculate tool selection score
    /// </summary>
    private double CalculateToolScore(ToolStatus tool, JobRequest job)
    {
        double score = 100.0;

        // Prefer tools with more wafers remaining until PM
        var daysSinceLastPM = (DateTime.UtcNow - tool.LastMaintenanceDate).TotalDays;
        score += (30 - daysSinceLastPM) * 2; // Up to 60 points

        // Load balancing - prefer less utilized tools
        score += (1000 - tool.ProcessedWafers) / 10.0; // Up to 100 points

        // Consumables status
        score += tool.SlurryLevel / 2; // Up to 50 points
        score += (100 - tool.PadWear) / 2; // Up to 50 points

        // Recipe compatibility (in production, check recipe requirements)
        if (tool.Capabilities.ContainsKey("recipes"))
        {
            score += 50; // Bonus for recipe support
        }

        return score;
    }

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public string GetCurrentState()
    {
        return _machine.CurrentState;
    }

    public int GetCurrentWip() => _currentWip;
    public int GetQueueLength() => GetQueueSize();
}

/// <summary>
/// Job request data model
/// </summary>
public class JobRequest
{
    public string JobId { get; set; } = "";
    public string WaferId { get; set; } = "";
    public string RecipeId { get; set; } = "";
    public Priority Priority { get; set; }
    public DateTime RequestTime { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Job priority levels
/// </summary>
public enum Priority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Tool status tracking
/// </summary>
public class ToolStatus
{
    public string ToolId { get; set; } = "";
    public string ToolType { get; set; } = "";
    public bool IsAvailable { get; set; }
    public string? CurrentJobId { get; set; }
    public Dictionary<string, object> Capabilities { get; set; } = new();
    public DateTime LastMaintenanceDate { get; set; }
    public int ProcessedWafers { get; set; }
    public double SlurryLevel { get; set; } = 100.0; // Percentage
    public double PadWear { get; set; } = 0.0; // Percentage
}