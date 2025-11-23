using Akka.Actor;
using CMPSimXS2.EventDriven;
using CMPSimXS2.EventDriven.Services;
using XStateNet2.Core.Messages;

Console.WriteLine("=== Event-Driven CMP System with SEMI E10 Compliance ===");
Console.WriteLine("1 robot, 1 carrier (25 wafers), 1 platen");
Console.WriteLine();

// Create Akka actor system with minimal logging
var config = Akka.Configuration.ConfigurationFactory.ParseString(@"
akka {
    loglevel = OFF
    stdout-loglevel = OFF
    log-config-on-start = off
    log-dead-letters = off
    log-dead-letters-during-shutdown = off
    loggers = []
}
");
var actorSystem = ActorSystem.Create("CMPSystem", config);

try
{
    // Load the state machine JSON definition from file
    // The file is copied to the output directory during build
    // Get the path relative to the executable location
    var exeDir = AppContext.BaseDirectory;
    var jsonPath = Path.Combine(exeDir, "cmp_machine.json");

    if (!File.Exists(jsonPath))
    {
        Console.Error.WriteLine($"[ERROR] State machine definition not found: {jsonPath}");
        Console.Error.WriteLine($"Looking in: {exeDir}");
        Console.Error.WriteLine("Please ensure cmp_machine.json exists and is copied to output directory.");
        return;
    }

    var machineJson = await File.ReadAllTextAsync(jsonPath);
    Console.WriteLine($"[INFO] Loaded state machine definition from: {jsonPath}");
    Console.WriteLine();

    // Create and configure the CMP state machine
    var cmpActor = CMPMachineFactory.Create(actorSystem, machineJson);

    // Start the machine
    cmpActor.Tell(new StartMachine());

    // Wait a bit for initialization
    await Task.Delay(100);

    Console.WriteLine("[INFO] CMP system started. Processing wafers...");
    Console.WriteLine();

    // Wait for completion or user interrupt
    Console.WriteLine("Press Ctrl+C to stop or wait for completion...");
    Console.WriteLine();

    // Keep the application running
    await Task.Delay(Timeout.Infinite);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ERROR] System error: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
}
finally
{
    // Cleanup
    await actorSystem.Terminate();
    Console.WriteLine();
    Console.WriteLine("[INFO] CMP system terminated");

    // Shutdown logger to flush remaining messages
    CMPLogger.Instance.Shutdown();
}
