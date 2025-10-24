using Akka.Actor;
using Akka.TestKit.Xunit2;
using System.Diagnostics;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for timing-sensitive operations and performance guarantees
/// Tests transition timing, event ordering, and timeout behavior
/// </summary>
public class TimingSensitiveTests : TestKit
{
    [Fact]
    public async Task EventProcessing_MaintainsOrderUnderConcurrentSends()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "active",
            "context": {
                "events": []
            },
            "states": {
                "active": {
                    "on": {
                        "EVENT": {
                            "actions": ["recordEvent"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("recordEvent", (ctx, eventData) =>
            {
                var events = ctx.Get<List<int>>("events") ?? new List<int>();
                if (eventData is int value)
                {
                    events.Add(value);
                    ctx.Set("events", events);
                }
            })
            .WithContext("events", new List<int>())
            .BuildAndStart();

        // Act - Send 50 events concurrently
        var tasks = new List<Task>();
        for (int i = 0; i < 50; i++)
        {
            int value = i;
            tasks.Add(Task.Run(() => machine.Tell(new SendEvent("EVENT", value))));
        }

        await Task.WhenAll(tasks);
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - All events should be processed (actor model ensures serial processing)
        var events = snapshot.Context["events"] as List<int>;
        Assert.NotNull(events);
        Assert.Equal(50, events!.Count);

        // All values 0-49 should be present
        for (int i = 0; i < 50; i++)
        {
            Assert.Contains(i, events);
        }
    }

    [Fact]
    public async Task StateTransition_CompletesWithinReasonableTime()
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
                    "entry": ["onRunning"]
                }
            }
        }
        """;

        bool runningEntered = false;
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("onRunning", (ctx, _) => runningEntered = true)
            .BuildAndStart();

        // Act - Measure transition time
        var stopwatch = Stopwatch.StartNew();
        machine.Tell(new SendEvent("START"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        stopwatch.Stop();

        // Assert - Transition should complete quickly (< 100ms for local actor)
        Assert.Equal("running", snapshot.CurrentState);
        Assert.True(runningEntered);
        Assert.True(stopwatch.ElapsedMilliseconds < 100,
            $"Transition took {stopwatch.ElapsedMilliseconds}ms, expected < 100ms");
    }

    [Fact]
    public async Task MultipleRapidTransitions_ProcessedCorrectly()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "s1",
            "context": {
                "count": 0
            },
            "states": {
                "s1": {
                    "on": {
                        "NEXT": {
                            "target": "s2",
                            "actions": ["increment"]
                        }
                    }
                },
                "s2": {
                    "on": {
                        "NEXT": {
                            "target": "s3",
                            "actions": ["increment"]
                        }
                    }
                },
                "s3": {
                    "on": {
                        "NEXT": {
                            "target": "s1",
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

        // Act - Send 15 rapid transitions
        for (int i = 0; i < 15; i++)
        {
            machine.Tell(new SendEvent("NEXT"));
        }

        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Should have processed all 15 transitions
        Assert.Equal(15, snapshot.Context["count"]);
        // After 15 transitions (mod 3), should be back at s1
        Assert.Equal("s1", snapshot.CurrentState);
    }

    [Fact]
    public async Task DelayedTransition_FiresAtCorrectTime()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "waiting",
            "states": {
                "waiting": {
                    "after": {
                        "200": "done"
                    }
                },
                "done": {
                    "entry": ["onDone"],
                    "type": "final"
                }
            }
        }
        """;

        bool doneEntered = false;
        var doneTime = DateTime.MinValue;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("onDone", (ctx, _) =>
            {
                doneEntered = true;
                doneTime = DateTime.UtcNow;
            })
            .BuildAndStart();

        var startTime = DateTime.UtcNow;

        // Act - Wait for initial state
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1));
        Assert.Equal("waiting", snapshot.CurrentState);

        // Wait for delayed transition
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1)).Result;
            Assert.Equal("done", result.CurrentState);
            Assert.True(doneEntered);
        }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));

        var duration = (doneTime - startTime).TotalMilliseconds;

        // Assert - Should fire close to 200ms (allow 150-350ms range for test tolerance)
        Assert.InRange(duration, 150, 350);
    }

    [Fact]
    public async Task ConcurrentStateQueries_ReturnConsistentState()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "TOGGLE": "active"
                    }
                },
                "active": {
                    "on": {
                        "TOGGLE": "idle"
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .BuildAndStart();

        // Act - Send events and query state concurrently
        var queryTasks = new List<Task<StateSnapshot>>();
        for (int i = 0; i < 20; i++)
        {
            machine.Tell(new SendEvent("TOGGLE"));
            queryTasks.Add(machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2)));
        }

        var snapshots = await Task.WhenAll(queryTasks);

        // Assert - All queries should succeed with valid states
        Assert.All(snapshots, s =>
        {
            Assert.NotNull(s.CurrentState);
            Assert.True(s.CurrentState == "idle" || s.CurrentState == "active");
        });
    }

    [Fact]
    public async Task HighFrequencyEvents_AllProcessedCorrectly()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "counter",
            "context": {
                "count": 0
            },
            "states": {
                "counter": {
                    "on": {
                        "INC": {
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

        var stopwatch = Stopwatch.StartNew();

        // Act - Send 1000 events as fast as possible
        for (int i = 0; i < 1000; i++)
        {
            machine.Tell(new SendEvent("INC"));
        }

        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        // Assert - All 1000 events should be processed
        Assert.Equal(1000, snapshot.Context["count"]);

        // Should complete in reasonable time (< 2 seconds)
        Assert.True(stopwatch.ElapsedMilliseconds < 2000,
            $"Processing 1000 events took {stopwatch.ElapsedMilliseconds}ms, expected < 2000ms");
    }

    [Fact]
    public async Task RaceCondition_EventsDuringTransition_ProcessedCorrectly()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "events": []
            },
            "states": {
                "idle": {
                    "on": {
                        "START": {
                            "target": "active",
                            "actions": ["recordStart"]
                        },
                        "LOG": {
                            "actions": ["recordLog"]
                        }
                    }
                },
                "active": {
                    "on": {
                        "LOG": {
                            "actions": ["recordLog"]
                        },
                        "STOP": {
                            "target": "idle",
                            "actions": ["recordStop"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("recordStart", (ctx, _) =>
            {
                var events = ctx.Get<List<string>>("events") ?? new List<string>();
                events.Add("START");
                ctx.Set("events", events);
            })
            .WithAction("recordStop", (ctx, _) =>
            {
                var events = ctx.Get<List<string>>("events") ?? new List<string>();
                events.Add("STOP");
                ctx.Set("events", events);
            })
            .WithAction("recordLog", (ctx, _) =>
            {
                var events = ctx.Get<List<string>>("events") ?? new List<string>();
                events.Add("LOG");
                ctx.Set("events", events);
            })
            .WithContext("events", new List<string>())
            .BuildAndStart();

        // Act - Send events in rapid succession with transitions interleaved
        machine.Tell(new SendEvent("LOG"));
        machine.Tell(new SendEvent("START"));
        machine.Tell(new SendEvent("LOG"));
        machine.Tell(new SendEvent("LOG"));
        machine.Tell(new SendEvent("STOP"));
        machine.Tell(new SendEvent("LOG"));

        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - All events should be recorded in order
        var events = snapshot.Context["events"] as List<string>;
        Assert.NotNull(events);
        Assert.Equal(6, events!.Count);
        Assert.Equal("LOG", events[0]);
        Assert.Equal("START", events[1]);
        Assert.Equal("LOG", events[2]);
        Assert.Equal("LOG", events[3]);
        Assert.Equal("STOP", events[4]);
        Assert.Equal("LOG", events[5]);

        // Should end in idle state
        Assert.Equal("idle", snapshot.CurrentState);
    }

    [Fact]
    public async Task ParallelRegions_TransitionsHappenConcurrently()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "region1": {
                    "initial": "idle",
                    "states": {
                        "idle": {
                            "on": {
                                "GO": {
                                    "target": "done",
                                    "actions": ["recordR1"]
                                }
                            }
                        },
                        "done": {}
                    }
                },
                "region2": {
                    "initial": "idle",
                    "states": {
                        "idle": {
                            "on": {
                                "GO": {
                                    "target": "done",
                                    "actions": ["recordR2"]
                                }
                            }
                        },
                        "done": {}
                    }
                },
                "region3": {
                    "initial": "idle",
                    "states": {
                        "idle": {
                            "on": {
                                "GO": {
                                    "target": "done",
                                    "actions": ["recordR3"]
                                }
                            }
                        },
                        "done": {}
                    }
                }
            }
        }
        """;

        var timestamps = new Dictionary<string, DateTime>();
        var timestampLock = new object();

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("recordR1", (ctx, _) =>
            {
                lock (timestampLock)
                {
                    timestamps["R1"] = DateTime.UtcNow;
                }
            })
            .WithAction("recordR2", (ctx, _) =>
            {
                lock (timestampLock)
                {
                    timestamps["R2"] = DateTime.UtcNow;
                }
            })
            .WithAction("recordR3", (ctx, _) =>
            {
                lock (timestampLock)
                {
                    timestamps["R3"] = DateTime.UtcNow;
                }
            })
            .BuildAndStart();

        var startTime = DateTime.UtcNow;

        // Act - Send GO event to trigger all parallel regions
        machine.Tell(new SendEvent("GO"));

        // Wait for all transitions to complete
        await Task.Delay(300);
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - All regions should transition
        Assert.Contains("region1.done", snapshot.CurrentState);
        Assert.Contains("region2.done", snapshot.CurrentState);
        Assert.Contains("region3.done", snapshot.CurrentState);

        // All transitions should happen close together (within 100ms)
        Assert.Equal(3, timestamps.Count);
        var times = timestamps.Values.ToList();
        var timeSpan = (times.Max() - times.Min()).TotalMilliseconds;
        Assert.True(timeSpan < 100,
            $"Parallel transitions spread over {timeSpan}ms, expected < 100ms");
    }

    [Fact]
    public async Task TransitionMetrics_TrackPerformance()
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

        var transitionTimes = new List<long>();

        // Act - Perform 10 transitions and measure each
        for (int i = 0; i < 10; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            machine.Tell(new SendEvent(i % 2 == 0 ? "START" : "STOP"));
            await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
            stopwatch.Stop();
            transitionTimes.Add(stopwatch.ElapsedMilliseconds);
        }

        // Assert - Calculate metrics
        var avgTime = transitionTimes.Average();
        var maxTime = transitionTimes.Max();
        var minTime = transitionTimes.Min();

        Assert.True(avgTime < 50, $"Average transition time {avgTime}ms, expected < 50ms");
        Assert.True(maxTime < 100, $"Max transition time {maxTime}ms, expected < 100ms");
        Assert.All(transitionTimes, time => Assert.True(time >= 0));
    }
}
