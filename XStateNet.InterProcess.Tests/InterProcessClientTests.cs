using Microsoft.Extensions.Logging;
using XStateNet.InterProcess.Service;
using XStateNet.InterProcess.TestClient;

namespace XStateNet.InterProcess.Tests;

/// <summary>
/// Unit tests for InterProcessClient
/// </summary>
public class InterProcessClientTests : IAsyncLifetime
{
    private NamedPipeMessageBus? _messageBus;
    private readonly string _testPipeName = $"XStateNet.Test.{Guid.NewGuid()}";
    private ILoggerFactory? _loggerFactory;

    public async Task InitializeAsync()
    {
        // Create logger factory
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning); // Reduce noise in tests
        });

        // Start message bus
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
    public async Task Client_Should_Connect_Successfully()
    {
        // Arrange
        var client = new InterProcessClient("test-client", _testPipeName);

        try
        {
            // Act
            await client.ConnectAsync();

            // Assert
            Assert.True(client.IsConnected);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Client_Should_Fail_To_Connect_With_Invalid_Pipe()
    {
        // Arrange
        var client = new InterProcessClient("test-client", "InvalidPipeName");

        try
        {
            // Act & Assert
            await Assert.ThrowsAsync<Exception>(async () =>
            {
                await client.ConnectAsync();
            });
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Client_Should_Send_And_Receive_Event()
    {
        // Arrange
        var client1 = new InterProcessClient("client-1", _testPipeName);
        var client2 = new InterProcessClient("client-2", _testPipeName);

        try
        {
            await client1.ConnectAsync();
            await client2.ConnectAsync();

            var receivedEvent = false;
            var tcs = new TaskCompletionSource<bool>();

            client2.OnEvent("TEST_EVENT", evt =>
            {
                receivedEvent = true;
                tcs.SetResult(true);
            });

            // Act
            await client1.SendEventAsync("client-2", "TEST_EVENT", new { Message = "Hello" });

            // Wait for event with timeout
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));

            // Assert
            Assert.True(receivedEvent, "Client 2 should have received the event");
            Assert.True(completed == tcs.Task, "Event should be received within timeout");
        }
        finally
        {
            client1.Dispose();
            client2.Dispose();
        }
    }

    [Fact]
    public async Task Client_Should_Handle_Multiple_Event_Handlers()
    {
        // Arrange
        var client1 = new InterProcessClient("sender", _testPipeName);
        var client2 = new InterProcessClient("receiver", _testPipeName);

        try
        {
            await client1.ConnectAsync();
            await client2.ConnectAsync();

            var handler1Called = false;
            var handler2Called = false;
            var tcs = new TaskCompletionSource<bool>();

            client2.OnEvent("MULTI_EVENT", evt => { handler1Called = true; });
            client2.OnEvent("MULTI_EVENT", evt =>
            {
                handler2Called = true;
                tcs.SetResult(true);
            });

            // Act
            await client1.SendEventAsync("receiver", "MULTI_EVENT", null);
            await Task.WhenAny(tcs.Task, Task.Delay(2000));

            // Assert
            Assert.True(handler1Called, "First handler should be called");
            Assert.True(handler2Called, "Second handler should be called");
        }
        finally
        {
            client1.Dispose();
            client2.Dispose();
        }
    }

    [Fact]
    public async Task Client_Should_Throw_When_Sending_Without_Connection()
    {
        // Arrange
        var client = new InterProcessClient("test", _testPipeName);

        try
        {
            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await client.SendEventAsync("target", "EVENT", null);
            });
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Multiple_Clients_Should_Connect_Concurrently()
    {
        // Arrange
        var clients = new List<InterProcessClient>();
        for (int i = 0; i < 10; i++)
        {
            clients.Add(new InterProcessClient($"client-{i}", _testPipeName));
        }

        try
        {
            // Act
            var connectTasks = clients.Select(c => c.ConnectAsync()).ToArray();
            await Task.WhenAll(connectTasks);

            // Assert
            Assert.All(clients, client => Assert.True(client.IsConnected));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task Client_Should_Dispose_Cleanly()
    {
        // Arrange
        var client = new InterProcessClient("test-dispose", _testPipeName);
        await client.ConnectAsync();
        Assert.True(client.IsConnected);

        // Act
        client.Dispose();

        // Assert
        Assert.False(client.IsConnected);
    }
}
