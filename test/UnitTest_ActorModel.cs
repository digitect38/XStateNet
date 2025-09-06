using NUnit.Framework;
using System;
using System.Threading.Tasks;
using XStateNet;

namespace ActorModelTests;

[TestFixture]
public class UnitTest_ActorModel
{
    private ActionMap _actions;
    private GuardMap _guards;
    
    [SetUp]
    public void Setup()
    {
        // Clear actor system for each test
        ActorSystem.Instance.StopAllActors().GetAwaiter().GetResult();
        
        _actions = new ActionMap
        {
            ["log"] = new List<NamedAction> { new NamedAction("log", (sm) => Console.WriteLine($"Action in {sm.machineId}")) }
        };
        
        _guards = new GuardMap();
    }
    
    [TearDown]
    public void TearDown()
    {
        ActorSystem.Instance.StopAllActors().GetAwaiter().GetResult();
    }
    
    [Test]
    public async Task TestBasicActorCreation()
    {
        const string script = @"
        {
            'id': 'testActor',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'WORK': 'working'
                    }
                },
                'working': {
                    'on': {
                        'DONE': 'idle'
                    }
                }
            }
        }";
        
        var stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        var actor = ActorSystem.Instance.Spawn("actor1", id => new StateMachineActor(id, stateMachine));
        
        Assert.That(actor.Id, Is.EqualTo("actor1"));
        Assert.That(actor.Status, Is.EqualTo(ActorStatus.Idle));
        
        await actor.StartAsync();
        Assert.That(actor.Status, Is.EqualTo(ActorStatus.Running));
        
        await actor.SendAsync("WORK");
        await Task.Delay(50); // Allow message processing
        
        Assert.That(actor.Machine.GetActiveStateString(), Does.Contain("working"));
        
        await actor.StopAsync();
        Assert.That(actor.Status, Is.EqualTo(ActorStatus.Stopped));
    }
    
    [Test]
    public async Task TestActorCommunication()
    {
        const string pingScript = @"
        {
            'id': 'ping',
            'initial': 'waiting',
            'states': {
                'waiting': {
                    'on': {
                        'START': 'pinging'
                    }
                },
                'pinging': {
                    'entry': ['sendPong'],
                    'on': {
                        'PONG_RECEIVED': 'waiting'
                    }
                }
            }
        }";
        
        const string pongScript = @"
        {
            'id': 'pong',
            'initial': 'waiting',
            'states': {
                'waiting': {
                    'on': {
                        'PING': 'ponging'
                    }
                },
                'ponging': {
                    'entry': ['sendPing'],
                    'on': {
                        'DONE': 'waiting'
                    }
                }
            }
        }";
        
        var pingReceived = false;
        var pongReceived = false;
        
        var pingActions = new ActionMap
        {
            ["sendPong"] = new List<NamedAction> { new NamedAction("sendPong", async (sm) => {
                await ActorSystem.Instance.SendToActor("pong", "PING");
            }) }
        };
        
        var pongActions = new ActionMap
        {
            ["sendPing"] = new List<NamedAction> { new NamedAction("sendPing", async (sm) => {
                pingReceived = true;
                await ActorSystem.Instance.SendToActor("ping", "PONG_RECEIVED");
                pongReceived = true;
            }) }
        };
        
        var pingMachine = StateMachine.CreateFromScript(pingScript, pingActions, _guards);
        var pongMachine = StateMachine.CreateFromScript(pongScript, pongActions, _guards);
        
        var pingActor = ActorSystem.Instance.Spawn("ping", id => new StateMachineActor(id, pingMachine));
        var pongActor = ActorSystem.Instance.Spawn("pong", id => new StateMachineActor(id, pongMachine));
        
        await pingActor.StartAsync();
        await pongActor.StartAsync();
        
        await pingActor.SendAsync("START");
        await Task.Delay(100); // Allow message processing
        
        Assert.That(pingReceived, Is.True);
        Assert.That(pongReceived, Is.True);
        Assert.That(pingActor.Machine.GetActiveStateString(), Does.Contain("waiting"));
        Assert.That(pongActor.Machine.GetActiveStateString(), Does.Contain("ponging"));
    }
    
    [Test]
    public async Task TestChildActorSpawning()
    {
        const string parentScript = @"
        {
            'id': 'parent',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'SPAWN_CHILD': 'managing'
                    }
                },
                'managing': {
                    'type': 'final'
                }
            }
        }";
        
        const string childScript = @"
        {
            'id': 'child',
            'initial': 'active',
            'states': {
                'active': {
                    'on': {
                        'STOP': 'stopped'
                    }
                },
                'stopped': {
                    'type': 'final'
                }
            }
        }";
        
        var parentMachine = StateMachine.CreateFromScript(parentScript, _actions, _guards);
        var childMachine = StateMachine.CreateFromScript(childScript, _actions, _guards);
        
        var parentActor = ActorSystem.Instance.Spawn("parent", id => new StateMachineActor(id, parentMachine));
        await parentActor.StartAsync();
        
        // Spawn child actor
        var childActor = parentActor.SpawnChild("child1", childMachine);
        await childActor.StartAsync();
        
        Assert.That(childActor.Id, Is.EqualTo("parent.child1"));
        Assert.That(childActor.Status, Is.EqualTo(ActorStatus.Running));
        Assert.That(childActor.Machine.GetActiveStateString(), Does.Contain("active"));
        
        await childActor.SendAsync("STOP");
        await Task.Delay(50);
        
        Assert.That(childActor.Machine.GetActiveStateString(), Does.Contain("stopped"));
    }
    
    [Test]
    public async Task TestActorErrorHandling()
    {
        const string script = @"
        {
            'id': 'errorActor',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'CAUSE_ERROR': 'error'
                    }
                },
                'error': {
                    'entry': ['throwError']
                }
            }
        }";
        
        var errorActions = new ActionMap
        {
            ["throwError"] = new List<NamedAction> { new NamedAction("throwError", (sm) => {
                throw new InvalidOperationException("Test error");
            }) }
        };
        
        var stateMachine = StateMachine.CreateFromScript(script, errorActions, _guards);
        var actor = ActorSystem.Instance.Spawn("errorActor", id => new StateMachineActor(id, stateMachine));
        
        await actor.StartAsync();
        await actor.SendAsync("CAUSE_ERROR");
        await Task.Delay(50);
        
        // Actor should handle the error and set status appropriately
        Assert.That(actor.Status, Is.EqualTo(ActorStatus.Error));
    }
    
    [Test]
    public async Task TestActorMessageQueuing()
    {
        var messageCount = 0;
        
        var countingActions = new ActionMap
        {
            ["count"] = new List<NamedAction> { new NamedAction("count", (sm) => {
                messageCount++;
            }) }
        };
        
        const string script = @"
        {
            'id': 'counter',
            'initial': 'counting',
            'states': {
                'counting': {
                    'on': {
                        'INCREMENT': {
                            'target': 'counting',
                            'actions': ['count']
                        }
                    }
                }
            }
        }";
        
        var stateMachine = StateMachine.CreateFromScript(script, countingActions, _guards);
        var actor = ActorSystem.Instance.Spawn("counter", id => new StateMachineActor(id, stateMachine));
        
        await actor.StartAsync();
        
        // Send multiple messages rapidly
        for (int i = 0; i < 10; i++)
        {
            await actor.SendAsync("INCREMENT");
        }
        
        await Task.Delay(100); // Allow all messages to be processed
        
        Assert.That(messageCount, Is.EqualTo(10));
    }
    
    [Test]
    public async Task TestActorDataPassing()
    {
        object? receivedData = null;
        
        var dataActions = new ActionMap
        {
            ["storeData"] = new List<NamedAction> { new NamedAction("storeData", (sm) => {
                receivedData = sm.ContextMap?["_eventData"];
            }) }
        };
        
        const string script = @"
        {
            'id': 'dataReceiver',
            'initial': 'waiting',
            'states': {
                'waiting': {
                    'on': {
                        'RECEIVE_DATA': {
                            'target': 'received',
                            'actions': ['storeData']
                        }
                    }
                },
                'received': {
                    'type': 'final'
                }
            }
        }";
        
        var stateMachine = StateMachine.CreateFromScript(script, dataActions, _guards);
        var actor = ActorSystem.Instance.Spawn("dataActor", id => new StateMachineActor(id, stateMachine));
        
        await actor.StartAsync();
        
        var testData = new { Name = "Test", Value = 123 };
        await actor.SendAsync("RECEIVE_DATA", testData);
        
        await Task.Delay(50);
        
        Assert.That(receivedData, Is.Not.Null);
        Assert.That(receivedData, Is.EqualTo(testData));
        Assert.That(actor.Machine.GetActiveStateString(), Does.Contain("received"));
    }
}