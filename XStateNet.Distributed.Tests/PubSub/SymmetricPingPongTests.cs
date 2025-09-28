using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet;
using XStateNet.Distributed.EventBus.Optimized;

namespace XStateNet.Distributed.Tests.PubSub
{
    /// <summary>
    /// Tests demonstrating symmetric distributed state machines communicating via event bus
    /// Both machines can initiate and respond to messages
    /// </summary>
    [Collection("Sequential")]
    public class SymmetricPingPongTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly OptimizedInMemoryEventBus _eventBus;

        public SymmetricPingPongTests(ITestOutputHelper output)
        {
            _output = output;
            _eventBus = new OptimizedInMemoryEventBus(workerCount: 1);
        }

        [Fact]
        public async Task SymmetricMachines_BidirectionalCommunication()
        {
            // Arrange
            await _eventBus.ConnectAsync();

            var machine1PingCount = 0;
            var machine1PongCount = 0;
            var machine2PingCount = 0;
            var machine2PongCount = 0;
            var maxExchanges = 3;
            var completed = new TaskCompletionSource<bool>();
            var testStartTime = DateTime.UtcNow;

            // Create symmetric machine JSON - both can ping and pong
            var createSymmetricMachine = (string id) => $@"{{
                ""id"": ""{id}"",
                ""initial"": ""idle"",
                ""states"": {{
                    ""idle"": {{
                        ""on"": {{
                            ""START"": ""active"",
                            ""PING"": ""active"",
                            ""PONG"": ""active""
                        }}
                    }},
                    ""active"": {{
                        ""on"": {{
                            ""PING"": ""active"",
                            ""PONG"": ""active"",
                            ""COMPLETE"": ""done""
                        }}
                    }},
                    ""done"": {{
                        ""type"": ""final""
                    }}
                }}
            }}";

            var machine1 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine1, createSymmetricMachine("machine1"));

            var machine2 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine2, createSymmetricMachine("machine2"));

            // Subscribe machine1 to events
            var sub1 = await _eventBus.SubscribeToMachineAsync("machine1", async evt =>
            {
                var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Received: {evt.EventName}");

                if (evt.EventName == "START")
                {
                    await machine1.SendAsync("START");
                    // Send initial ping
                    machine1PingCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Sending PING #{machine1PingCount}");
                    await _eventBus.PublishEventAsync("machine2", "PING", machine1PingCount);
                }
                else if (evt.EventName == "PING")
                {
                    await machine1.SendAsync("PING");
                    // Respond with pong
                    machine1PongCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Received PING, sending PONG #{machine1PongCount}");
                    await _eventBus.PublishEventAsync("machine2", "PONG", machine1PongCount);

                    if (machine1PongCount >= maxExchanges)
                    {
                        await machine1.SendAsync("COMPLETE");
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Complete");
                    }
                }
                else if (evt.EventName == "PONG")
                {
                    await machine1.SendAsync("PONG");
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Received PONG");

                    if (machine1PingCount < maxExchanges)
                    {
                        // Send another ping
                        machine1PingCount++;
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Sending PING #{machine1PingCount}");
                        await _eventBus.PublishEventAsync("machine2", "PING", machine1PingCount);
                    }
                    else
                    {
                        await machine1.SendAsync("COMPLETE");
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Complete");
                        completed.TrySetResult(true);
                    }
                }
            });

            // Subscribe machine2 to events
            var sub2 = await _eventBus.SubscribeToMachineAsync("machine2", async evt =>
            {
                var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Received: {evt.EventName}");

                if (evt.EventName == "START")
                {
                    await machine2.SendAsync("START");
                    // Machine2 can also initiate with a ping
                    machine2PingCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Sending PING #{machine2PingCount}");
                    await _eventBus.PublishEventAsync("machine1", "PING", machine2PingCount);
                }
                else if (evt.EventName == "PING")
                {
                    await machine2.SendAsync("PING");
                    // Respond with pong
                    machine2PongCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Received PING, sending PONG #{machine2PongCount}");
                    await _eventBus.PublishEventAsync("machine1", "PONG", machine2PongCount);

                    if (machine2PongCount >= maxExchanges)
                    {
                        await machine2.SendAsync("COMPLETE");
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Complete");
                    }
                }
                else if (evt.EventName == "PONG")
                {
                    await machine2.SendAsync("PONG");
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Received PONG");

                    if (machine2PingCount < maxExchanges)
                    {
                        // Send another ping
                        machine2PingCount++;
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Sending PING #{machine2PingCount}");
                        await _eventBus.PublishEventAsync("machine1", "PING", machine2PingCount);
                    }
                    else
                    {
                        await machine2.SendAsync("COMPLETE");
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Complete");
                    }
                }
            });

            // Act
            await machine1.StartAsync();
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] Machine1 started");

            await machine2.StartAsync();
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] Machine2 started");

            // Only machine1 initiates for this test
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] Machine1 initiating communication");
            await _eventBus.PublishEventAsync("machine1", "START", null);

            // Wait for completion
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await completed.Task.WaitAsync(cts.Token);

            // Assert
            Assert.Equal(maxExchanges, machine1PingCount);
            Assert.Equal(maxExchanges, machine2PongCount);
            Assert.Equal("#machine1.done", machine1.GetActiveStateNames());
            Assert.Equal("#machine2.done", machine2.GetActiveStateNames());

            // Cleanup
            sub1.Dispose();
            sub2.Dispose();
            machine1.Stop();
            machine2.Stop();

            var totalElapsed = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
            _output.WriteLine($"\n[{totalElapsed:F1}ms] === Test Complete ===");
            _output.WriteLine($"[{totalElapsed:F1}ms] Machine1 sent {machine1PingCount} pings, received {machine1PongCount} pongs");
            _output.WriteLine($"[{totalElapsed:F1}ms] Machine2 sent {machine2PongCount} pongs, received {machine2PingCount} pings");
        }

        [Fact]
        public async Task SymmetricMachines_SimultaneousInitiation()
        {
            // Arrange
            await _eventBus.ConnectAsync();

            var machine1MessageCount = 0;
            var machine2MessageCount = 0;
            var testStartTime = DateTime.UtcNow;
            var bothComplete = new TaskCompletionSource<bool>();
            var machine1Complete = false;
            var machine2Complete = false;

            // Create symmetric machines with IDs
            var createMachine = (string id) => $@"{{
                ""id"": ""{id}"",
                ""initial"": ""ready"",
                ""states"": {{
                    ""ready"": {{
                        ""on"": {{
                            ""START"": ""sending"",
                            ""MESSAGE"": ""receiving""
                        }}
                    }},
                    ""sending"": {{
                        ""on"": {{
                            ""SENT"": ""ready"",
                            ""MESSAGE"": ""receiving""
                        }}
                    }},
                    ""receiving"": {{
                        ""on"": {{
                            ""REPLY"": ""ready"",
                            ""DONE"": ""complete""
                        }}
                    }},
                    ""complete"": {{
                        ""type"": ""final""
                    }}
                }}
            }}";

            var machine1 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine1, createMachine("sym1"));

            var machine2 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine2, createMachine("sym2"));

            // Subscribe machine1
            var sub1 = await _eventBus.SubscribeToMachineAsync("sym1", async evt =>
            {
                var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                _output.WriteLine($"[{elapsedMs:F1}ms] [Sym1] Received: {evt.EventName}");

                if (evt.EventName == "START")
                {
                    await machine1.SendAsync("START");
                    // Send initial message
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Sym1] Sending MESSAGE");
                    await _eventBus.PublishEventAsync("sym2", "MESSAGE", new { from = "sym1" });
                    await machine1.SendAsync("SENT");
                }
                else if (evt.EventName == "MESSAGE")
                {
                    await machine1.SendAsync("MESSAGE");
                    machine1MessageCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Sym1] Received MESSAGE (count: {machine1MessageCount})");

                    if (machine1MessageCount < 3)
                    {
                        // Reply
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Sym1] Sending REPLY");
                        await _eventBus.PublishEventAsync("sym2", "REPLY", new { from = "sym1" });
                        await machine1.SendAsync("REPLY");
                    }
                    else
                    {
                        // Done
                        await machine1.SendAsync("DONE");
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Sym1] Complete");
                        machine1Complete = true;
                        if (machine2Complete)
                        {
                            bothComplete.TrySetResult(true);
                        }
                    }
                }
                else if (evt.EventName == "REPLY")
                {
                    await machine1.SendAsync("REPLY");
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Sym1] Received REPLY, sending MESSAGE");
                    await _eventBus.PublishEventAsync("sym2", "MESSAGE", new { from = "sym1" });
                }
            });

            // Subscribe machine2
            var sub2 = await _eventBus.SubscribeToMachineAsync("sym2", async evt =>
            {
                var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                _output.WriteLine($"[{elapsedMs:F1}ms] [Sym2] Received: {evt.EventName}");

                if (evt.EventName == "START")
                {
                    await machine2.SendAsync("START");
                    // Send initial message
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Sym2] Sending MESSAGE");
                    await _eventBus.PublishEventAsync("sym1", "MESSAGE", new { from = "sym2" });
                    await machine2.SendAsync("SENT");
                }
                else if (evt.EventName == "MESSAGE")
                {
                    await machine2.SendAsync("MESSAGE");
                    machine2MessageCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Sym2] Received MESSAGE (count: {machine2MessageCount})");

                    if (machine2MessageCount < 3)
                    {
                        // Reply
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Sym2] Sending REPLY");
                        await _eventBus.PublishEventAsync("sym1", "REPLY", new { from = "sym2" });
                        await machine2.SendAsync("REPLY");
                    }
                    else
                    {
                        // Done
                        await machine2.SendAsync("DONE");
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Sym2] Complete");
                        machine2Complete = true;
                        if (machine1Complete)
                        {
                            bothComplete.TrySetResult(true);
                        }
                    }
                }
                else if (evt.EventName == "REPLY")
                {
                    await machine2.SendAsync("REPLY");
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Sym2] Received REPLY, sending MESSAGE");
                    await _eventBus.PublishEventAsync("sym1", "MESSAGE", new { from = "sym2" });
                }
            });

            // Act
            await machine1.StartAsync();
            await machine2.StartAsync();
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] Both machines started");

            // Both machines initiate simultaneously
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] Both machines initiating simultaneously");
            await Task.WhenAll(
                _eventBus.PublishEventAsync("sym1", "START", null),
                _eventBus.PublishEventAsync("sym2", "START", null)
            );

            // Wait for both to complete
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await bothComplete.Task.WaitAsync(cts.Token);

            // Assert
            Assert.True(machine1MessageCount >= 3, $"Machine1 should have received at least 3 messages, got {machine1MessageCount}");
            Assert.True(machine2MessageCount >= 3, $"Machine2 should have received at least 3 messages, got {machine2MessageCount}");
            Assert.Equal("#sym1.complete", machine1.GetActiveStateNames());
            Assert.Equal("#sym2.complete", machine2.GetActiveStateNames());

            // Cleanup
            sub1.Dispose();
            sub2.Dispose();
            machine1.Stop();
            machine2.Stop();

            var totalElapsed = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
            _output.WriteLine($"\n[{totalElapsed:F1}ms] === Test Complete ===");
            _output.WriteLine($"[{totalElapsed:F1}ms] Sym1 received: {machine1MessageCount} messages");
            _output.WriteLine($"[{totalElapsed:F1}ms] Sym2 received: {machine2MessageCount} messages");
        }

        public void Dispose()
        {
            _eventBus?.Dispose();
        }
    }
}