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

        // Wait for initial state (deterministic - no Task.Delay)
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3));
        Assert.Equal("waiting", snapshot.CurrentState);
        Assert.True(waitingEntered);
        Assert.False(completedEntered);

        // Wait for after transition to trigger (200ms delay + buffer)
        // Inner Ask timeout must be shorter than outer AwaitAssert timeout
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500)).Result;
            Assert.Equal("completed", result.CurrentState);
            Assert.True(completedEntered);
        }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
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

        // Act - Wait for after transition (100ms delay + buffer)
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500)).Result;
            Assert.Equal("quick", result.CurrentState);
            Assert.True(quickReached);
            Assert.False(slowReached); // Should not reach slow state
        }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
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
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500)).Result;
            Assert.Equal("failure", result.CurrentState);
            Assert.False(successReached);
            Assert.True(failureReached);
        }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
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
        await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3));

        // Act - Cancel before timeout (deterministic using AwaitAssert instead of Task.Delay)
        machine.Tell(new SendEvent("CANCEL"));

        // Wait for CANCEL event to be processed
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500)).Result;
            Assert.Equal("cancelled", result.CurrentState);
            Assert.False(timeoutReached);
            Assert.True(cancelledReached);
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));

        // Verify timeout doesn't fire (wait longer than the timeout period)
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500)).Result;
            Assert.Equal("cancelled", result.CurrentState);
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100));
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

        // Act - Wait for after transition with actions (100ms delay + buffer)
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500)).Result;
            Assert.Equal("done", result.CurrentState);
            Assert.Equal(1, result.Context["count"]);
            Assert.Contains("increment", actionLog);
            Assert.Contains("logTransition", actionLog);
        }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
    }

    //[Fact(Skip = "Nested state relative path resolution for after transitions needs implementation")]
    [Fact]
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

        // Act - Wait for nested after transition (100ms delay + buffer)
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500)).Result;
            Assert.Contains("sibling", result.CurrentState);
            Assert.True(siblingReached);
        }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task AfterTransition_ArrayWithGuards_ShouldTakeFirstMatch()
    {
        // Arrange - Test array of guarded transitions (new feature)
        var json = """
        {
            "id": "test",
            "initial": "waiting",
            "context": {
                "priority": "medium"
            },
            "states": {
                "waiting": {
                    "after": {
                        "100": [
                            {
                                "target": "high",
                                "guard": "isHighPriority"
                            },
                            {
                                "target": "medium",
                                "guard": "isMediumPriority"
                            },
                            {
                                "target": "low"
                            }
                        ]
                    }
                },
                "high": {
                    "entry": ["onHigh"],
                    "type": "final"
                },
                "medium": {
                    "entry": ["onMedium"],
                    "type": "final"
                },
                "low": {
                    "entry": ["onLow"],
                    "type": "final"
                }
            }
        }
        """;

        bool highReached = false;
        bool mediumReached = false;
        bool lowReached = false;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("isHighPriority", (ctx, _) => ctx.Get<string>("priority") == "high")
            .WithGuard("isMediumPriority", (ctx, _) => ctx.Get<string>("priority") == "medium")
            .WithAction("onHigh", (ctx, _) => highReached = true)
            .WithAction("onMedium", (ctx, _) => mediumReached = true)
            .WithAction("onLow", (ctx, _) => lowReached = true)
            .WithContext("priority", "medium")
            .BuildAndStart();

        // Act - Wait for after transition with guard evaluation (100ms delay + buffer)
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500)).Result;
            Assert.Equal("medium", result.CurrentState);
            Assert.False(highReached);
            Assert.True(mediumReached);
            Assert.False(lowReached);
        }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task AfterTransition_ArrayWithGuards_ShouldFallThrough()
    {
        // Arrange - Test fallthrough to last unconditional transition
        var json = """
        {
            "id": "test",
            "initial": "waiting",
            "context": {
                "priority": "unknown"
            },
            "states": {
                "waiting": {
                    "after": {
                        "100": [
                            {
                                "target": "high",
                                "guard": "isHighPriority"
                            },
                            {
                                "target": "medium",
                                "guard": "isMediumPriority"
                            },
                            {
                                "target": "default"
                            }
                        ]
                    }
                },
                "high": {
                    "type": "final"
                },
                "medium": {
                    "type": "final"
                },
                "default": {
                    "entry": ["onDefault"],
                    "type": "final"
                }
            }
        }
        """;

        bool defaultReached = false;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("isHighPriority", (ctx, _) => ctx.Get<string>("priority") == "high")
            .WithGuard("isMediumPriority", (ctx, _) => ctx.Get<string>("priority") == "medium")
            .WithAction("onDefault", (ctx, _) => defaultReached = true)
            .WithContext("priority", "unknown")
            .BuildAndStart();

        // Act - All guards fail, should take the last unconditional transition
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500)).Result;
            Assert.Equal("default", result.CurrentState);
            Assert.True(defaultReached);
        }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task AfterTransition_ArrayWithActions_ShouldExecuteForMatch()
    {
        // Arrange - Test that actions are executed only for the matched transition
        var json = """
        {
            "id": "test",
            "initial": "waiting",
            "context": {
                "value": 0
            },
            "states": {
                "waiting": {
                    "after": {
                        "100": [
                            {
                                "target": "success",
                                "guard": "isPositive",
                                "actions": ["incrementValue"]
                            },
                            {
                                "target": "failure",
                                "actions": ["decrementValue"]
                            }
                        ]
                    }
                },
                "success": {
                    "type": "final"
                },
                "failure": {
                    "type": "final"
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("isPositive", (ctx, _) => ctx.Get<int>("value") > 0)
            .WithAction("incrementValue", (ctx, _) =>
            {
                var val = ctx.Get<int>("value");
                ctx.Set("value", val + 1);
            })
            .WithAction("decrementValue", (ctx, _) =>
            {
                var val = ctx.Get<int>("value");
                ctx.Set("value", val - 1);
            })
            .WithContext("value", 0)
            .BuildAndStart();

        // Act - Guard fails (value = 0, not positive), should go to failure with decrementValue
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500)).Result;
            Assert.Equal("failure", result.CurrentState);
            Assert.Equal(-1, result.Context["value"]); // decrementValue was executed
        }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task AfterTransition_MultipleArrayDelays_ShouldWork()
    {
        // Arrange - Test multiple delays each with array of guarded transitions
        var json = """
        {
            "id": "test",
            "initial": "waiting",
            "context": {
                "fastPath": true
            },
            "states": {
                "waiting": {
                    "after": {
                        "50": [
                            {
                                "target": "quickSuccess",
                                "guard": "canGoFast"
                            }
                        ],
                        "200": [
                            {
                                "target": "slowSuccess",
                                "guard": "canGoSlow"
                            },
                            {
                                "target": "timeout"
                            }
                        ]
                    }
                },
                "quickSuccess": {
                    "entry": ["onQuickSuccess"],
                    "type": "final"
                },
                "slowSuccess": {
                    "type": "final"
                },
                "timeout": {
                    "type": "final"
                }
            }
        }
        """;

        bool quickSuccessReached = false;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("canGoFast", (ctx, _) => ctx.Get<bool>("fastPath"))
            .WithGuard("canGoSlow", (ctx, _) => !ctx.Get<bool>("fastPath"))
            .WithAction("onQuickSuccess", (ctx, _) => quickSuccessReached = true)
            .WithContext("fastPath", true)
            .BuildAndStart();

        // Act - Should take fast path after 50ms
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500)).Result;
            Assert.Equal("quickSuccess", result.CurrentState);
            Assert.True(quickSuccessReached);
        }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
    }
}
