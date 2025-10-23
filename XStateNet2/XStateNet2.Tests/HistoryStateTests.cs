using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for history states (shallow and deep history)
/// History states allow state machines to "remember" the last active state
/// </summary>
public class HistoryStateTests : TestKit
{
    [Fact]
    public async Task ShallowHistory_RemembersLastState()
    {
        // Arrange
        var json = """
        {
            "id": "testMachine",
            "initial": "A",
            "states": {
                "A": {
                    "initial": "A1",
                    "states": {
                        "A1": {
                            "on": { "TO_A2": "A2" }
                        },
                        "A2": {
                            "on": { "TO_A1": "A1" }
                        },
                        "hist": {
                            "type": "history",
                            "history": "shallow"
                        }
                    },
                    "on": { "TO_B": "B" }
                },
                "B": {
                    "on": { "TO_A": "A.hist" }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .BuildAndStart();

        // Act - Use Ask pattern for deterministic testing (no Task.Delay needed)
        machine.Tell(new SendEvent("TO_A2"));
        var beforeLeaving = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Contains("A2", beforeLeaving.CurrentState);

        machine.Tell(new SendEvent("TO_B"));
        var inB = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Contains("B", inB.CurrentState);

        machine.Tell(new SendEvent("TO_A"));
        var afterReturning = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Should restore A2 (shallow history)
        Assert.Contains("A2", afterReturning.CurrentState);
    }

    [Fact]
    public async Task DeepHistory_RemembersNestedState()
    {
        // Arrange
        var json = """
        {
            "id": "testMachine",
            "initial": "A",
            "states": {
                "A": {
                    "initial": "A1",
                    "states": {
                        "hist": {
                            "type": "history",
                            "history": "deep"
                        },
                        "A1": {
                            "initial": "A1a",
                            "states": {
                                "A1a": {
                                    "on": { "TO_A1b": "A1b" }
                                },
                                "A1b": {}
                            }
                        },
                        "A2": {}
                    },
                    "on": {
                        "TO_B": "B"
                    }
                },
                "B": {
                    "on": { "TO_A": "A.hist" },
                    "initial": "B1",
                    "states": {
                        "B1": {},
                        "B2": {}
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .BuildAndStart();

        // Act - Navigate to nested state A.A1.A1b (deterministic, no Task.Delay)
        machine.Tell(new SendEvent("TO_A1b"));
        var beforeLeaving = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Contains("A1b", beforeLeaving.CurrentState);

        // Leave state A
        machine.Tell(new SendEvent("TO_B"));
        var inB = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Contains("B", inB.CurrentState);

        // Return to A via history
        machine.Tell(new SendEvent("TO_A"));
        var afterReturning = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Deep history should restore A.A1.A1b (nested state)
        Assert.Contains("A1b", afterReturning.CurrentState);
    }

    [Fact]
    public async Task HistoryState_WithDefaultTarget_UsesDefaultWhenNoHistory()
    {
        // Arrange
        var json = """
        {
            "id": "testMachine",
            "initial": "B",
            "states": {
                "A": {
                    "initial": "A1",
                    "states": {
                        "A1": {
                            "on": { "TO_A2": "A2" }
                        },
                        "A2": {},
                        "hist": {
                            "type": "history",
                            "history": "shallow",
                            "target": "A2"
                        }
                    },
                    "on": { "TO_B": "B" }
                },
                "B": {
                    "on": { "TO_A": "A.hist" }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .BuildAndStart();

        // Act - Enter A via history without ever being in A before (deterministic)
        machine.Tell(new SendEvent("TO_A"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Should use default target A2 (no history exists)
        Assert.Contains("A2", snapshot.CurrentState);
    }

    [Fact]
    public async Task HistoryState_WorksWithParallelStates()
    {
        // Arrange
        var json = """
        {
            "id": "testMachine",
            "initial": "A",
            "states": {
                "A": {
                    "type": "parallel",
                    "states": {
                        "region1": {
                            "initial": "R1_S1",
                            "states": {
                                "R1_S1": {
                                    "on": { "TO_R1_S2": "R1_S2" }
                                },
                                "R1_S2": {},
                                "hist": {
                                    "type": "history",
                                    "history": "shallow"
                                }
                            }
                        },
                        "region2": {
                            "initial": "R2_S1",
                            "states": {
                                "R2_S1": {
                                    "on": { "TO_R2_S2": "R2_S2" }
                                },
                                "R2_S2": {},
                                "hist": {
                                    "type": "history",
                                    "history": "shallow"
                                }
                            }
                        }
                    },
                    "on": { "TO_B": "B" }
                },
                "B": {
                    "on": { "TO_A": "A" }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .BuildAndStart();

        // Act - Change states in both parallel regions (deterministic)
        machine.Tell(new SendEvent("TO_R1_S2"));
        machine.Tell(new SendEvent("TO_R2_S2"));
        var beforeLeaving = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Contains("R1_S2", beforeLeaving.CurrentState);
        Assert.Contains("R2_S2", beforeLeaving.CurrentState);

        // Leave and return
        machine.Tell(new SendEvent("TO_B"));
        await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        machine.Tell(new SendEvent("TO_A"));
        var afterReturning = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Both regions should restore their history
        Assert.Contains("R1_S2", afterReturning.CurrentState);
        Assert.Contains("R2_S2", afterReturning.CurrentState);
    }
}
