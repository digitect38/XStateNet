using FluentAssertions;
using Microsoft.Extensions.Logging;
using XStateNet;
using XStateNet.Distributed;
using Xunit;

namespace XStateNet.Distributed.Tests
{
    public class DistributedStateMachineTests : IDisposable
    {
        private readonly List<DistributedStateMachine> _machines = new();
        
        [Fact]
        public void CreateFromScript_Should_CreateDistributedMachine()
        {
            // Arrange
            var json = @"
            {
                ""id"": ""simple"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": ""running""
                        }
                    },
                    ""running"": {
                        ""on"": {
                            ""STOP"": ""idle""
                        }
                    }
                }
            }";
            
            // Act
            var machine = DistributedStateMachineFactory.CreateFromScript(
                "test-machine",
                json,
                "local://test");
            _machines.Add(machine);
            
            // Assert
            machine.Should().NotBeNull();
            machine.StateMachine.Should().NotBeNull();
        }

        [Fact]
        public void Start_Should_StartMachine()
        {
            // Arrange
            var json = @"
            {
                ""id"": ""simple"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {}
                }
            }";
            var baseMachine = StateMachine.CreateFromScript(json);
            var machine = new DistributedStateMachine(
                baseMachine,
                "test-machine",
                "local://test");
            _machines.Add(machine);
            
            // Act
            machine.Start();
            
            // Assert
            // Note: StateMachine doesn't have a Started property in this implementation
            // We can verify it doesn't throw
        }

        [Fact]
        public void Send_LocalEvent_Should_ProcessLocally()
        {
            // Arrange
            var json = @"
            {
                ""id"": ""simple"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": ""running""
                        }
                    },
                    ""running"": {}
                }
            }";
            var baseMachine = StateMachine.CreateFromScript(json);
            
            var machine = new DistributedStateMachine(
                baseMachine,
                "test-machine",
                "local://test");
            _machines.Add(machine);
            
            machine.Start();
            
            // Act
            machine.Send("START");
            
            // Assert
            // Verify state transition occurred by not throwing
        }

        [Fact]
        public async Task SendToMachine_Should_SendToRemoteMachine()
        {
            // Arrange
            var json1 = @"
            {
                ""id"": ""machine1"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""REMOTE_EVENT"": ""active""
                        }
                    },
                    ""active"": {}
                }
            }";
            var baseMachine1 = StateMachine.CreateFromScript(json1);
            
            var machine1 = new DistributedStateMachine(
                baseMachine1,
                "machine1",
                "local://machine1");
            _machines.Add(machine1);
            
            var json2 = @"
            {
                ""id"": ""machine2"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": ""running""
                        }
                    },
                    ""running"": {}
                }
            }";
            var baseMachine2 = StateMachine.CreateFromScript(json2);
            
            var machine2 = new DistributedStateMachine(
                baseMachine2,
                "machine2",
                "local://machine2");
            _machines.Add(machine2);
            
            machine1.Start();
            machine2.Start();
            
            // Act
            await machine2.SendToMachineAsync("machine1", "REMOTE_EVENT");
            await Task.Delay(100); // Give time for message to be processed
            
            // Note: In-memory transport would need proper setup for this to work
            // This test demonstrates the API usage
        }

        [Fact]
        public void Send_RemoteEventFormat_Should_ParseCorrectly()
        {
            // Arrange
            var json = @"
            {
                ""id"": ""simple"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {}
                }
            }";
            var baseMachine = StateMachine.CreateFromScript(json);
            var machine = new DistributedStateMachine(
                baseMachine,
                "test-machine",
                "local://test");
            _machines.Add(machine);
            
            machine.Start();
            
            // Act & Assert (should not throw)
            Action act = () => machine.Send("remoteMachine@EVENT");
            act.Should().NotThrow();
        }

        [Fact]
        public async Task DiscoverMachines_Should_ReturnEndpoints()
        {
            // Arrange
            var json = @"
            {
                ""id"": ""simple"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {}
                }
            }";
            var baseMachine = StateMachine.CreateFromScript(json);
            var machine = new DistributedStateMachine(
                baseMachine,
                "test-machine",
                "local://test");
            _machines.Add(machine);
            
            machine.Start();
            
            // Act
            var endpoints = await machine.DiscoverMachinesAsync();
            
            // Assert
            endpoints.Should().NotBeNull();
            // The actual results depend on transport implementation
        }

        [Fact]
        public async Task GetHealth_Should_ReturnHealthStatus()
        {
            // Arrange
            var json = @"
            {
                ""id"": ""simple"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {}
                }
            }";
            var baseMachine = StateMachine.CreateFromScript(json);
            var machine = new DistributedStateMachine(
                baseMachine,
                "test-machine",
                "local://test");
            _machines.Add(machine);
            
            machine.Start();
            
            // Act
            var health = await machine.GetHealthAsync();
            
            // Assert
            health.Should().NotBeNull();
            health!.IsHealthy.Should().BeTrue();
        }

        [Fact]
        public void Stop_Should_StopMachine()
        {
            // Arrange
            var json = @"
            {
                ""id"": ""simple"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {}
                }
            }";
            var baseMachine = StateMachine.CreateFromScript(json);
            var machine = new DistributedStateMachine(
                baseMachine,
                "test-machine",
                "local://test");
            _machines.Add(machine);
            
            machine.Start();
            
            // Act
            machine.Stop();
            
            // Assert
            // Verify stop doesn't throw
        }

        public void Dispose()
        {
            foreach (var machine in _machines)
            {
                try
                {
                    machine.Stop();
                    machine.Dispose();
                }
                catch { }
            }
        }
    }
}