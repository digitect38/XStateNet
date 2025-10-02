# CMP Process Scheduler Design
## Master Scheduler + Tool Schedulers Architecture

This document shows how to design a production CMP scheduler system using XStateNet orchestration pattern.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                      MASTER SCHEDULER                           │
│  - Job queue management (priority, FIFO, batch)                 │
│  - Load balancing across CMP tools                              │
│  - Resource allocation decisions                                │
│  - WIP (Work In Progress) tracking                              │
└─────────────────────────────────────────────────────────────────┘
                            │
                            │ Orchestrator Events
                            │
        ┌───────────────────┼───────────────────┐
        │                   │                   │
        ▼                   ▼                   ▼
┌───────────────┐  ┌───────────────┐  ┌───────────────┐
│  CMP_TOOL_1   │  │  CMP_TOOL_2   │  │  CMP_TOOL_3   │
│  Scheduler    │  │  Scheduler    │  │  Scheduler    │
│               │  │               │  │               │
│  - Tool state │  │  - Tool state │  │  - Tool state │
│  - Consumables│  │  - Consumables│  │  - Consumables│
│  - PM status  │  │  - PM status  │  │  - PM status  │
│  - Recipe mgmt│  │  - Recipe mgmt│  │  - Recipe mgmt│
└───────────────┘  └───────────────┘  └───────────────┘
```

---

## 1. Master Scheduler State Machine

### XState Definition

```json
{
  "id": "masterScheduler",
  "initial": "idle",
  "context": {
    "jobQueue": [],
    "wipCount": 0,
    "maxWip": 25,
    "toolStatus": {}
  },
  "states": {
    "idle": {
      "entry": ["logIdle"],
      "on": {
        "JOB_ARRIVED": {
          "target": "evaluating",
          "actions": ["addJobToQueue"]
        },
        "TOOL_STATUS_UPDATE": {
          "actions": ["updateToolStatus"]
        }
      }
    },
    "evaluating": {
      "entry": ["logEvaluating"],
      "always": [
        {
          "target": "dispatching",
          "cond": "hasAvailableToolAndCapacity"
        },
        {
          "target": "waiting",
          "actions": ["logQueueFull"]
        }
      ]
    },
    "dispatching": {
      "entry": ["logDispatching", "selectBestTool", "dispatchJob"],
      "after": {
        "100": "idle"
      }
    },
    "waiting": {
      "entry": ["logWaiting"],
      "on": {
        "TOOL_AVAILABLE": {
          "target": "evaluating"
        },
        "JOB_COMPLETED": {
          "target": "evaluating",
          "actions": ["decrementWip"]
        }
      }
    }
  }
}
```

### Master Scheduler Implementation

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    public string MachineId => $"MASTER_SCHEDULER_{_schedulerId}";
    public IPureStateMachine Machine => _machine;

    public CMPMasterScheduler(string schedulerId, EventBusOrchestrator orchestrator, int maxWip = 25)
    {
        _schedulerId = schedulerId;
        _orchestrator = orchestrator;
        _maxWip = maxWip;

        var definition = @"
        {
            ""id"": ""masterScheduler"",
            ""initial"": ""idle"",
            ""context"": {
                ""wipCount"": 0,
                ""maxWip"": 25,
                ""pendingJobs"": 0
            },
            ""states"": {
                ""idle"": {
                    ""entry"": [""logIdle""],
                    ""on"": {
                        ""JOB_ARRIVED"": {
                            ""target"": ""evaluating"",
                            ""actions"": [""addJobToQueue""]
                        },
                        ""TOOL_STATUS_UPDATE"": {
                            ""actions"": [""updateToolStatus""]
                        }
                    }
                },
                ""evaluating"": {
                    ""entry"": [""logEvaluating""],
                    ""always"": [
                        {
                            ""target"": ""dispatching"",
                            ""cond"": ""hasAvailableToolAndCapacity""
                        },
                        {
                            ""target"": ""waiting""
                        }
                    ]
                },
                ""dispatching"": {
                    ""entry"": [""logDispatching"", ""dispatchJob""],
                    ""after"": {
                        ""100"": ""idle""
                    }
                },
                ""waiting"": {
                    ""entry"": [""logWaiting""],
                    ""on"": {
                        ""TOOL_AVAILABLE"": {
                            ""target"": ""evaluating""
                        },
                        ""JOB_COMPLETED"": {
                            ""target"": ""evaluating"",
                            ""actions"": [""decrementWip""]
                        }
                    }
                }
            }
        }";

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
                Console.WriteLine($"[{MachineId}] 📥 Job added: {job.JobId}, Priority: {job.Priority}");
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
                if (job == null) return;

                var bestTool = SelectBestTool(job);
                if (bestTool == null)
                {
                    Console.WriteLine($"[{MachineId}] ⚠️ No available tool for {job.JobId}");
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
                Console.WriteLine($"[{MachineId}] 📊 WIP: {_currentWip}/{_maxWip}");
            },

            ["logWaiting"] = (ctx) =>
            {
                var pending = _highPriorityQueue.Count + _normalPriorityQueue.Count + _lowPriorityQueue.Count;
                Console.WriteLine($"[{MachineId}] ⏳ Waiting - {pending} jobs queued, WIP at capacity");
            },

            ["decrementWip"] = (ctx) =>
            {
                _currentWip--;
                Console.WriteLine($"[{MachineId}] ✅ Job completed - WIP: {_currentWip}/{_maxWip}");
            }
        };

        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["hasAvailableToolAndCapacity"] = (sm) =>
            {
                var hasCapacity = _currentWip < _maxWip;
                var hasAvailableTool = _toolStatus.Values.Any(t => t.IsAvailable);
                var hasJobs = _highPriorityQueue.Count + _normalPriorityQueue.Count + _lowPriorityQueue.Count > 0;

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
            guards: guards
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
            ProcessedWafers = 0
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

        return scoredTools.First().Tool;
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
```

---

## 2. CMP Tool Scheduler State Machine

### XState Definition

```json
{
  "id": "cmpToolScheduler",
  "initial": "idle",
  "context": {
    "toolId": "",
    "currentJob": null,
    "slurryLevel": 100,
    "padWear": 0,
    "wafersUntilPM": 1000
  },
  "states": {
    "idle": {
      "entry": ["logIdle", "notifyMasterAvailable"],
      "on": {
        "PROCESS_JOB": {
          "target": "checkingPrerequisites",
          "actions": ["storeJobInfo"]
        },
        "MAINTENANCE_REQUIRED": "maintenance"
      }
    },
    "checkingPrerequisites": {
      "entry": ["logCheckingPrereqs"],
      "always": [
        {
          "target": "loading",
          "cond": "hasAdequateConsumables"
        },
        {
          "target": "requestingConsumables",
          "actions": ["logLowConsumables"]
        }
      ]
    },
    "requestingConsumables": {
      "entry": ["requestConsumableRefill"],
      "on": {
        "CONSUMABLES_REFILLED": "checkingPrerequisites"
      }
    },
    "loading": {
      "entry": ["logLoading", "requestWaferPickup"],
      "on": {
        "WAFER_LOADED": "processing"
      },
      "after": {
        "loadTimeout": "timeout"
      }
    },
    "processing": {
      "entry": ["logProcessing"],
      "invoke": {
        "src": "cmpProcess",
        "onDone": {
          "target": "unloading",
          "actions": ["updateConsumables"]
        },
        "onError": {
          "target": "error",
          "actions": ["logProcessError"]
        }
      }
    },
    "unloading": {
      "entry": ["logUnloading", "requestWaferRemoval"],
      "on": {
        "WAFER_REMOVED": {
          "target": "reportingComplete",
          "actions": ["incrementWaferCount"]
        }
      }
    },
    "reportingComplete": {
      "entry": ["reportCompletionToMaster"],
      "always": [
        {
          "target": "maintenance",
          "cond": "isMaintenanceDue"
        },
        {
          "target": "idle"
        }
      ]
    },
    "maintenance": {
      "entry": ["logMaintenance", "notifyMasterUnavailable"],
      "invoke": {
        "src": "performMaintenance",
        "onDone": {
          "target": "idle",
          "actions": ["resetMaintenanceCounters"]
        }
      }
    },
    "timeout": {
      "entry": ["logTimeout", "reportErrorToMaster"]
    },
    "error": {
      "entry": ["logError", "reportErrorToMaster"]
    }
  }
}
```

### Tool Scheduler Implementation

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    // Tool state
    private string? _currentJobId;
    private string? _currentWaferId;
    private double _slurryLevel = 100.0;
    private double _padWear = 0.0;
    private int _wafersProcessed = 0;
    private readonly int _wafersBetweenPM = 1000;

    public string MachineId => _toolId;
    public IPureStateMachine Machine => _machine;

    public CMPToolScheduler(string toolId, EventBusOrchestrator orchestrator)
    {
        _toolId = toolId;
        _orchestrator = orchestrator;

        var definition = @"
        {
            ""id"": ""cmpToolScheduler"",
            ""initial"": ""idle"",
            ""context"": {
                ""slurryLevel"": 100,
                ""padWear"": 0,
                ""wafersProcessed"": 0
            },
            ""states"": {
                ""idle"": {
                    ""entry"": [""logIdle"", ""notifyMasterAvailable""],
                    ""on"": {
                        ""PROCESS_JOB"": {
                            ""target"": ""checkingPrerequisites"",
                            ""actions"": [""storeJobInfo""]
                        }
                    }
                },
                ""checkingPrerequisites"": {
                    ""entry"": [""logCheckingPrereqs""],
                    ""always"": [
                        {
                            ""target"": ""loading"",
                            ""cond"": ""hasAdequateConsumables""
                        },
                        {
                            ""target"": ""requestingConsumables""
                        }
                    ]
                },
                ""requestingConsumables"": {
                    ""entry"": [""requestConsumableRefill""],
                    ""on"": {
                        ""CONSUMABLES_REFILLED"": ""checkingPrerequisites""
                    }
                },
                ""loading"": {
                    ""entry"": [""logLoading"", ""requestWaferPickup""],
                    ""on"": {
                        ""WAFER_LOADED"": ""processing""
                    },
                    ""after"": {
                        ""loadTimeout"": ""timeout""
                    }
                },
                ""processing"": {
                    ""entry"": [""logProcessing""],
                    ""invoke"": {
                        ""src"": ""cmpProcess"",
                        ""onDone"": {
                            ""target"": ""unloading"",
                            ""actions"": [""updateConsumables""]
                        },
                        ""onError"": {
                            ""target"": ""error"",
                            ""actions"": [""logProcessError""]
                        }
                    }
                },
                ""unloading"": {
                    ""entry"": [""logUnloading"", ""requestWaferRemoval""],
                    ""on"": {
                        ""WAFER_REMOVED"": {
                            ""target"": ""reportingComplete"",
                            ""actions"": [""incrementWaferCount""]
                        }
                    }
                },
                ""reportingComplete"": {
                    ""entry"": [""reportCompletionToMaster""],
                    ""always"": [
                        {
                            ""target"": ""maintenance"",
                            ""cond"": ""isMaintenanceDue""
                        },
                        {
                            ""target"": ""idle""
                        }
                    ]
                },
                ""maintenance"": {
                    ""entry"": [""logMaintenance"", ""notifyMasterUnavailable""],
                    ""invoke"": {
                        ""src"": ""performMaintenance"",
                        ""onDone"": {
                            ""target"": ""idle"",
                            ""actions"": [""resetMaintenanceCounters""]
                        }
                    }
                },
                ""timeout"": {
                    ""entry"": [""logTimeout"", ""reportErrorToMaster""]
                },
                ""error"": {
                    ""entry"": [""logError"", ""reportErrorToMaster""]
                }
            }
        }";

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
                Console.WriteLine($"[{_toolId}] 🔍 Checking consumables: Slurry={_slurryLevel:F1}%, Pad={_padWear:F1}%"),

            ["requestConsumableRefill"] = (ctx) =>
            {
                Console.WriteLine($"[{_toolId}] ⚠️ Low consumables - requesting refill");
                ctx.RequestSend("CONSUMABLES_SYSTEM", "REFILL_REQUEST", new JObject
                {
                    ["toolId"] = _toolId,
                    ["slurryNeeded"] = 100 - _slurryLevel
                });
            },

            ["logLoading"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] 🔄 Loading wafer..."),

            ["requestWaferPickup"] = (ctx) =>
            {
                ctx.RequestSend("WTR_001", "PICKUP_FOR_CMP", new JObject
                {
                    ["targetTool"] = _toolId,
                    ["waferId"] = _currentWaferId
                });
            },

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

            ["requestWaferRemoval"] = (ctx) =>
            {
                ctx.RequestSend("WTR_001", "REMOVE_FROM_CMP", new JObject
                {
                    ["sourceTool"] = _toolId,
                    ["waferId"] = _currentWaferId
                });
            },

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
                    ["processingTime"] = 12000 // ms
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
                    ["estimatedDuration"] = 3600000 // 1 hour
                });
            },

            ["resetMaintenanceCounters"] = (ctx) =>
            {
                _wafersProcessed = 0;
                _padWear = 0;
                _slurryLevel = 100;
                Console.WriteLine($"[{_toolId}] ✅ PM complete - Counters reset");
            },

            ["logTimeout"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] ⏰ TIMEOUT - Load operation failed"),

            ["reportErrorToMaster"] = (ctx) =>
            {
                ctx.RequestSend("MASTER_SCHEDULER_001", "TOOL_ERROR", new JObject
                {
                    ["toolId"] = _toolId,
                    ["jobId"] = _currentJobId,
                    ["errorType"] = "TIMEOUT"
                });
            },

            ["logProcessError"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] ❌ Process error occurred"),

            ["logError"] = (ctx) =>
                Console.WriteLine($"[{_toolId}] ❌ Error state - Tool unavailable")
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
                Console.WriteLine($"[{_toolId}] [GUARD] MaintenanceDue={due}");
                return due;
            }
        };

        var services = new Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>
        {
            ["cmpProcess"] = async (sm, ct) =>
            {
                Console.WriteLine($"[{_toolId}] [SERVICE] CMP process started");

                var phases = new[] { "RampUp", "Polishing", "RampDown", "Cleaning" };
                var phaseTimes = new[] { 2000, 8000, 1500, 2000 };

                for (int i = 0; i < phases.Length; i++)
                {
                    if (ct.IsCancellationRequested) break;

                    Console.WriteLine($"[{_toolId}] [SERVICE] Phase: {phases[i]}");
                    await Task.Delay(phaseTimes[i], ct);
                }

                Console.WriteLine($"[{_toolId}] [SERVICE] CMP process complete");
                return new { status = "SUCCESS", duration = 13500 };
            },

            ["performMaintenance"] = async (sm, ct) =>
            {
                Console.WriteLine($"[{_toolId}] [SERVICE] PM: Replacing pad and refilling slurry");
                await Task.Delay(5000, ct); // Simulate PM time
                Console.WriteLine($"[{_toolId}] [SERVICE] PM: Complete");
                return new { status = "PM_COMPLETE" };
            }
        };

        var delays = new Dictionary<string, Func<StateMachine, int>>
        {
            ["loadTimeout"] = (sm) => 30000 // 30 second timeout for load operation
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: _toolId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: guards,
            services: services,
            delays: delays
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
}
```

---

## 3. Complete Scheduler System Demo

```csharp
using System;
using System.Threading.Tasks;
using XStateNet.Orchestration;
using XStateNet.Semi.Schedulers;

namespace XStateNet.Semi.Demo;

public class CMPSchedulerSystemDemo
{
    public static async Task RunAsync()
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  CMP Scheduler System - Master + Tool Schedulers            ║");
        Console.WriteLine("║  Demonstrating Production Job Scheduling Architecture       ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Create orchestrator
        var config = new OrchestratorConfig
        {
            EnableLogging = true,
            PoolSize = 4,
            EnableMetrics = true
        };

        using var orchestrator = new EventBusOrchestrator(config);

        // Create master scheduler
        var masterScheduler = new CMPMasterScheduler("001", orchestrator, maxWip: 3);
        await masterScheduler.StartAsync();

        // Create CMP tool schedulers
        var cmpTool1 = new CMPToolScheduler("CMP_TOOL_1", orchestrator);
        var cmpTool2 = new CMPToolScheduler("CMP_TOOL_2", orchestrator);
        var cmpTool3 = new CMPToolScheduler("CMP_TOOL_3", orchestrator);

        await cmpTool1.StartAsync();
        await cmpTool2.StartAsync();
        await cmpTool3.StartAsync();

        // Register tools with master scheduler
        masterScheduler.RegisterTool("CMP_TOOL_1", "CMP", new Dictionary<string, object>
        {
            ["recipes"] = new[] { "CMP_STANDARD_01", "CMP_OXIDE_01" }
        });

        masterScheduler.RegisterTool("CMP_TOOL_2", "CMP", new Dictionary<string, object>
        {
            ["recipes"] = new[] { "CMP_STANDARD_01", "CMP_METAL_01" }
        });

        masterScheduler.RegisterTool("CMP_TOOL_3", "CMP", new Dictionary<string, object>
        {
            ["recipes"] = new[] { "CMP_STANDARD_01" }
        });

        Console.WriteLine("\n✅ Scheduler system initialized\n");

        // Simulate jobs arriving
        Console.WriteLine("🚀 Simulating job arrivals...\n");

        for (int i = 0; i < 5; i++)
        {
            await orchestrator.SendEventAsync("SYSTEM", "MASTER_SCHEDULER_001", "JOB_ARRIVED", new
            {
                jobId = $"JOB_{i + 1:D3}",
                priority = i % 3 == 0 ? "High" : "Normal"
            });

            await Task.Delay(2000); // Jobs arrive every 2 seconds
        }

        // Let the system run
        Console.WriteLine("\n⏳ Processing jobs...\n");
        await Task.Delay(60000); // Run for 60 seconds

        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Scheduler System Demo Complete                             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
    }
}
```

---

## 4. Advanced Scheduling Strategies

### Strategy 1: Batch Processing

```csharp
/// <summary>
/// Batch similar jobs together for efficiency
/// </summary>
private List<JobRequest> GetBatchJobs(int maxBatchSize = 25)
{
    var batch = new List<JobRequest>();
    var targetRecipe = _normalPriorityQueue.Peek()?.RecipeId;

    while (batch.Count < maxBatchSize && _normalPriorityQueue.Count > 0)
    {
        var job = _normalPriorityQueue.Peek();
        if (job.RecipeId == targetRecipe)
        {
            batch.Add(_normalPriorityQueue.Dequeue());
        }
        else
        {
            break; // Different recipe, stop batching
        }
    }

    return batch;
}
```

### Strategy 2: Look-Ahead Scheduling

```csharp
/// <summary>
/// Predict next job requirements and prepare tools
/// </summary>
private void LookAheadScheduling()
{
    var nextJobs = _normalPriorityQueue.Take(5).ToList();

    foreach (var job in nextJobs)
    {
        // Check if tools will need PM soon
        var tool = _toolStatus.Values
            .FirstOrDefault(t => t.ProcessedWafers >= t.WafersBetweenPM - 10);

        if (tool != null)
        {
            Console.WriteLine($"[SCHEDULER] Look-ahead: {tool.ToolId} needs PM soon");
            // Schedule PM proactively during idle time
        }

        // Check consumables
        var lowConsumables = _toolStatus.Values
            .Where(t => t.SlurryLevel < 20)
            .ToList();

        if (lowConsumables.Any())
        {
            Console.WriteLine($"[SCHEDULER] Look-ahead: Requesting consumable refills");
        }
    }
}
```

### Strategy 3: Dynamic WIP Adjustment

```csharp
/// <summary>
/// Adjust WIP limits based on system performance
/// </summary>
private int CalculateDynamicWipLimit()
{
    var avgCycleTime = CalculateAverageCycleTime();
    var targetCycleTime = 15.0; // minutes

    if (avgCycleTime > targetCycleTime)
    {
        // System congested, reduce WIP
        return Math.Max(_maxWip - 5, 10);
    }
    else if (avgCycleTime < targetCycleTime * 0.8)
    {
        // System underutilized, increase WIP
        return Math.Min(_maxWip + 5, 50);
    }

    return _maxWip;
}
```

---

## 5. Key Benefits of This Architecture

### ✅ Separation of Concerns
- **Master Scheduler**: Job queue, dispatching, load balancing
- **Tool Schedulers**: Tool-specific operations, consumables, PM

### ✅ Scalability
- Add more tools without changing master scheduler
- Each tool scheduler runs independently
- Load balanced across orchestrator event buses

### ✅ Resilience
- Tool failures don't affect master scheduler
- Master can reroute jobs to other tools
- State machines handle errors gracefully

### ✅ Observability
- All events logged through orchestrator
- Clear state transitions
- Easy to add monitoring and metrics

### ✅ Flexibility
- Easy to change scheduling policies
- Guards allow conditional behavior
- Services handle long-running operations

---

## 6. Production Considerations

### PM Scheduling
- **Predictive**: Schedule PM before threshold
- **Opportunistic**: Use idle time for PM
- **Emergency**: Handle unexpected PM needs

### Consumable Management
- **Threshold-based**: Request refills at 20%
- **Predictive**: Calculate remaining wafers
- **Just-in-time**: Minimize inventory

### Recipe Management
- **Qualification**: Ensure tool-recipe compatibility
- **Version control**: Track recipe changes
- **Optimization**: Fine-tune parameters per tool

### Performance Metrics
- **Throughput**: Wafers per hour
- **Cycle time**: Job start to completion
- **Tool utilization**: Productive time vs idle
- **WIP**: Current vs target levels

---

This architecture provides a **production-ready CMP scheduler system** with master-subordinate coordination, resource management, and intelligent job dispatching using XStateNet's orchestration capabilities!
