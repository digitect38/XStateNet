using Microsoft.Extensions.Logging;
using XStateNet.InterProcess.Service;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

Console.WriteLine("=== XStateNet InterProcess Message Bus - Self Test ===\n");

// Create logger
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

var logger = loggerFactory.CreateLogger<Program>();

// Create message bus
var pipeName = "XStateNet.MessageBus.SelfTest";
var messageBus = new NamedPipeMessageBus(pipeName,
    loggerFactory.CreateLogger<NamedPipeMessageBus>());

try
{
    logger.LogInformation("Starting message bus...");
    await messageBus.StartAsync();
    await Task.Delay(500); // Let it fully start
    logger.LogInformation("✓ Message bus started\n");

    // =====================================================
    // PART 1: Test Named Pipe Protocol Layer
    // =====================================================
    logger.LogInformation("=== PART 1: Named Pipe Protocol Test ===\n");

    logger.LogInformation("--- Test 1.1: Connect to Named Pipe ---");
    using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipeClient.ConnectAsync(5000);
    logger.LogInformation("✓ Connected to pipe: {PipeName}", pipeName);

    using var reader = new StreamReader(pipeClient, Encoding.UTF8);
    using var writer = new StreamWriter(pipeClient, Encoding.UTF8);

    logger.LogInformation("\n--- Test 1.2: Send Registration through Pipe ---");
    var registerMessage = new
    {
        Type = 0, // MessageType.Register
        Payload = new
        {
            MachineId = "pipe-test-client",
            ProcessName = "SelfTest",
            ProcessId = Environment.ProcessId,
            RegisteredAt = DateTime.UtcNow
        }
    };

    var json = JsonSerializer.Serialize(registerMessage);
    logger.LogInformation("Sending registration: {Json}", json);
    await writer.WriteLineAsync(json);
    await writer.FlushAsync();
    logger.LogInformation("✓ Registration sent through pipe");

    logger.LogInformation("\n--- Test 1.3: Read Response from Pipe ---");
    var responseLine = await reader.ReadLineAsync();
    logger.LogInformation("Received response: {Response}", responseLine);

    if (responseLine != null)
    {
        var response = JsonSerializer.Deserialize<JsonDocument>(responseLine);
        var success = response?.RootElement.GetProperty("Success").GetBoolean() ?? false;
        if (success)
        {
            logger.LogInformation("✓ Registration response received successfully");
        }
        else
        {
            logger.LogError("✗ Registration failed: {Response}", responseLine);
        }
    }
    else
    {
        logger.LogError("✗ No response received from service");
    }

    logger.LogInformation("\n--- Test 1.4: Subscribe through Pipe ---");
    var subscribeMessage = new
    {
        Type = 3, // MessageType.Subscribe
        Payload = "pipe-test-client"
    };
    json = JsonSerializer.Serialize(subscribeMessage);
    await writer.WriteLineAsync(json);
    await writer.FlushAsync();
    logger.LogInformation("✓ Subscribe message sent");

    // Read subscribe response
    responseLine = await reader.ReadLineAsync();
    logger.LogInformation("Subscribe response: {Response}", responseLine);

    logger.LogInformation("\n✓✓✓ Named Pipe Protocol Test PASSED!\n");

    // =====================================================
    // PART 2: Test Direct API (Original Tests)
    // =====================================================
    logger.LogInformation("=== PART 2: Direct API Test ===\n");

    // Test 1: Register two test machines
    logger.LogInformation("--- Test 1: Machine Registration ---");
    await messageBus.RegisterMachineAsync("test-ping",
        new MachineRegistration("test-ping", "SelfTest", Environment.ProcessId, DateTime.UtcNow));
    logger.LogInformation("✓ Registered: test-ping");

    await messageBus.RegisterMachineAsync("test-pong",
        new MachineRegistration("test-pong", "SelfTest", Environment.ProcessId, DateTime.UtcNow));
    logger.LogInformation("✓ Registered: test-pong\n");

    // Test 2: Subscribe to events
    logger.LogInformation("--- Test 2: Event Subscription ---");
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
    logger.LogInformation("✓ test-ping subscribed\n");

    // Test 3: Send event and verify routing
    logger.LogInformation("--- Test 3: Event Routing ---");
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
    logger.LogInformation("  Last Activity: {LastActivityAt:s}", health.LastActivityAt);

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

    Console.WriteLine("\n=== SELF-TEST COMPLETE ===");
}
catch (Exception ex)
{
    logger.LogError(ex, "Self-test failed");
    Console.WriteLine($"\n✗✗✗ SELF-TEST FAILED: {ex.Message}");
    return 1;
}

return 0;
