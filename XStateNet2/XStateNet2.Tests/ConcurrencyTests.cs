using Akka.Actor;
using Akka.TestKit.Xunit2;
using System.Collections.Concurrent;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for thread safety and concurrent access to state machines
/// </summary>
public class ConcurrencyTests : TestKit
{
    [Fact]
    public async Task ConcurrentEvents_ToSameMachine_ShouldBeProcessedSafely()
    {
        // Arrange
        var json = """
        {
            "id": "counter",
            "initial": "active",
            "context": {
                "count": 0
            },
            "states": {
                "active": {
                    "on": {
                        "INCREMENT": {
                            "actions": ["increment"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithContext("count", 0)
            .WithAction("increment", (ctx, _) =>
            {
                var count = ctx.Get<int>("count");
                ctx.Set("count", count + 1);
            })
            .BuildAndStart();


        // Act - Send 100 concurrent INCREMENT events
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => machine.Tell(new SendEvent("INCREMENT"))));
        }

        await Task.WhenAll(tasks);
        // Wait for all events to be processed

        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - All increments should be processed (actor model ensures serial processing)
        Assert.Equal(100, snapshot.Context["count"]);
    }

    [Fact]
    public async Task MultipleStateMachines_ConcurrentOperation_ShouldNotInterfere()
    {
        // Arrange
        var json = """
        {
            "id": "counter",
            "initial": "active",
            "context": {
                "id": 0,
                "count": 0
            },
            "states": {
                "active": {
                    "on": {
                        "INCREMENT": {
                            "actions": ["increment"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machines = new List<IActorRef>();

        // Create 10 separate machines
        for (int i = 0; i < 10; i++)
        {
            int machineId = i;
            var machine = factory.FromJson(json)
                .WithContext("id", machineId)
                .WithContext("count", 0)
                .WithAction("increment", (ctx, _) =>
                {
                    var count = ctx.Get<int>("count");
                    ctx.Set("count", count + 1);
                })
                .BuildAndStart($"machine-{machineId}");
            machines.Add(machine);
        }

        await Task.Delay(200);

        // Act - Send 10 events to each machine concurrently
        var tasks = new List<Task>();
        foreach (var machine in machines)
        {
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() => machine.Tell(new SendEvent("INCREMENT"))));
            }
        }

        await Task.WhenAll(tasks);
        await Task.Delay(500);

        // Assert - Each machine should have count = 10
        foreach (var machine in machines)
        {
            var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
            Assert.Equal(10, snapshot.Context["count"]);
        }
    }

    [Fact]
    public async Task ConcurrentStateQueries_ShouldNotCauseRaceConditions()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "START": "running"
                    }
                },
                "running": {
                    "on": {
                        "STOP": "idle"
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .BuildAndStart();

        await Task.Delay(200);

        // Act - Perform concurrent state queries while sending events
        var queryTasks = new List<Task<StateSnapshot>>();
        var eventTasks = new List<Task>();

        for (int i = 0; i < 50; i++)
        {
            queryTasks.Add(machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2)));
            eventTasks.Add(Task.Run(() => machine.Tell(new SendEvent(i % 2 == 0 ? "START" : "STOP"))));
        }

        var snapshots = await Task.WhenAll(queryTasks);
        await Task.WhenAll(eventTasks);

        // Assert - All queries should succeed
        Assert.Equal(50, snapshots.Length);
        Assert.All(snapshots, s => Assert.NotNull(s.CurrentState));
    }

    [Fact]
    public async Task ParallelStateTransitions_ShouldBeThreadSafe()
    {
        // Arrange
        var completedRegions = new ConcurrentBag<string>();

        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "region1": {
                    "initial": "idle",
                    "states": {
                        "idle": {
                            "on": { "GO": "running" }
                        },
                        "running": {
                            "entry": ["markRegion1Complete"],
                            "type": "final"
                        }
                    }
                },
                "region2": {
                    "initial": "idle",
                    "states": {
                        "idle": {
                            "on": { "GO": "running" }
                        },
                        "running": {
                            "entry": ["markRegion2Complete"],
                            "type": "final"
                        }
                    }
                },
                "region3": {
                    "initial": "idle",
                    "states": {
                        "idle": {
                            "on": { "GO": "running" }
                        },
                        "running": {
                            "entry": ["markRegion3Complete"],
                            "type": "final"
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("markRegion1Complete", (ctx, _) => completedRegions.Add("region1"))
            .WithAction("markRegion2Complete", (ctx, _) => completedRegions.Add("region2"))
            .WithAction("markRegion3Complete", (ctx, _) => completedRegions.Add("region3"))
            .BuildAndStart();

        await Task.Delay(200);

        // Act - Send GO event to trigger all parallel regions
        machine.Tell(new SendEvent("GO"));
        await Task.Delay(500);

        // Assert - All regions should complete
        Assert.Equal(3, completedRegions.Count);
        Assert.Contains("region1", completedRegions);
        Assert.Contains("region2", completedRegions);
        Assert.Contains("region3", completedRegions);
    }

    [Fact]
    public async Task ConcurrentContextUpdates_ShouldBeSerializedByActor()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "active",
            "context": {
                "values": []
            },
            "states": {
                "active": {
                    "on": {
                        "ADD": {
                            "actions": ["addValue"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("addValue", (ctx, data) =>
            {
                var values = ctx.Get<List<int>>("values") ?? new List<int>();
                if (data is int value)
                {
                    values.Add(value);
                    ctx.Set("values", values);
                }
            })
            .WithContext("values", new List<int>())
            .BuildAndStart();

        await Task.Delay(200);

        // Act - Send 50 concurrent ADD events with different values
        var tasks = new List<Task>();
        for (int i = 0; i < 50; i++)
        {
            int value = i;
            tasks.Add(Task.Run(() => machine.Tell(new SendEvent("ADD", value))));
        }

        await Task.WhenAll(tasks);
        await Task.Delay(500);

        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - All 50 values should be in the list (actor ensures serial processing)
        var values = snapshot.Context["values"] as List<int>;
        Assert.NotNull(values);
        Assert.Equal(50, values?.Count);
    }
}
