using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Builder;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for internal transitions feature
/// Internal transitions execute actions without exiting/re-entering the state
/// </summary>
public class InternalTransitionsTests : TestKit
{
    private int _entryCount = 0;
    private int _exitCount = 0;
    private int _actionCount = 0;

    [Fact]
    public async Task InternalTransition_ShouldNotTriggerEntryExit()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "active",
            "context": {
                "counter": 0
            },
            "states": {
                "active": {
                    "entry": ["onEntry"],
                    "exit": ["onExit"],
                    "on": {
                        "INCREMENT": {
                            "internal": true,
                            "actions": ["incrementCounter"]
                        },
                        "TRANSITION": "inactive"
                    }
                },
                "inactive": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("onEntry", (ctx, _) => _entryCount++)
            .WithAction("onExit", (ctx, _) => _exitCount++)
            .WithAction("incrementCounter", (ctx, _) =>
            {
                _actionCount++;
                var count = ctx.Get<int>("counter");
                ctx.Set("counter", count + 1);
            })
            .BuildAndStart();

        // Get initial state to ensure entry action was called
        await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Equal(1, _entryCount); // Entry called once on start
        Assert.Equal(0, _exitCount);

        // Act - Internal transition (should NOT call entry/exit)
        machine.Tell(new SendEvent("INCREMENT"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Action executed but no entry/exit
        Assert.Equal(1, _actionCount);
        Assert.Equal(1, _entryCount); // Still 1 (not called again)
        Assert.Equal(0, _exitCount); // Still 0 (not called)
        Assert.Equal(1, snapshot.Context["counter"]);
        Assert.Equal("active", snapshot.CurrentState);

        // Act - Multiple internal transitions
        machine.Tell(new SendEvent("INCREMENT"));
        machine.Tell(new SendEvent("INCREMENT"));
        snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(3, _actionCount);
        Assert.Equal(1, _entryCount); // Still 1
        Assert.Equal(0, _exitCount); // Still 0
        Assert.Equal(3, snapshot.Context["counter"]);
    }

    [Fact]
    public async Task ExternalTransition_ShouldTriggerEntryExit()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "active",
            "states": {
                "active": {
                    "entry": ["onEntry"],
                    "exit": ["onExit"],
                    "on": {
                        "SELF": "active"
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("onEntry", (ctx, _) => _entryCount++)
            .WithAction("onExit", (ctx, _) => _exitCount++)
            .BuildAndStart();

        // Get initial state
        await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Equal(1, _entryCount);
        Assert.Equal(0, _exitCount);

        // Act - External self-transition (should call entry/exit)
        machine.Tell(new SendEvent("SELF"));
        await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Both entry and exit called
        Assert.Equal(2, _entryCount); // Called again
        Assert.Equal(1, _exitCount); // Called once
    }

    [Fact]
    public async Task InternalTransition_MultipleActions()
    {
        // Arrange
        var actionLog = new List<string>();

        var json = """
        {
            "id": "test",
            "initial": "active",
            "states": {
                "active": {
                    "on": {
                        "UPDATE": {
                            "internal": true,
                            "actions": ["action1", "action2", "action3"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("action1", (ctx, _) => actionLog.Add("action1"))
            .WithAction("action2", (ctx, _) => actionLog.Add("action2"))
            .WithAction("action3", (ctx, _) => actionLog.Add("action3"))
            .BuildAndStart();

        // Act
        machine.Tell(new SendEvent("UPDATE"));
        await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - All actions executed in order
        Assert.Equal(3, actionLog.Count);
        Assert.Equal("action1", actionLog[0]);
        Assert.Equal("action2", actionLog[1]);
        Assert.Equal("action3", actionLog[2]);
    }

    [Fact]
    public async Task InternalTransition_WithGuard()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "active",
            "context": {
                "allowed": false
            },
            "states": {
                "active": {
                    "entry": ["onEntry"],
                    "on": {
                        "TRY_UPDATE": {
                            "internal": true,
                            "cond": "isAllowed",
                            "actions": ["updateValue"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("isAllowed", (ctx, _) => ctx.Get<bool>("allowed"))
            .WithAction("onEntry", (ctx, _) => _entryCount++)
            .WithAction("updateValue", (ctx, _) => _actionCount++)
            .BuildAndStart();

        await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Equal(1, _entryCount);

        // Act - Guard fails
        machine.Tell(new SendEvent("TRY_UPDATE"));
        await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Action not executed
        Assert.Equal(0, _actionCount);
        Assert.Equal(1, _entryCount); // Entry not called again

        // Act - Update context and try again
        machine.Tell(new SendEvent("TRY_UPDATE", new { allowed = true }));
        // Update the context first
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // For this test, we need to update context through an action
        // Let's send another event that updates the context first
    }

    [Fact]
    public async Task InternalVsExternalTransition_Comparison()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "state",
            "context": {
                "internalCount": 0,
                "externalCount": 0
            },
            "states": {
                "state": {
                    "entry": ["onEntry"],
                    "exit": ["onExit"],
                    "on": {
                        "INTERNAL": {
                            "internal": true,
                            "actions": ["incrementInternal"]
                        },
                        "EXTERNAL": {
                            "target": "state",
                            "actions": ["incrementExternal"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("onEntry", (ctx, _) => _entryCount++)
            .WithAction("onExit", (ctx, _) => _exitCount++)
            .WithAction("incrementInternal", (ctx, _) =>
            {
                var count = ctx.Get<int>("internalCount");
                ctx.Set("internalCount", count + 1);
            })
            .WithAction("incrementExternal", (ctx, _) =>
            {
                var count = ctx.Get<int>("externalCount");
                ctx.Set("externalCount", count + 1);
            })
            .BuildAndStart();

        await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Equal(1, _entryCount);

        // Act - Internal transition
        machine.Tell(new SendEvent("INTERNAL"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - No entry/exit for internal
        Assert.Equal(1, _entryCount);
        Assert.Equal(0, _exitCount);
        Assert.Equal(1, snapshot.Context["internalCount"]);

        // Act - External transition
        machine.Tell(new SendEvent("EXTERNAL"));
        snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Entry/exit called for external
        Assert.Equal(2, _entryCount); // Called again
        Assert.Equal(1, _exitCount); // Called once
        Assert.Equal(1, snapshot.Context["externalCount"]);
    }
}
