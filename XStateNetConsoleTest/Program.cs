using XStateNet;
using XStateNet.Orchestration;
using Newtonsoft.Json.Linq;

Console.WriteLine("=== XStateNet State Machine Console Test ===\n");

// Create orchestrator (required for ExtendedPureStateMachineFactory)
var orchestrator = new EventBusOrchestrator();

// Test 1: Simple state machine with Orchestrator
Console.WriteLine("Test 1: Simple Traffic Light with Orchestrator");
Console.WriteLine("-------------------------------------------");

var trafficLightDefinition = """
{
    "id": "trafficLight",
    "initial": "red",
    "states": {
        "red": {
            "on": {
                "NEXT": { "target": "green", "actions": ["logTransition"] }
            }
        },
        "green": {
            "on": {
                "NEXT": { "target": "yellow", "actions": ["logTransition"] }
            }
        },
        "yellow": {
            "on": {
                "NEXT": { "target": "red", "actions": ["logTransition"] }
            }
        }
    }
}
""";

var trafficLightActions = new Dictionary<string, Action<OrchestratedContext>>
{
    ["logTransition"] = (ctx) =>
    {
        Console.WriteLine($"  Action: Transition executed");
    }
};

var trafficLight = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
    id: "trafficLight",
    json: trafficLightDefinition,
    orchestrator: orchestrator,
    orchestratedActions: trafficLightActions,
    guards: null,
    services: null,
    enableGuidIsolation: false
);

await trafficLight.StartAsync();

Console.WriteLine($"Initial State: {trafficLight.CurrentState}");

await orchestrator.SendEventAsync("external", "trafficLight", "NEXT");
Console.WriteLine($"After NEXT: {trafficLight.CurrentState}");

await orchestrator.SendEventAsync("external", "trafficLight", "NEXT");
Console.WriteLine($"After NEXT: {trafficLight.CurrentState}");

await orchestrator.SendEventAsync("external", "trafficLight", "NEXT");
Console.WriteLine($"After NEXT: {trafficLight.CurrentState}\n");

// Test 2: Station → Scheduler pattern
Console.WriteLine("Test 2: Station → Scheduler Pattern");
Console.WriteLine("-------------------------------------------");

int? currentWafer = null;

var stationDefinition = """
{
    "id": "processingStation",
    "initial": "empty",
    "states": {
        "empty": {
            "on": {
                "PLACE": { "target": "processing", "actions": ["onPlace"] }
            }
        },
        "processing": {
            "entry": ["startProcessing"],
            "on": {
                "DONE": { "target": "done", "actions": ["onDone"] }
            }
        },
        "done": {
            "on": {
                "PICK": { "target": "empty", "actions": ["onPick"] }
            }
        }
    }
}
""";

var stationActions = new Dictionary<string, Action<OrchestratedContext>>
{
    ["onPlace"] = (ctx) =>
    {
        Console.WriteLine($"  [Station] Wafer placed");
        // Report state to Scheduler (not requesting action from Robot!)
        ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
        {
            ["station"] = "processingStation",
            ["state"] = "processing",
            ["waferId"] = currentWafer
        });
    },
    ["startProcessing"] = (ctx) =>
    {
        Console.WriteLine($"  [Station] Starting processing...");
    },
    ["onDone"] = (ctx) =>
    {
        Console.WriteLine($"  [Station] Processing complete");
        // Report completion to Scheduler
        ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
        {
            ["station"] = "processingStation",
            ["state"] = "done",
            ["waferId"] = currentWafer
        });
    },
    ["onPick"] = (ctx) =>
    {
        Console.WriteLine($"  [Station] Wafer picked");
        // Report back to empty state
        ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
        {
            ["station"] = "processingStation",
            ["state"] = "empty"
        });
    }
};

// Create Station with services for async processing
var stationServices = new Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>
{
    ["processWafer"] = async (sm, ct) =>
    {
        Console.WriteLine($"  [Station Service] Processing wafer {currentWafer}...");
        await Task.Delay(2000, ct); // Simulate 2 seconds processing
        Console.WriteLine($"  [Station Service] Processing complete");
        return new { status = "SUCCESS" };
    }
};

// Update station definition to use invoke service
var stationDefWithService = """
{
    "id": "processingStation",
    "initial": "empty",
    "states": {
        "empty": {
            "on": {
                "PLACE": { "target": "processing", "actions": ["onPlace"] }
            }
        },
        "processing": {
            "entry": ["startProcessing"],
            "invoke": {
                "src": "processWafer",
                "onDone": {
                    "target": "done",
                    "actions": ["onDone"]
                }
            }
        },
        "done": {
            "on": {
                "PICK": { "target": "empty", "actions": ["onPick"] }
            }
        }
    }
}
""";

var station = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
    id: "processingStation",
    json: stationDefWithService,
    orchestrator: orchestrator,
    orchestratedActions: stationActions,
    guards: null,
    services: stationServices,
    enableGuidIsolation: false
);

await station.StartAsync();

Console.WriteLine($"Initial State: {station.CurrentState}");
Console.WriteLine("[Scheduler] Commanding robot to place wafer...\n");

currentWafer = 1;
await orchestrator.SendEventAsync("scheduler", "processingStation", "PLACE");
await Task.Delay(100);

// Wait for processing to complete
await Task.Delay(2500);

Console.WriteLine($"\nCurrent State: {station.CurrentState}");
Console.WriteLine("[Scheduler] Commanding robot to pick wafer...\n");

await orchestrator.SendEventAsync("scheduler", "processingStation", "PICK");
await Task.Delay(100);

Console.WriteLine($"\nFinal State: {station.CurrentState}\n");

// Test 3: API Summary
Console.WriteLine("Test 3: XStateNet API Summary");
Console.WriteLine("-------------------------------------------");
Console.WriteLine("✓ Use ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices()");
Console.WriteLine("✓ Pass EventBusOrchestrator to factory");
Console.WriteLine("✓ Access state via machine.CurrentState property");
Console.WriteLine("✓ Use ctx.RequestSend() for Station → Scheduler communication");
Console.WriteLine("✓ Use orchestrator.SendEventAsync() to send events from external");
Console.WriteLine("✓ IPureStateMachine has NO SendAsync - all via orchestrator");
Console.WriteLine("✓ Use invoke services for async operations");

Console.WriteLine("\n=== All Tests Complete ===");
