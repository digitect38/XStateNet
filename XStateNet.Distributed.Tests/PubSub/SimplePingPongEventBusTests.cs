using XStateNet.Distributed.EventBus.Optimized;
using Xunit;
using Xunit.Abstractions;

// Suppress obsolete warning - these tests are specifically testing the OptimizedInMemoryEventBus
// infrastructure itself, not demonstrating application patterns. Machines communicate via
// _eventBus.PublishEventAsync() and _eventBus.SubscribeToMachineAsync() for distributed scenarios.
// For local inter-machine communication, use EventBusOrchestrator with ExtendedPureStateMachineFactory.
#pragma warning disable CS0618

namespace XStateNet.Distributed.Tests.PubSub
{
    /// <summary>
    /// Simple ping-pong tests demonstrating state machine communication via event bus
    /// </summary>
    [Collection("Sequential")]
    public class SimplePingPongEventBusTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly OptimizedInMemoryEventBus _eventBus;

        public SimplePingPongEventBusTests(ITestOutputHelper output)
        {
            _output = output;
            _eventBus = new OptimizedInMemoryEventBus(workerCount: 1);
        }

        [Fact]
        public async Task PingPong_ViaEventBus_ExchangesMessages()
        {
            // Arrange
            await _eventBus.ConnectAsync();

            var pingCount = 0;
            var pongCount = 0;
            var maxExchanges = 5;
            var completed = new TaskCompletionSource<bool>();
            var testStartTime = DateTime.UtcNow;

            // Create simple ping machine
            var pingJson = @"{
                ""id"": ""ping"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": ""active""
                        }
                    },
                    ""active"": {
                        ""on"": {
                            ""PONG"": ""active"",
                            ""COMPLETE"": ""done""
                        }
                    },
                    ""done"": {
                        ""type"": ""final""
                    }
                }
            }";

            // Create simple pong machine
            var pongJson = @"{
                ""id"": ""pong"",
                ""initial"": ""waiting"",
                ""states"": {
                    ""waiting"": {
                        ""on"": {
                            ""PING"": ""waiting""
                        }
                    }
                }
            }";

            var pingMachine = StateMachineFactory.CreateFromScript(pingJson, true, false);
            var pongMachine = StateMachineFactory.CreateFromScript(pongJson, true, false);

            // Subscribe ping machine to events
            var pingSubscription = await _eventBus.SubscribeToMachineAsync("ping", async evt =>
            {
                var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                _output.WriteLine($"[{elapsedMs:F1}ms] [Ping] Received: {evt.EventName}");

                if (evt.EventName == "START")
                {
                    await pingMachine.SendAsync("START");
                    // Send first ping
                    pingCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Ping] Sending PING #{pingCount}");
                    await _eventBus.PublishEventAsync("pong", "PING", pingCount);
                }
                else if (evt.EventName == "PONG")
                {
                    pongCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Ping] Got PONG #{pongCount}");

                    if (pingCount < maxExchanges)
                    {
                        // Send another ping
                        await Task.Yield();
                        pingCount++;
                        var sendElapsed = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                        _output.WriteLine($"[{sendElapsed:F1}ms] [Ping] Sending PING #{pingCount}");
                        await _eventBus.PublishEventAsync("pong", "PING", pingCount);
                    }
                    else
                    {
                        // Complete the test
                        await pingMachine.SendAsync("COMPLETE");
                        completed.TrySetResult(true);
                    }
                }
            });

            // Subscribe pong machine to events
            var pongSubscription = await _eventBus.SubscribeToMachineAsync("pong", async evt =>
            {
                var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                _output.WriteLine($"[{elapsedMs:F1}ms] [Pong] Received: {evt.EventName}");

                if (evt.EventName == "PING")
                {
                    await pongMachine.SendAsync("PING");
                    // Send pong back
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Pong] Sending PONG back");
                    await _eventBus.PublishEventAsync("ping", "PONG", evt.Payload);
                }
            });

            // Act
            await pingMachine.StartAsync();
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] [Ping] Machine started");

            await pongMachine.StartAsync();
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] [Pong] Machine started");

            // Start the ping-pong sequence
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] Starting ping-pong sequence");
            await _eventBus.PublishEventAsync("ping", "START", null);

            // Wait for completion
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await completed.Task.WaitAsync(cts.Token);

            // Assert
            Assert.Equal(maxExchanges, pingCount);
            Assert.Equal(maxExchanges, pongCount);
            Assert.Equal("#ping.done", pingMachine.GetActiveStateNames());

            // Cleanup
            pingSubscription.Dispose();
            pongSubscription.Dispose();
            pingMachine.Stop();
            pongMachine.Stop();

            var totalElapsed = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
            _output.WriteLine($"\n[{totalElapsed:F1}ms] === Test Complete ===");
            _output.WriteLine($"[{totalElapsed:F1}ms] Total pings sent: {pingCount}");
            _output.WriteLine($"[{totalElapsed:F1}ms] Total pongs received: {pongCount}");
            _output.WriteLine($"[{totalElapsed:F1}ms] Total test duration: {totalElapsed:F1}ms");
        }

        [Fact]
        public async Task MultipleMachines_RoundRobin_ViaEventBus()
        {
            // Arrange
            await _eventBus.ConnectAsync();

            var machineCount = 3;
            var machines = new StateMachine[machineCount];
            var messageCount = new int[machineCount];
            var totalMessages = 0;
            var targetTotal = machineCount * 3; // Each machine sends 3 messages
            var completed = new TaskCompletionSource<bool>();
            var testStartTime = DateTime.UtcNow;

            // Create machines
            for (int i = 0; i < machineCount; i++)
            {
                var json = $$"""
                {                    
                    id: 'machine-{i}',
                    initial: 'ready',
                    states: {
                        ready: {
                            on: {
                                TOKEN: 'ready'
                            }
                        }
                    }                
                }
                """;

                machines[i] = StateMachineFactory.CreateFromScript(json);
            }

            // Subscribe each machine
            for (int i = 0; i < machineCount; i++)
            {
                var machineIndex = i;
                var machineId = $"machine-{i}";

                var subscription = await _eventBus.SubscribeToMachineAsync(machineId, async evt =>
                {
                    if (evt.EventName == "TOKEN")
                    {
                        var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                        messageCount[machineIndex]++;
                        var currentTotal = Interlocked.Increment(ref totalMessages);

                        _output.WriteLine($"[{elapsedMs:F1}ms] [{machineId}] Received TOKEN (count: {messageCount[machineIndex]}, total: {currentTotal})");

                        await machines[machineIndex].SendAsync("TOKEN");

                        // Pass token to next machine
                        if (currentTotal < targetTotal)
                        {
                            await Task.Yield();
                            var nextIndex = (machineIndex + 1) % machineCount;
                            var nextMachine = $"machine-{nextIndex}";
                            _output.WriteLine($"[{elapsedMs:F1}ms] [{machineId}] Passing TOKEN to {nextMachine}");
                            await _eventBus.PublishEventAsync(nextMachine, "TOKEN", currentTotal);
                        }
                        else
                        {
                            _output.WriteLine($"[{elapsedMs:F1}ms] [{machineId}] Completed - all messages processed");
                            completed.TrySetResult(true);
                        }
                    }
                });
            }

            // Act
            foreach (var machine in machines)
            {
                await machine.StartAsync();
            }

            // Start the round-robin
            var startElapsed = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{startElapsed:F1}ms] Starting round-robin with first TOKEN");
            await _eventBus.PublishEventAsync("machine-0", "TOKEN", 0);

            // Wait for completion
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await completed.Task.WaitAsync(cts.Token);

            // Assert
            Assert.Equal(targetTotal, totalMessages);
            for (int i = 0; i < machineCount; i++)
            {
                Assert.Equal(3, messageCount[i]);
            }

            // Cleanup
            foreach (var machine in machines)
            {
                machine.Stop();
            }

            var totalElapsed = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
            _output.WriteLine($"\n[{totalElapsed:F1}ms] === Round-Robin Complete ===");
            _output.WriteLine($"[{totalElapsed:F1}ms] Total messages: {totalMessages}");
            for (int i = 0; i < machineCount; i++)
            {
                _output.WriteLine($"[{totalElapsed:F1}ms] Machine {i} processed {messageCount[i]} messages");
            }
            _output.WriteLine($"[{totalElapsed:F1}ms] Total test duration: {totalElapsed:F1}ms");
        }

        public void Dispose()
        {
            _eventBus?.Dispose();
        }
    }
}