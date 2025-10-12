using System.Collections.Generic;
using XStateNet;
using XStateNet.Orchestration;

Console.WriteLine("=== CMP Tool Simulator - Console Version ===");
Console.WriteLine("XStateNet Event-Driven Architecture Test\n");

// Create orchestrator
var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
{
    PoolSize = 4,
    EnableLogging = true
});

Console.WriteLine("[INIT] Creating state machines...\n");

// Create stations
var loadPort = CreateLoadPortStation("loadport", orchestrator);
var wtr1 = CreateWTRStation("wtr1", orchestrator);
var polisher = CreatePolisherStation("polisher", orchestrator);
var wtr2 = CreateWTRStation("wtr2", orchestrator);
var cleaner = CreateCleanerStation("cleaner", orchestrator);
var buffer = CreateBufferStation("buffer", orchestrator);

Console.WriteLine("[INIT] All stations created\n");

// Start all machines
Console.WriteLine("[START] Starting all state machines...");
await loadPort.StartAsync();
await wtr1.StartAsync();
await polisher.StartAsync();
await wtr2.StartAsync();
await cleaner.StartAsync();
await buffer.StartAsync();

Console.WriteLine("[START] All machines started\n");

// Send START_SIMULATION event
Console.WriteLine("[SIMULATION] Sending START_SIMULATION to LoadPort...\n");
await orchestrator.SendEventAsync("system", "loadport", "START_SIMULATION");

Console.WriteLine("\n[SIMULATION] Simulation started! Watching for 30 seconds...\n");

// Wait and watch
await Task.Delay(30000);

Console.WriteLine("\n[END] Simulation complete!");
orchestrator.Dispose();

// ==================== STATION DEFINITIONS ====================

static IStateMachine CreateLoadPortStation(string id, EventBusOrchestrator orchestrator)
{
    int waferCount = 25;
    int nextWaferId = 1;

    var definition = $$"""
    {
        "id": "{{id}}",
        "initial": "ready",
        "states": {
            "ready": {
                "on": {
                    "START_SIMULATION": {
                        "target": "dispatching",
                        "cond": "hasWafers"
                    }
                }
            },
            "dispatching": {
                "entry": ["dispatchWafer"],
                "on": {
                    "WAFER_PICKED_UP": {
                        "target": "waiting",
                        "actions": ["decrementCount"]
                    }
                }
            },
            "waiting": {
                "after": {
                    "3500": [
                        { "target": "dispatching", "cond": "hasWafers" },
                        { "target": "ready" }
                    ]
                }
            }
        }
    }
    """;

    var actions = new ActionMap();

    actions["dispatchWafer"] = new List<NamedAction>
    {
        new NamedAction("dispatchWafer", async (sm) =>
        {
            var waferId = nextWaferId;
            Console.WriteLine($"[LoadPort] 📦 Dispatching Wafer {waferId}");

            var payload = new Dictionary<string, object>
            {
                ["waferId"] = waferId,
                ["from"] = "loadport",
                ["to"] = "polisher"
            };

            await orchestrator.SendEventFireAndForgetAsync(id, "wtr1", "TRANSFER_REQUEST", payload);
        })
    };

    actions["decrementCount"] = new List<NamedAction>
    {
        new NamedAction("decrementCount", async (sm) =>
        {
            waferCount--;
            nextWaferId++;
            Console.WriteLine($"[LoadPort] ✓ Wafer dispatched. Remaining: {waferCount}/25");
            await Task.CompletedTask;
        })
    };

    var guards = new GuardMap();
    guards["hasWafers"] = new NamedGuard((sm) => nextWaferId <= 25 && waferCount > 0, "hasWafers");

    var machine = StateMachineFactory.CreateFromScript(definition, false, false, actions, guards);
    orchestrator.RegisterMachine(id, machine);

    return machine;
}

static IStateMachine CreateWTRStation(string id, EventBusOrchestrator orchestrator)
{
    int? currentWaferId = null;
    string? fromStation = null;
    string? toStation = null;

    var definition = $$"""
    {
        "id": "{{id}}",
        "initial": "idle",
        "states": {
            "idle": {
                "on": {
                    "TRANSFER_REQUEST": {
                        "target": "picking",
                        "actions": ["saveTransferInfo"]
                    }
                }
            },
            "picking": {
                "entry": ["pickupWafer"],
                "after": { "300": "transiting" }
            },
            "transiting": {
                "after": { "600": "placing" }
            },
            "placing": {
                "entry": ["placeWafer"],
                "after": {
                    "300": {
                        "target": "idle",
                        "actions": ["notifyDestination"]
                    }
                }
            }
        }
    }
    """;

    var actions = new ActionMap();

    actions["saveTransferInfo"] = new List<NamedAction>
    {
        new NamedAction("saveTransferInfo", async (sm) =>
        {
            var eventData = sm.ContextMap?["_event"];

            Console.WriteLine($"[{id.ToUpper()}] 🔍 saveTransferInfo - eventData type: {eventData?.GetType().Name ?? "NULL"}");

            if (eventData is Dictionary<string, object> dict)
            {
                if (dict.ContainsKey("waferId") && dict.ContainsKey("from") && dict.ContainsKey("to"))
                {
                    currentWaferId = Convert.ToInt32(dict["waferId"]);
                    fromStation = dict["from"]?.ToString();
                    toStation = dict["to"]?.ToString();

                    Console.WriteLine($"[{id.ToUpper()}] 🤖 Transfer request: Wafer {currentWaferId} from {fromStation} to {toStation}");
                }
                else
                {
                    Console.WriteLine($"[{id.ToUpper()}] ⚠️ Dictionary missing keys. Has: {string.Join(", ", dict.Keys)}");
                }
            }
            else
            {
                Console.WriteLine($"[{id.ToUpper()}] ⚠️ eventData is not Dictionary!");
            }

            await Task.CompletedTask;
        })
    };

    actions["pickupWafer"] = new List<NamedAction>
    {
        new NamedAction("pickupWafer", async (sm) =>
        {
            Console.WriteLine($"[{id.ToUpper()}] 🤖 Picking up Wafer {currentWaferId} from {fromStation}");

            var payload = new Dictionary<string, object> { ["waferId"] = currentWaferId! };
            await orchestrator.SendEventFireAndForgetAsync(id, fromStation!, "WAFER_PICKED_UP", payload);
        })
    };

    actions["placeWafer"] = new List<NamedAction>
    {
        new NamedAction("placeWafer", async (sm) =>
        {
            Console.WriteLine($"[{id.ToUpper()}] 🤖 Placing Wafer {currentWaferId} at {toStation}");
            await Task.CompletedTask;
        })
    };

    actions["notifyDestination"] = new List<NamedAction>
    {
        new NamedAction("notifyDestination", async (sm) =>
        {
            Console.WriteLine($"[{id.ToUpper()}] 🤖 Delivered Wafer {currentWaferId} to {toStation}");

            var payload = new Dictionary<string, object> { ["waferId"] = currentWaferId! };
            await orchestrator.SendEventFireAndForgetAsync(id, toStation!, "WAFER_ARRIVED", payload);

            currentWaferId = null;
            fromStation = null;
            toStation = null;
        })
    };

    var machine = StateMachineFactory.CreateFromScript(definition, false, false, actions);
    orchestrator.RegisterMachine(id, machine);

    return machine;
}

static IStateMachine CreatePolisherStation(string id, EventBusOrchestrator orchestrator)
{
    int? currentWaferId = null;

    var definition = $$"""
    {
        "id": "{{id}}",
        "initial": "idle",
        "states": {
            "idle": {
                "on": {
                    "WAFER_ARRIVED": {
                        "target": "processing",
                        "actions": ["acceptWafer"]
                    }
                }
            },
            "processing": {
                "entry": ["startProcessing"],
                "after": {
                    "3000": {
                        "target": "ready",
                        "actions": ["processingComplete"]
                    }
                }
            },
            "ready": {
                "entry": ["notifyReady"],
                "on": {
                    "WAFER_PICKED_UP": {
                        "target": "idle",
                        "actions": ["clearWafer"]
                    }
                }
            }
        }
    }
    """;

    var actions = new ActionMap();

    actions["acceptWafer"] = new List<NamedAction>
    {
        new NamedAction("acceptWafer", async (sm) =>
        {
            var eventData = sm.ContextMap?["_event"];

            if (eventData is Dictionary<string, object> dict && dict.ContainsKey("waferId"))
            {
                currentWaferId = Convert.ToInt32(dict["waferId"]);
            }

            Console.WriteLine($"[Polisher] 📥 Accepted Wafer {currentWaferId}");
            await Task.CompletedTask;
        })
    };

    actions["startProcessing"] = new List<NamedAction>
    {
        new NamedAction("startProcessing", async (sm) =>
        {
            Console.WriteLine($"[Polisher] ⚙️  Processing Wafer {currentWaferId} (3000ms)");
            await Task.CompletedTask;
        })
    };

    actions["processingComplete"] = new List<NamedAction>
    {
        new NamedAction("processingComplete", async (sm) =>
        {
            Console.WriteLine($"[Polisher] ✓ Wafer {currentWaferId} processing complete");
            await Task.CompletedTask;
        })
    };

    actions["notifyReady"] = new List<NamedAction>
    {
        new NamedAction("notifyReady", async (sm) =>
        {
            Console.WriteLine($"[Polisher] 📤 Wafer {currentWaferId} ready for pickup");

            var payload = new Dictionary<string, object>
            {
                ["waferId"] = currentWaferId!,
                ["from"] = "polisher",
                ["to"] = "cleaner"
            };
            await orchestrator.SendEventFireAndForgetAsync(id, "wtr2", "TRANSFER_REQUEST", payload);
        })
    };

    actions["clearWafer"] = new List<NamedAction>
    {
        new NamedAction("clearWafer", async (sm) =>
        {
            Console.WriteLine($"[Polisher] ✓ Wafer {currentWaferId} picked up, now Idle");
            currentWaferId = null;
            await Task.CompletedTask;
        })
    };

    var machine = StateMachineFactory.CreateFromScript(definition, false, false, actions);
    orchestrator.RegisterMachine(id, machine);

    return machine;
}

static IStateMachine CreateCleanerStation(string id, EventBusOrchestrator orchestrator)
{
    int? currentWaferId = null;

    var definition = $$"""
    {
        "id": "{{id}}",
        "initial": "idle",
        "states": {
            "idle": {
                "on": {
                    "WAFER_ARRIVED": {
                        "target": "processing",
                        "actions": ["acceptWafer"]
                    }
                }
            },
            "processing": {
                "entry": ["startProcessing"],
                "after": {
                    "2500": {
                        "target": "ready",
                        "actions": ["processingComplete"]
                    }
                }
            },
            "ready": {
                "entry": ["notifyReady"],
                "on": {
                    "WAFER_PICKED_UP": {
                        "target": "idle",
                        "actions": ["clearWafer"]
                    }
                }
            }
        }
    }
    """;

    var actions = new ActionMap();

    actions["acceptWafer"] = new List<NamedAction>
    {
        new NamedAction("acceptWafer", async (sm) =>
        {
            var eventData = sm.ContextMap?["_event"];

            if (eventData is Dictionary<string, object> dict && dict.ContainsKey("waferId"))
            {
                currentWaferId = Convert.ToInt32(dict["waferId"]);
            }

            Console.WriteLine($"[Cleaner] 📥 Accepted Wafer {currentWaferId}");
            await Task.CompletedTask;
        })
    };

    actions["startProcessing"] = new List<NamedAction>
    {
        new NamedAction("startProcessing", async (sm) =>
        {
            Console.WriteLine($"[Cleaner] ⚙️  Cleaning Wafer {currentWaferId} (2500ms)");
            await Task.CompletedTask;
        })
    };

    actions["processingComplete"] = new List<NamedAction>
    {
        new NamedAction("processingComplete", async (sm) =>
        {
            Console.WriteLine($"[Cleaner] ✓ Wafer {currentWaferId} cleaning complete");
            await Task.CompletedTask;
        })
    };

    actions["notifyReady"] = new List<NamedAction>
    {
        new NamedAction("notifyReady", async (sm) =>
        {
            Console.WriteLine($"[Cleaner] 📤 Wafer {currentWaferId} ready for return via Buffer");

            var payload = new Dictionary<string, object>
            {
                ["waferId"] = currentWaferId!,
                ["from"] = "cleaner",
                ["to"] = "buffer"
            };
            await orchestrator.SendEventFireAndForgetAsync(id, "wtr2", "TRANSFER_REQUEST", payload);
        })
    };

    actions["clearWafer"] = new List<NamedAction>
    {
        new NamedAction("clearWafer", async (sm) =>
        {
            Console.WriteLine($"[Cleaner] ✓ Wafer {currentWaferId} picked up, now Idle");
            currentWaferId = null;
            await Task.CompletedTask;
        })
    };

    var machine = StateMachineFactory.CreateFromScript(definition, false, false, actions);
    orchestrator.RegisterMachine(id, machine);

    return machine;
}

static IStateMachine CreateBufferStation(string id, EventBusOrchestrator orchestrator)
{
    int? currentWaferId = null;

    var definition = $$"""
    {
        "id": "{{id}}",
        "initial": "empty",
        "states": {
            "empty": {
                "on": {
                    "WAFER_ARRIVED": {
                        "target": "occupied",
                        "actions": ["storeWafer"]
                    }
                }
            },
            "occupied": {
                "after": {
                    "100": "ready"
                }
            },
            "ready": {
                "entry": ["notifyReady"],
                "on": {
                    "WAFER_PICKED_UP": {
                        "target": "empty",
                        "actions": ["clearWafer"]
                    }
                }
            }
        }
    }
    """;

    var actions = new ActionMap();

    actions["storeWafer"] = new List<NamedAction>
    {
        new NamedAction("storeWafer", async (sm) =>
        {
            var eventData = sm.ContextMap?["_event"];

            if (eventData is Dictionary<string, object> dict && dict.ContainsKey("waferId"))
            {
                currentWaferId = Convert.ToInt32(dict["waferId"]);
            }

            Console.WriteLine($"[Buffer] 🟡 Stored Wafer {currentWaferId}");
            await Task.CompletedTask;
        })
    };

    actions["notifyReady"] = new List<NamedAction>
    {
        new NamedAction("notifyReady", async (sm) =>
        {
            Console.WriteLine($"[Buffer] 📤 Wafer {currentWaferId} ready for return to LoadPort");

            var payload = new Dictionary<string, object>
            {
                ["waferId"] = currentWaferId!,
                ["from"] = "buffer",
                ["to"] = "loadport"
            };
            await orchestrator.SendEventFireAndForgetAsync(id, "wtr1", "TRANSFER_REQUEST", payload);
        })
    };

    actions["clearWafer"] = new List<NamedAction>
    {
        new NamedAction("clearWafer", async (sm) =>
        {
            Console.WriteLine($"[Buffer] ✓ Wafer {currentWaferId} picked up, now Empty");
            currentWaferId = null;
            await Task.CompletedTask;
        })
    };

    var machine = StateMachineFactory.CreateFromScript(definition, false, false, actions);
    orchestrator.RegisterMachine(id, machine);

    return machine;
}
