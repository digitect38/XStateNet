using XStateNet;
using Xunit;

// Suppress obsolete warning - ActorSystem-based tests use a different communication pattern
// For standard inter-machine tests, use OrchestratorTestBase with EventBusOrchestrator
#pragma warning disable CS0618

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
            ["log"] = new List<NamedAction> { new NamedAction("log", (sm) => { Console.WriteLine($"Action in {sm.machineId}"); return Task.CompletedTask; }) }
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
        string script = @"
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

        var stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, guidIsolate: true, _actions, _guards);
        var actor = ActorSystem.Instance.Spawn("actor1", id => new StateMachineActor(id, stateMachine));

        Assert.Equal("actor1", actor.Id);
        Assert.Equal(ActorStatus.Idle, actor.Status);

        await actor.StartAsync();
        Assert.Equal(ActorStatus.Running, actor.Status);

        await actor.SendAsync("WORK");
        //await actor.Machine.WaitForStateWithActionsAsync("working", 1000);
        await actor.Machine.WaitForStateAsync("working", 1000);
        Assert.Contains($"{actor.Machine.machineId}.working", actor.Machine.GetActiveStateNames().ToString());

        await actor.StopAsync();
        Assert.Equal(ActorStatus.Stopped, actor.Status);
    }

    [Fact]
    public async Task TestActorCommunication()
    {
        string pingScript = @"
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
                    'entry': 'sendPing',
                    'on': {
                        'PONG_RECEIVED': 'waiting'
                    }
                }
            }
        }";

        string pongScript = @"
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
                    'entry': 'sendPong',
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
            ["sendPing"] = new List<NamedAction> { new NamedAction("sendPing", async (sm) => {
                await ActorSystem.Instance.SendToActor("pongActor", "PING");
            }) }
        };

        var pongActions = new ActionMap
        {
            ["sendPong"] = new List<NamedAction> { new NamedAction("sendPong", async (sm) => {
                pingReceived = true;
                await ActorSystem.Instance.SendToActor("pingActor", "PONG_RECEIVED");
                await ActorSystem.Instance.SendToActor("pongActor", "DONE");
                pongReceived = true;
            }) }
        };

        var pingMachine = StateMachineFactory.CreateFromScript(pingScript, threadSafe: false, guidIsolate: true, pingActions, _guards);
        var pongMachine = StateMachineFactory.CreateFromScript(pongScript, threadSafe: false, guidIsolate: true, pongActions, _guards);

        var pingActor = ActorSystem.Instance.Spawn("pingActor", id => new StateMachineActor(id, pingMachine));
        var pongActor = ActorSystem.Instance.Spawn("pongActor", id => new StateMachineActor(id, pongMachine));

        await pingActor.StartAsync();
        await pongActor.StartAsync();

        await pingActor.SendAsync("START");
        await pingActor.Machine.WaitForStateWithActionsAsync("waiting", 1000);
        await pongActor.Machine.WaitForStateWithActionsAsync("waiting", 1000);


        Assert.True(pingReceived);
        Assert.True(pongReceived);
        Assert.Contains($"{pingActor.Machine.machineId}.waiting", pingActor.Machine.GetActiveStateNames());
        Assert.Contains($"{pongActor.Machine.machineId}.waiting", pongActor.Machine.GetActiveStateNames());
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

        var parentMachine = StateMachineFactory.CreateFromScript(parentScript, threadSafe: false, false, _actions, _guards);
        var childMachine = StateMachineFactory.CreateFromScript(childScript, threadSafe: false, false, _actions, _guards);

        var parentActor = ActorSystem.Instance.Spawn($"parent_{testId}", id => new StateMachineActor(id, parentMachine));
        await parentActor.StartAsync();

        // Spawn child actor
        var childActor = parentActor.SpawnChild("child1", childMachine);
        await childActor.StartAsync();

        Assert.Equal($"parent_{testId}.child1", childActor.Id);
        Assert.Equal(ActorStatus.Running, childActor.Status);
        Assert.Contains($"{childActor.Machine.machineId}.active", childActor.Machine.GetActiveStateNames());

        await childActor.SendAsync("STOP");

        // Wait for the state to actually change with a timeout
        var maxWaitTime = 1000; // 1 second timeout
        var waitInterval = 10;
        var elapsed = 0;

        while (elapsed < maxWaitTime)
        {
            var currentState = childActor.Machine.GetActiveStateNames();
            if (currentState.Contains("stopped"))
            {
                break;
            }
            await Task.Delay(waitInterval);
            elapsed += waitInterval;
        }

        Assert.Contains($"{childActor.Machine.machineId}.stopped", childActor.Machine.GetActiveStateNames());
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

        var stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, errorActions, _guards);
        var actor = ActorSystem.Instance.Spawn("errorActor", id => new StateMachineActor(id, stateMachine));

        await actor.StartAsync();
        await actor.SendAsync("CAUSE_ERROR");
        await actor.Machine.WaitForStateWithActionsAsync("error", 1000);

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

        var stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, countingActions, _guards);
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

        var stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, dataActions, _guards);
        var actor = ActorSystem.Instance.Spawn("dataActor", id => new StateMachineActor(id, stateMachine));

        await actor.StartAsync();

        var testData = new { Name = "Test", Value = 123 };
        await actor.SendAsync("RECEIVE_DATA", testData);
        await actor.Machine.WaitForStateWithActionsAsync("received", 1000);


        Assert.NotNull(receivedData);
        Assert.Equal(testData, receivedData);
        Assert.Contains($"{actor.Machine.machineId}.received", actor.Machine.GetActiveStateNames());
    }
}
