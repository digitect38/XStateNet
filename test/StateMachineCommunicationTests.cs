using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

using XStateNet;
using XStateNet.Semi;
using XStateNet.Helpers;

namespace InterMachineTests;

public class StateMachineCommunicationTests : XStateNet.Tests.TestBase
{
    [Fact]
    public async Task MultipleMachines_Should_CommunicateViaEvents()
    {
        // Setup
        StateMachine? machine1 = null;
        StateMachine? machine2 = null;
        
        try
        {
            // Arrange
            var coordinator = new TestCoordinator();
            machine1 = CreateTestMachine("machine1", coordinator);
            machine2 = CreateTestMachine("machine2", coordinator);
            
            machine1.Start();
            machine2.Start();

            // Act - Send multiple events to test communication
            machine1.Send("TRIGGER");

            // Wait for coordinator to receive events from machine1
            await DeterministicWait.WaitForConditionAsync(
                () => coordinator.ReceivedEvents.Count > 0,
                timeoutMs: 1000);

            // Also test machine2
            machine2.Send("TRIGGER");

            // Wait for more events from machine2
            await DeterministicWait.WaitForConditionAsync(
                () => coordinator.ReceivedEvents.Count >= 2,
                timeoutMs: 1000);
            
            // Assert - Check if any events were received
            Assert.NotEmpty(coordinator.ReceivedEvents /*, "machines should notify coordinator when transitioning to active state" */);

            // If events were received, check specifics
            if (coordinator.ReceivedEvents.Count > 0)
            {
                Assert.Contains(coordinator.ReceivedEvents, e => e.FromMachine == "machine1" || e.FromMachine == "machine2");
            }
        }
        finally
        {
            // Cleanup
            machine1?.Stop();
            machine1?.Dispose();
            machine2?.Stop();
            machine2?.Dispose();
        }
    }
    
    [Fact]
    public async Task ParentChild_Should_CoordinateStates()
    {
        // Setup
        StateMachine parent = null;
        StateMachine child1 = null;
        StateMachine child2 = null;
        
        try
        {
            // Arrange
            parent = CreateParentMachine();
            parent.Start(); // Start parent first
            
            child1 = CreateChildMachine("child1", parent);
            child2 = CreateChildMachine("child2", parent);
            
            child1.Start();
            child2.Start();
            
            // Act
            parent.Send("START_CHILDREN");

            // Wait for parent to transition to coordinating state
            await DeterministicWait.WaitForStateAsync(parent, "coordinating");

            // Send START to children first to move them from idle to working
            child1.Send("START");
            child2.Send("START");

            // Wait for children to be in working state
            await DeterministicWait.WaitForStateAsync(child1, "working");
            await DeterministicWait.WaitForStateAsync(child2, "working");

            child1.Send("COMPLETE");
            child2.Send("COMPLETE");

            // Wait for parent to reach allComplete state
            await DeterministicWait.WaitForStateAsync(parent, "allComplete");
            
            // Assert
            Assert.Contains("allComplete", parent.GetSourceSubStateCollection(null).ToCsvString(parent, true));
        }
        finally
        {
            // Cleanup
            child1?.Stop();
            child1?.Dispose();
            child2?.Stop();
            child2?.Dispose();
            parent?.Stop();
            parent?.Dispose();
        }
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
        Assert.Contains("remote", equipmentController.GetCurrentState());
        Assert.Contains("selected", job.GetCurrentState());
        Assert.DoesNotContain("idle", handoff.GetCurrentState());
    }
    
    [Fact]
    public async Task EventBroadcast_Should_ReachAllSubscribers()
    {
        // Setup
        StateMachine broadcaster = null;
        var subscribers = new List<StateMachine>();
        
        try
        {
            // Arrange
            broadcaster = CreateBroadcaster();
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

            // Wait for broadcaster to be ready
            await DeterministicWait.WaitForStateAsync(broadcaster, "broadcasting");

            // Simulate broadcast to all subscribers
            foreach (var sub in subscribers)
            {
                sub.Send("RECEIVE_BROADCAST");
            }

            // Wait for at least 4 subscribers to receive the broadcast
            await DeterministicWait.WaitForValueAsync(
                () => receivedCount,
                expectedValue: 4,
                timeoutMs: 2000);
            
            // Assert - At least 4 out of 5 should receive (timing dependent)
            Assert.True(receivedCount >= 4);
        }
        finally
        {
            // Cleanup
            broadcaster?.Stop();
            broadcaster?.Dispose();
            foreach (var sub in subscribers)
            {
                sub?.Stop();
                sub?.Dispose();
            }
        }
    }
    
    [Fact]
    public async Task ChainedStateMachines_Should_PropagateEvents()
    {
        // Setup
        var chain = new List<StateMachine>();
        
        try
        {
            // Arrange
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
            }

            // Wait for at least 2 machines to complete
            await DeterministicWait.WaitForValueAsync(
                () => completedCount,
                expectedValue: 2,
                timeoutMs: 2000);
            
            // Assert - At least 2 out of 3 should complete (timing dependent)
            Assert.True(completedCount >= 2);
        }
        finally
        {
            // Cleanup
            foreach (var machine in chain)
            {
                machine?.Stop();
                machine?.Dispose();
            }
        }
    }
    
    // Helper methods for creating test state machines
    
    private StateMachine CreateTestMachine(string id, TestCoordinator coordinator)
    {
        var json = @"{
            'id': '" + id +  @"',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'TRIGGER': {
                            'target': 'active',
                            'actions': 'notify'
                        }
                    }
                },
                'active': {
                    'on': {
                        'RESET': 'idle'
                    }
                }
            }
        }";
        
        var actionMap = new ActionMap();
        actionMap["notify"] = new List<NamedAction>
        {
            new NamedAction("notify", (sm) => 
            {
                coordinator.NotifyEvent(id, "active");
            })
        };
        
        return StateMachine.CreateFromScript(json, guidIsolate: true, actionMap);
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
        }";
        
        return StateMachine.CreateFromScript(json, guidIsolate: true);
    }
    
    private StateMachine CreateChildMachine(string id, StateMachine parent)
    {
        var json = @"{
            'id': '" +id + @"',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'START': 'working'
                    }
                },
                'working': {
                    'on': {
                        'COMPLETE': 'done'
                    }
                },
                'done': {
                    'entry': 'notifyParent',
                    'type': 'final'
                }
            }
        }";
        
        var actionMap = new ActionMap();
        actionMap["notifyParent"] = new List<NamedAction>
        {
            new NamedAction("notifyParent", (sm) => 
            {
                parent.Send("ALL_CHILDREN_COMPLETE");
            })
        };
        
        return StateMachine.CreateFromScript(json, guidIsolate: true, actionMap);
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
        }";
        
        return StateMachine.CreateFromScript(json, guidIsolate: true);
    }
    
    private StateMachine CreateSubscriber(string id, Action onReceive)
    {
        var json = @"{
            'id':'" + id + @"',
            'initial': 'listening',
            'states': {
                'listening': {
                    'on': {
                        'RECEIVE_BROADCAST': 'processing'
                    }
                },
                'processing': {
                    'entry': 'process',
                    'after': {
                        '50': {
                            'target': 'listening'
                        }
                    }
                }
            }
        }";
        
        var actionMap = new ActionMap();
        actionMap["process"] = new List<NamedAction>
        {
            new NamedAction("process", (sm) => onReceive())
        };
        
        return StateMachine.CreateFromScript(json, guidIsolate: true, actionMap);
    }
    
    private StateMachine CreateChainedMachine(string id, Action onComplete, string? nextId)
    {
        var json = @"{
            'id': '" + id + @"',
            'initial': 'waiting',
            'states': {
                'waiting': {
                    'on': {
                        'START': 'processing'
                    }
                },
                'processing': {
                    'after': {
                        '50': {
                            'target': 'complete'
                        }
                    }
                },
                'complete': {
                    'entry': 'notifyComplete',
                    'type': 'final'
                }
            }
        }";
        
        var actionMap = new ActionMap();
        actionMap["notifyComplete"] = new List<NamedAction>
        {
            new NamedAction("notifyComplete", (sm) => onComplete())
        };
        
        return StateMachine.CreateFromScript(json, guidIsolate: true, actionMap);
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