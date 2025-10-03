using XStateNet.InterProcess.Service;

var builder = Host.CreateApplicationBuilder(args);

// Add Windows Services support
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "XStateNet InterProcess Message Bus";
});

// Configure logging
builder.Logging.AddConsole();
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "XStateNet.InterProcess";
});

// Get pipe name from configuration (default: XStateNet.MessageBus)
var pipeName = builder.Configuration.GetValue<string>("MessageBus:PipeName") ?? "XStateNet.MessageBus";

// Check for self-test mode BEFORE registering hosted services
var isSelfTest = args.Contains("--self-test");

// Register services
builder.Services.AddSingleton<IInterProcessMessageBus>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<NamedPipeMessageBus>>();
    return new NamedPipeMessageBus(pipeName, logger);
});

// Only register hosted services if NOT in self-test mode
if (!isSelfTest)
{
    builder.Services.AddHostedService<InterProcessMessageBusWorker>();
    builder.Services.AddHostedService<HealthMonitor>();
}

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("XStateNet InterProcess Message Bus Service");
logger.LogInformation("Pipe Name: {PipeName}", pipeName);
logger.LogInformation("Run Mode: {Mode}", isSelfTest ? "Self-Test" : (OperatingSystem.IsWindows() && args.Contains("--windows-service") ? "Windows Service" : "Console"));

// Run self-test if --self-test argument provided
if (isSelfTest)
{
    logger.LogInformation("=== SELF-TEST MODE ===");
    await RunSelfTestAsync(host, logger);
    logger.LogInformation("=== SELF-TEST COMPLETE ===");
    return;
}

host.Run();

static async Task RunSelfTestAsync(IHost host, ILogger<Program> logger)
{
    var messageBus = host.Services.GetRequiredService<IInterProcessMessageBus>();

    logger.LogInformation("Starting message bus...");
    await messageBus.StartAsync();
    await Task.Delay(500); // Let it fully start

    logger.LogInformation("✓ Message bus started");

    // Test 1: Register two test machines
    logger.LogInformation("\n--- Test 1: Machine Registration ---");
    await messageBus.RegisterMachineAsync("test-ping",
        new MachineRegistration("test-ping", "SelfTest", Environment.ProcessId, DateTime.UtcNow));
    logger.LogInformation("✓ Registered: test-ping");

    await messageBus.RegisterMachineAsync("test-pong",
        new MachineRegistration("test-pong", "SelfTest", Environment.ProcessId, DateTime.UtcNow));
    logger.LogInformation("✓ Registered: test-pong");

    // Test 2: Subscribe to events
    logger.LogInformation("\n--- Test 2: Event Subscription ---");
    var receivedPing = false;
    var receivedPong = false;

    await messageBus.SubscribeAsync("test-pong", async evt =>
    {
        if (evt.EventName == "PING")
        {
            logger.LogInformation("✓ test-pong received PING from {Source}", evt.SourceMachineId);
            receivedPing = true;

            // Send PONG back
            await messageBus.SendEventAsync("test-pong", "test-ping", "PONG", new { Message = "Pong!" });
        }
    });
    logger.LogInformation("✓ test-pong subscribed");

    await messageBus.SubscribeAsync("test-ping", evt =>
    {
        if (evt.EventName == "PONG")
        {
            logger.LogInformation("✓ test-ping received PONG from {Source}", evt.SourceMachineId);
            receivedPong = true;
        }
        return Task.CompletedTask;
    });
    logger.LogInformation("✓ test-ping subscribed");

    // Test 3: Send event and verify routing
    logger.LogInformation("\n--- Test 3: Event Routing ---");
    await messageBus.SendEventAsync("test-ping", "test-pong", "PING", new { Message = "Ping!" });
    logger.LogInformation("Sent: test-ping -> test-pong: PING");

    // Wait for message processing
    await Task.Delay(200);

    // Test 4: Verify results
    logger.LogInformation("\n--- Test 4: Verification ---");
    if (receivedPing && receivedPong)
    {
        logger.LogInformation("✓✓✓ SUCCESS: All messages delivered correctly!");
        logger.LogInformation("  ✓ test-pong received PING");
        logger.LogInformation("  ✓ test-ping received PONG");
    }
    else
    {
        logger.LogError("✗✗✗ FAILURE: Message delivery failed!");
        logger.LogError("  receivedPing: {ReceivedPing}", receivedPing);
        logger.LogError("  receivedPong: {ReceivedPong}", receivedPong);
    }

    // Test 5: Health status
    logger.LogInformation("\n--- Test 5: Health Status ---");
    var health = messageBus.GetHealthStatus();
    logger.LogInformation("Health Status:");
    logger.LogInformation("  IsHealthy: {IsHealthy}", health.IsHealthy);
    logger.LogInformation("  Connections: {ConnectionCount}", health.ConnectionCount);
    logger.LogInformation("  Registered Machines: {RegisteredMachines}", health.RegisteredMachines);
    logger.LogInformation("  Last Activity: {LastActivityAt}", health.LastActivityAt);

    if (health.RegisteredMachines == 2)
    {
        logger.LogInformation("✓ Health check passed");
    }
    else
    {
        logger.LogError("✗ Health check failed: Expected 2 machines, got {Count}", health.RegisteredMachines);
    }

    await messageBus.StopAsync();
    logger.LogInformation("\n✓ Message bus stopped");
}
