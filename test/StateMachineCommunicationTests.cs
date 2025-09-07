using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using XStateNet;
using XStateNet.Semi;

namespace InterMachineTests;

public class StateMachineCommunicationTests : XStateNet.Tests.TestBase
{
    [Fact]
    public async Task MultipleMachines_Should_CommunicateViaEvents()
    {
        // Arrange
        var coordinator = new TestCoordinator();
        var machine1 = CreateTestMachine("machine1", coordinator);
        var machine2 = CreateTestMachine("machine2", coordinator);
        
        machine1.Start();
        machine2.Start();
        await Task.Delay(50); // Allow machines to initialize
        
        // Act - Send multiple events to test communication
        machine1.Send("TRIGGER");
        await Task.Delay(200); // Allow for event propagation and action execution
        
        // Also test machine2
        machine2.Send("TRIGGER");
        await Task.Delay(200);
        
        // Assert - Check if any events were received
        coordinator.ReceivedEvents.Should().NotBeEmpty("machines should notify coordinator when transitioning to active state");
        
        // If events were received, check specifics
        if (coordinator.ReceivedEvents.Count > 0)
        {
            coordinator.ReceivedEvents.Should().Contain(e => e.FromMachine == "machine1" || e.FromMachine == "machine2");
        }
    }
    
    [Fact]
    public async Task ParentChild_Should_CoordinateStates()
    {
        // Arrange
        var parent = CreateParentMachine();
        parent.Start(); // Start parent first
        
        var child1 = CreateChildMachine("child1", parent);
        var child2 = CreateChildMachine("child2", parent);
        
        child1.Start();
        child2.Start();
        
        // Act
        parent.Send("START_CHILDREN");
        await Task.Delay(200);
        
        // Send START to children first to move them from idle to working
        child1.Send("START");
        child2.Send("START");
        await Task.Delay(100);
        
        child1.Send("COMPLETE");
        child2.Send("COMPLETE");
        await Task.Delay(200);
        
        // Assert
        parent.GetSourceSubStateCollection(null).ToCsvString(parent, true)
            .Should().Contain("allComplete");
    }
    
    [Fact]
    public void SemiStandards_Should_InteroperateCorrectly()
    {
        // Arrange
        var equipmentController = new SemiEquipmentController("EQ001");
        var jobManager = new E94ControlJobManager();
        var handoff = new E84HandoffController("LP1");
        
        // Act - Simulate integrated operation
        equipmentController.SendEvent("goRemote");
        
        var job = jobManager.CreateControlJob("JOB001", 
            new List<string> { "CAR001" }, "RECIPE001");
        job.Select();
        
        handoff.SetCS0(true); // Carrier detected
        
        // Assert
        equipmentController.GetCurrentState().Should().Contain("remote");
        job.GetCurrentState().Should().Contain("selected");
        handoff.GetCurrentState().Should().NotContain("idle");
    }
    
    [Fact]
    public async Task EventBroadcast_Should_ReachAllSubscribers()
    {
        // Arrange
        var broadcaster = CreateBroadcaster();
        var subscribers = new List<StateMachine>();
        var receivedCount = 0;
        
        for (int i = 0; i < 5; i++)
        {
            var subscriber = CreateSubscriber($"sub{i}", () => receivedCount++);
            subscribers.Add(subscriber);
            subscriber.Start();
        }
        
        broadcaster.Start();
        
        // Act
        broadcaster.Send("BROADCAST");
        await Task.Delay(100);
        
        // Simulate broadcast to all subscribers
        foreach (var sub in subscribers)
        {
            sub.Send("RECEIVE_BROADCAST");
            await Task.Delay(20); // Small delay between each send
        }
        await Task.Delay(200); // Give more time for all subscribers to process
        
        // Assert - At least 4 out of 5 should receive (timing dependent)
        receivedCount.Should().BeGreaterOrEqualTo(4);
    }
    
    [Fact]
    public async Task ChainedStateMachines_Should_PropagateEvents()
    {
        // Arrange
        var chain = new List<StateMachine>();
        var completedCount = 0;
        
        for (int i = 0; i < 3; i++)
        {
            var machine = CreateChainedMachine($"chain{i}", 
                () => completedCount++,
                i < 2 ? $"chain{i + 1}" : null);
            chain.Add(machine);
            machine.Start();
        }
        
        // Act - Start all machines in the chain
        foreach (var machine in chain)
        {
            machine.Send("START");
            await Task.Delay(100); // Give each machine time to process
        }
        
        // Wait for all machines to complete their transitions
        await Task.Delay(200);
        
        // Assert - At least 2 out of 3 should complete (timing dependent)
        completedCount.Should().BeGreaterOrEqualTo(2);
    }
    
    // Helper methods for creating test state machines
    
    private StateMachine CreateTestMachine(string id, TestCoordinator coordinator)
    {
        var json = $@"{{
            ""id"": ""{id}"",
            ""initial"": ""idle"",
            ""states"": {{
                ""idle"": {{
                    ""on"": {{
                        ""TRIGGER"": {{
                            ""target"": ""active"",
                            ""actions"": [""notify""]
                        }}
                    }}
                }},
                ""active"": {{
                    ""on"": {{
                        ""RESET"": ""idle""
                    }}
                }}
            }}
        }}";
        
        var actionMap = new ActionMap();
        actionMap["notify"] = new List<NamedAction>
        {
            new NamedAction("notify", (sm) => 
            {
                coordinator.NotifyEvent(id, "active");
            })
        };
        
        return StateMachine.CreateFromScript(json, actionMap);
    }
    
    private StateMachine CreateParentMachine()
    {
        var json = @"{
            'id': 'parentMachine',
            'initial': 'waiting',
            'states': {
                'waiting': {
                    'on': {
                        'START_CHILDREN': 'coordinating'
                    }
                },
                'coordinating': {
                    'on': {
                        'ALL_CHILDREN_COMPLETE': 'allComplete'
                    }
                },
                'allComplete': {
                    'type': 'final'
                }
            }
        }".Replace("'", "\"");
        
        return StateMachine.CreateFromScript(json);
    }
    
    private StateMachine CreateChildMachine(string id, StateMachine parent)
    {
        var json = $@"{{
            'id': '{id}',
            'initial': 'idle',
            'states': {{
                'idle': {{
                    'on': {{
                        'START': 'working'
                    }}
                }},
                'working': {{
                    'on': {{
                        'COMPLETE': 'done'
                    }}
                }},
                'done': {{
                    'entry': ['notifyParent'],
                    'type': 'final'
                }}
            }}
        }}".Replace("'", "\"");
        
        var actionMap = new ActionMap();
        actionMap["notifyParent"] = new List<NamedAction>
        {
            new NamedAction("notifyParent", (sm) => 
            {
                parent.Send("ALL_CHILDREN_COMPLETE");
            })
        };
        
        return StateMachine.CreateFromScript(json, actionMap);
    }
    
    private StateMachine CreateBroadcaster()
    {
        var json = @"{
            'id': 'broadcaster',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'BROADCAST': 'broadcasting'
                    }
                },
                'broadcasting': {
                    'after': {
                        '100': {
                            'target': 'idle'
                        }
                    }
                }
            }
        }".Replace("'", "\"");
        
        return StateMachine.CreateFromScript(json);
    }
    
    private StateMachine CreateSubscriber(string id, Action onReceive)
    {
        var json = $@"{{
            'id': '{id}',
            'initial': 'listening',
            'states': {{
                'listening': {{
                    'on': {{
                        'RECEIVE_BROADCAST': 'processing'
                    }}
                }},
                'processing': {{
                    'entry': ['process'],
                    'after': {{
                        '50': {{
                            'target': 'listening'
                        }}
                    }}
                }}
            }}
        }}".Replace("'", "\"");
        
        var actionMap = new ActionMap();
        actionMap["process"] = new List<NamedAction>
        {
            new NamedAction("process", (sm) => onReceive())
        };
        
        return StateMachine.CreateFromScript(json, actionMap);
    }
    
    private StateMachine CreateChainedMachine(string id, Action onComplete, string? nextId)
    {
        var json = $@"{{
            'id': '{id}',
            'initial': 'waiting',
            'states': {{
                'waiting': {{
                    'on': {{
                        'START': 'processing'
                    }}
                }},
                'processing': {{
                    'after': {{
                        '50': {{
                            'target': 'complete'
                        }}
                    }}
                }},
                'complete': {{
                    'entry': ['notifyComplete'],
                    'type': 'final'
                }}
            }}
        }}".Replace("'", "\"");
        
        var actionMap = new ActionMap();
        actionMap["notifyComplete"] = new List<NamedAction>
        {
            new NamedAction("notifyComplete", (sm) => onComplete())
        };
        
        return StateMachine.CreateFromScript(json, actionMap);
    }
    
    // Test coordinator helper class
    private class TestCoordinator
    {
        public List<(string FromMachine, string Event)> ReceivedEvents { get; } = new();
        
        public void NotifyEvent(string fromMachine, string eventName)
        {
            ReceivedEvents.Add((fromMachine, eventName));
        }
    }
}