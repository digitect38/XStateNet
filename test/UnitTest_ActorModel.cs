using Xunit;

using System;
using System.Threading;
using System.Threading.Tasks;
using XStateNet;

namespace ActorModelTests;

public class UnitTest_ActorModel : IDisposable
{
    private ActionMap _actions;
    private GuardMap _guards;
    
    public UnitTest_ActorModel()
    {
        // Clear actor system for each test
        ActorSystem.Instance.StopAllActors().GetAwaiter().GetResult();
        
        _actions = new ActionMap
        {
            ["log"] = new List<NamedAction> { new NamedAction("log", (sm) => Console.WriteLine($"Action in {sm.machineId}")) }
        };
        
        _guards = new GuardMap();
    }
    
    public void Dispose()
    {
        ActorSystem.Instance.StopAllActors().GetAwaiter().GetResult();
    }
    
    [Fact]
    public async Task TestBasicActorCreation()
    {
        var uniqueId = $"testActor-{Guid.NewGuid():N}";
        string script = @"
        {
            'id': '" + uniqueId + @"',
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
        
        Assert.Equal("actor1", actor.Id);
        Assert.Equal(ActorStatus.Idle, actor.Status);
        
        await actor.StartAsync();
        Assert.Equal(ActorStatus.Running, actor.Status);
        
        await actor.SendAsync("WORK");
        await Task.Delay(50); // Allow message processing
        
        Assert.Contains("working", actor.Machine.GetActiveStateString());
        
        await actor.StopAsync();
        Assert.Equal(ActorStatus.Stopped, actor.Status);
    }
    
    [Fact]
    public async Task TestActorCommunication()
    {
        // Use unique IDs to avoid conflicts with parallel tests
        var testId = Guid.NewGuid().ToString().Substring(0, 8);
        
        var uniqueId = $"ping_{Guid.NewGuid():N}";

        string pingScript = @"
        {
            'id': '" + uniqueId + @"',
            'initial': 'waiting',
            'states': {
                'waiting': {
                    'on': {
                        'START': 'pinging'
                    }
                },
                'pinging': {
                    'entry': 'sendPong',
                    'on': {
                        'PONG_RECEIVED': 'waiting'
                    }
                }
            }
        }";

        uniqueId = $"pong_{Guid.NewGuid():N}";

        string pongScript = @"
        {
            'id': '" + uniqueId + @"',
            'initial': 'waiting',
            'states': {
                'waiting': {
                    'on': {
                        'PING': 'ponging'
                    }
                },
                'ponging': {
                    'entry': 'sendPing',
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
                await ActorSystem.Instance.SendToActor($"pong_{testId}", "PING");
            }) }
        };

        var pongActions = new ActionMap
        {
            ["sendPing"] = new List<NamedAction> { new NamedAction("sendPing", async (sm) => {
                pingReceived = true;
                await ActorSystem.Instance.SendToActor($"ping_{testId}", "PONG_RECEIVED");
                pongReceived = true;
            }) }
        };

        var pingMachine = StateMachine.CreateFromScript(pingScript, pingActions, _guards);
        var pongMachine = StateMachine.CreateFromScript(pongScript, pongActions, _guards);

        var pingActor = ActorSystem.Instance.Spawn($"ping_{testId}", id => new StateMachineActor(id, pingMachine));
        var pongActor = ActorSystem.Instance.Spawn($"pong_{testId}", id => new StateMachineActor(id, pongMachine));
        
        await pingActor.StartAsync();
        await pongActor.StartAsync();
        
        await pingActor.SendAsync("START");
        await Task.Delay(100); // Allow message processing
        
        Assert.True(pingReceived);
        Assert.True(pongReceived);
        Assert.Contains("waiting", pingActor.Machine.GetActiveStateString());
        Assert.Contains("ponging", pongActor.Machine.GetActiveStateString());
    }
    
    [Fact]
    public async Task TestChildActorSpawning()
    {
        // Use unique IDs to avoid conflicts with parallel tests
        var testId = Guid.NewGuid().ToString().Substring(0, 8);

        var uniqueId = $"parent_{Guid.NewGuid():N}";

        string parentScript = @"
        {
            'id': '" + uniqueId + @"',
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
        
        uniqueId = $"child_{Guid.NewGuid():N}";

        string childScript = @"
        {
            'id': '" + uniqueId + @"',
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

        var parentActor = ActorSystem.Instance.Spawn($"parent_{testId}", id => new StateMachineActor(id, parentMachine));
        await parentActor.StartAsync();
        
        // Spawn child actor
        var childActor = parentActor.SpawnChild("child1", childMachine);
        await childActor.StartAsync();

        Assert.Equal($"parent_{testId}.child1", childActor.Id);
        Assert.Equal(ActorStatus.Running, childActor.Status);
        Assert.Contains("active", childActor.Machine.GetActiveStateString());
        
        await childActor.SendAsync("STOP");

        // Wait for the state to actually change with a timeout
        var maxWaitTime = 1000; // 1 second timeout
        var waitInterval = 10;
        var elapsed = 0;

        while (elapsed < maxWaitTime)
        {
            var currentState = childActor.Machine.GetActiveStateString();
            if (currentState.Contains("stopped"))
            {
                break;
            }
            await Task.Delay(waitInterval);
            elapsed += waitInterval;
        }

        Assert.Contains("stopped", childActor.Machine.GetActiveStateString());
    }
    
    [Fact]
    public async Task TestActorErrorHandling()
    {
        var uniqueId = $"errorActor_{Guid.NewGuid():N}";

        var script = @"{
          'id':'" + uniqueId + @"',
          'initial':'idle',
          'states':{
            'idle':{
                'on':{
                    'CAUSE_ERROR':'error'
                }
            },
            'error':{
                'entry':'throwError'
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
        Assert.Equal(ActorStatus.Error, actor.Status);
    }
    
    [Fact]
    public async Task TestActorMessageQueuing()
    {
        var messageCount = 0;

        var countingActions = new ActionMap
        {
            ["count"] = new List<NamedAction> { new NamedAction("count", (sm) => {
                Interlocked.Increment(ref messageCount);
            }) }
        };

        var uniqueId = $"counter_{Guid.NewGuid():N}";
        string script = @"
        {
            'id': '" + uniqueId + @"',
            'initial': 'counting',
            'states': {
                'counting': {
                    'on': {
                        'INCREMENT': {
                            'target': 'counting',
                            'actions': 'count'
                        }
                    }
                }
            }
        }";

        var stateMachine = StateMachine.CreateFromScript(script, countingActions, _guards);
        var actor = ActorSystem.Instance.Spawn(uniqueId, id => new StateMachineActor(id, stateMachine));

        await actor.StartAsync();

        // Send multiple messages rapidly
        for (int i = 0; i < 10; i++)
        {
            await actor.SendAsync("INCREMENT");
        }

        // Allow more time for all messages to be processed
        await Task.Delay(200);

        Assert.Equal(10, messageCount);
    }
    
    [Fact]
    public async Task TestActorDataPassing()
    {
        object? receivedData = null;
        
        var dataActions = new ActionMap
        {
            ["storeData"] = new List<NamedAction> { new NamedAction("storeData", (sm) => {
                receivedData = sm.ContextMap?["_eventData"];
            }) }
        };
        var uniqueId = $"dataReceiver-{Guid.NewGuid():N}";

        string script = @"
        {
            'id': '" + uniqueId + @"',
            'initial': 'waiting',
            'states': {
                'waiting': {
                    'on': {
                        'RECEIVE_DATA': {
                            'target': 'received',
                            'actions': 'storeData'
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
        
        Assert.NotNull(receivedData);
        Assert.Equal(testData, receivedData);
        Assert.Contains("received", actor.Machine.GetActiveStateString());
    }
}