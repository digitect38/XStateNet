using Akka.Actor;
using CMPSimXS2.Parallel;

Console.WriteLine("=== PUSH MODEL: CMP System with Coordinator-Driven Architecture ===");
Console.WriteLine("SystemCoordinatorPush → Wafer Schedulers → Robot Schedulers");
Console.WriteLine("Coordinator proactively commands resources using bitmask scheduling");
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
var actorSystem = ActorSystem.Create("CMPParallelSystem", config);

try
{
    // Load JSON definitions
    var exeDir = AppContext.BaseDirectory;
    var waferJson = await File.ReadAllTextAsync(Path.Combine(exeDir, "wafer_scheduler.json"));
    var robotsJson = await File.ReadAllTextAsync(Path.Combine(exeDir, "robot_schedulers.json"));

    Console.WriteLine("[INFO] Loaded state machine definitions");
    Console.WriteLine();

    // Initialize table logger
    TableLogger.Initialize();

    // Create the PUSH-based CMP system
    var systemCoordinator = actorSystem.ActorOf(
        Props.Create(() => new SystemCoordinatorPush(waferJson, robotsJson)),
        "system-coordinator-push"
    );

    // Start the system
    systemCoordinator.Tell(new StartSystem());

    Console.WriteLine();
    Console.WriteLine("Press Ctrl+C to stop...");
    Console.WriteLine();

    // Keep running
    await Task.Delay(Timeout.Infinite);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ERROR] System error: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
}
finally
{
    await actorSystem.Terminate();
    Console.WriteLine();
    Console.WriteLine("[INFO] System terminated");
}

// Message types
public record StartSystem();
public record SpawnWafer(string WaferId);
public record WaferCompleted(string WaferId);
public record WaferFailed(string WaferId, string Reason);
public record WaferAtPlaten(string WaferId);
public record ProcessingComplete(string StationId, string WaferId);
public record StartWaferProcessing();
