using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// E84 Handoff Machine - SEMI E84 Parallel I/O Protocol for Load Ports
/// Implements automated material handling handoff between equipment and material transport
/// Refactored to use ExtendedPureStateMachineFactory with EventBusOrchestrator
/// </summary>
public class E84HandoffMachine
{
    private readonly string _portId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    // E84 signal states
    private bool _loadRequest;
    private bool _unloadRequest;
    private bool _ready;
    private bool _hoAvailable;
    private bool _esInterlock;

    public string MachineId => $"E84_HANDOFF_{_portId}";
    public IPureStateMachine Machine => _machine;

    // Public properties for E84 signals
    public bool LoadRequest => _loadRequest;
    public bool UnloadRequest => _unloadRequest;
    public bool Ready => _ready;
    public bool HoAvailable => _hoAvailable;
    public bool EsInterlock => _esInterlock;

    public E84HandoffMachine(string portId, EventBusOrchestrator orchestrator)
    {
        _portId = portId;
        _orchestrator = orchestrator;

        // Inline XState JSON definition (from E84HandoffStates.json)
        var definition = @"{
            ""id"": ""E84HandoffStateMachine"",
            ""initial"": ""idle"",
            ""context"": {
                ""portId"": """",
                ""loadRequest"": false,
                ""unloadRequest"": false,
                ""ready"": false,
                ""hoAvailable"": false,
                ""esInterlock"": false
            },
            ""states"": {
                ""idle"": {
                    ""on"": {
                        ""CS_0_ON"": ""notReady"",
                        ""VALID_ON"": ""waitingForTransfer""
                    }
                },
                ""notReady"": {
                    ""on"": {
                        ""CS_0_OFF"": ""idle"",
                        ""READY"": ""readyToLoad""
                    }
                },
                ""readyToLoad"": {
                    ""entry"": ""setLoadRequest"",
                    ""on"": {
                        ""CS_0_OFF"": ""idle"",
                        ""TR_REQ_ON"": ""transferReady"",
                        ""TIMEOUT"": ""transferBlocked""
                    },
                    ""after"": {
                        ""30000"": {
                            ""target"": ""transferBlocked""
                        }
                    }
                },
                ""transferReady"": {
                    ""entry"": ""setReady"",
                    ""on"": {
                        ""BUSY_ON"": ""transferring"",
                        ""TR_REQ_OFF"": ""readyToLoad"",
                        ""TIMEOUT"": ""transferBlocked""
                    },
                    ""after"": {
                        ""30000"": {
                            ""target"": ""transferBlocked""
                        }
                    }
                },
                ""transferring"": {
                    ""on"": {
                        ""BUSY_OFF"": ""transferComplete"",
                        ""COMPT_ON"": ""transferComplete""
                    }
                },
                ""transferComplete"": {
                    ""entry"": ""clearLoadRequest"",
                    ""exit"": ""clearReady"",
                    ""on"": {
                        ""COMPT_OFF"": ""idle"",
                        ""TR_REQ_OFF"": ""idle""
                    }
                },
                ""waitingForTransfer"": {
                    ""on"": {
                        ""VALID_OFF"": ""idle"",
                        ""TR_REQ_ON"": ""readyToUnload""
                    }
                },
                ""readyToUnload"": {
                    ""entry"": ""setUnloadRequest"",
                    ""on"": {
                        ""VALID_OFF"": ""idle"",
                        ""BUSY_ON"": ""unloading"",
                        ""TIMEOUT"": ""transferBlocked""
                    },
                    ""after"": {
                        ""30000"": {
                            ""target"": ""transferBlocked""
                        }
                    }
                },
                ""unloading"": {
                    ""on"": {
                        ""BUSY_OFF"": ""unloadComplete"",
                        ""COMPT_ON"": ""unloadComplete""
                    }
                },
                ""unloadComplete"": {
                    ""entry"": ""clearUnloadRequest"",
                    ""on"": {
                        ""COMPT_OFF"": ""idle"",
                        ""TR_REQ_OFF"": ""idle"",
                        ""VALID_OFF"": ""idle""
                    }
                },
                ""transferBlocked"": {
                    ""entry"": ""setAlarm"",
                    ""exit"": ""clearAlarm"",
                    ""on"": {
                        ""RESET"": ""idle"",
                        ""CS_0_OFF"": ""idle""
                    }
                }
            }
        }";

        // Orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["setLoadRequest"] = (ctx) =>
            {
                _loadRequest = true;
                Console.WriteLine($"[{MachineId}] E84 Load Request ON");

                // Notify E87 Carrier Management if needed
                ctx.RequestSend("E87_CARRIER_MANAGEMENT", "LOAD_PORT_READY", new JObject
                {
                    ["portId"] = _portId,
                    ["requestType"] = "LOAD"
                });
            },

            ["clearLoadRequest"] = (ctx) =>
            {
                _loadRequest = false;
                Console.WriteLine($"[{MachineId}] E84 Load Request OFF");
            },

            ["setUnloadRequest"] = (ctx) =>
            {
                _unloadRequest = true;
                Console.WriteLine($"[{MachineId}] E84 Unload Request ON");

                // Notify E87 Carrier Management if needed
                ctx.RequestSend("E87_CARRIER_MANAGEMENT", "LOAD_PORT_READY", new JObject
                {
                    ["portId"] = _portId,
                    ["requestType"] = "UNLOAD"
                });
            },

            ["clearUnloadRequest"] = (ctx) =>
            {
                _unloadRequest = false;
                Console.WriteLine($"[{MachineId}] E84 Unload Request OFF");
            },

            ["setReady"] = (ctx) =>
            {
                _ready = true;
                _hoAvailable = true;
                Console.WriteLine($"[{MachineId}] E84 Ready ON (HO Available)");
            },

            ["clearReady"] = (ctx) =>
            {
                _ready = false;
                _hoAvailable = false;
                Console.WriteLine($"[{MachineId}] E84 Ready OFF (HO Not Available)");
            },

            ["setAlarm"] = (ctx) =>
            {
                _esInterlock = true;
                Console.WriteLine($"[{MachineId}] ❌ E84 Transfer BLOCKED - ES Interlock ON");

                // Notify equipment controller of alarm condition
                ctx.RequestSend("EQUIPMENT_CONTROLLER", "E84_ALARM", new JObject
                {
                    ["portId"] = _portId,
                    ["alarmType"] = "TRANSFER_TIMEOUT"
                });
            },

            ["clearAlarm"] = (ctx) =>
            {
                _esInterlock = false;
                Console.WriteLine($"[{MachineId}] ✅ E84 Transfer alarm cleared - ES Interlock OFF");
            }
        };

        // Guards for E84 signal validation (not currently used in JSON definition)
        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["isTransferSafe"] = (sm) =>
            {
                // Check if transfer is safe (no interlock conditions)
                return !_esInterlock;
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

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public string GetCurrentState()
    {
        return _machine.CurrentState;
    }

    // Public API methods for E84 signal control

    /// <summary>
    /// Signal carrier stage 0 sensor (CS_0)
    /// </summary>
    public async Task<bool> SetCS0Async(bool on)
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            on ? "CS_0_ON" : "CS_0_OFF",
            new JObject { ["cs0"] = on }
        );
        return result.Success;
    }

    /// <summary>
    /// Signal valid carrier (VALID)
    /// </summary>
    public async Task<bool> SetValidAsync(bool on)
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            on ? "VALID_ON" : "VALID_OFF",
            new JObject { ["valid"] = on }
        );
        return result.Success;
    }

    /// <summary>
    /// Signal transfer request from AGV/OHT (TR_REQ)
    /// </summary>
    public async Task<bool> SetTransferRequestAsync(bool on)
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            on ? "TR_REQ_ON" : "TR_REQ_OFF",
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Signal busy status (BUSY)
    /// </summary>
    public async Task<bool> SetBusyAsync(bool on)
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            on ? "BUSY_ON" : "BUSY_OFF",
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Signal transfer complete (COMPT)
    /// </summary>
    public async Task<bool> SetCompleteAsync(bool on)
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            on ? "COMPT_ON" : "COMPT_OFF",
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Signal ready to load/unload (READY)
    /// </summary>
    public async Task<bool> SetReadyAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "READY",
            null
        );
        return result.Success;
    }

    /// <summary>
    /// Reset handoff controller to idle state
    /// </summary>
    public async Task<bool> ResetAsync()
    {
        // Clear all signals
        _loadRequest = false;
        _unloadRequest = false;
        _ready = false;
        _hoAvailable = false;
        _esInterlock = false;

        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "RESET",
            null
        );
        return result.Success;
    }
}
