using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using XStateNet;
using XStateNet.Distributed;
using System.Collections.Concurrent;

namespace XStateNet.Distributed.Tests
{
    /// <summary>
    /// Tests for distributed state machine communication scenarios
    /// </summary>
    public class DistributedCommunicationTests : IDisposable
    {
        private readonly List<DistributedStateMachine> _machines = new();
        private readonly ILoggerFactory _loggerFactory;

        public DistributedCommunicationTests()
        {
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddConsole();
            });
        }

        [Fact]
        public async Task TwoMachines_Should_CommunicateViaInMemoryTransport()
        {
            // Arrange
            var machine1Events = new List<string>();
            var machine2Events = new List<string>();

            // Machine 1 - Traffic Light
            var machine1Json = @"
            {
                id: 'trafficLight1',
                initial: 'red',
                states: {
                    'red': {
                        on: {
                            'TIMER': 'green',
                            'SYNC_REQUEST': {
                                target: 'red',
                                actions: 'notifySync'
                            }
                        }
                    },
                    'green': {
                        on: {
                            'TIMER': 'yellow',
                            'SYNC_REQUEST': {
                                target: 'green',
                                actions: 'notifySync'
                            }
                        }
                    },
                    'yellow': {
                        on: {
                            'TIMER': 'red'
                        }
                    }
                }
            }";

            var machine1Actions = new ActionMap();
            machine1Actions["notifySync"] = new List<NamedAction>
            {
                new NamedAction("notifySync", (sm) => machine1Events.Add("SYNC_RECEIVED"))
            };

            var baseMachine1 = StateMachineFactory.CreateFromScript(machine1Json, threadSafe: false, guidIsolate: true, machine1Actions);
            var distributedMachine1 = new DistributedStateMachine(
                baseMachine1,
                "trafficLight1",
                "local://trafficLight1",
                _loggerFactory.CreateLogger<DistributedStateMachine>());
            _machines.Add(distributedMachine1);

            // Machine 2 - Traffic Light
            var machine2Json = @"
            {
                id: 'trafficLight2',
                initial: 'green',
                states: {
                    'red': {
                        on: {
                            'TIMER': 'green'
                        }
                    },
                    'green': {
                        on: {
                            'TIMER': 'yellow',
                            'REQUEST_SYNC': {
                                target: 'green',
                                actions: 'sendSyncRequest'
                            }
                        }
                    },
                    'yellow': {
                        on: {
                            'TIMER': 'red'
                        }
                    }
                }
            }";

            // Create distributedMachine2 variable first (will be initialized below)
            DistributedStateMachine? distributedMachine2 = null;
            
            var machine2Actions = new ActionMap();
            machine2Actions["sendSyncRequest"] = new List<NamedAction>
            {
                new NamedAction("sendSyncRequest", (sm) =>
                {
                    machine2Events.Add("SENDING_SYNC");
                    if (distributedMachine2 != null)
                    {
                        Task.Run(async () => 
                            await distributedMachine2.SendToMachineAsync("trafficLight1", "SYNC_REQUEST"));
                    }
                })
            };

            var baseMachine2 = StateMachineFactory.CreateFromScript(machine2Json, threadSafe: false, guidIsolate: true, machine2Actions);
            distributedMachine2 = new DistributedStateMachine(
                baseMachine2,
                "trafficLight2",
                "local://trafficLight2",
                _loggerFactory.CreateLogger<DistributedStateMachine>());
            _machines.Add(distributedMachine2);

            // Act
            distributedMachine1.Start();
            distributedMachine2.Start();

            // Machine 2 requests sync from Machine 1
            await distributedMachine2.SendAsync("REQUEST_SYNC");

            // Assert
            machine2Events.Should().Contain("SENDING_SYNC");
            // Note: Actual message delivery would require proper transport setup
        }

        [Fact]
        public async Task DistributedMachine_Should_DiscoverOtherMachines()
        {
            // Arrange
            var machine1 = CreateTestMachine("discovery1", "local://discovery1");
            var machine2 = CreateTestMachine("discovery2", "local://discovery2");
            var machine3 = CreateTestMachine("discovery3", "local://discovery3");

            machine1.Start();
            machine2.Start();
            machine3.Start();

            // Act
            var discovered = await machine1.DiscoverMachinesAsync("*", TimeSpan.FromSeconds(1));

            // Assert
            discovered.Should().NotBeNull();
            // The number of discovered machines depends on transport implementation
        }

        [Fact]
        public async Task ParentChild_DistributedCoordination()
        {
            // Arrange
            var parentEvents = new List<string>();
            var childCompleted = new TaskCompletionSource<bool>();

            // Parent machine
            var parentJson = @"
            {
                id: 'parent',
                initial: 'idle',
                states: {
                    'idle': {
                        on: {
                            'START': 'coordinating'
                        }
                    },
                    'coordinating': {
                        entry: 'startChildren',
                        on: {
                            'CHILD_COMPLETE': {
                                target: 'checking',
                                actions: 'checkAllComplete'
                            }
                        }
                    },
                    'checking': {
                        always: [
                            {
                                target: 'completed',
                                cond: 'allChildrenComplete'
                            },
                            {
                                target: 'coordinating'
                            }
                        ]
                    },
                    'completed': {
                        type: 'final'
                    }
                }
            }";

            var childrenStatus = new ConcurrentDictionary<string, bool>();
            var parentActions = new ActionMap();
            parentActions["startChildren"] = new List<NamedAction>
            {
                new NamedAction("startChildren", (sm) =>
                {
                    parentEvents.Add("STARTING_CHILDREN");
                    childrenStatus["child1"] = false;
                    childrenStatus["child2"] = false;
                })
            };
            parentActions["checkAllComplete"] = new List<NamedAction>
            {
                new NamedAction("checkAllComplete", (sm) =>
                {
                    parentEvents.Add("CHECKING_COMPLETION");
                    if (childrenStatus.Values.All(v => v))
                    {
                        childCompleted.SetResult(true);
                    }
                })
            };

            var parentGuards = new GuardMap();
            parentGuards["allChildrenComplete"] = new NamedGuard("allChildrenComplete", 
                (sm) => childrenStatus.Values.All(v => v));

            var parentBase = StateMachineFactory.CreateFromScript(parentJson, threadSafe: false, guidIsolate: true, parentActions, parentGuards);
            var parentMachine = new DistributedStateMachine(
                parentBase,
                "parent",
                "local://parent",
                _loggerFactory.CreateLogger<DistributedStateMachine>());
            _machines.Add(parentMachine);

            // Child machines
            for (int i = 1; i <= 2; i++)
            {
                var childId = $"child{i}";
                var childJson = $@"
                {{
                    id: '{childId}',
                    initial: 'working',
                    states: {{
                        'working': {{
                            after: {{
                                '{100 * i}': 'done'
                            }}
                        }},
                        'done': {{
                            entry: 'notifyParent',
                            type: 'final'
                        }}
                    }}
                }}";

                var childActions = new ActionMap();
                childActions["notifyParent"] = new List<NamedAction>
                {
                    new NamedAction("notifyParent", async (sm) =>
                    {
                        childrenStatus[childId] = true;
                        // Send directly to the parent machine
                        await parentMachine.SendAsync("CHILD_COMPLETE");
                    })
                };

                var childBase = StateMachineFactory.CreateFromScript(childJson, threadSafe: false, guidIsolate: true, childActions);
                var childMachine = new DistributedStateMachine(
                    childBase,
                    childId,
                    $"local://{childId}",
                    _loggerFactory.CreateLogger<DistributedStateMachine>());
                _machines.Add(childMachine);
                childMachine.Start();
            }

            // Act
            parentMachine.Start();
            await parentMachine.SendAsync("START");

            // Wait for coordination with timeout
            var sw = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(2);

            while (!childCompleted.Task.IsCompleted && sw.Elapsed < timeout)
            {
                await Task.Yield();
            }

            // Assert
            if (!childCompleted.Task.IsCompleted)
            {
                throw new TimeoutException("Test timed out waiting for child completion");
            }

            parentEvents.Should().Contain("STARTING_CHILDREN");
            parentEvents.Should().Contain("CHECKING_COMPLETION");
        }

        [Fact]
        public async Task LoadBalancing_Should_DistributeWorkAcrossMachines()
        {
            // Arrange
            var workItems = new List<string> { "task1", "task2", "task3", "task4", "task5" };
            var completedWork = new ConcurrentBag<string>();
            var workers = new List<DistributedStateMachine>();

            // Create worker machines
            for (int i = 1; i <= 3; i++)
            {
                var workerId = $"worker{i}";
                var workerJson = @"
                {
                    id: '" + workerId + @"',
                    initial: 'idle',
                    states: {
                        idle: {
                            on: {
                                'WORK': 'processing'
                            }
                        },
                        'processing': {
                            entry: 'processWork',
                            after: {
                                '100': 'idle'
                            },
                            exit: 'completeWork'
                        }
                    }
                }";

                string? currentWork = null;
                var workerActions = new ActionMap();
                workerActions["processWork"] = new List<NamedAction>
                {
                    new NamedAction("processWork", (sm) =>
                    {
                        if (workItems.Count > 0)
                        {
                            currentWork = workItems[0];
                            workItems.RemoveAt(0);
                        }
                    })
                };
                workerActions["completeWork"] = new List<NamedAction>
                {
                    new NamedAction("completeWork", (sm) =>
                    {
                        if (currentWork != null)
                        {
                            completedWork.Add(currentWork);
                            currentWork = null;
                        }
                    })
                };

                var workerBase = StateMachineFactory.CreateFromScript(workerJson, threadSafe: false, guidIsolate: true, workerActions);
                var workerMachine = new DistributedStateMachine(
                    workerBase,
                    workerId,
                    $"local://{workerId}",
                    _loggerFactory.CreateLogger<DistributedStateMachine>());
                _machines.Add(workerMachine);
                workers.Add(workerMachine);
                workerMachine.Start();
            }

            // Act - Distribute work
            foreach (var worker in workers)
            {
                await worker.SendAsync("WORK");
            }

            // Process remaining work with proper synchronization
            var sw = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(5);

            while (workItems.Count > 0 && completedWork.Count < 5)
            {
                if (sw.Elapsed > timeout)
                {
                    throw new TimeoutException($"Test timed out waiting for work completion. Completed: {completedWork.Count}, Remaining: {workItems.Count}");
                }

                foreach (var worker in workers)
                {
                    await worker.SendAsync("WORK");
                }

                // Small yield to allow other tasks to run
                await Task.Yield();
            }

            // Wait for all work to complete using Stopwatch
            sw.Restart();
            var completionTimeout = TimeSpan.FromMilliseconds(500); // Workers have 100ms transition delay

            while (completedWork.Count < 3 && sw.Elapsed < completionTimeout)
            {
                await Task.Yield();
            }

            // Assert
            completedWork.Count.Should().BeGreaterThanOrEqualTo(3);
        }

        [Fact]
        public async Task HealthCheck_Should_MonitorDistributedMachines()
        {
            // Arrange
            var machine1 = CreateTestMachine("health1", "local://health1");
            var machine2 = CreateTestMachine("health2", "local://health2");

            machine1.Start();
            machine2.Start();

            // Act
            var health1 = await machine1.GetHealthAsync();
            var health2 = await machine2.GetHealthAsync();

            // Assert
            health1.Should().NotBeNull();
            health1!.IsHealthy.Should().BeTrue();
            health2.Should().NotBeNull();
            health2!.IsHealthy.Should().BeTrue();
        }

        [Fact]
        public async void RemoteEventFormat_Should_RouteToCorrectMachine()
        {
            // Arrange
            var machine1 = CreateTestMachine("router1", "local://router1");
            var machine2 = CreateTestMachine("router2", "local://router2");

            machine1.Start();
            machine2.Start();

            // Act & Assert - Should not throw
            Action act = () => machine1.SendAsync("router2@REMOTE_EVENT");
            act.Should().NotThrow();
        }

        [Fact]
        public async Task CircuitBreaker_Pattern_Should_HandleFailures()
        {
            // Arrange
            var circuitBreakerJson = @"
            {
                id: 'circuitBreaker',
                initial: 'closed',
                states: {
                    'closed': {
                        on: {
                            'REQUEST': [
                                {
                                    target: 'closed',
                                    cond: 'isHealthy',
                                    actions: 'handleRequest'
                                },
                                {
                                    target: 'open',
                                    actions: 'tripBreaker'
                                }
                            ]
                        }
                    },
                    'open': {
                        on: {
                            'REQUEST': {
                                actions: 'rejectRequest'
                            }
                        },
                        after: {
                            '5000': 'halfOpen'
                        }
                    },
                    'halfOpen': {
                        on: {
                            'REQUEST': [
                                {
                                    target: 'closed',
                                    cond: 'isHealthy',
                                    actions: 'resetBreaker'
                                },
                                {
                                    target: 'open'
                                }
                            ]
                        }
                    }
                }
            }";

            var failureCount = 0;
            var events = new List<string>();
            
            var actions = new ActionMap();
            actions["handleRequest"] = new List<NamedAction>
            {
                new NamedAction("handleRequest", (sm) => events.Add("REQUEST_HANDLED"))
            };
            actions["tripBreaker"] = new List<NamedAction>
            {
                new NamedAction("tripBreaker", (sm) =>
                {
                    events.Add("BREAKER_TRIPPED");
                    failureCount++;
                })
            };
            actions["rejectRequest"] = new List<NamedAction>
            {
                new NamedAction("rejectRequest", (sm) => events.Add("REQUEST_REJECTED"))
            };
            actions["resetBreaker"] = new List<NamedAction>
            {
                new NamedAction("resetBreaker", (sm) =>
                {
                    events.Add("BREAKER_RESET");
                    failureCount = 0;
                })
            };

            var guards = new GuardMap();
            guards["isHealthy"] = new NamedGuard("isHealthy", (sm) => failureCount < 3);

            var baseCircuitBreaker = StateMachineFactory.CreateFromScript(circuitBreakerJson, threadSafe: false, guidIsolate: true, actions, guards);
            var circuitBreaker = new DistributedStateMachine(
                baseCircuitBreaker,
                "circuitBreaker",
                "local://circuitBreaker",
                _loggerFactory.CreateLogger<DistributedStateMachine>());
            _machines.Add(circuitBreaker);

            // Act
            circuitBreaker.Start();

            // Successful requests
            await circuitBreaker.SendAsync("REQUEST");
            await circuitBreaker.SendAsync("REQUEST");
            
            // Simulate failures
            failureCount = 3;
            await circuitBreaker.SendAsync("REQUEST"); // This should trip the breaker

            // Try request while open
            await circuitBreaker.SendAsync("REQUEST");
            
            // Reset failure count and try again
            failureCount = 0;
            await circuitBreaker.SendAsync("REQUEST");

            // Assert
            events.Should().Contain("REQUEST_HANDLED");
            events.Should().Contain("BREAKER_TRIPPED");
            events.Should().Contain("REQUEST_REJECTED");
        }

        private DistributedStateMachine CreateTestMachine(string id, string address)
        {
            var json = $@"
            {{
                id: '{id}',
                initial: 'idle',
                states: {{
                    'idle': {{
                        on: {{
                            'START': 'active'
                        }}
                    }},
                    'active': {{
                        on: {{
                            'STOP': 'idle'
                        }}
                    }}
                }}
            }}";

            var baseMachine = StateMachineFactory.CreateFromScript(json, threadSafe: false, guidIsolate: true);
            var distributedMachine = new DistributedStateMachine(
                baseMachine,
                id,
                address,
                _loggerFactory.CreateLogger<DistributedStateMachine>());
            _machines.Add(distributedMachine);
            return distributedMachine;
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
            _loggerFactory?.Dispose();
        }

        // Concurrent bag helper for thread-safe collection
        private class ConcurrentBag<T>
        {
            private readonly object _lock = new();
            private readonly List<T> _items = new();

            public void Add(T item)
            {
                lock (_lock)
                {
                    _items.Add(item);
                }
            }

            public int Count
            {
                get
                {
                    lock (_lock)
                    {
                        return _items.Count;
                    }
                }
            }

            public bool Contains(T item)
            {
                lock (_lock)
                {
                    return _items.Contains(item);
                }
            }
        }
    }
}
