using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet;
using XStateNet.InterMachine;

namespace Test.InterMachine
{
    /// <summary>
    /// Fixed symmetric tests for XStateNet-IM framework
    /// </summary>
    [Collection("Sequential")]
    public class InterMachineSymmetricFixedTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly InterMachineSession _session;
        private DateTime _testStartTime;

        public InterMachineSymmetricFixedTests(ITestOutputHelper output)
        {
            _output = output;
            _session = new InterMachineSession();
            _testStartTime = DateTime.UtcNow;
        }

        [Fact]
        public async Task SymmetricPingPong_SimpleExchange()
        {
            // Arrange
            _testStartTime = DateTime.UtcNow;
            var machine1Exchanges = 0;
            var machine2Exchanges = 0;
            var targetExchanges = 5;
            var completed = new TaskCompletionSource<bool>();

            // Simple symmetric machines
            var machineJson = @"{
                ""id"": ""symmetric"",
                ""initial"": ""ready"",
                ""states"": {
                    ""ready"": {
                        ""on"": {
                            ""PING"": ""ready"",
                            ""PONG"": ""ready"",
                            ""DONE"": ""complete""
                        }
                    },
                    ""complete"": {
                        ""type"": ""final""
                    }
                }
            }";

            var machine1 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine1, machineJson.Replace("\"symmetric\"", "\"machine1\""));

            var machine2 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine2, machineJson.Replace("\"symmetric\"", "\"machine2\""));

            var cm1 = _session.AddMachine(machine1, "machine1");
            var cm2 = _session.AddMachine(machine2, "machine2");
            _session.Connect("machine1", "machine2");

            // Machine1 message handler
            cm1.OnMessage(async (msg) =>
            {
                if (msg.EventName == "PONG")
                {
                    await Task.Delay(1); // Reduce load
                    machine1Exchanges++;
                    _output.WriteLine($"Machine1: Received PONG #{machine1Exchanges}");
                    await machine1.SendAsync("PONG");

                    if (machine1Exchanges < targetExchanges)
                    {
                        // Continue exchange
                        await cm1.SendToAsync("machine2", "PING", machine1Exchanges + 1);
                    }
                    else
                    {
                        // Complete
                        await machine1.SendAsync("DONE");
                        var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
                        _output.WriteLine($"[{elapsed:F1}ms] Machine1: Complete");
                        completed.TrySetResult(true);
                    }
                }
            });

            // Machine2 message handler
            cm2.OnMessage(async (msg) =>
            {
                if (msg.EventName == "PING")
                {
                    await Task.Delay(1); // Reduce load
                    machine2Exchanges++;
                    _output.WriteLine($"Machine2: Received PING #{machine2Exchanges}, sending PONG");
                    await machine2.SendAsync("PING");

                    // Always respond with PONG
                    await cm2.SendToAsync("machine1", "PONG", machine2Exchanges);

                    if (machine2Exchanges >= targetExchanges)
                    {
                        await machine2.SendAsync("DONE");
                        var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
                        _output.WriteLine($"[{elapsed:F1}ms] Machine2: Complete");
                    }
                }
            });

            await machine1.StartAsync();
            await machine2.StartAsync();

            // Act - Start the exchange
            var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] Starting symmetric exchange");
            await cm1.SendToAsync("machine2", "PING", 1);

            // Assert
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(targetExchanges, machine1Exchanges);
            Assert.Equal(targetExchanges, machine2Exchanges);
            Assert.Equal("#machine1.complete", machine1.GetActiveStateNames());
            Assert.Equal("#machine2.complete", machine2.GetActiveStateNames());

            elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] Success: {targetExchanges} symmetric exchanges completed!");
        }

        [Fact]
        public async Task BothMachinesInitiate_Simultaneously()
        {
            // Arrange
            _testStartTime = DateTime.UtcNow;
            var machine1Received = 0;
            var machine2Received = 0;
            var completed1 = new TaskCompletionSource<bool>();
            var completed2 = new TaskCompletionSource<bool>();

            var machineJson = @"{
                ""id"": ""machine"",
                ""initial"": ""active"",
                ""states"": {
                    ""active"": {
                        ""on"": {
                            ""MESSAGE"": ""active"",
                            ""FINISH"": ""done""
                        }
                    },
                    ""done"": {
                        ""type"": ""final""
                    }
                }
            }";

            var machine1 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine1, machineJson.Replace("\"machine\"", "\"machine1\""));

            var machine2 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine2, machineJson.Replace("\"machine\"", "\"machine2\""));

            var cm1 = _session.AddMachine(machine1, "machine1");
            var cm2 = _session.AddMachine(machine2, "machine2");
            _session.Connect("machine1", "machine2");

            cm1.OnMessage(async (msg) =>
            {
                if (msg.EventName == "MESSAGE")
                {
                    await Task.Delay(1); // Reduce load
                    machine1Received++;
                    _output.WriteLine($"Machine1 received message #{machine1Received} from {msg.FromMachineId}");
                    await machine1.SendAsync("MESSAGE");

                    if (machine1Received >= 3)
                    {
                        await machine1.SendAsync("FINISH");
                        completed1.TrySetResult(true);
                    }
                }
            });

            cm2.OnMessage(async (msg) =>
            {
                if (msg.EventName == "MESSAGE")
                {
                    await Task.Delay(1); // Reduce load
                    machine2Received++;
                    _output.WriteLine($"Machine2 received message #{machine2Received} from {msg.FromMachineId}");
                    await machine2.SendAsync("MESSAGE");

                    if (machine2Received >= 3)
                    {
                        await machine2.SendAsync("FINISH");
                        completed2.TrySetResult(true);
                    }
                }
            });

            await machine1.StartAsync();
            await machine2.StartAsync();

            // Act - Both initiate simultaneously
            var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] Both machines initiating simultaneously");
            var tasks = new[]
            {
                Task.Run(async () =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        await cm1.SendToAsync("machine2", "MESSAGE", new { seq = i });
                        await Task.Delay(10);
                    }
                }),
                Task.Run(async () =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        await cm2.SendToAsync("machine1", "MESSAGE", new { seq = i });
                        await Task.Delay(10);
                    }
                })
            };

            await Task.WhenAll(tasks);
            await Task.WhenAll(completed1.Task, completed2.Task).WaitAsync(TimeSpan.FromSeconds(2));

            // Assert
            Assert.Equal(3, machine1Received);
            Assert.Equal(3, machine2Received);
            Assert.Equal("#machine1.done", machine1.GetActiveStateNames());
            Assert.Equal("#machine2.done", machine2.GetActiveStateNames());

            elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] Success: Both machines exchanged messages simultaneously!");
        }

        [Fact]
        public async Task TrulySymmetric_BothCanPingAndPong()
        {
            // Arrange
            _testStartTime = DateTime.UtcNow;
            var machine1Stats = new { PingsSent = 0, PongsReceived = 0, PingsReceived = 0, PongsSent = 0 };
            var machine2Stats = new { PingsSent = 0, PongsReceived = 0, PingsReceived = 0, PongsSent = 0 };
            var completed = new TaskCompletionSource<bool>();

            var machine1PingsSent = 0;
            var machine1PongsReceived = 0;
            var machine1PingsReceived = 0;
            var machine1PongsSent = 0;

            var machine2PingsSent = 0;
            var machine2PongsReceived = 0;
            var machine2PingsReceived = 0;
            var machine2PongsSent = 0;

            var machineJson = @"{
                ""id"": ""machine"",
                ""initial"": ""ready"",
                ""states"": {
                    ""ready"": {}
                }
            }";

            var machine1 = new StateMachine();
            var machine2 = new StateMachine();

            StateMachineFactory.CreateFromScript(machine1, machineJson.Replace("\"machine\"", "\"machine1\""));
            StateMachineFactory.CreateFromScript(machine2, machineJson.Replace("\"machine\"", "\"machine2\""));

            var cm1 = _session.AddMachine(machine1, "machine1");
            var cm2 = _session.AddMachine(machine2, "machine2");
            _session.Connect("machine1", "machine2");

            // Machine1: Can both send PING and respond with PONG
            cm1.OnMessage(async (msg) =>
            {
                if (msg.EventName == "PING")
                {
                    await Task.Delay(1); // Reduce load
                    machine1PingsReceived++;
                    machine1PongsSent++;
                    _output.WriteLine($"Machine1: PING received, sending PONG (total pongs sent: {machine1PongsSent})");
                    await cm1.SendToAsync("machine2", "PONG", new { from = "machine1" });
                }
                else if (msg.EventName == "PONG")
                {
                    await Task.Delay(1); // Reduce load
                    machine1PongsReceived++;
                    _output.WriteLine($"Machine1: PONG received (total: {machine1PongsReceived})");

                    if (machine1PongsReceived >= 3 && machine2PongsReceived >= 3)
                    {
                        completed.TrySetResult(true);
                    }
                }
            });

            // Machine2: Can both send PING and respond with PONG
            cm2.OnMessage(async (msg) =>
            {
                if (msg.EventName == "PING")
                {
                    await Task.Delay(1); // Reduce load
                    machine2PingsReceived++;
                    machine2PongsSent++;
                    _output.WriteLine($"Machine2: PING received, sending PONG (total pongs sent: {machine2PongsSent})");
                    await cm2.SendToAsync("machine1", "PONG", new { from = "machine2" });
                }
                else if (msg.EventName == "PONG")
                {
                    await Task.Delay(1); // Reduce load
                    machine2PongsReceived++;
                    _output.WriteLine($"Machine2: PONG received (total: {machine2PongsReceived})");
                }
            });

            await machine1.StartAsync();
            await machine2.StartAsync();

            // Act - Both machines can initiate with PING
            var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] === Demonstrating True Symmetry ===");

            // Machine1 sends PINGs
            elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] Machine1 sending 3 PINGs:");
            for (int i = 0; i < 3; i++)
            {
                machine1PingsSent++;
                await cm1.SendToAsync("machine2", "PING", new { from = "machine1", seq = i });
                await Task.Delay(10);
            }

            // Machine2 also sends PINGs
            elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] Machine2 also sending 3 PINGs:");
            for (int i = 0; i < 3; i++)
            {
                machine2PingsSent++;
                await cm2.SendToAsync("machine1", "PING", new { from = "machine2", seq = i });
                await Task.Delay(10);
            }

            await Task.Delay(100); // Allow messages to process

            // Assert
            elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] === Results ===");
            _output.WriteLine($"Machine1: Sent {machine1PingsSent} PINGs, Received {machine1PingsReceived} PINGs");
            _output.WriteLine($"Machine1: Sent {machine1PongsSent} PONGs, Received {machine1PongsReceived} PONGs");
            _output.WriteLine($"Machine2: Sent {machine2PingsSent} PINGs, Received {machine2PingsReceived} PINGs");
            _output.WriteLine($"Machine2: Sent {machine2PongsSent} PONGs, Received {machine2PongsReceived} PONGs");

            Assert.Equal(3, machine1PingsSent);
            Assert.Equal(3, machine1PingsReceived);
            Assert.Equal(3, machine1PongsSent);
            Assert.Equal(3, machine1PongsReceived);

            Assert.Equal(3, machine2PingsSent);
            Assert.Equal(3, machine2PingsReceived);
            Assert.Equal(3, machine2PongsSent);
            Assert.Equal(3, machine2PongsReceived);

            elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] ✓ True symmetry achieved: Both machines can initiate PING and respond with PONG!");
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}