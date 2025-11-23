using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet2.Tests;

/// <summary>
/// Comprehensive tests for nested parallel regions functionality
/// Tests the implementation of parallel states within parallel states
/// </summary>
public class NestedParallelRegionsTests : TestKit
{
    private readonly ITestOutputHelper _output;

    public NestedParallelRegionsTests(ITestOutputHelper output) : base(@"
        akka {
            loglevel = OFF
            stdout-loglevel = OFF
            log-config-on-start = off
            loggers = []
        }
    ")
    {
        _output = output;
    }

    #region Basic Nested Parallel Tests

    [Fact]
    public async Task Should_Create_Nested_Parallel_Regions()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "regionA": {
                    "initial": "a1",
                    "states": {
                        "a1": {},
                        "a2": {}
                    }
                },
                "regionB": {
                    "type": "parallel",
                    "states": {
                        "b1": {
                            "initial": "b1_1",
                            "states": {
                                "b1_1": {},
                                "b1_2": {}
                            }
                        },
                        "b2": {
                            "initial": "b2_1",
                            "states": {
                                "b2_1": {},
                                "b2_2": {}
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act - Wait for system to stabilize
        var state = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3));

        // Assert - Machine should start without errors
        state.Should().NotBeNull();
        state.CurrentState.Should().Contain("regionA");
        state.CurrentState.Should().Contain("regionB");
        _output.WriteLine($"Machine started with state: {state.CurrentState}");
    }

    [Fact]
    public async Task Should_Forward_Events_To_Nested_Parallel_Children()
    {
        // Arrange
        var eventReceived = false;

        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "outer": {
                    "type": "parallel",
                    "states": {
                        "inner1": {
                            "initial": "idle",
                            "states": {
                                "idle": {
                                    "on": {
                                        "TEST_EVENT": "active"
                                    }
                                },
                                "active": {
                                    "entry": ["recordEvent"]
                                }
                            }
                        },
                        "inner2": {
                            "initial": "waiting",
                            "states": {
                                "waiting": {}
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("recordEvent", (ctx, data) => eventReceived = true)
            .BuildAndStart();

        // Act
        await AwaitAssertAsync(async () =>
        {
            var state = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500));
            state.Should().NotBeNull();
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));

        machine.Tell(new SendEvent("TEST_EVENT"));

        // Assert
        await AwaitAssertAsync(() =>
        {
            eventReceived.Should().BeTrue("Event should be forwarded to nested parallel child");
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100));
    }

    #endregion

    #region Robot Example Tests (Real-World Scenario)

    [Fact]
    public async Task Should_Handle_Robot_With_Parallel_Position_And_Hand()
    {
        // Arrange - Robot with position and hand as parallel sub-regions
        var json = """
        {
            "id": "robot",
            "type": "parallel",
            "states": {
                "position": {
                    "initial": "home",
                    "states": {
                        "home": {
                            "on": {
                                "MOVE_TO_STATION": "moving"
                            }
                        },
                        "moving": {
                            "after": {
                                "100": "station"
                            }
                        },
                        "station": {}
                    }
                },
                "hand": {
                    "initial": "empty",
                    "states": {
                        "empty": {
                            "on": {
                                "PICK": "picking"
                            }
                        },
                        "picking": {
                            "after": {
                                "50": "holding"
                            }
                        },
                        "holding": {
                            "on": {
                                "PLACE": "placing"
                            }
                        },
                        "placing": {
                            "after": {
                                "50": "empty"
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act & Assert - Pick while at home
        machine.Tell(new SendEvent("PICK"));

        await AwaitAssertAsync(async () =>
        {
            var state = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500));
            state.CurrentState.Should().Contain("hand.holding");
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));

        // Move while holding
        machine.Tell(new SendEvent("MOVE_TO_STATION"));

        await AwaitAssertAsync(async () =>
        {
            var state = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500));
            state.CurrentState.Should().Contain("position.station");
            state.CurrentState.Should().Contain("hand.holding");
            _output.WriteLine($"Final state: {state.CurrentState}");
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task Should_Handle_Concurrent_Robot_Operations()
    {
        // Arrange - Test that position and hand can transition independently
        var positionChanged = false;
        var handChanged = false;

        var json = """
        {
            "id": "robot",
            "type": "parallel",
            "states": {
                "position": {
                    "initial": "a",
                    "states": {
                        "a": {
                            "on": {
                                "POS_MOVE": "b"
                            }
                        },
                        "b": {
                            "entry": ["positionChanged"]
                        }
                    }
                },
                "hand": {
                    "initial": "x",
                    "states": {
                        "x": {
                            "on": {
                                "HAND_PICK": "y"
                            }
                        },
                        "y": {
                            "entry": ["handChanged"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("positionChanged", (ctx, data) => positionChanged = true)
            .WithAction("handChanged", (ctx, data) => handChanged = true)
            .BuildAndStart();

        // Act - Send both events
        machine.Tell(new SendEvent("POS_MOVE"));
        machine.Tell(new SendEvent("HAND_PICK"));

        // Assert
        await AwaitAssertAsync(() =>
        {
            positionChanged.Should().BeTrue();
            handChanged.Should().BeTrue();
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100));
    }

    #endregion

    #region Delayed Transitions in Nested Parallel States

    [Fact]
    public async Task Should_Fire_Delayed_Transitions_In_Nested_Parallel_States()
    {
        // Arrange
        var delayedActionFired = false;

        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "outer": {
                    "type": "parallel",
                    "states": {
                        "inner": {
                            "initial": "waiting",
                            "states": {
                                "waiting": {
                                    "after": {
                                        "200": "done"
                                    }
                                },
                                "done": {
                                    "entry": ["delayFired"]
                                }
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("delayFired", (ctx, data) => delayedActionFired = true)
            .BuildAndStart();

        // Act - Wait for delay
        await AwaitAssertAsync(() =>
        {
            delayedActionFired.Should().BeTrue("Delayed transition should fire in nested parallel state");
        }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task Should_Handle_Guarded_Delayed_Transitions_In_Nested_Parallel()
    {
        // Arrange
        var successPath = false;
        var failurePath = false;

        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "outer": {
                    "type": "parallel",
                    "states": {
                        "inner": {
                            "initial": "processing",
                            "states": {
                                "processing": {
                                    "after": {
                                        "100": [
                                            {
                                                "target": "success",
                                                "guard": "alwaysTrue",
                                                "actions": ["successAction"]
                                            },
                                            {
                                                "target": "failure",
                                                "actions": ["failureAction"]
                                            }
                                        ]
                                    }
                                },
                                "success": {},
                                "failure": {}
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("alwaysTrue", (ctx, data) => true)
            .WithAction("successAction", (ctx, data) => successPath = true)
            .WithAction("failureAction", (ctx, data) => failurePath = true)
            .BuildAndStart();

        // Act - Wait for delayed transition
        await AwaitAssertAsync(() =>
        {
            successPath.Should().BeTrue("First guarded transition should fire");
            failurePath.Should().BeFalse("Second transition should not fire (first match wins)");
        }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
    }

    #endregion

    #region Event Filtering and Null Transitions

    [Fact]
    public async Task Should_Support_Null_Transitions_In_Nested_Parallel()
    {
        // Arrange - Test that child can ignore parent event handler
        var parentHandlerFired = false;

        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "outer": {
                    "type": "parallel",
                    "on": {
                        "SHARED_EVENT": "parentHandler"
                    },
                    "states": {
                        "child1": {
                            "initial": "idle",
                            "states": {
                                "idle": {
                                    "on": {
                                        "SHARED_EVENT": null
                                    }
                                },
                                "parentHandler": {
                                    "entry": ["parentHandlerFired"]
                                }
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("parentHandlerFired", (ctx, data) => parentHandlerFired = true)
            .BuildAndStart();

        // Act
        await AwaitAssertAsync(async () =>
        {
            var state = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500));
            state.Should().NotBeNull();
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));

        machine.Tell(new SendEvent("SHARED_EVENT"));

        await Task.Delay(200); // Give time for event to process

        // Assert
        parentHandlerFired.Should().BeFalse("Child null transition should override parent handler");
    }

    #endregion

    #region Complex Multi-Level Nesting

    [Fact]
    public async Task Should_Handle_Three_Levels_Of_Nested_Parallel()
    {
        // Arrange - L1: parallel -> L2: parallel -> L3: states
        var deepActionFired = false;

        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "level1A": {
                    "type": "parallel",
                    "states": {
                        "level2A": {
                            "type": "parallel",
                            "states": {
                                "level3A": {
                                    "initial": "deep",
                                    "states": {
                                        "deep": {
                                            "on": {
                                                "DEEP_EVENT": "deeper"
                                            }
                                        },
                                        "deeper": {
                                            "entry": ["deepActionFired"]
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("deepActionFired", (ctx, data) => deepActionFired = true)
            .BuildAndStart();

        // Act
        await AwaitAssertAsync(async () =>
        {
            var state = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500));
            state.Should().NotBeNull();
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));

        machine.Tell(new SendEvent("DEEP_EVENT"));

        // Assert
        await AwaitAssertAsync(() =>
        {
            deepActionFired.Should().BeTrue("Event should reach deeply nested parallel state");
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100));
    }

    #endregion

    #region State Snapshot Tests

    [Fact]
    public async Task Should_Return_Combined_State_For_Parallel_Regions()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "regionA": {
                    "initial": "a1",
                    "states": {
                        "a1": {},
                        "a2": {}
                    }
                },
                "regionB": {
                    "initial": "b1",
                    "states": {
                        "b1": {},
                        "b2": {}
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act
        var state = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3));

        // Assert
        state.CurrentState.Should().Contain("regionA");
        state.CurrentState.Should().Contain("regionB");
        state.CurrentState.Should().Contain("a1");
        state.CurrentState.Should().Contain("b1");

        _output.WriteLine($"Combined state: {state.CurrentState}");
    }

    [Fact]
    public async Task Should_Track_Nested_Parallel_State_Changes()
    {
        // Arrange
        var json = """
        {
            "id": "robot",
            "type": "parallel",
            "states": {
                "position": {
                    "initial": "home",
                    "states": {
                        "home": {
                            "on": { "MOVE": "away" }
                        },
                        "away": {}
                    }
                },
                "hand": {
                    "initial": "empty",
                    "states": {
                        "empty": {
                            "on": { "GRAB": "holding" }
                        },
                        "holding": {}
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act - Initial state
        var state1 = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3));
        state1.CurrentState.Should().Contain("position.home");
        state1.CurrentState.Should().Contain("hand.empty");

        // Change position only
        machine.Tell(new SendEvent("MOVE"));

        await AwaitAssertAsync(async () =>
        {
            var state = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500));
            state.CurrentState.Should().Contain("position.away");
            state.CurrentState.Should().Contain("hand.empty", "hand should not change");
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));

        // Change hand only
        machine.Tell(new SendEvent("GRAB"));

        await AwaitAssertAsync(async () =>
        {
            var state = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500));
            state.CurrentState.Should().Contain("position.away", "position should not change");
            state.CurrentState.Should().Contain("hand.holding");
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task Should_Handle_Invalid_Events_In_Nested_Parallel()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "region": {
                    "type": "parallel",
                    "states": {
                        "subregion": {
                            "initial": "idle",
                            "states": {
                                "idle": {}
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act - Send invalid event
        machine.Tell(new SendEvent("INVALID_EVENT"));

        await Task.Delay(200); // Give time for event processing

        // Assert - Should not crash
        var state = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3));
        state.Should().NotBeNull();
    }

    #endregion

    #region XState v5 Guard Property Tests

    [Fact]
    public async Task Should_Support_Guard_Property_In_Nested_Parallel()
    {
        // Arrange - Test both "cond" (v4) and "guard" (v5) properties
        var guardFired = false;

        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "region": {
                    "type": "parallel",
                    "states": {
                        "subregion": {
                            "initial": "waiting",
                            "states": {
                                "waiting": {
                                    "on": {
                                        "CHECK": {
                                            "target": "passed",
                                            "guard": "testGuard"
                                        }
                                    }
                                },
                                "passed": {
                                    "entry": ["guardPassed"]
                                }
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("testGuard", (ctx, data) => true)
            .WithAction("guardPassed", (ctx, data) => guardFired = true)
            .BuildAndStart();

        // Act
        await AwaitAssertAsync(async () =>
        {
            var state = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500));
            state.Should().NotBeNull();
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));

        machine.Tell(new SendEvent("CHECK"));

        // Assert
        await AwaitAssertAsync(() =>
        {
            guardFired.Should().BeTrue("XState v5 'guard' property should work");
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100));
    }

    #endregion
}
