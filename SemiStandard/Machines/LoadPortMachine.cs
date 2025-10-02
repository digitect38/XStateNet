using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Machines;

/// <summary>
/// Load Port State Machine with E84 Protocol
/// Manages wafer carrier loading and E84 handoff sequence
/// </summary>
public class LoadPortMachine
{
    private readonly string _portId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    public string MachineId => $"LOADPORT_{_portId}";
    public IPureStateMachine Machine => _machine;

    public LoadPortMachine(string portId, EventBusOrchestrator orchestrator)
    {
        _portId = portId;
        _orchestrator = orchestrator;

        var definition = @"
        {
            ""id"": ""loadPort"",
            ""initial"": ""idle"",
            ""context"": {
                ""portId"": """",
                ""carrierId"": """",
                ""waferId"": """",
                ""slotNumber"": 1
            },
            ""states"": {
                ""idle"": {
                    ""on"": {
                        ""LOAD_CARRIER"": {
                            ""target"": ""validating"",
                            ""actions"": [""storeCarrierId""]
                        }
                    }
                },
                ""validating"": {
                    ""entry"": [""logValidating"", ""e84_valid""],
                    ""after"": {
                        ""500"": ""waitingCarrierSeated""
                    }
                },
                ""waitingCarrierSeated"": {
                    ""entry"": [""logWaitingCS0"", ""e84_cs0""],
                    ""after"": {
                        ""300"": ""transferRequest""
                    }
                },
                ""transferRequest"": {
                    ""entry"": [""logTransferRequest"", ""e84_tr_req""],
                    ""after"": {
                        ""200"": ""waitingReady""
                    }
                },
                ""waitingReady"": {
                    ""entry"": [""logWaitingReady"", ""e84_ready""],
                    ""after"": {
                        ""300"": ""transferring""
                    }
                },
                ""transferring"": {
                    ""entry"": [""logTransferring"", ""e84_busy""],
                    ""after"": {
                        ""800"": ""transferComplete""
                    }
                },
                ""transferComplete"": {
                    ""entry"": [""logTransferComplete"", ""e84_compt"", ""notifyRobotReady""],
                    ""on"": {
                        ""WAFER_PICKED"": ""idle"",
                        ""LOAD_CARRIER"": {
                            ""target"": ""validating"",
                            ""actions"": [""storeCarrierId""]
                        }
                    }
                }
            }
        }";

        // Create orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["storeCarrierId"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📥 Storing carrier information");
            },
            ["logValidating"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 📍 E84 Step 1: VALID - Carrier is valid"),

            ["e84_valid"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 E84 Signal: VALID=1"),

            ["logWaitingCS0"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 📍 E84 Step 2: Waiting for CS_0 (carrier seated)"),

            ["e84_cs0"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 E84 Signal: CS_0=1"),

            ["logTransferRequest"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 📍 E84 Step 3: TR_REQ (transfer request)"),

            ["e84_tr_req"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 E84 Signal: TR_REQ=1"),

            ["logWaitingReady"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 📍 E84 Step 4: Waiting for READY"),

            ["e84_ready"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 E84 Signal: READY=1"),

            ["logTransferring"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 📍 E84 Step 5: BUSY (transfer in progress)"),

            ["e84_busy"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 E84 Signal: BUSY=1"),

            ["logTransferComplete"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 📍 E84 Step 6: COMPT (transfer complete)"),

            ["e84_compt"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 E84 Signal: COMPT=1"),

            ["notifyRobotReady"] = (ctx) =>
            {
                ctx.RequestSend("WTR_001", "WAFER_READY", new JObject
                {
                    ["waferId"] = $"W{DateTime.Now:HHmmss}",
                    ["sourcePort"] = MachineId
                });
                Console.WriteLine($"[{MachineId}] ✅ E84 Load complete - Notified robot");
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