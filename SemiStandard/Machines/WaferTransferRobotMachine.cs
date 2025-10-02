using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Machines;

/// <summary>
/// Wafer Transfer Robot (WTR) State Machine
/// Handles wafer movement between all stations
/// </summary>
public class WaferTransferRobotMachine
{
    private readonly string _robotId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    public string MachineId => $"WTR_{_robotId}";
    public IPureStateMachine Machine => _machine;

    public WaferTransferRobotMachine(string robotId, EventBusOrchestrator orchestrator)
    {
        _robotId = robotId;
        _orchestrator = orchestrator;

        var definition = @"
        {
            ""id"": ""waferTransferRobot"",
            ""initial"": ""idle"",
            ""context"": {
                ""waferId"": """",
                ""sourceStation"": """",
                ""targetStation"": """",
                ""hasWafer"": false
            },
            ""states"": {
                ""idle"": {
                    ""entry"": [""logIdle""],
                    ""on"": {
                        ""WAFER_READY"": {
                            ""target"": ""pickingFromLoadPort"",
                            ""actions"": [""storeWaferInfo""]
                        },
                        ""TRANSFER_TO_ALIGNER"": {
                            ""target"": ""movingToAligner"",
                            ""actions"": [""storeTransferInfo""]
                        },
                        ""TRANSFER_TO_BUFFER"": {
                            ""target"": ""movingToBuffer"",
                            ""actions"": [""storeTransferInfo""]
                        },
                        ""TRANSFER_TO_CMP"": {
                            ""target"": ""movingToCMP"",
                            ""actions"": [""storeTransferInfo""]
                        },
                        ""TRANSFER_TO_CLEAN"": {
                            ""target"": ""movingToClean"",
                            ""actions"": [""storeTransferInfo""]
                        },
                        ""TRANSFER_TO_DRYER"": {
                            ""target"": ""movingToDryer"",
                            ""actions"": [""storeTransferInfo""]
                        },
                        ""TRANSFER_TO_INSPECTION"": {
                            ""target"": ""movingToInspection"",
                            ""actions"": [""storeTransferInfo""]
                        },
                        ""TRANSFER_TO_UNLOAD"": {
                            ""target"": ""movingToUnload"",
                            ""actions"": [""storeTransferInfo""]
                        }
                    }
                },
                ""pickingFromLoadPort"": {
                    ""entry"": [""logPickingFromLoadPort"", ""notifyLoadPortPicked""],
                    ""after"": {
                        ""800"": {
                            ""target"": ""movingToAligner"",
                            ""actions"": [""setHasWafer""]
                        }
                    }
                },
                ""movingToAligner"": {
                    ""entry"": [""logMovingToAligner""],
                    ""after"": {
                        ""1000"": ""placingAtAligner""
                    }
                },
                ""placingAtAligner"": {
                    ""entry"": [""logPlacingAtAligner"", ""notifyAlignerReady""],
                    ""after"": {
                        ""600"": {
                            ""target"": ""idle"",
                            ""actions"": [""clearWafer""]
                        }
                    }
                },
                ""movingToBuffer"": {
                    ""entry"": [""logMovingToBuffer""],
                    ""after"": {
                        ""1200"": ""placingAtBuffer""
                    }
                },
                ""placingAtBuffer"": {
                    ""entry"": [""logPlacingAtBuffer"", ""notifyBufferReady""],
                    ""after"": {
                        ""600"": {
                            ""target"": ""idle"",
                            ""actions"": [""clearWafer""]
                        }
                    }
                },
                ""movingToCMP"": {
                    ""entry"": [""logMovingToCMP""],
                    ""after"": {
                        ""1500"": ""placingAtCMP""
                    }
                },
                ""placingAtCMP"": {
                    ""entry"": [""logPlacingAtCMP"", ""notifyCMPReady""],
                    ""after"": {
                        ""800"": {
                            ""target"": ""idle"",
                            ""actions"": [""clearWafer""]
                        }
                    }
                },
                ""movingToClean"": {
                    ""entry"": [""logMovingToClean""],
                    ""after"": {
                        ""1000"": ""placingAtClean""
                    }
                },
                ""placingAtClean"": {
                    ""entry"": [""logPlacingAtClean"", ""notifyCleanReady""],
                    ""after"": {
                        ""600"": {
                            ""target"": ""idle"",
                            ""actions"": [""clearWafer""]
                        }
                    }
                },
                ""movingToDryer"": {
                    ""entry"": [""logMovingToDryer""],
                    ""after"": {
                        ""900"": ""placingAtDryer""
                    }
                },
                ""placingAtDryer"": {
                    ""entry"": [""logPlacingAtDryer"", ""notifyDryerReady""],
                    ""after"": {
                        ""600"": {
                            ""target"": ""idle"",
                            ""actions"": [""clearWafer""]
                        }
                    }
                },
                ""movingToInspection"": {
                    ""entry"": [""logMovingToInspection""],
                    ""after"": {
                        ""1000"": ""placingAtInspection""
                    }
                },
                ""placingAtInspection"": {
                    ""entry"": [""logPlacingAtInspection"", ""notifyInspectionReady""],
                    ""after"": {
                        ""600"": {
                            ""target"": ""idle"",
                            ""actions"": [""clearWafer""]
                        }
                    }
                },
                ""movingToUnload"": {
                    ""entry"": [""logMovingToUnload""],
                    ""after"": {
                        ""1200"": ""placingAtUnload""
                    }
                },
                ""placingAtUnload"": {
                    ""entry"": [""logPlacingAtUnload"", ""notifyUnloadReady""],
                    ""after"": {
                        ""600"": {
                            ""target"": ""idle"",
                            ""actions"": [""clearWafer""]
                        }
                    }
                }
            }
        }";

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["storeWaferInfo"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📥 Received wafer ready");
            },
            ["storeTransferInfo"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📥 Transfer request received");
            },
            ["setHasWafer"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Wafer secured");
            },
            ["clearWafer"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Wafer released");
            },
            ["logIdle"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖 Robot ready for next transfer"),

            ["logPickingFromLoadPort"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖 Picking wafer from load port"),

            ["notifyLoadPortPicked"] = (ctx) =>
            {
                ctx.RequestSend("LOADPORT_001", "WAFER_PICKED", new JObject());
                Console.WriteLine($"[{MachineId}] ✅ Notified load port: WAFER_PICKED");
            },

            ["logMovingToAligner"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖➡️ Moving to pre-aligner"),

            ["logPlacingAtAligner"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖 Placing wafer at pre-aligner"),

            ["notifyAlignerReady"] = (ctx) =>
            {
                ctx.RequestSend("PREALIGNER_001", "WAFER_ARRIVED", new JObject());
                Console.WriteLine($"[{MachineId}] ✅ Notified pre-aligner: WAFER_ARRIVED");
            },

            ["logMovingToBuffer"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖➡️ Moving to buffer"),

            ["logPlacingAtBuffer"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖 Placing wafer at buffer"),

            ["notifyBufferReady"] = (ctx) =>
            {
                ctx.RequestSend("BUFFER_001", "WAFER_ARRIVED", new JObject());
                Console.WriteLine($"[{MachineId}] ✅ Notified buffer: WAFER_ARRIVED");
            },

            ["logMovingToCMP"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖➡️ Moving to CMP station"),

            ["logPlacingAtCMP"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖 Placing wafer at CMP"),

            ["notifyCMPReady"] = (ctx) =>
            {
                ctx.RequestSend("CMP_001", "WAFER_ARRIVED", new JObject());
                Console.WriteLine($"[{MachineId}] ✅ Notified CMP: WAFER_ARRIVED");
            },

            ["logMovingToClean"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖➡️ Moving to cleaning station"),

            ["logPlacingAtClean"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖 Placing wafer at cleaning station"),

            ["notifyCleanReady"] = (ctx) =>
            {
                ctx.RequestSend("CLEAN_001", "WAFER_ARRIVED", new JObject());
                Console.WriteLine($"[{MachineId}] ✅ Notified cleaning: WAFER_ARRIVED");
            },

            ["logMovingToDryer"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖➡️ Moving to dryer"),

            ["logPlacingAtDryer"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖 Placing wafer at dryer"),

            ["notifyDryerReady"] = (ctx) =>
            {
                ctx.RequestSend("DRYER_001", "WAFER_ARRIVED", new JObject());
                Console.WriteLine($"[{MachineId}] ✅ Notified dryer: WAFER_ARRIVED");
            },

            ["logMovingToInspection"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖➡️ Moving to inspection"),

            ["logPlacingAtInspection"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖 Placing wafer at inspection"),

            ["notifyInspectionReady"] = (ctx) =>
            {
                ctx.RequestSend("INSPECTION_001", "WAFER_ARRIVED", new JObject());
                Console.WriteLine($"[{MachineId}] ✅ Notified inspection: WAFER_ARRIVED");
            },

            ["logMovingToUnload"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖➡️ Moving to unload port"),

            ["logPlacingAtUnload"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 🤖 Placing wafer at unload port"),

            ["notifyUnloadReady"] = (ctx) =>
            {
                ctx.RequestSend("UNLOADPORT_001", "WAFER_ARRIVED", new JObject());
                Console.WriteLine($"[{MachineId}] ✅ Notified unload port: WAFER_ARRIVED");
            }
        };

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