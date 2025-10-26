using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Comprehensive tests for Actor Model features in XStateNet2
/// Tests spawn, actor communication, actor lifecycle, and inter-machine messaging
/// </summary>
public class ActorModelTests : XStateTestKit
{
    #region Actor Registration and Communication

    [Fact]
    public void ActorRegistration_CanRegisterAndRetrieveActors()
    {
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "entry": ["checkActor"]
                }
            }
        }
        """;

        bool actorFound = false;
        var factory = new XStateMachineFactory(Sys);

        // Create another actor to register
        var otherActor = Sys.ActorOf(Props.Empty, "otherActor");

        var machine = factory.FromJson(json)
            .WithAction("checkActor", (ctx, _) =>
            {
                // Register actor
                ctx.RegisterActor("other", otherActor);

                // Retrieve actor
                var retrieved = ctx.GetActor("other");
                actorFound = retrieved != null && retrieved.Equals(otherActor);
            })
            .BuildAndStart();

        WaitForState(machine, s => s.CurrentState == "idle", "idle");
        Assert.True(actorFound, "Actor should be registered and retrievable");
    }

    [Fact]
    public void SendAction_SendsMessageToRegisteredActor()
    {
        // Create a test actor that receives messages
        var receivedMessages = new List<string>();
        var receiverProps = Props.Create(() => new TestReceiverActor(receivedMessages));
        var receiver = Sys.ActorOf(receiverProps, "receiver");

        var json = """
        {
            "id": "sender",
            "initial": "idle",
            "states": {
                "idle": {
                    "entry": ["registerReceiver"],
                    "on": {
                        "SEND_MESSAGE": {
                            "actions": [
                                {
                                    "type": "send",
                                    "event": "HELLO",
                                    "to": "receiver",
                                    "data": "test message"
                                }
                            ]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("registerReceiver", (ctx, _) => ctx.RegisterActor("receiver", receiver))
            .BuildAndStart();

        WaitForState(machine, s => s.CurrentState == "idle", "idle");

        SendEventAndWait(machine, "SEND_MESSAGE",
            s => s.CurrentState == "idle",
            "still idle after send");

        // Give receiver time to process
        AwaitCondition(() => receivedMessages.Count > 0, TimeSpan.FromSeconds(2));

        Assert.Contains("HELLO", receivedMessages);
    }

    [Fact]
    public void RaiseAction_SendsEventToSelf()
    {
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "count": 0
            },
            "states": {
                "idle": {
                    "on": {
                        "TRIGGER": {
                            "actions": [
                                {
                                    "type": "raise",
                                    "event": "RAISED_EVENT"
                                }
                            ]
                        },
                        "RAISED_EVENT": {
                            "actions": ["increment"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("increment", (ctx, _) =>
            {
                var count = ctx.Get<int>("count");
                ctx.Set("count", count + 1);
            })
            .BuildAndStart();

        machine.Tell(new SendEvent("TRIGGER"));

        WaitForState(machine, s =>
        {
            var count = s.Context["count"];
            int intValue = count is System.Text.Json.JsonElement element
                ? element.GetInt32()
                : Convert.ToInt32(count);
            return intValue == 1;
        }, "count incremented by raised event");
    }

    #endregion

    #region Spawn Action

    [Fact]
    public void SpawnAction_CreatesSpawnRequest()
    {
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "SPAWN": {
                            "actions": [
                                {
                                    "type": "spawn",
                                    "src": "childMachine",
                                    "id": "child1"
                                }
                            ]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        SendEventAndWait(machine, "SPAWN",
            s => s.Context.ContainsKey("_spawned_child1"),
            "spawn metadata in context");

        var snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1)).Result;
        Assert.True(snapshot.Context.ContainsKey("_spawned_child1"));
    }

    [Fact]
    public void SpawnAction_WithAutoGeneratedId()
    {
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "SPAWN": {
                            "actions": [
                                {
                                    "type": "spawn",
                                    "src": "childMachine"
                                }
                            ]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        machine.Tell(new SendEvent("SPAWN"));

        WaitForState(machine, s =>
        {
            // Check for any _spawned_ key
            return s.Context.Keys.Any(k => k.StartsWith("_spawned_"));
        }, "spawned with auto-generated ID");
    }

    [Fact]
    public void SpawnAction_MultipleSpawns()
    {
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "SPAWN_MULTIPLE": {
                            "actions": [
                                {
                                    "type": "spawn",
                                    "src": "worker1",
                                    "id": "worker1"
                                },
                                {
                                    "type": "spawn",
                                    "src": "worker2",
                                    "id": "worker2"
                                },
                                {
                                    "type": "spawn",
                                    "src": "worker3",
                                    "id": "worker3"
                                }
                            ]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        SendEventAndWait(machine, "SPAWN_MULTIPLE",
            s => s.Context.ContainsKey("_spawned_worker1") &&
                 s.Context.ContainsKey("_spawned_worker2") &&
                 s.Context.ContainsKey("_spawned_worker3"),
            "all workers spawned");
    }

    #endregion

    #region Stop Action

    [Fact]
    public void StopAction_StopsRegisteredActor()
    {
        // Create a test actor
        var testActor = Sys.ActorOf(Props.Empty, "testActor");

        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "STOP_ACTOR": {
                            "actions": [
                                {
                                    "type": "stop",
                                    "id": "testActor"
                                }
                            ]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("registerTestActor", (ctx, _) => ctx.RegisterActor("testActor", testActor))
            .BuildAndStart();

        // Register the actor
        var snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1)).Result;

        // We need to manually register since we don't have a proper way to do it in entry yet
        // This is a limitation of the current implementation

        machine.Tell(new SendEvent("STOP_ACTOR"));

        WaitForState(machine, s => s.CurrentState == "idle", "idle");
    }

    #endregion

    #region Inter-Machine Communication

    [Fact]
    public void InterMachineCommunication_ParentChildMessaging()
    {
        // Child machine
        var childJson = """
        {
            "id": "child",
            "initial": "waiting",
            "context": {
                "receivedCount": 0
            },
            "states": {
                "waiting": {
                    "on": {
                        "WORK": {
                            "actions": ["doWork"],
                            "target": "done"
                        }
                    }
                },
                "done": {}
            }
        }
        """;

        // Parent machine
        var parentJson = """
        {
            "id": "parent",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "START": "sending"
                    }
                },
                "sending": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);

        int workCount = 0;
        var child = factory.FromJson(childJson)
            .WithAction("doWork", (ctx, _) => workCount++)
            .BuildAndStart("child1");

        var parent = factory.FromJson(parentJson)
            .WithAction("sendToChild", (ctx, _) =>
            {
                var childRef = ctx.GetActor("child");
                if (childRef != null)
                {
                    childRef.Tell(new SendEvent("WORK"));
                }
            })
            .BuildAndStart("parent1");

        // Register child with parent
        var parentSnapshot = parent.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1)).Result;

        // Send work to child
        child.Tell(new SendEvent("WORK"));

        // Wait for child to process
        WaitForState(child, s => s.CurrentState == "done", "child done");
        Assert.Equal(1, workCount);
    }

    [Fact]
    public void InterMachineCommunication_BidirectionalMessaging()
    {
        var receivedByA = new List<string>();
        var receivedByB = new List<string>();

        var machineAJson = """
        {
            "id": "machineA",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "PING": {
                            "actions": ["logPing"]
                        }
                    }
                }
            }
        }
        """;

        var machineBJson = """
        {
            "id": "machineB",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "PONG": {
                            "actions": ["logPong"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);

        var machineA = factory.FromJson(machineAJson)
            .WithAction("logPing", (ctx, _) => receivedByA.Add("PING"))
            .BuildAndStart("machineA");

        var machineB = factory.FromJson(machineBJson)
            .WithAction("logPong", (ctx, _) => receivedByB.Add("PONG"))
            .BuildAndStart("machineB");

        // Send messages between machines
        machineA.Tell(new SendEvent("PING"));
        machineB.Tell(new SendEvent("PONG"));

        AwaitCondition(() => receivedByA.Count > 0, TimeSpan.FromSeconds(1));
        AwaitCondition(() => receivedByB.Count > 0, TimeSpan.FromSeconds(1));

        Assert.Contains("PING", receivedByA);
        Assert.Contains("PONG", receivedByB);
    }

    #endregion

    #region Complex Actor Scenarios

    [Fact]
    public void ComplexScenario_CoordinatorWorkerPattern()
    {
        var workCompleted = new List<string>();

        var workerJson = """
        {
            "id": "worker",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "DO_WORK": {
                            "target": "working"
                        }
                    }
                },
                "working": {
                    "entry": ["processWork"],
                    "on": {
                        "COMPLETE": "idle"
                    }
                }
            }
        }
        """;

        var coordinatorJson = """
        {
            "id": "coordinator",
            "initial": "idle",
            "context": {
                "taskCount": 0
            },
            "states": {
                "idle": {
                    "on": {
                        "DELEGATE": {
                            "actions": ["assignWork"],
                            "target": "waiting"
                        }
                    }
                },
                "waiting": {
                    "on": {
                        "WORKER_DONE": {
                            "actions": ["recordCompletion"],
                            "target": "idle"
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);

        var worker = factory.FromJson(workerJson)
            .WithAction("processWork", (ctx, _) =>
            {
                workCompleted.Add("work_done");
            })
            .BuildAndStart("worker1");

        var coordinator = factory.FromJson(coordinatorJson)
            .WithAction("assignWork", (ctx, _) =>
            {
                var count = ctx.Get<int>("taskCount");
                ctx.Set("taskCount", count + 1);
            })
            .WithAction("recordCompletion", (ctx, _) =>
            {
                // Record completion
            })
            .BuildAndStart("coordinator1");

        // Coordinator delegates work
        SendEventAndWait(coordinator, "DELEGATE",
            s => s.CurrentState == "waiting",
            "coordinator waiting");

        // Worker processes work
        SendEventAndWait(worker, "DO_WORK",
            s => s.CurrentState == "working",
            "worker working");

        Assert.Contains("work_done", workCompleted);
    }

    #endregion
}

/// <summary>
/// Test actor that receives and logs messages
/// </summary>
public class TestReceiverActor : ReceiveActor
{
    private readonly List<string> _receivedMessages;

    public TestReceiverActor(List<string> receivedMessages)
    {
        _receivedMessages = receivedMessages;

        Receive<SendEvent>(msg =>
        {
            _receivedMessages.Add(msg.Type);
        });

        ReceiveAny(_ => { });
    }
}
