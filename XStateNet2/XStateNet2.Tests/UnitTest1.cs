using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

public class BasicStateMachineTests : TestKit
{
    [Fact]
    public async Task BasicTransition_ShouldWork()
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
                    "entry": ["onRunning"],
                    "type": "final"
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        bool runningActionExecuted = false;

        var machine = factory.FromJson(json)
            .WithAction("onRunning", (ctx, _) => runningActionExecuted = true)
            .BuildAndStart();

        // Act
        machine.Tell(new SendEvent("START"));
        await Task.Delay(200);

        // Assert
        Assert.True(runningActionExecuted);
    }

    [Fact]
    public async Task AssignAction_ShouldUpdateContext()
    {
        // Arrange
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
                        "INCREMENT": {
                            "actions": [
                                {
                                    "type": "assign",
                                    "assignment": {
                                        "count": 1
                                    }
                                },
                                "checkCount"
                            ]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        int? count = null;

        var machine = factory.FromJson(json)
            .WithAction("checkCount", (ctx, _) => count = ctx.Get<int?>("count"))
            .BuildAndStart();

        // Act
        machine.Tell(new SendEvent("INCREMENT"));
        await Task.Delay(200);

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task AlwaysTransition_WithGuard_ShouldEvaluate()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "routing",
            "context": {
                "priority": 10
            },
            "states": {
                "routing": {
                    "always": [
                        {
                            "target": "highPriority",
                            "cond": "isHighPriority"
                        },
                        {
                            "target": "lowPriority"
                        }
                    ]
                },
                "highPriority": {
                    "entry": ["onHighPriority"],
                    "type": "final"
                },
                "lowPriority": {
                    "entry": ["onLowPriority"],
                    "type": "final"
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        bool highPriorityCalled = false;

        var machine = factory.FromJson(json)
            .WithGuard("isHighPriority", (ctx, _) => ctx.Get<int>("priority") > 5)
            .WithAction("onHighPriority", (ctx, _) => highPriorityCalled = true)
            .WithAction("onLowPriority", (ctx, _) => {})
            .WithContext("priority", 10)
            .BuildAndStart();

        await Task.Delay(200);

        // Assert
        Assert.True(highPriorityCalled);
    }

    [Fact]
    public async Task ParallelStates_ShouldCompleteAllRegions()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "processing",
            "states": {
                "processing": {
                    "type": "parallel",
                    "states": {
                        "task1": {
                            "initial": "running",
                            "states": {
                                "running": {
                                    "after": {
                                        "100": "done"
                                    }
                                },
                                "done": {
                                    "type": "final",
                                    "entry": ["onTask1Done"]
                                }
                            }
                        },
                        "task2": {
                            "initial": "running",
                            "states": {
                                "running": {
                                    "after": {
                                        "150": "done"
                                    }
                                },
                                "done": {
                                    "type": "final",
                                    "entry": ["onTask2Done"]
                                }
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        bool task1Done = false;
        bool task2Done = false;

        var machine = factory.FromJson(json)
            .WithAction("onTask1Done", (ctx, _) => task1Done = true)
            .WithAction("onTask2Done", (ctx, _) => task2Done = true)
            .BuildAndStart();

        // Act
        await Task.Delay(300);

        // Assert
        Assert.True(task1Done);
        Assert.True(task2Done);
    }
}