using Microsoft.Extensions.Logging;
using XStateNet.Helpers;
using XStateNet.InterProcess.Service;

namespace XStateNet.InterProcess.Tests;

/// <summary>
/// Integration tests for NamedPipeMessageBus
/// </summary>
public class MessageBusIntegrationTests : IAsyncLifetime
{
    private NamedPipeMessageBus? _messageBus;
    private readonly string _testPipeName = $"XStateNet.Test.{Guid.NewGuid()}";
    private ILoggerFactory? _loggerFactory;

    public async Task InitializeAsync()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        _messageBus = new NamedPipeMessageBus(
            _testPipeName,
            _loggerFactory.CreateLogger<NamedPipeMessageBus>());

        await _messageBus.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_messageBus != null)
        {
            await _messageBus.StopAsync();
            _messageBus.Dispose();
        }
        _loggerFactory?.Dispose();
    }

    [Fact]
    public async Task MessageBus_Should_Start_And_Stop_Cleanly()
    {
        // Arrange & Act
        var health = _messageBus!.GetHealthStatus();

        // Assert
        Assert.True(health.IsHealthy);
        Assert.Equal(0, health.RegisteredMachines);
    }

    [Fact]
    public async Task MessageBus_Should_Register_Machines()
    {
        // Arrange
        var registration = new MachineRegistration(
            "test-machine",
            "TestProcess",
            Environment.ProcessId,
            DateTime.UtcNow);

        // Act
        await _messageBus!.RegisterMachineAsync("test-machine", registration);

        // Assert
        var health = _messageBus.GetHealthStatus();
        Assert.Equal(1, health.RegisteredMachines);
    }

    [Fact]
    public async Task MessageBus_Should_Unregister_Machines()
    {
        // Arrange
        var registration = new MachineRegistration("test-machine", "Test", Environment.ProcessId, DateTime.UtcNow);
        await _messageBus!.RegisterMachineAsync("test-machine", registration);

        // Act
        await _messageBus.UnregisterMachineAsync("test-machine");

        // Assert
        var health = _messageBus.GetHealthStatus();
        Assert.Equal(0, health.RegisteredMachines);
    }

    [Fact]
    public async Task MessageBus_Should_Route_Events_Between_Subscribers()
    {
        // Arrange
        var receivedEvent = false;
        var eventPayload = "";
        var tcs = new TaskCompletionSource<bool>();

        await _messageBus!.SubscribeAsync("receiver", evt =>
        {
            if (evt.EventName == "TEST_EVENT")
            {
                receivedEvent = true;
                eventPayload = evt.Payload?.ToString() ?? "";
                tcs.SetResult(true);
            }
            return Task.CompletedTask;
        });

        // Act
        await _messageBus.SendEventAsync("sender", "receiver", "TEST_EVENT", "Hello World");
        await Task.WhenAny(tcs.Task, Task.Delay(2000));

        // Assert
        Assert.True(receivedEvent, "Event should be received");
        Assert.Contains("Hello", eventPayload);
    }

    [Fact]
    public async Task MessageBus_Should_Handle_Multiple_Subscribers()
    {
        // Arrange
        var received1 = false;
        var received2 = false;
        var tcs = new TaskCompletionSource<bool>();

        await _messageBus!.SubscribeAsync("machine-1", evt =>
        {
            received1 = true;
            return Task.CompletedTask;
        });

        await _messageBus.SubscribeAsync("machine-2", evt =>
        {
            received2 = true;
            tcs.SetResult(true);
            return Task.CompletedTask;
        });

        // Act
        await _messageBus.SendEventAsync("sender", "machine-1", "EVENT_1", null);
        await _messageBus.SendEventAsync("sender", "machine-2", "EVENT_2", null);
        await Task.WhenAny(tcs.Task, Task.Delay(2000));

        // Assert
        Assert.True(received1, "Machine 1 should receive event");
        Assert.True(received2, "Machine 2 should receive event");
    }

    [Fact]
    public async Task MessageBus_Should_Update_Health_Status()
    {
        // Arrange
        var registration = new MachineRegistration("test", "Test", Environment.ProcessId, DateTime.UtcNow);
        await _messageBus!.RegisterMachineAsync("test", registration);

        // Act
        await _messageBus.SendEventAsync("sender", "test", "PING", null);
        // Brief grace period for health status to update
        await Task.Yield();

        var health = _messageBus.GetHealthStatus();

        // Assert
        Assert.True(health.IsHealthy);
        Assert.True(health.LastActivityAt > DateTime.UtcNow.AddSeconds(-2));
    }

    [Fact]
    public async Task MessageBus_Should_Handle_Rapid_Event_Sending()
    {
        // Arrange
        var receivedCount = 0;
        var tcs = new TaskCompletionSource<bool>();

        await _messageBus!.SubscribeAsync("receiver", evt =>
        {
            Interlocked.Increment(ref receivedCount);
            if (receivedCount >= 100)
            {
                tcs.SetResult(true);
            }
            return Task.CompletedTask;
        });

        // Act
        var sendTasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            sendTasks.Add(_messageBus.SendEventAsync("sender", "receiver", "RAPID_EVENT", new { Index = i }));
        }
        await Task.WhenAll(sendTasks);
        await Task.WhenAny(tcs.Task, Task.Delay(5000));

        // Assert
        Assert.Equal(100, receivedCount);
    }

    [Fact]
    public async Task MessageBus_Should_Support_Unsubscribe()
    {
        // Arrange
        var receivedCount = 0;

        var subscription = await _messageBus!.SubscribeAsync("test", evt =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        });

        // Act
        await _messageBus.SendEventAsync("sender", "test", "EVENT_1", null);
        await DeterministicWait.WaitForCountAsync(
            getCount: () => receivedCount,
            targetValue: 1,
            timeoutSeconds: 2);
        Assert.Equal(1, receivedCount);

        subscription.Dispose(); // Unsubscribe

        await _messageBus.SendEventAsync("sender", "test", "EVENT_2", null);
        // Brief grace period to verify no additional messages received
        await Task.Yield();

        // Assert
        Assert.Equal(1, receivedCount); // Should still be 1, not 2
    }
}
