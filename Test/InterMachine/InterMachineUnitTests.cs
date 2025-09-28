using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet;
using XStateNet.InterMachine;

namespace Test.InterMachine
{
    /// <summary>
    /// Unit tests for XStateNet-IM (InterMachine) framework
    /// </summary>
    [Collection("Sequential")]
    public class InterMachineUnitTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly InterMachineSession _session;
        private readonly DateTime _testStartTime;

        public InterMachineUnitTests(ITestOutputHelper output)
        {
            _output = output;
            _session = new InterMachineSession();
            _testStartTime = DateTime.UtcNow;
        }

        [Fact]
        public void RegisterMachine_Success()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, @"{""id"": ""test"", ""initial"": ""idle"", ""states"": {""idle"": {}}}");

            // Act
            var connectedMachine = _session.AddMachine(machine, "test-machine");

            // Assert
            Assert.NotNull(connectedMachine);
            Assert.Equal("test-machine", connectedMachine.MachineId);
            Assert.Same(machine, connectedMachine.Machine);
        }

        [Fact]
        public void ConnectMachines_Success()
        {
            // Arrange
            var machine1 = CreateSimpleMachine("machine1");
            var machine2 = CreateSimpleMachine("machine2");

            _session.AddMachine(machine1, "machine1");
            _session.AddMachine(machine2, "machine2");

            // Act
            _session.Connect("machine1", "machine2");

            // Assert - no exception means success
            var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] Machines connected successfully");
        }

        [Fact]
        public async Task SendDirectMessage_Success()
        {
            // Arrange
            var messageReceived = new TaskCompletionSource<string>();
            var machine1 = CreateSimpleMachine("machine1");
            var machine2 = CreateSimpleMachine("machine2");

            var cm1 = _session.AddMachine(machine1, "machine1");
            var cm2 = _session.AddMachine(machine2, "machine2");
            _session.Connect("machine1", "machine2");

            // Set up message handler for machine2
            cm2.OnMessage(async (msg) =>
            {
                _output.WriteLine($"Machine2 received: {msg.EventName} from {msg.FromMachineId}");
                messageReceived.SetResult(msg.EventName);
                await Task.CompletedTask;
            });

            // Act
            await cm1.SendToAsync("machine2", "TEST_MESSAGE", new { data = "Hello" });

            // Assert
            var result = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal("TEST_MESSAGE", result);
        }

        [Fact]
        public async Task BidirectionalCommunication_Success()
        {
            // Arrange
            var machine1Received = new TaskCompletionSource<string>();
            var machine2Received = new TaskCompletionSource<string>();

            var machine1 = CreateSimpleMachine("machine1");
            var machine2 = CreateSimpleMachine("machine2");

            var cm1 = _session.AddMachine(machine1, "machine1");
            var cm2 = _session.AddMachine(machine2, "machine2");
            _session.Connect("machine1", "machine2");

            cm1.OnMessage(async (msg) =>
            {
                _output.WriteLine($"Machine1 received: {msg.EventName}");
                machine1Received.TrySetResult(msg.EventName);
                await Task.CompletedTask;
            });

            cm2.OnMessage(async (msg) =>
            {
                _output.WriteLine($"Machine2 received: {msg.EventName}");
                machine2Received.TrySetResult(msg.EventName);
                await Task.CompletedTask;
            });

            // Act
            await cm1.SendToAsync("machine2", "HELLO_FROM_M1");
            await cm2.SendToAsync("machine1", "HELLO_FROM_M2");

            // Assert
            var result1 = await machine1Received.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var result2 = await machine2Received.Task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal("HELLO_FROM_M2", result1);
            Assert.Equal("HELLO_FROM_M1", result2);
        }

        [Fact]
        public async Task MultipleConnections_MeshTopology()
        {
            // Arrange
            var machineCount = 4;
            var machines = new StateMachine[machineCount];
            var connectedMachines = new ConnectedMachine[machineCount];
            var receivedMessages = new List<string>[machineCount];

            for (int i = 0; i < machineCount; i++)
            {
                machines[i] = CreateSimpleMachine($"machine{i}");
                connectedMachines[i] = _session.AddMachine(machines[i], $"machine{i}");
                receivedMessages[i] = new List<string>();

                var index = i;
                connectedMachines[i].OnMessage(async (msg) =>
                {
                    receivedMessages[index].Add($"{msg.FromMachineId}:{msg.EventName}");
                    _output.WriteLine($"Machine{index} received {msg.EventName} from {msg.FromMachineId}");
                    await Task.CompletedTask;
                });
            }

            // Create full mesh
            for (int i = 0; i < machineCount; i++)
            {
                for (int j = i + 1; j < machineCount; j++)
                {
                    _session.Connect($"machine{i}", $"machine{j}");
                }
            }

            // Act - each machine sends to all others
            for (int i = 0; i < machineCount; i++)
            {
                for (int j = 0; j < machineCount; j++)
                {
                    if (i != j)
                    {
                        await connectedMachines[i].SendToAsync($"machine{j}", $"MSG_FROM_{i}");
                    }
                }
            }

            await Task.Delay(100); // Allow messages to process

            // Assert
            for (int i = 0; i < machineCount; i++)
            {
                Assert.Equal(machineCount - 1, receivedMessages[i].Count);
                _output.WriteLine($"Machine{i} received {receivedMessages[i].Count} messages");
            }
        }

        [Fact]
        public async Task Broadcast_SendsToAllConnected()
        {
            // Arrange
            var receivedCount = 0;
            var machines = new StateMachine[3];
            var connectedMachines = new ConnectedMachine[3];

            for (int i = 0; i < 3; i++)
            {
                machines[i] = CreateSimpleMachine($"machine{i}");
                connectedMachines[i] = _session.AddMachine(machines[i], $"machine{i}");

                connectedMachines[i].OnMessage(async (msg) =>
                {
                    Interlocked.Increment(ref receivedCount);
                    _output.WriteLine($"Received broadcast: {msg.EventName}");
                    await Task.CompletedTask;
                });
            }

            // Connect machine0 to others
            _session.Connect("machine0", "machine1");
            _session.Connect("machine0", "machine2");

            // Act
            await connectedMachines[0].BroadcastAsync("BROADCAST_MSG");
            await Task.Delay(100);

            // Assert
            Assert.Equal(2, receivedCount); // machine1 and machine2 should receive
        }

        [Fact]
        public async Task StateTransitions_ThroughInterMachine()
        {
            // Arrange
            var machine1Json = @"{
                ""id"": ""machine1"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": ""active""
                        }
                    },
                    ""active"": {
                        ""on"": {
                            ""STOP"": ""idle""
                        }
                    }
                }
            }";

            var machine1 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine1, machine1Json);
            var machine2 = CreateSimpleMachine("machine2");

            var cm1 = _session.AddMachine(machine1, "machine1");
            var cm2 = _session.AddMachine(machine2, "machine2");
            _session.Connect("machine1", "machine2");

            await machine1.StartAsync();
            Assert.Equal("#machine1.idle", machine1.GetActiveStateNames());

            // Act - machine2 sends START to machine1
            await cm2.SendToAsync("machine1", "START");
            await Task.Delay(50);

            // Assert
            Assert.Equal("#machine1.active", machine1.GetActiveStateNames());

            // Act - machine2 sends STOP to machine1
            await cm2.SendToAsync("machine1", "STOP");
            await Task.Delay(50);

            // Assert
            Assert.Equal("#machine1.idle", machine1.GetActiveStateNames());
        }

        [Fact]
        public async Task PingPong_SymmetricExchange()
        {
            // Arrange
            var exchangeCount = 0;
            var maxExchanges = 5;
            var completed = new TaskCompletionSource<bool>();

            var machine1 = CreatePingPongMachine("machine1");
            var machine2 = CreatePingPongMachine("machine2");

            var cm1 = _session.AddMachine(machine1, "machine1");
            var cm2 = _session.AddMachine(machine2, "machine2");
            _session.Connect("machine1", "machine2");

            // Machine1 handles messages
            cm1.OnMessage(async (msg) =>
            {
                if (msg.EventName == "PONG")
                {
                    var count = Interlocked.Increment(ref exchangeCount);
                    _output.WriteLine($"Machine1 received PONG (exchange #{count})");

                    if (count < maxExchanges)
                    {
                        await Task.Delay(5);
                        await cm1.SendToAsync("machine2", "PING");
                        _output.WriteLine($"Machine1 sent PING");
                    }
                    else
                    {
                        completed.TrySetResult(true);
                    }
                }
            });

            // Machine2 responds to PING with PONG
            cm2.OnMessage(async (msg) =>
            {
                if (msg.EventName == "PING")
                {
                    _output.WriteLine($"Machine2 received PING, sending PONG");
                    await cm2.SendToAsync("machine1", "PONG");
                }
            });

            await machine1.StartAsync();
            await machine2.StartAsync();

            // Act - Start the ping-pong exchange
            var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] Starting ping-pong exchange");
            await cm1.SendToAsync("machine2", "PING");

            // Assert
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(exchangeCount >= maxExchanges, $"Should complete {maxExchanges} exchanges, got {exchangeCount}");
        }

        [Fact]
        public async Task ErrorHandling_UnregisteredMachine()
        {
            // Arrange
            var connector = new InterMachineConnector();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await connector.SendAsync("unknown1", "unknown2", "TEST");
            });
        }

        [Fact]
        public void ErrorHandling_DuplicateRegistration()
        {
            // Arrange
            var connector = new InterMachineConnector();
            var machine = CreateSimpleMachine("test");

            // Act
            connector.RegisterMachine("test", machine);

            // Assert
            Assert.Throws<InvalidOperationException>(() =>
            {
                connector.RegisterMachine("test", machine);
            });
        }

        [Fact]
        public async Task ErrorHandling_UnconnectedMachines()
        {
            // Arrange
            var connector = new InterMachineConnector();
            var machine1 = CreateSimpleMachine("m1");
            var machine2 = CreateSimpleMachine("m2");

            connector.RegisterMachine("m1", machine1);
            connector.RegisterMachine("m2", machine2);
            // Note: NOT connecting them

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await connector.SendAsync("m1", "m2", "TEST");
            });
        }

        [Fact]
        public async Task DataPayload_PreservedInTransmission()
        {
            // Arrange
            var receivedData = new TaskCompletionSource<object>();
            var testData = new { Name = "Test", Value = 42, Items = new[] { 1, 2, 3 } };

            var machine1 = CreateSimpleMachine("machine1");
            var machine2 = CreateSimpleMachine("machine2");

            var cm1 = _session.AddMachine(machine1, "machine1");
            var cm2 = _session.AddMachine(machine2, "machine2");
            _session.Connect("machine1", "machine2");

            cm2.OnMessage(async (msg) =>
            {
                receivedData.SetResult(msg.Data);
                await Task.CompletedTask;
            });

            // Act
            await cm1.SendToAsync("machine2", "DATA_TEST", testData);

            // Assert
            var result = await receivedData.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.NotNull(result);

            var resultDynamic = result as dynamic;
            Assert.Equal("Test", resultDynamic.Name);
            Assert.Equal(42, resultDynamic.Value);
        }

        [Fact]
        public async Task Performance_HighVolumeMessages()
        {
            // Arrange
            var messageCount = 1000;
            var receivedCount = 0;
            var completed = new TaskCompletionSource<bool>();

            var machine1 = CreateSimpleMachine("machine1");
            var machine2 = CreateSimpleMachine("machine2");

            var cm1 = _session.AddMachine(machine1, "machine1");
            var cm2 = _session.AddMachine(machine2, "machine2");
            _session.Connect("machine1", "machine2");

            cm2.OnMessage(async (msg) =>
            {
                var count = Interlocked.Increment(ref receivedCount);
                if (count == messageCount)
                {
                    completed.SetResult(true);
                }
                await Task.CompletedTask;
            });

            var startTime = DateTime.UtcNow;

            // Act
            for (int i = 0; i < messageCount; i++)
            {
                await cm1.SendToAsync("machine2", $"MSG_{i}", i);
            }

            // Assert
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            Assert.Equal(messageCount, receivedCount);
            _output.WriteLine($"Sent {messageCount} messages in {duration:F1}ms");
            _output.WriteLine($"Throughput: {messageCount / (duration / 1000):F0} messages/second");
        }

        [Fact]
        public async Task Disconnect_StopsCommunication()
        {
            // Arrange
            var connector = new InterMachineConnector();
            var machine1 = CreateSimpleMachine("m1");
            var machine2 = CreateSimpleMachine("m2");

            connector.RegisterMachine("m1", machine1);
            connector.RegisterMachine("m2", machine2);
            connector.Connect("m1", "m2");

            // Verify connection works
            await connector.SendAsync("m1", "m2", "TEST1");

            // Act
            connector.Disconnect("m1", "m2");

            // Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await connector.SendAsync("m1", "m2", "TEST2");
            });
        }

        [Fact]
        public void UnregisterMachine_RemovesAllConnections()
        {
            // Arrange
            var connector = new InterMachineConnector();
            var machine1 = CreateSimpleMachine("m1");
            var machine2 = CreateSimpleMachine("m2");
            var machine3 = CreateSimpleMachine("m3");

            connector.RegisterMachine("m1", machine1);
            connector.RegisterMachine("m2", machine2);
            connector.RegisterMachine("m3", machine3);

            connector.Connect("m1", "m2");
            connector.Connect("m1", "m3");

            // Act
            connector.UnregisterMachine("m1");

            // Assert
            var m2Connections = connector.GetConnections("m2");
            var m3Connections = connector.GetConnections("m3");

            Assert.Empty(m2Connections);
            Assert.Empty(m3Connections);
        }

        // Helper methods
        private StateMachine CreateSimpleMachine(string id)
        {
            var json = $@"{{
                ""id"": ""{id}"",
                ""initial"": ""idle"",
                ""states"": {{
                    ""idle"": {{}}
                }}
            }}";

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json);
            return machine;
        }

        private StateMachine CreatePingPongMachine(string id)
        {
            var json = $@"{{
                ""id"": ""{id}"",
                ""initial"": ""ready"",
                ""states"": {{
                    ""ready"": {{
                        ""on"": {{
                            ""PING"": ""ready"",
                            ""PONG"": ""ready""
                        }}
                    }}
                }}
            }}";

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json);
            return machine;
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}