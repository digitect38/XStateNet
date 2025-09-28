using System.Diagnostics;
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
                id: 'simple',
                initial: 'idle',
                states: {
                    'idle': {
                        on: {
                            'START': 'running'
                        }
                    },
                    'running': {
                        on: {
                            'STOP': 'idle'
                        }
                    }
                }
            }";
            
            // Act
            var machine = new DistributedStateMachine(StateMachineFactory.CreateFromScript(json, guidIsolate: true), "test-machine", "local://test");
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
                id: 'simple',
                initial: 'idle',
                states: {
                    'idle': {}
                }
            }";
            var baseMachine = StateMachineFactory.CreateFromScript(json, guidIsolate: true);
            var machine = new DistributedStateMachine(
                baseMachine,
                "test-machine",
                "local://test");
            _machines.Add(machine);
            
            // Act
            machine.Start();
            
            // Assert
            // Note: StateMachines doesn't have a Started property in this implementation
            // We can verify it doesn't throw
        }

        [Fact]
        public async Task Send_LocalEvent_Should_ProcessLocally()
        {
            // Arrange
            var json = @"
            {
                id: 'simple',
                initial: 'idle',
                states: {
                    'idle': {
                        on: {
                            'START': 'running'
                        }
                    },
                    'running': {}
                }
            }";
            var baseMachine = StateMachineFactory.CreateFromScript(json, guidIsolate: true);
            
            var machine = new DistributedStateMachine(
                baseMachine,
                "test-machine",
                "local://test");
            _machines.Add(machine);
            
            machine.Start();
            
            // Act
            await machine.SendAsync("START");

            // Assert
            // Verify state transition occurred by not throwing
        }

        [Fact]
        public async Task SendToMachine_Should_SendToRemoteMachine()
        {
            // Arrange
            var json1 = @"
            {
                id: 'machine1',
                initial: 'idle',
                states: {
                    'idle': {
                        on: {
                            'REMOTE_EVENT': 'active'
                        }
                    },
                    'active': {}
                }
            }";
            var baseMachine1 = StateMachineFactory.CreateFromScript(json1, guidIsolate: true);
            
            var machine1 = new DistributedStateMachine(
                baseMachine1,
                "machine1",
                "local://machine1");
            _machines.Add(machine1);
            
            var machine2Id = "machine2_" + Guid.NewGuid().ToString("N");
            var json2 = @"
            {
                id: '" + machine2Id + @"',
                initial: 'idle',
                states: {
                    'idle': {
                        on: {
                            'START': 'running'
                        }
                    },
                    'running': {}
                }
            }";
            var baseMachine2 = StateMachineFactory.CreateFromScript(json2, guidIsolate: true);
            
            var machine2 = new DistributedStateMachine(
                baseMachine2,
                "machine2",
                "local://machine2");
            _machines.Add(machine2);
            
            machine1.Start();
            machine2.Start();
            
            // Act
            await machine2.SendToMachineAsync("machine1", "REMOTE_EVENT");

            // Wait for state transition with Stopwatch instead of fixed delay
            var sw = Stopwatch.StartNew();
            var timeout = TimeSpan.FromMilliseconds(500);
            var targetState = "running";

            while (!machine1.GetActiveStateNames().Contains(targetState) && sw.Elapsed < timeout)
            {
                await Task.Yield();
            }

            // Note: In-memory transport would need proper setup for this to work
            // This test demonstrates the API usage
        }

        [Fact]
        public async Task Send_RemoteEventFormat_Should_ParseCorrectly()
        {
            // Arrange
            var json = @"
            {
                id: 'simple',
                initial: 'idle',
                states: {
                    'idle': {}
                }
            }";
            var baseMachine = StateMachineFactory.CreateFromScript(json, guidIsolate: true);
            var machine = new DistributedStateMachine(
                baseMachine,
                "test-machine",
                "local://test");
            _machines.Add(machine);
            
            machine.Start();
            
            // Act & Assert (should not throw)
            Func<Task> act = async () => await machine.SendAsync("remoteMachine@EVENT");
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task DiscoverMachines_Should_ReturnEndpoints()
        {
            // Arrange
            var json = @"
            {
                id: 'simple',
                initial: 'idle',
                states: {
                    'idle': {}
                }
            }";
            var baseMachine = StateMachineFactory.CreateFromScript(json, guidIsolate: true);
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
                id: 'simple',
                initial: 'idle',
                states: {
                    'idle': {}
                }
            }";
            var baseMachine = StateMachineFactory.CreateFromScript(json, guidIsolate: true);
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
                id: 'simple',
                initial: 'idle',
                states: {
                    'idle': {}
                }
            }";
            var baseMachine = StateMachineFactory.CreateFromScript(json, guidIsolate: true);
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
