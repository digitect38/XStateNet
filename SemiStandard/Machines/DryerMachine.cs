using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Machines;

/// <summary>
/// Dryer Machine State Machine
/// Manages wafer drying process with spin drying
/// </summary>
public class DryerMachine
{
    private readonly string _dryerId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    public string MachineId => $"DRYER_{_dryerId}";
    public IPureStateMachine Machine => _machine;

    public DryerMachine(string dryerId, EventBusOrchestrator orchestrator)
    {
        _dryerId = dryerId;
        _orchestrator = orchestrator;

        var definition = @"
        {
            ""id"": ""dryer"",
            ""initial"": ""ready"",
            ""context"": { ""waferId"": """", ""spinRPM"": 0 },
            ""states"": {
                ""ready"": {
                    ""entry"": [""logReady""],
                    ""on"": {
                        ""WAFER_ARRIVED"": { ""target"": ""lowSpinRinse"", ""actions"": [""storeWaferId""] }
                    }
                },
                ""lowSpinRinse"": {
                    ""entry"": [""logLowSpin"", ""setLowSpin""],
                    ""after"": { ""1000"": ""highSpinDry"" }
                },
                ""highSpinDry"": {
                    ""entry"": [""logHighSpin"", ""setHighSpin""],
                    ""after"": { ""2500"": ""marangoEffect"" }
                },
                ""marangoEffect"": {
                    ""entry"": [""logMarango""],
                    ""after"": { ""1000"": ""spinDown"" }
                },
                ""spinDown"": {
                    ""entry"": [""logSpinDown"", ""stopSpin""],
                    ""after"": { ""800"": ""dry"" }
                },
                ""dry"": {
                    ""entry"": [""logDry"", ""requestInspectionTransfer""],
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
                Console.WriteLine($"[{MachineId}] 🌀 Spin dryer ready"),

            ["logLowSpin"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🌀 Low spin rinse"),

            ["setLowSpin"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 Spin: 500 RPM"),

            ["logHighSpin"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🌀 High speed spin dry"),

            ["setHighSpin"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 Spin: 3000 RPM"),

            ["logMarango"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 Marango effect drying (IPA vapor)"),

            ["logSpinDown"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🌀 Spinning down"),

            ["stopSpin"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔧 Spin: 0 RPM"),

            ["logDry"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] ✅ Wafer completely dry"),

            ["requestInspectionTransfer"] = (ctx) =>
            {
                ctx.RequestSend("WTR_001", "TRANSFER_TO_INSPECTION", new JObject
                {
                    ["sourceStation"] = MachineId,
                    ["targetStation"] = "INSPECTION_001"
                });
                Console.WriteLine($"[{MachineId}] 📤 Requesting inspection transfer");
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