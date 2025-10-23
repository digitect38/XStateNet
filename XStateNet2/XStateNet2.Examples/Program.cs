using Akka.Actor;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

Console.WriteLine("=== XStateNet2 Examples ===\n");

// Create actor system
var actorSystem = ActorSystem.Create("XStateNet2Examples");

try
{
    Console.WriteLine("1. Simple Robot Example (Assign + Send actions)");
    await RunRobotExample(actorSystem);

    Console.WriteLine("\n2. Traffic Light Example (Delayed transitions)");
    await RunTrafficLightExample(actorSystem);

    Console.WriteLine("\n3. Always Transitions Example (Eventless)");
    await RunAlwaysExample(actorSystem);

    Console.WriteLine("\n4. Parallel States Example (Actor-per-region)");
    await RunParallelExample(actorSystem);

    Console.WriteLine("\n5. RobotMachineV2 Example (CMP Simulator Migration)");
    await RunRobotV2Example(actorSystem);
}
finally
{
    await actorSystem.Terminate();
}

Console.WriteLine("\n=== Examples Complete ===");

static async Task RunRobotExample(ActorSystem system)
{
    // Define a simple robot state machine with assign and send actions
    var robotJson = """
    {
        "id": "robot",
        "initial": "idle",
        "context": {
            "heldWafer": null,
            "pickFrom": null,
            "placeTo": null
        },
        "states": {
            "idle": {
                "on": {
                    "TRANSFER": {
                        "target": "movingToPickup",
                        "actions": [
                            {
                                "type": "assign",
                                "assignment": {
                                    "heldWafer": 1,
                                    "pickFrom": "LoadPort",
                                    "placeTo": "polisher"
                                }
                            },
                            "logTransfer"
                        ]
                    }
                }
            },
            "movingToPickup": {
                "entry": ["logMovingToPickup"],
                "invoke": {
                    "src": "moveDelay",
                    "onDone": {
                        "target": "pickingWafer"
                    }
                }
            },
            "pickingWafer": {
                "entry": [
                    "logPickingWafer",
                    {
                        "type": "send",
                        "event": "PICK_COMPLETE",
                        "to": "scheduler"
                    }
                ],
                "on": {
                    "PICK_COMPLETE": {
                        "target": "movingToPlace"
                    }
                }
            },
            "movingToPlace": {
                "entry": ["logMovingToPlace"],
                "invoke": {
                    "src": "moveDelay",
                    "onDone": {
                        "target": "placingWafer"
                    }
                }
            },
            "placingWafer": {
                "entry": [
                    "logPlacingWafer",
                    {
                        "type": "assign",
                        "assignment": {
                            "heldWafer": null,
                            "pickFrom": null,
                            "placeTo": null
                        }
                    }
                ],
                "after": {
                    "500": {
                        "target": "idle",
                        "actions": ["logComplete"]
                    }
                }
            }
        }
    }
    """;

    // Create a dummy scheduler actor to receive PICK_COMPLETE
    var scheduler = system.ActorOf(Props.Create(() => new SchedulerActor()), "scheduler");

    var factory = new XStateMachineFactory(system);

    var robot = factory.FromJson(robotJson)
        .WithAction("logTransfer", (ctx, data) =>
        {
            var wafer = ctx.Get<int?>("heldWafer");
            var from = ctx.Get<string>("pickFrom");
            var to = ctx.Get<string>("placeTo");
            Console.WriteLine($"  [R1] 📋 Transfer request: Wafer {wafer} from {from} to {to}");
        })
        .WithAction("logMovingToPickup", (ctx, _) =>
        {
            var from = ctx.Get<string>("pickFrom");
            Console.WriteLine($"  [R1] 🚶 Moving to pickup location: {from}");
        })
        .WithAction("logPickingWafer", (ctx, _) =>
        {
            var wafer = ctx.Get<int?>("heldWafer");
            var from = ctx.Get<string>("pickFrom");
            Console.WriteLine($"  [R1] ✋ Picking wafer {wafer} from {from}");
        })
        .WithAction("logMovingToPlace", (ctx, _) =>
        {
            var to = ctx.Get<string>("placeTo");
            Console.WriteLine($"  [R1] 🚶 Moving to place location: {to}");
        })
        .WithAction("logPlacingWafer", (ctx, _) =>
        {
            var wafer = ctx.Get<int?>("heldWafer");
            var to = ctx.Get<string>("placeTo");
            Console.WriteLine($"  [R1] 📍 Placing wafer {wafer} at {to}");
        })
        .WithAction("logComplete", (ctx, _) =>
        {
            Console.WriteLine($"  [R1] ✅ Transfer complete - returning to idle");
        })
        .WithDelayService("moveDelay", 500)
        .WithActor("scheduler", scheduler)
        .BuildAndStart("R1");

    // Wait for robot to be ready
    await Task.Delay(100);

    // Send transfer command
    Console.WriteLine("  Sending TRANSFER command...\n");
    robot.Tell(new SendEvent("TRANSFER", new { waferId = 1, from = "LoadPort", to = "polisher" }));

    // Wait for completion
    await Task.Delay(3000);
}

static async Task RunTrafficLightExample(ActorSystem system)
{
    var trafficLightJson = """
    {
        "id": "trafficLight",
        "initial": "red",
        "states": {
            "red": {
                "entry": ["logRed"],
                "after": {
                    "1000": "green"
                }
            },
            "green": {
                "entry": ["logGreen"],
                "after": {
                    "1000": "yellow"
                }
            },
            "yellow": {
                "entry": ["logYellow"],
                "after": {
                    "1000": "red"
                }
            }
        }
    }
    """;

    var factory = new XStateMachineFactory(system);

    var trafficLight = factory.FromJson(trafficLightJson)
        .WithAction("logRed", (ctx, _) => Console.WriteLine("  🔴 RED"))
        .WithAction("logGreen", (ctx, _) => Console.WriteLine("  🟢 GREEN"))
        .WithAction("logYellow", (ctx, _) => Console.WriteLine("  🟡 YELLOW"))
        .BuildAndStart("trafficLight");

    // Run for 3 cycles
    await Task.Delay(3500);
    trafficLight.Tell(new StopMachine());
}

static async Task RunAlwaysExample(ActorSystem system)
{
    // Wafer routing based on type - demonstrates always transitions with guards
    var routerJson = """
    {
        "id": "waferRouter",
        "initial": "idle",
        "context": {
            "waferType": null,
            "priority": 0
        },
        "states": {
            "idle": {
                "on": {
                    "WAFER_ARRIVED": {
                        "target": "routing",
                        "actions": [
                            {
                                "type": "assign",
                                "assignment": {
                                    "waferType": "copper",
                                    "priority": 1
                                }
                            }
                        ]
                    }
                }
            },
            "routing": {
                "entry": ["logRouting"],
                "always": [
                    {
                        "target": "highPriorityPath",
                        "cond": "isHighPriority"
                    },
                    {
                        "target": "copperPath",
                        "cond": "isCopper"
                    },
                    {
                        "target": "siliconPath",
                        "cond": "isSilicon"
                    },
                    {
                        "target": "defaultPath"
                    }
                ]
            },
            "highPriorityPath": {
                "entry": ["logHighPriority"],
                "after": {
                    "500": "complete"
                }
            },
            "copperPath": {
                "entry": ["logCopper"],
                "after": {
                    "500": "complete"
                }
            },
            "siliconPath": {
                "entry": ["logSilicon"],
                "after": {
                    "500": "complete"
                }
            },
            "defaultPath": {
                "entry": ["logDefault"],
                "after": {
                    "500": "complete"
                }
            },
            "complete": {
                "entry": ["logComplete"],
                "type": "final"
            }
        }
    }
    """;

    var factory = new XStateMachineFactory(system);

    var router = factory.FromJson(routerJson)
        .WithAction("logRouting", (ctx, _) =>
        {
            var waferType = ctx.Get<string>("waferType");
            var priority = ctx.Get<int>("priority");
            Console.WriteLine($"  [Router] 🔄 Routing wafer (type={waferType}, priority={priority})");
        })
        .WithAction("logHighPriority", (ctx, _) =>
        {
            Console.WriteLine($"  [Router] ⚡ HIGH PRIORITY path selected");
        })
        .WithAction("logCopper", (ctx, _) =>
        {
            Console.WriteLine($"  [Router] 🟤 COPPER path selected");
        })
        .WithAction("logSilicon", (ctx, _) =>
        {
            Console.WriteLine($"  [Router] ⚪ SILICON path selected");
        })
        .WithAction("logDefault", (ctx, _) =>
        {
            Console.WriteLine($"  [Router] ⚫ DEFAULT path selected");
        })
        .WithAction("logComplete", (ctx, _) =>
        {
            Console.WriteLine($"  [Router] ✅ Routing complete");
        })
        .WithGuard("isHighPriority", (ctx, _) => ctx.Get<int>("priority") > 5)
        .WithGuard("isCopper", (ctx, _) => ctx.Get<string>("waferType") == "copper")
        .WithGuard("isSilicon", (ctx, _) => ctx.Get<string>("waferType") == "silicon")
        .BuildAndStart("waferRouter");

    // Wait for router to be ready
    await Task.Delay(100);

    // Test case 1: Copper wafer, normal priority
    Console.WriteLine("  Test 1: Copper wafer, priority=1");
    router.Tell(new SendEvent("WAFER_ARRIVED", new { waferType = "copper", priority = 1 }));
    await Task.Delay(1000);

    Console.WriteLine("\n  Test 2: High priority wafer");
    // Modify JSON for test 2 to assign different values
    var router2Json = routerJson.Replace("\"waferType\": \"copper\"", "\"waferType\": \"silicon\"")
                                 .Replace("\"priority\": 1", "\"priority\": 10");

    var router2 = factory.FromJson(router2Json)
        .WithAction("logRouting", (ctx, _) =>
        {
            var waferType = ctx.Get<string>("waferType");
            var priority = ctx.Get<int>("priority");
            Console.WriteLine($"  [Router] 🔄 Routing wafer (type={waferType}, priority={priority})");
        })
        .WithAction("logHighPriority", (ctx, _) =>
        {
            Console.WriteLine($"  [Router] ⚡ HIGH PRIORITY path selected");
        })
        .WithAction("logCopper", (ctx, _) =>
        {
            Console.WriteLine($"  [Router] 🟤 COPPER path selected");
        })
        .WithAction("logSilicon", (ctx, _) =>
        {
            Console.WriteLine($"  [Router] ⚪ SILICON path selected");
        })
        .WithAction("logDefault", (ctx, _) =>
        {
            Console.WriteLine($"  [Router] ⚫ DEFAULT path selected");
        })
        .WithAction("logComplete", (ctx, _) =>
        {
            Console.WriteLine($"  [Router] ✅ Routing complete");
        })
        .WithGuard("isHighPriority", (ctx, _) => ctx.Get<int>("priority") > 5)
        .WithGuard("isCopper", (ctx, _) => ctx.Get<string>("waferType") == "copper")
        .WithGuard("isSilicon", (ctx, _) => ctx.Get<string>("waferType") == "silicon")
        .BuildAndStart("waferRouter2");

    await Task.Delay(100);
    router2.Tell(new SendEvent("WAFER_ARRIVED", null));
    await Task.Delay(1000);
}

static async Task RunParallelExample(ActorSystem system)
{
    // Parallel processing - data processing and status monitoring run concurrently
    var parallelJson = """
    {
        "id": "parallelProcessor",
        "initial": "idle",
        "states": {
            "idle": {
                "on": {
                    "START": "processing"
                }
            },
            "processing": {
                "type": "parallel",
                "entry": ["logStartProcessing"],
                "states": {
                    "dataProcessing": {
                        "initial": "loading",
                        "states": {
                            "loading": {
                                "entry": ["logLoadingData"],
                                "after": {
                                    "500": "processing"
                                }
                            },
                            "processing": {
                                "entry": ["logProcessingData"],
                                "after": {
                                    "800": "saving"
                                }
                            },
                            "saving": {
                                "entry": ["logSavingData"],
                                "after": {
                                    "500": "done"
                                }
                            },
                            "done": {
                                "type": "final",
                                "entry": ["logDataDone"]
                            }
                        }
                    },
                    "statusMonitoring": {
                        "initial": "monitoring",
                        "states": {
                            "monitoring": {
                                "entry": ["logMonitoringStart"],
                                "after": {
                                    "300": "checkingHealth"
                                }
                            },
                            "checkingHealth": {
                                "entry": ["logCheckingHealth"],
                                "after": {
                                    "400": "reportingStatus"
                                }
                            },
                            "reportingStatus": {
                                "entry": ["logReportingStatus"],
                                "after": {
                                    "400": "done"
                                }
                            },
                            "done": {
                                "type": "final",
                                "entry": ["logMonitoringDone"]
                            }
                        }
                    }
                }
            },
            "complete": {
                "entry": ["logComplete"],
                "type": "final"
            }
        }
    }
    """;

    var factory = new XStateMachineFactory(system);

    var processor = factory.FromJson(parallelJson)
        .WithAction("logStartProcessing", (ctx, _) =>
        {
            Console.WriteLine("  [Processor] 🚀 Starting parallel processing...");
        })
        .WithAction("logLoadingData", (ctx, _) =>
        {
            Console.WriteLine("  [DataProcessing] 📥 Loading data...");
        })
        .WithAction("logProcessingData", (ctx, _) =>
        {
            Console.WriteLine("  [DataProcessing] ⚙️  Processing data...");
        })
        .WithAction("logSavingData", (ctx, _) =>
        {
            Console.WriteLine("  [DataProcessing] 💾 Saving data...");
        })
        .WithAction("logDataDone", (ctx, _) =>
        {
            Console.WriteLine("  [DataProcessing] ✅ Data processing complete!");
        })
        .WithAction("logMonitoringStart", (ctx, _) =>
        {
            Console.WriteLine("  [StatusMonitoring] 👀 Starting monitoring...");
        })
        .WithAction("logCheckingHealth", (ctx, _) =>
        {
            Console.WriteLine("  [StatusMonitoring] 🏥 Checking health...");
        })
        .WithAction("logReportingStatus", (ctx, _) =>
        {
            Console.WriteLine("  [StatusMonitoring] 📊 Reporting status...");
        })
        .WithAction("logMonitoringDone", (ctx, _) =>
        {
            Console.WriteLine("  [StatusMonitoring] ✅ Monitoring complete!");
        })
        .WithAction("logComplete", (ctx, _) =>
        {
            Console.WriteLine("  [Processor] 🎉 All parallel tasks completed!");
        })
        .BuildAndStart("parallelProcessor");

    // Wait for processor to be ready
    await Task.Delay(100);

    // Start processing
    Console.WriteLine("  Sending START command...\n");
    processor.Tell(new SendEvent("START", null));

    // Wait for completion (both regions should complete in ~1.8-2.0 seconds)
    await Task.Delay(2500);
}

static async Task RunRobotV2Example(ActorSystem system)
{
    // Load RobotMachineV2.json - demonstrates full CMP migration pattern
    var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RobotMachineV2.json");
    var robotJson = await File.ReadAllTextAsync(jsonPath);

    // Create scheduler actor to receive ROBOT_STATUS events
    var scheduler = system.ActorOf(Props.Create(() => new SchedulerActor()), "schedulerV2");

    // Create station actors to receive PICK and PLACE events
    var loadPort = system.ActorOf(Props.Create(() => new StationActor("LoadPort")), "LoadPortV2");
    var polisher = system.ActorOf(Props.Create(() => new StationActor("P1")), "P1V2");

    var factory = new XStateMachineFactory(system);

    var robot = factory.FromJson(robotJson)
        // Logging actions
        .WithAction("logReset", (ctx, _) =>
        {
            Console.WriteLine($"  [R1] 🔄 RESET - clearing all context");
        })
        .WithAction("logIdle", (ctx, _) =>
        {
            Console.WriteLine($"  [R1] 💤 Idle - waiting for transfer command");
        })
        .WithAction("storeTransferInfo", (ctx, data) =>
        {
            // Extract values from event data and update context
            if (data != null)
            {
                var eventData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                    System.Text.Json.JsonSerializer.Serialize(data));

                if (eventData != null)
                {
                    if (eventData.TryGetValue("heldWafer", out var waferElem))
                        ctx.Set("heldWafer", waferElem.GetInt32());
                    if (eventData.TryGetValue("pickFrom", out var fromElem))
                        ctx.Set("pickFrom", fromElem.GetString());
                    if (eventData.TryGetValue("placeTo", out var toElem))
                        ctx.Set("placeTo", toElem.GetString());
                }
            }

            var wafer = ctx.Get<int?>("heldWafer");
            var from = ctx.Get<string>("pickFrom");
            var to = ctx.Get<string>("placeTo");
            Console.WriteLine($"  [R1] 📋 Transfer info stored: Wafer {wafer} from {from} to {to}");
        })
        .WithAction("logTransferCommand", (ctx, _) =>
        {
            Console.WriteLine($"  [R1] ✅ Transfer command accepted");
        })
        .WithAction("logPickingUp", (ctx, _) =>
        {
            var from = ctx.Get<string>("pickFrom");
            Console.WriteLine($"  [R1] 🚶 Moving to pickup location: {from}");
        })
        .WithAction("logPickedWafer", (ctx, _) =>
        {
            var wafer = ctx.Get<int?>("heldWafer");
            var from = ctx.Get<string>("pickFrom");
            Console.WriteLine($"  [R1] ✋ Picked wafer {wafer} from {from}");
        })
        .WithAction("logHolding", (ctx, _) =>
        {
            var wafer = ctx.Get<int?>("heldWafer");
            var to = ctx.Get<string>("placeTo");
            Console.WriteLine($"  [R1] 🤲 Holding wafer {wafer}, waiting for destination {to} to be ready");
        })
        .WithAction("logPlacingDown", (ctx, _) =>
        {
            var to = ctx.Get<string>("placeTo");
            Console.WriteLine($"  [R1] 🚶 Moving to place location: {to}");
        })
        .WithAction("logPlacedWafer", (ctx, _) =>
        {
            var wafer = ctx.Get<int?>("heldWafer");
            var to = ctx.Get<string>("placeTo");
            Console.WriteLine($"  [R1] 📍 Placed wafer {wafer} at {to}");
        })
        .WithAction("logReturning", (ctx, _) =>
        {
            Console.WriteLine($"  [R1] 🔙 Returning to home position");
        })
        .WithAction("logTransferComplete", (ctx, _) =>
        {
            Console.WriteLine($"  [R1] ✅ Transfer complete!");
        })
        // Services for movement delays
        .WithDelayService("moveToPickup", 500)
        .WithDelayService("moveToPlace", 500)
        .WithDelayService("returnToIdle", 300)
        // Actors for message routing
        .WithActor("scheduler", scheduler)
        .WithActor("pickFromStation", loadPort)  // Will route to LoadPort
        .WithActor("placeToStation", polisher)   // Will route to P1
        .BuildAndStart("R1V2");

    await Task.Delay(200);

    // Scenario: Transfer wafer 1 from LoadPort to P1 (Polisher 1)
    Console.WriteLine("  📤 Sending TRANSFER command: Wafer 1, LoadPort → P1\n");
    robot.Tell(new SendEvent("TRANSFER", new
    {
        heldWafer = 1,
        pickFrom = "LoadPort",
        placeTo = "P1"
    }));

    // Wait for robot to pick up wafer and enter holding state
    await Task.Delay(1000);

    // Simulate destination ready signal from scheduler
    Console.WriteLine("\n  🟢 Destination P1 is now ready - sending DESTINATION_READY\n");
    robot.Tell(new SendEvent("DESTINATION_READY", null));

    // Wait for transfer to complete
    await Task.Delay(1500);

    Console.WriteLine("\n  🔄 Testing RESET command\n");
    robot.Tell(new SendEvent("RESET", null));

    await Task.Delay(500);
}

// Simple scheduler actor to demonstrate send action
class SchedulerActor : ReceiveActor
{
    public SchedulerActor()
    {
        Receive<SendEvent>(evt =>
        {
            Console.WriteLine($"  [Scheduler] 📨 Received event: {evt.Type}");

            // Echo back to sender
            Sender.Tell(evt);
        });
    }
}

// Station actor to handle PICK and PLACE events
class StationActor : ReceiveActor
{
    private readonly string _name;
    private int? _heldWafer = null;

    public StationActor(string name)
    {
        _name = name;

        Receive<SendEvent>(evt =>
        {
            if (evt.Type == "PICK")
            {
                Console.WriteLine($"  [{_name}] 📤 Wafer picked up by robot");
                _heldWafer = null;
            }
            else if (evt.Type == "PLACE")
            {
                Console.WriteLine($"  [{_name}] 📥 Wafer placed by robot");
            }
        });
    }
}
