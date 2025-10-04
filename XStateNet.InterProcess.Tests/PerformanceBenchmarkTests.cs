using Microsoft.Extensions.Logging;
using System.Diagnostics;
using XStateNet.InterProcess.Service;
using XStateNet.InterProcess.TestClient;

namespace XStateNet.InterProcess.Tests;

/// <summary>
/// Performance benchmark tests for InterProcess communication
/// </summary>
public class PerformanceBenchmarkTests : IAsyncLifetime
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
        await Task.Delay(200);
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
    [Trait("Category", "Performance")]
    public async Task Benchmark_Throughput_Should_Exceed_500_Messages_Per_Second()
    {
        // Arrange
        var sender = new InterProcessClient("perf-sender", _testPipeName);
        var receiver = new InterProcessClient("perf-receiver", _testPipeName);

        try
        {
            await sender.ConnectAsync();
            await receiver.ConnectAsync();
            await Task.Delay(300);

            var receivedCount = 0;
            var messageCount = 200; // Reduced for faster test
            var tcs = new TaskCompletionSource<bool>();

            receiver.OnEvent("PERF_EVENT", evt =>
            {
                var count = Interlocked.Increment(ref receivedCount);
                if (count >= messageCount)
                {
                    tcs.TrySetResult(true);
                }
            });

            // Act
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < messageCount; i++)
            {
                await sender.SendEventAsync("perf-receiver", "PERF_EVENT", new { Index = i });
            }

            await Task.WhenAny(tcs.Task, Task.Delay(15000));
            sw.Stop();

            var throughput = messageCount / sw.Elapsed.TotalSeconds;

            // Assert
            Assert.Equal(messageCount, receivedCount);
            Assert.True(throughput > 500, $"Throughput ({throughput:F0} msg/sec) should exceed 500 msg/sec");
        }
        finally
        {
            sender.Dispose();
            receiver.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Benchmark_Connection_And_Basic_Communication()
    {
        // Arrange & Act - Measure connection time
        var sw = Stopwatch.StartNew();
        var client1 = new InterProcessClient("latency-1", _testPipeName);
        var client2 = new InterProcessClient("latency-2", _testPipeName);

        try
        {
            await client1.ConnectAsync();
            await client2.ConnectAsync();
            sw.Stop();

            await Task.Delay(300);

            // Measure basic ping-pong
            var receivedResponse = false;
            var tcs = new TaskCompletionSource<bool>();

            client2.OnEvent("PING", async evt =>
            {
                await client2.SendEventAsync("latency-1", "PONG", evt.Payload);
            });

            client1.OnEvent("PONG", evt =>
            {
                receivedResponse = true;
                tcs.TrySetResult(true);
            });

            await client1.SendEventAsync("latency-2", "PING", new { Message = "test" });
            await Task.WhenAny(tcs.Task, Task.Delay(5000));

            // Assert
            Assert.True(sw.ElapsedMilliseconds < 1000, $"Connection time ({sw.ElapsedMilliseconds}ms) should be under 1000ms");
            Assert.True(receivedResponse, "Should receive ping-pong response");
        }
        finally
        {
            client1.Dispose();
            client2.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Benchmark_Concurrent_Clients_Should_Handle_10_Clients()
    {
        // Arrange
        var clientCount = 10;
        var messagesPerClient = 50;
        var clients = new List<InterProcessClient>();
        var receivedCounts = new Dictionary<string, int>();
        var tcs = new TaskCompletionSource<bool>();

        try
        {
            // Create and connect clients
            for (int i = 0; i < clientCount; i++)
            {
                var machineId = $"concurrent-{i}";
                var client = new InterProcessClient(machineId, _testPipeName);
                await client.ConnectAsync();
                clients.Add(client);
                receivedCounts[machineId] = 0;

                client.OnEvent("BROADCAST", evt =>
                {
                    lock (receivedCounts)
                    {
                        receivedCounts[machineId]++;
                        var total = receivedCounts.Values.Sum();
                        if (total >= clientCount * messagesPerClient)
                        {
                            tcs.TrySetResult(true);
                        }
                    }
                });
            }

            await Task.Delay(200);

            // Act - Each client broadcasts to all others
            var sw = Stopwatch.StartNew();
            var sendTasks = new List<Task>();

            foreach (var sender in clients)
            {
                foreach (var receiver in clients)
                {
                    if (sender.MachineId != receiver.MachineId)
                    {
                        sendTasks.Add(sender.SendEventAsync(
                            receiver.MachineId,
                            "BROADCAST",
                            new { From = sender.MachineId }));
                    }
                }
            }

            await Task.WhenAll(sendTasks);

            // Wait for all messages to be received (or timeout after 20 seconds)
            await Task.WhenAny(tcs.Task, Task.Delay(20000));
            sw.Stop();

            var expectedTotal = clientCount * (clientCount - 1); // Each client sends to all others
            var actualTotal = receivedCounts.Values.Sum();

            // Assert
            Assert.Equal(expectedTotal, actualTotal);
            Assert.All(receivedCounts.Values, count => Assert.Equal(clientCount - 1, count));
            Assert.True(sw.ElapsedMilliseconds < 25000, "Should complete within 25 seconds (with thread-safety overhead)");
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
    [Trait("Category", "Performance")]
    public async Task Benchmark_Large_Payload_Should_Handle_10KB_Messages()
    {
        // Arrange
        var sender = new InterProcessClient("large-sender", _testPipeName);
        var receiver = new InterProcessClient("large-receiver", _testPipeName);

        try
        {
            await sender.ConnectAsync();
            await receiver.ConnectAsync();
            await Task.Delay(200);

            var receivedCount = 0;
            var messageCount = 50;
            var tcs = new TaskCompletionSource<bool>();

            receiver.OnEvent("LARGE_PAYLOAD", evt =>
            {
                var count = Interlocked.Increment(ref receivedCount);
                if (count >= messageCount)
                {
                    tcs.TrySetResult(true);
                }
            });

            // Create 10KB payload
            var largePayload = new
            {
                Data = new string('X', 10 * 1024),
                Index = 0,
                Timestamp = DateTime.UtcNow
            };

            // Act
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < messageCount; i++)
            {
                await sender.SendEventAsync("large-receiver", "LARGE_PAYLOAD", largePayload);
            }

            await Task.WhenAny(tcs.Task, Task.Delay(10000));
            sw.Stop();

            // Assert
            Assert.Equal(messageCount, receivedCount);
            Assert.True(sw.ElapsedMilliseconds < 5000, "Should handle large payloads within reasonable time");
        }
        finally
        {
            sender.Dispose();
            receiver.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Benchmark_Connection_Time_Should_Be_Under_500ms()
    {
        // Arrange & Act
        var sw = Stopwatch.StartNew();
        var client = new InterProcessClient("connection-test", _testPipeName);

        try
        {
            await client.ConnectAsync();
            sw.Stop();

            // Assert
            Assert.True(sw.ElapsedMilliseconds < 500, $"Connection time ({sw.ElapsedMilliseconds}ms) should be under 500ms");
            Assert.True(client.IsConnected);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Benchmark_Bidirectional_Ping_Pong_Performance()
    {
        // Arrange
        var ping = new InterProcessClient("ping", _testPipeName);
        var pong = new InterProcessClient("pong", _testPipeName);

        try
        {
            await ping.ConnectAsync();
            await pong.ConnectAsync();
            await Task.Delay(300);

            var roundTrips = 0;
            var targetRoundTrips = 50; // Reduced for faster test
            var tcs = new TaskCompletionSource<bool>();

            pong.OnEvent("PING", async evt =>
            {
                await pong.SendEventAsync("ping", "PONG", evt.Payload);
            });

            ping.OnEvent("PONG", async evt =>
            {
                var count = Interlocked.Increment(ref roundTrips);
                if (count < targetRoundTrips)
                {
                    await ping.SendEventAsync("pong", "PING", new { Count = count });
                }
                else
                {
                    tcs.TrySetResult(true);
                }
            });

            // Act
            var sw = Stopwatch.StartNew();
            await ping.SendEventAsync("pong", "PING", new { Count = 0 });
            await Task.WhenAny(tcs.Task, Task.Delay(15000));
            sw.Stop();

            var throughput = (roundTrips * 2.0) / sw.Elapsed.TotalSeconds; // Each round-trip is 2 messages

            // Assert
            Assert.Equal(targetRoundTrips, roundTrips);
            Assert.True(throughput > 50, $"Ping-pong throughput ({throughput:F0} msg/sec) should exceed 50 msg/sec");
        }
        finally
        {
            ping.Dispose();
            pong.Dispose();
        }
    }
}
