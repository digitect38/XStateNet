using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// Integrated SEMI Equipment State Machine - Orchestrated Version
/// SEMI E30: Generic Equipment Model (GEM) - Equipment Control State Model
///
/// Represents the top-level equipment control state machine that coordinates
/// all SEMI standard machines through the orchestrator event bus.
/// </summary>
public class SemiEquipmentMachine
{
    private readonly string _id;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    public string MachineId => $"EQUIPMENT_{_id}";
    public IPureStateMachine Machine => _machine;

    /// <summary>
    /// Creates a new SEMI Equipment state machine instance
    /// </summary>
    /// <param name="id">Unique equipment identifier</param>
    /// <param name="orchestrator">Event bus orchestrator for inter-machine communication</param>
    public SemiEquipmentMachine(string id, EventBusOrchestrator orchestrator)
    {
        _id = id;
        _orchestrator = orchestrator;

        // Equipment state machine definition
        // Based on SEMI E30 Equipment States Model
        var definition = @"
        {
            ""id"": ""semiEquipment"",
            ""initial"": ""offline"",
            ""context"": {
                ""equipmentId"": """",
                ""controlState"": ""offline"",
                ""processingState"": ""idle"",
                ""alarmState"": ""none"",
                ""spooling"": false
            },
            ""states"": {
                ""offline"": {
                    ""entry"": [""onOfflineEntry""],
                    ""on"": {
                        ""ATTEMPT_ONLINE"": ""local""
                    }
                },
                ""local"": {
                    ""entry"": [""onLocalEntry""],
                    ""on"": {
                        ""GO_REMOTE"": ""remote"",
                        ""GO_OFFLINE"": ""offline""
                    },
                    ""initial"": ""idle"",
                    ""states"": {
                        ""idle"": {
                            ""entry"": [""onIdleEntry""],
                            ""on"": {
                                ""SETUP"": ""setup"",
                                ""EXECUTE"": {
                                    ""target"": ""executing"",
                                    ""cond"": ""isReady""
                                }
                            }
                        },
                        ""setup"": {
                            ""entry"": [""onSetupEntry""],
                            ""on"": {
                                ""SETUP_COMPLETE"": ""ready"",
                                ""ABORT"": ""idle""
                            }
                        },
                        ""ready"": {
                            ""entry"": [""onReadyEntry""],
                            ""on"": {
                                ""EXECUTE"": ""executing"",
                                ""SETUP"": ""setup""
                            }
                        },
                        ""executing"": {
                            ""entry"": [""onExecutingEntry""],
                            ""on"": {
                                ""PAUSE"": ""paused"",
                                ""COMPLETE"": ""completing"",
                                ""ABORT"": ""idle""
                            }
                        },
                        ""paused"": {
                            ""entry"": [""onPausedEntry""],
                            ""on"": {
                                ""RESUME"": ""executing"",
                                ""ABORT"": ""idle""
                            }
                        },
                        ""completing"": {
                            ""entry"": [""onCompletingEntry""],
                            ""on"": {
                                ""COMPLETE_DONE"": ""idle""
                            }
                        }
                    }
                },
                ""remote"": {
                    ""entry"": [""onRemoteEntry""],
                    ""on"": {
                        ""GO_LOCAL"": ""local"",
                        ""GO_OFFLINE"": ""offline""
                    },
                    ""initial"": ""idle"",
                    ""states"": {
                        ""idle"": {
                            ""entry"": [""onIdleEntry""],
                            ""on"": {
                                ""HOST_SETUP"": ""setup"",
                                ""HOST_START"": {
                                    ""target"": ""executing"",
                                    ""cond"": ""isReady""
                                }
                            }
                        },
                        ""setup"": {
                            ""entry"": [""onSetupEntry""],
                            ""on"": {
                                ""SETUP_COMPLETE"": ""ready"",
                                ""HOST_ABORT"": ""idle""
                            }
                        },
                        ""ready"": {
                            ""entry"": [""onReadyEntry""],
                            ""on"": {
                                ""HOST_START"": ""executing"",
                                ""HOST_SETUP"": ""setup""
                            }
                        },
                        ""executing"": {
                            ""entry"": [""onExecutingEntry""],
                            ""on"": {
                                ""HOST_PAUSE"": ""paused"",
                                ""COMPLETE"": ""completing"",
                                ""HOST_ABORT"": ""idle""
                            }
                        },
                        ""paused"": {
                            ""entry"": [""onPausedEntry""],
                            ""on"": {
                                ""HOST_RESUME"": ""executing"",
                                ""HOST_ABORT"": ""idle""
                            }
                        },
                        ""completing"": {
                            ""entry"": [""onCompletingEntry""],
                            ""on"": {
                                ""COMPLETE_DONE"": ""idle""
                            }
                        }
                    }
                }
            }
        }";

        // Orchestrated actions for equipment state machine
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["onOfflineEntry"] = (ctx) =>
            {
                ctx.RequestSend("HOST_SYSTEM", "EQUIPMENT_OFFLINE", new JObject
                {
                    ["equipmentId"] = _id,
                    ["timestamp"] = DateTime.UtcNow
                });
            },

            ["onLocalEntry"] = (ctx) =>
            {
                ctx.RequestSend("HOST_SYSTEM", "EQUIPMENT_LOCAL", new JObject
                {
                    ["equipmentId"] = _id,
                    ["timestamp"] = DateTime.UtcNow
                });
                ctx.RequestSend("E30_GEM", "CONTROL_STATE_CHANGED", new JObject
                {
                    ["state"] = "local"
                });
            },

            ["onRemoteEntry"] = (ctx) =>
            {
                ctx.RequestSend("HOST_SYSTEM", "EQUIPMENT_REMOTE", new JObject
                {
                    ["equipmentId"] = _id,
                    ["timestamp"] = DateTime.UtcNow
                });
                ctx.RequestSend("E30_GEM", "CONTROL_STATE_CHANGED", new JObject
                {
                    ["state"] = "remote"
                });
            },

            ["onIdleEntry"] = (ctx) =>
            {
                ctx.RequestSend("E39_EQUIPMENT_METRICS", "EQUIPMENT_IDLE", new JObject
                {
                    ["equipmentId"] = _id,
                    ["timestamp"] = DateTime.UtcNow
                });
            },

            ["onSetupEntry"] = (ctx) =>
            {
                ctx.RequestSend("E39_EQUIPMENT_METRICS", "EQUIPMENT_SETUP", new JObject
                {
                    ["equipmentId"] = _id,
                    ["timestamp"] = DateTime.UtcNow
                });
            },

            ["onReadyEntry"] = (ctx) =>
            {
                ctx.RequestSend("HOST_SYSTEM", "EQUIPMENT_READY", new JObject
                {
                    ["equipmentId"] = _id,
                    ["timestamp"] = DateTime.UtcNow
                });
            },

            ["onExecutingEntry"] = (ctx) =>
            {
                ctx.RequestSend("E39_EQUIPMENT_METRICS", "EQUIPMENT_PRODUCTIVE", new JObject
                {
                    ["equipmentId"] = _id,
                    ["timestamp"] = DateTime.UtcNow
                });
                ctx.RequestSend("HOST_SYSTEM", "PROCESSING_STARTED", new JObject
                {
                    ["equipmentId"] = _id,
                    ["timestamp"] = DateTime.UtcNow
                });
            },

            ["onPausedEntry"] = (ctx) =>
            {
                ctx.RequestSend("HOST_SYSTEM", "PROCESSING_PAUSED", new JObject
                {
                    ["equipmentId"] = _id,
                    ["timestamp"] = DateTime.UtcNow
                });
            },

            ["onCompletingEntry"] = (ctx) =>
            {
                ctx.RequestSend("HOST_SYSTEM", "PROCESSING_COMPLETING", new JObject
                {
                    ["equipmentId"] = _id,
                    ["timestamp"] = DateTime.UtcNow
                });

                // Auto-complete after brief delay
                Task.Run(async () =>
                {
                    await Task.Delay(500);
                    await _orchestrator.SendEventAsync("SYSTEM", MachineId, "COMPLETE_DONE", null);
                });
            }
        };

        // Guards for conditional transitions
        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["isReady"] = (sm) =>
            {
                // Check if we're in a ready state to execute
                // This is a simplified check - in production, verify actual equipment readiness
                return true;
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
    /// Start the equipment state machine
    /// </summary>
    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    /// <summary>
    /// Get the current state of the equipment
    /// </summary>
    public string GetCurrentState()
    {
        return _machine.CurrentState;
    }

    /// <summary>
    /// Attempt to bring equipment online (to local mode)
    /// </summary>
    public async Task<bool> AttemptOnlineAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "ATTEMPT_ONLINE",
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Switch to remote control mode
    /// </summary>
    public async Task<bool> GoRemoteAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "GO_REMOTE",
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Switch to local control mode
    /// </summary>
    public async Task<bool> GoLocalAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "GO_LOCAL",
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Take equipment offline
    /// </summary>
    public async Task<bool> GoOfflineAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "GO_OFFLINE",
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Start setup process
    /// </summary>
    public async Task<bool> SetupAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "SETUP",
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Complete setup and move to ready
    /// </summary>
    public async Task<bool> SetupCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "SETUP_COMPLETE",
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Start execution (local or remote mode)
    /// </summary>
    public async Task<bool> ExecuteAsync()
    {
        var currentState = GetCurrentState();
        var eventName = currentState.Contains("remote") ? "HOST_START" : "EXECUTE";

        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            eventName,
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Pause execution
    /// </summary>
    public async Task<bool> PauseAsync()
    {
        var currentState = GetCurrentState();
        var eventName = currentState.Contains("remote") ? "HOST_PAUSE" : "PAUSE";

        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            eventName,
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Resume execution
    /// </summary>
    public async Task<bool> ResumeAsync()
    {
        var currentState = GetCurrentState();
        var eventName = currentState.Contains("remote") ? "HOST_RESUME" : "RESUME";

        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            eventName,
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Complete execution
    /// </summary>
    public async Task<bool> CompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "COMPLETE",
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Abort current operation
    /// </summary>
    public async Task<bool> AbortAsync()
    {
        var currentState = GetCurrentState();
        var eventName = currentState.Contains("remote") ? "HOST_ABORT" : "ABORT";

        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            eventName,
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Get current control state (offline/local/remote)
    /// </summary>
    public string GetControlState()
    {
        var currentState = _machine.CurrentState;
        if (currentState.Contains("offline")) return "offline";
        if (currentState.Contains("local")) return "local";
        if (currentState.Contains("remote")) return "remote";
        return "unknown";
    }

    /// <summary>
    /// Get current processing state (idle/setup/ready/executing/paused/completing)
    /// </summary>
    public string GetProcessingState()
    {
        var currentState = _machine.CurrentState;
        if (currentState.Contains("completing")) return "completing";
        if (currentState.Contains("paused")) return "paused";
        if (currentState.Contains("executing")) return "executing";
        if (currentState.Contains("ready")) return "ready";
        if (currentState.Contains("setup")) return "setup";
        if (currentState.Contains("idle")) return "idle";
        return "unknown";
    }
}
