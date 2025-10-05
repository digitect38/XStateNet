using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Machines;

/// <summary>
/// Inspection Machine State Machine
/// Manages wafer inspection process with optical and particle scanning
/// </summary>
public class InspectionMachine
{
    private readonly string _inspectionId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    public string MachineId => $"INSPECTION_{_inspectionId}";
    public IPureStateMachine Machine => _machine;

    public InspectionMachine(string inspectionId, EventBusOrchestrator orchestrator)
    {
        _inspectionId = inspectionId;
        _orchestrator = orchestrator;

        var definition = @"
        {
            ""id"": ""inspection"",
            ""initial"": ""ready"",
            ""context"": { ""waferId"": """", ""defectCount"": 0, ""passed"": false },
            ""states"": {
                ""ready"": {
                    ""entry"": [""logReady""],
                    ""on"": {
                        ""WAFER_ARRIVED"": { ""target"": ""opticalScan"", ""actions"": [""storeWaferId""] }
                    }
                },
                ""opticalScan"": {
                    ""entry"": [""logOpticalScan""],
                    ""after"": { ""1500"": ""particleScan"" }
                },
                ""particleScan"": {
                    ""entry"": [""logParticleScan""],
                    ""after"": { ""1200"": ""analysis"" }
                },
                ""analysis"": {
                    ""entry"": [""logAnalysis"", ""analyzeResults""],
                    ""after"": { ""800"": ""passed"" }
                },
                ""passed"": {
                    ""entry"": [""logPassed"", ""requestUnloadTransfer""],
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
                Console.WriteLine($"[{MachineId}] 🔍 Inspection station ready"),

            ["logOpticalScan"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔍 Optical surface scan"),

            ["logParticleScan"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔍 Particle detection scan"),

            ["logAnalysis"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔍 Analyzing scan results"),

            ["analyzeResults"] = (ctx) =>
            {
                var defects = Random.Shared.Next(0, 3);
                Console.WriteLine($"[{MachineId}] 🔧 Defects found: {defects}");
                Console.WriteLine($"[{MachineId}] 🔧 Wafer Quality: {(defects < 5 ? "PASS" : "FAIL")}");
            },

            ["logPassed"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] ✅ Inspection passed"),

            ["requestUnloadTransfer"] = (ctx) =>
            {
                ctx.RequestSend("WTR_001", "TRANSFER_TO_UNLOAD", new JObject
                {
                    ["sourceStation"] = MachineId,
                    ["targetStation"] = "UNLOADPORT_001"
                });
                Console.WriteLine($"[{MachineId}] 📤 Requesting unload transfer");
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