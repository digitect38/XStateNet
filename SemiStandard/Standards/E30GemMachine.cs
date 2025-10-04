using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// E30 GEM (Generic Equipment Model) Machine - SEMI E30 Standard
/// Implements the GEM communication and control state model for semiconductor equipment
/// Refactored to use ExtendedPureStateMachineFactory with EventBusOrchestrator
/// </summary>
public class E30GemMachine
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;
    private readonly string _instanceId;

    public string MachineId => $"E30_GEM_{_equipmentId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public E30GemMachine(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Inline XState JSON definition (from E30GemStates.json)
        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'disabled',
            context: {
                equipmentId: '',
                communicationMode: 'disabled',
                controlState: 'notSelected',
                onlineState: 'hostOffline'
            },
            states: {
                disabled: {
                    entry: 'logDisabled',
                    on: {
                        ENABLE: 'waitDelay',
                        ENABLE_IMMEDIATE: 'waitCRA'
                    }
                },
                waitDelay: {
                    entry: 'logWaitingDelay',
                    after: {
                        '5000': {
                            target: 'waitCRA'
                        }
                    }
                },
                waitCRA: {
                    entry: 'logWaitingCRA',
                    on: {
                        RECEIVE_S1F13: 'waitCRFromHost',
                        TIMEOUT: 'commFail'
                    },
                    after: {
                        '10000': {
                            target: 'commFail'
                        }
                    }
                },
                waitCRFromHost: {
                    entry: 'logWaitingCRFromHost',
                    on: {
                        SEND_S1F14: 'communicating'
                    }
                },
                communicating: {
                    entry: 'logCommunicating',
                    initial: 'notSelected',
                    on: {
                        DISABLE: 'disabled',
                        COMM_FAIL: 'commFail'
                    },
                    states: {
                        notSelected: {
                            entry: 'logNotSelected',
                            on: {
                                SELECT: 'selected'
                            }
                        },
                        selected: {
                            entry: 'logSelected',
                            initial: 'hostOffline',
                            on: {
                                DESELECT: 'notSelected'
                            },
                            states: {
                                hostOffline: {
                                    entry: 'logHostOffline',
                                    on: {
                                        OFFLINE_REQUEST: 'equipmentOffline',
                                        ONLINE_LOCAL: 'local',
                                        ONLINE_REMOTE: 'remote'
                                    }
                                },
                                equipmentOffline: {
                                    entry: 'logEquipmentOffline',
                                    on: {
                                        ONLINE_LOCAL: 'local',
                                        ONLINE_REMOTE: 'remote'
                                    }
                                },
                                local: {
                                    entry: 'logLocal',
                                    on: {
                                        OFFLINE: 'equipmentOffline',
                                        REMOTE: 'remote'
                                    }
                                },
                                remote: {
                                    entry: 'logRemote',
                                    on: {
                                        OFFLINE: 'equipmentOffline',
                                        LOCAL: 'local'
                                    }
                                }
                            }
                        }
                    }
                },
                commFail: {
                    entry: 'logCommFail',
                    on: {
                        ENABLE: 'waitDelay',
                        DISABLE: 'disabled'
                    }
                }
            }
        }
        """;

        // Orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logDisabled"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] GEM State: DISABLED (Communication not enabled)");

                // Notify host system of disabled state
                ctx.RequestSend("HOST_SYSTEM", "GEM_STATE_CHANGED", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["gemState"] = "DISABLED",
                    ["communicationEnabled"] = false
                });
            },

            ["logWaitingDelay"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] GEM State: Waiting for T1 delay (5 seconds)...");
            },

            ["logWaitingCRA"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] GEM State: Waiting for CRA (Communication Request Acknowledge) from host");

                // Request host to send S1F13
                ctx.RequestSend("HOST_SYSTEM", "REQUEST_S1F13", new JObject
                {
                    ["equipmentId"] = _equipmentId
                });
            },

            ["logWaitingCRFromHost"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] GEM State: Received S1F13, sending S1F14 response...");
            },

            ["logCommunicating"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ GEM State: COMMUNICATING (Link established with host)");

                // Notify all interested parties that communication is established
                ctx.RequestSend("HOST_SYSTEM", "GEM_STATE_CHANGED", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["gemState"] = "COMMUNICATING",
                    ["communicationEnabled"] = true
                });

                ctx.RequestSend("E87_CARRIER_MANAGEMENT", "GEM_COMM_READY", new JObject
                {
                    ["equipmentId"] = _equipmentId
                });

                ctx.RequestSend("E40_PROCESS_JOB", "GEM_COMM_READY", new JObject
                {
                    ["equipmentId"] = _equipmentId
                });
            },

            ["logNotSelected"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] Control State: NOT SELECTED");

                ctx.RequestSend("HOST_SYSTEM", "GEM_CONTROL_STATE_CHANGED", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["controlState"] = "NOT_SELECTED"
                });
            },

            ["logSelected"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] Control State: SELECTED");

                ctx.RequestSend("HOST_SYSTEM", "GEM_CONTROL_STATE_CHANGED", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["controlState"] = "SELECTED"
                });
            },

            ["logHostOffline"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] Online State: HOST OFFLINE");

                ctx.RequestSend("HOST_SYSTEM", "GEM_ONLINE_STATE_CHANGED", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["onlineState"] = "HOST_OFFLINE"
                });
            },

            ["logEquipmentOffline"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] Online State: EQUIPMENT OFFLINE");

                ctx.RequestSend("HOST_SYSTEM", "GEM_ONLINE_STATE_CHANGED", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["onlineState"] = "EQUIPMENT_OFFLINE"
                });
            },

            ["logLocal"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⚙️ Online State: LOCAL (Operator control)");

                ctx.RequestSend("HOST_SYSTEM", "GEM_ONLINE_STATE_CHANGED", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["onlineState"] = "LOCAL",
                    ["hostControl"] = false
                });

                // Notify process job manager that host control is disabled
                ctx.RequestSend("E40_PROCESS_JOB", "HOST_CONTROL_DISABLED", new JObject
                {
                    ["equipmentId"] = _equipmentId
                });
            },

            ["logRemote"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🖥️ Online State: REMOTE (Host control enabled)");

                ctx.RequestSend("HOST_SYSTEM", "GEM_ONLINE_STATE_CHANGED", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["onlineState"] = "REMOTE",
                    ["hostControl"] = true
                });

                // Notify process job manager that host control is enabled
                ctx.RequestSend("E40_PROCESS_JOB", "HOST_CONTROL_ENABLED", new JObject
                {
                    ["equipmentId"] = _equipmentId
                });
            },

            ["logCommFail"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ❌ GEM State: COMMUNICATION FAILED");

                ctx.RequestSend("HOST_SYSTEM", "GEM_COMM_FAILED", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["reason"] = "TIMEOUT_WAITING_FOR_CRA"
                });

                // Notify dependent systems of communication failure
                ctx.RequestSend("E87_CARRIER_MANAGEMENT", "GEM_COMM_FAILED", new JObject
                {
                    ["equipmentId"] = _equipmentId
                });
            }
        };

        // Guards for state validation
        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["canEnableCommunication"] = (sm) =>
            {
                // Check if communication prerequisites are met
                return true; // Placeholder - would check network status, configuration, etc.
            },

            ["isHostResponsive"] = (sm) =>
            {
                // Check if host is responding
                return true; // Placeholder - would check actual host connectivity
            }
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

    // Public API methods for GEM control

    /// <summary>
    /// Enable communication (with T1 delay)
    /// </summary>
    public async Task<EventResult> EnableAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "ENABLE",
            null
        );
        return result;
    }

    /// <summary>
    /// Enable communication immediately (no T1 delay)
    /// </summary>
    public async Task<EventResult> EnableImmediateAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "ENABLE_IMMEDIATE",
            null
        );
        return result;
    }

    /// <summary>
    /// Disable communication
    /// </summary>
    public async Task<EventResult> DisableAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "DISABLE",
            null
        );
        return result;
    }

    /// <summary>
    /// Handle S1F13 communication request from host
    /// </summary>
    public async Task<EventResult> ReceiveS1F13Async()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "RECEIVE_S1F13",
            null
        );
        return result;
    }

    /// <summary>
    /// Send S1F14 communication acknowledge to host
    /// </summary>
    public async Task<EventResult> SendS1F14Async()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "SEND_S1F14",
            null
        );
        return result;
    }

    /// <summary>
    /// Select equipment for host control
    /// </summary>
    public async Task<EventResult> SelectAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "SELECT",
            null
        );
        return result;
    }

    /// <summary>
    /// Deselect equipment from host control
    /// </summary>
    public async Task<EventResult> DeselectAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "DESELECT",
            null
        );
        return result;
    }

    /// <summary>
    /// Go online in local mode (operator control)
    /// </summary>
    public async Task<EventResult> GoOnlineLocalAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "ONLINE_LOCAL",
            null
        );
        return result;
    }

    /// <summary>
    /// Go online in remote mode (host control)
    /// </summary>
    public async Task<EventResult> GoOnlineRemoteAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "ONLINE_REMOTE",
            null
        );
        return result;
    }

    /// <summary>
    /// Go offline
    /// </summary>
    public async Task<EventResult> GoOfflineAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "OFFLINE",
            null
        );
        return result;
    }

    /// <summary>
    /// Switch from local to remote mode
    /// </summary>
    public async Task<EventResult> SwitchToRemoteAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "REMOTE",
            null
        );
        return result;
    }

    /// <summary>
    /// Switch from remote to local mode
    /// </summary>
    public async Task<EventResult> SwitchToLocalAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "LOCAL",
            null
        );
        return result;
    }

    /// <summary>
    /// Signal communication failure
    /// </summary>
    public async Task<EventResult> SignalCommFailureAsync()
    {
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            MachineId,
            "COMM_FAIL",
            null
        );
        return result;
    }
}
