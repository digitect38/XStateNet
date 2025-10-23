using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for delayed/after transitions feature
/// Tests time-based automatic state transitions
/// </summary>
public class DelayedTransitionsTests : TestKit
{
    [Fact]
    public async Task AfterTransition_SingleDelay_ShouldTransition()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "waiting",
            "states": {
                "waiting": {
                    "entry": ["onWaiting"],
                    "after": {
                        "200": "completed"
                    }
                },
                "completed": {
                    "entry": ["onCompleted"],
                    "type": "final"
                }
            }
        }
        """;

        bool waitingEntered = false;
        bool completedEntered = false;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("onWaiting", (ctx, _) => waitingEntered = true)
            .WithAction("onCompleted", (ctx, _) => completedEntered = true)
            .BuildAndStart();

        // Act - Wait for initial state
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1));
        Assert.Equal("waiting", snapshot.CurrentState);
        Assert.True(waitingEntered);
        Assert.False(completedEntered);

        // Wait for after transition to trigger
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1)).Result;
            Assert.Equal("completed", result.CurrentState);
            Assert.True(completedEntered);
        }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task AfterTransition_MultipleDelays_ShouldTakeFirst()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "after": {
                        "100": "quick",
                        "500": "slow"
                    }
                },
                "quick": {
                    "entry": ["onQuick"],
                    "type": "final"
                },
                "slow": {
                    "entry": ["onSlow"],
                    "type": "final"
                }
            }
        }
        """;

        bool quickReached = false;
        bool slowReached = false;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("onQuick", (ctx, _) => quickReached = true)
            .WithAction("onSlow", (ctx, _) => slowReached = true)
            .BuildAndStart();

        // Act - Wait for after transition
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1)).Result;
            Assert.Equal("quick", result.CurrentState);
            Assert.True(quickReached);
            Assert.False(slowReached); // Should not reach slow state
        }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task AfterTransition_WithGuard_ShouldEvaluate()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "checking",
            "context": {
                "shouldProceed": false
            },
            "states": {
                "checking": {
                    "after": {
                        "100": {
                            "target": "success",
                            "cond": "canProceed"
                        },
                        "150": "failure"
                    }
                },
                "success": {
                    "entry": ["onSuccess"],
                    "type": "final"
                },
                "failure": {
                    "entry": ["onFailure"],
                    "type": "final"
                }
            }
        }
        """;

        bool successReached = false;
        bool failureReached = false;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("canProceed", (ctx, _) => ctx.Get<bool>("shouldProceed"))
            .WithAction("onSuccess", (ctx, _) => successReached = true)
            .WithAction("onFailure", (ctx, _) => failureReached = true)
            .WithContext("shouldProceed", false)
            .BuildAndStart();

        // Act - Guard fails, should go to failure after 150ms
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1)).Result;
            Assert.Equal("failure", result.CurrentState);
            Assert.False(successReached);
            Assert.True(failureReached);
        }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task AfterTransition_CancelledByEvent_ShouldNotTransition()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "waiting",
            "states": {
                "waiting": {
                    "after": {
                        "500": "timeout"
                    },
                    "on": {
                        "CANCEL": "cancelled"
                    }
                },
                "timeout": {
                    "entry": ["onTimeout"],
                    "type": "final"
                },
                "cancelled": {
                    "entry": ["onCancelled"],
                    "type": "final"
                }
            }
        }
        """;

        bool timeoutReached = false;
        bool cancelledReached = false;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("onTimeout", (ctx, _) => timeoutReached = true)
            .WithAction("onCancelled", (ctx, _) => cancelledReached = true)
            .BuildAndStart();

        // Get initial state
        await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1));

        // Act - Cancel before timeout
        await Task.Delay(100);
        machine.Tell(new SendEvent("CANCEL"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1));

        // Assert - Should be in cancelled state, not timeout
        Assert.Equal("cancelled", snapshot.CurrentState);
        Assert.False(timeoutReached);
        Assert.True(cancelledReached);

        // Wait to ensure timeout doesn't fire
        await Task.Delay(500);
        snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1));
        Assert.Equal("cancelled", snapshot.CurrentState);
    }

    [Fact]
    public async Task AfterTransition_WithActions_ShouldExecute()
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
                    "after": {
                        "100": {
                            "target": "done",
                            "actions": ["increment", "logTransition"]
                        }
                    }
                },
                "done": {
                    "type": "final"
                }
            }
        }
        """;

        var actionLog = new List<string>();

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("increment", (ctx, _) =>
            {
                var count = ctx.Get<int>("count");
                ctx.Set("count", count + 1);
                actionLog.Add("increment");
            })
            .WithAction("logTransition", (ctx, _) => actionLog.Add("logTransition"))
            .BuildAndStart();

        // Act - Wait for after transition with actions
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1)).Result;
            Assert.Equal("done", result.CurrentState);
            Assert.Equal(1, result.Context["count"]);
            Assert.Contains("increment", actionLog);
            Assert.Contains("logTransition", actionLog);
        }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));
    }

    [Fact(Skip = "Nested state relative path resolution for after transitions needs implementation")]
    public async Task AfterTransition_NestedStates_ShouldWork()
    {
        // Arrange
        // TODO: Implement relative path resolution for nested state after transitions
        // Current issue: "sibling" target is not resolved to "parent.sibling"
        var json = """
        {
            "id": "test",
            "initial": "parent",
            "states": {
                "parent": {
                    "initial": "child",
                    "states": {
                        "child": {
                            "after": {
                                "100": "sibling"
                            }
                        },
                        "sibling": {
                            "entry": ["onSibling"],
                            "type": "final"
                        }
                    }
                }
            }
        }
        """;

        bool siblingReached = false;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("onSibling", (ctx, _) => siblingReached = true)
            .BuildAndStart();

        // Act - Wait for nested after transition
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1)).Result;
            Assert.Contains("sibling", result.CurrentState);
            Assert.True(siblingReached);
        }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));
    }
}
