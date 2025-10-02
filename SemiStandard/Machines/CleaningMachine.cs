using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Machines;

/// <summary>
/// Cleaning Machine State Machine
/// Manages wafer cleaning process with megasonic cleaning
/// </summary>
public class CleaningMachine
{
    private readonly string _cleanId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    public string MachineId => $"CLEAN_{_cleanId}";
    public IPureStateMachine Machine => _machine;

    public CleaningMachine(string cleanId, EventBusOrchestrator orchestrator)
    {
        _cleanId = cleanId;
        _orchestrator = orchestrator;

        var definition = @"
        {
            ""id"": ""cleaning"",
            ""initial"": ""ready"",
            ""context"": { ""waferId"": """" },
            ""states"": {
                ""ready"": {
                    ""entry"": [""logReady""],
                    ""on"": {
                        ""WAFER_ARRIVED"": { ""target"": ""prewet"", ""actions"": [""storeWaferId""] }
                    }
                },
                ""prewet"": {
                    ""entry"": [""logPrewet""],
                    ""after"": { ""800"": ""megasonicClean"" }
                },
                ""megasonicClean"": {
                    ""entry"": [""logMegasonic""],
                    ""after"": { ""2500"": ""diRinse"" }
                },
                ""diRinse"": {
                    ""entry"": [""logDIRinse""],
                    ""after"": { ""1500"": ""complete"" }
                },
                ""complete"": {
                    ""entry"": [""logComplete"", ""requestDryerTransfer""],
                    ""on"": {
                        ""WAFER_PICKED"": { ""target"": ""ready"", ""actions"": [""clearWafer""] }
                    }
                }
            }
        }";

        // Create orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["storeWaferId"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📥 Wafer received");
            },
            ["logReady"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 💧 Cleaning station ready"),

            ["logPrewet"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 💧 Pre-wet with DI water"),

            ["logMegasonic"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔊 Megasonic cleaning @ 1MHz"),

            ["logDIRinse"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 💧 Final DI water rinse"),

            ["logComplete"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] ✅ Cleaning complete"),

            ["requestDryerTransfer"] = (ctx) =>
            {
                ctx.RequestSend("WTR_001", "TRANSFER_TO_DRYER", new JObject
                {
                    ["sourceStation"] = MachineId,
                    ["targetStation"] = "DRYER_001"
                });
                Console.WriteLine($"[{MachineId}] 📤 Requesting dryer transfer");
            },

            ["clearWafer"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Wafer cleared");
            }
        };

        // Create machine using ExtendedPureStateMachineFactory with orchestrator
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