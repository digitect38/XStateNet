using XStateNet.Orchestration;

namespace XStateNet.Semi.Machines;

/// <summary>
/// Unload Port State Machine with E84 Protocol
/// Manages wafer unloading and E84 handoff sequence
/// </summary>
public class UnloadPortMachine
{
    private readonly string _portId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    public string MachineId => $"UNLOADPORT_{_portId}";
    public IPureStateMachine Machine => _machine;

    public UnloadPortMachine(string portId, EventBusOrchestrator orchestrator)
    {
        _portId = portId;
        _orchestrator = orchestrator;

        var definition = @"
        {
            ""id"": ""unloadPort"",
            ""initial"": ""idle"",
            ""context"": {
                ""portId"": """",
                ""carrierId"": """",
                ""waferId"": """"
            },
            ""states"": {
                ""idle"": {
                    ""entry"": [""logIdle""],
                    ""on"": {
                        ""WAFER_ARRIVED"": {
                            ""target"": ""validating"",
                            ""actions"": [""storeWaferId"", ""e90TrackUnload""]
                        }
                    }
                },
                ""validating"": {
                    ""entry"": [""logValidating"", ""e84_valid""],
                    ""after"": {
                        ""400"": ""transferRequest""
                    }
                },
                ""transferRequest"": {
                    ""entry"": [""logTransferRequest"", ""e84_tr_req""],
                    ""after"": {
                        ""300"": ""waitingReady""
                    }
                },
                ""waitingReady"": {
                    ""entry"": [""logWaitingReady"", ""e84_ready""],
                    ""after"": {
                        ""200"": ""transferring""
                    }
                },
                ""transferring"": {
                    ""entry"": [""logTransferring"", ""e84_busy""],
                    ""after"": {
                        ""1000"": ""transferComplete""
                    }
                },
                ""transferComplete"": {
                    ""entry"": [""logTransferComplete"", ""e84_compt"", ""e87Unbind""],
                    ""after"": {
                        ""500"": ""complete""
                    }
                },
                ""complete"": {
                    ""entry"": [""logComplete""],
                    ""after"": {
                        ""1000"": {
                            ""target"": ""idle"",
                            ""actions"": [""clearWafer""]
                        }
                    }
                }
            }
        }";

        // Create orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["storeWaferId"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📥 Wafer arrived for unload");
            },
            ["logIdle"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 📤 Unload port ready"),

            ["e90TrackUnload"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔧 E90: Tracking substrate unload");
                Console.WriteLine($"[{MachineId}] 🔧 E90: Location=UNLOAD_PORT, Event=UNLOAD, Time={DateTime.UtcNow:HH:mm:ss}");
            },

            ["logValidating"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 📍 E84 Unload Step 1: VALID"),

            ["e84_valid"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 E84 Signal: VALID=1"),

            ["logTransferRequest"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 📍 E84 Unload Step 2: TR_REQ"),

            ["e84_tr_req"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 E84 Signal: TR_REQ=1"),

            ["logWaitingReady"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 📍 E84 Unload Step 3: Waiting READY"),

            ["e84_ready"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 E84 Signal: READY=1"),

            ["logTransferring"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 📍 E84 Unload Step 4: BUSY (transferring to carrier)"),

            ["e84_busy"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 E84 Signal: BUSY=1"),

            ["logTransferComplete"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 📍 E84 Unload Step 5: COMPT (transfer complete)"),

            ["e84_compt"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 E84 Signal: COMPT=1"),

            ["e87Unbind"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔧 E87: Unbinding substrate from carrier");
                Console.WriteLine($"[{MachineId}] ✅ E87: Unbind complete");
            },

            ["logComplete"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Wafer unloaded successfully");
                Console.WriteLine($"[{MachineId}] ✅ ========== WAFER JOURNEY COMPLETE ==========");
            },

            ["clearWafer"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Wafer cleared");
            }
        };

        // Create machine using PureStateMachineFactory with orchestrator
        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: null,
            services: null,
            delays: null,
            activities: null);
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