using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Machines;

/// <summary>
/// Pre-Aligner State Machine
/// Rotates wafer to find notch/flat and align to reference orientation
/// </summary>
public class PreAlignerMachine
{
    private readonly string _alignerId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    public string MachineId => $"PREALIGNER_{_alignerId}";
    public IPureStateMachine Machine => _machine;

    public PreAlignerMachine(string alignerId, EventBusOrchestrator orchestrator)
    {
        _alignerId = alignerId;
        _orchestrator = orchestrator;

        var definition = @"
        {
            ""id"": ""preAligner"",
            ""initial"": ""ready"",
            ""context"": {
                ""waferId"": """",
                ""currentAngle"": 0,
                ""targetAngle"": 0,
                ""notchFound"": false
            },
            ""states"": {
                ""ready"": {
                    ""entry"": [""logReady""],
                    ""on"": {
                        ""WAFER_ARRIVED"": {
                            ""target"": ""scanning"",
                            ""actions"": [""storeWaferId""]
                        }
                    }
                },
                ""scanning"": {
                    ""entry"": [""logScanning"", ""startRotation""],
                    ""after"": {
                        ""1500"": ""notchDetection""
                    }
                },
                ""notchDetection"": {
                    ""entry"": [""logNotchDetected"", ""calculateAlignment""],
                    ""after"": {
                        ""300"": ""aligning""
                    }
                },
                ""aligning"": {
                    ""entry"": [""logAligning"", ""rotateToTarget""],
                    ""after"": {
                        ""1000"": ""verifying""
                    }
                },
                ""verifying"": {
                    ""entry"": [""logVerifying""],
                    ""after"": {
                        ""500"": ""aligned""
                    }
                },
                ""aligned"": {
                    ""entry"": [""logAligned"", ""notifyRobotForBuffer""],
                    ""on"": {
                        ""WAFER_PICKED"": {
                            ""target"": ""ready"",
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
                Console.WriteLine($"[{MachineId}] 📥 Wafer received");
            },

            ["logReady"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] ⚪ Pre-aligner ready"),

            ["logScanning"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔄 Scanning wafer for notch/flat"),

            ["startRotation"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔄 Rotating wafer 360° for notch detection"),

            ["logNotchDetected"] = (ctx) =>
            {
                var angle = Random.Shared.Next(0, 360);
                Console.WriteLine($"[{MachineId}] ✅ Notch detected at {angle}°");
            },

            ["calculateAlignment"] = (ctx) =>
            {
                var targetAngle = 0; // Standard orientation
                Console.WriteLine($"[{MachineId}] 📐 Calculated rotation needed: {targetAngle}°");
            },

            ["logAligning"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔄 Aligning wafer to 0° reference"),

            ["rotateToTarget"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Rotating to target angle: 0°");
            },

            ["logVerifying"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🔍 Verifying alignment accuracy"),

            ["logAligned"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] ✅ Wafer aligned successfully - Accuracy: ±0.1°"),

            ["notifyRobotForBuffer"] = (ctx) =>
            {
                ctx.RequestSend("WTR_001", "TRANSFER_TO_BUFFER", new JObject
                {
                    ["sourceStation"] = MachineId,
                    ["targetStation"] = "BUFFER_001"
                });
                Console.WriteLine($"[{MachineId}] 📤 Requesting robot pickup for buffer transfer");
            },

            ["clearWafer"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Wafer cleared");
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