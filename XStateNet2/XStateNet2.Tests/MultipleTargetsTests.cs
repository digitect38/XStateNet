using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for multiple targets feature
/// Multiple targets allow a single event to transition multiple parallel regions simultaneously
/// </summary>
public class MultipleTargetsTests : XStateTestKit
{

    [Fact]
    public void SingleTarget_ShouldTransitionOneRegion()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "region1": {
                    "initial": "state1",
                    "states": {
                        "state1": {
                            "on": {
                                "EVENT": "state2"
                            }
                        },
                        "state2": {}
                    }
                },
                "region2": {
                    "initial": "stateA",
                    "states": {
                        "stateA": {},
                        "stateB": {}
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Initial state
        WaitForState(machine, s => s.CurrentState.Contains("region1.state1") && s.CurrentState.Contains("region2.stateA"),
            "initial state with region1.state1 and region2.stateA");

        // Act
        SendEventAndWait(machine, "EVENT",
            s => s.CurrentState.Contains("region1.state2") && s.CurrentState.Contains("region2.stateA"),
            "region1 transitioned to state2, region2 unchanged at stateA");
    }

    [Fact]
    public void MultipleTargets_ShouldTransitionMultipleRegions()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "type": "parallel",
            "on": {
                "RESET_ALL": {
                    "target": [
                        ".region1.state1",
                        ".region2.stateA",
                        ".region3.initial"
                    ]
                }
            },
            "states": {
                "region1": {
                    "initial": "state1",
                    "states": {
                        "state1": {
                            "on": { "NEXT": "state2" }
                        },
                        "state2": {}
                    }
                },
                "region2": {
                    "initial": "stateA",
                    "states": {
                        "stateA": {
                            "on": { "NEXT": "stateB" }
                        },
                        "stateB": {}
                    }
                },
                "region3": {
                    "initial": "initial",
                    "states": {
                        "initial": {
                            "on": { "NEXT": "final" }
                        },
                        "final": {}
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Initial state
        WaitForState(machine,
            s => s.CurrentState.Contains("region1.state1") && s.CurrentState.Contains("region2.stateA") && s.CurrentState.Contains("region3.initial"),
            "all regions in initial states");

        // Move all regions to their second states
        SendEventAndWait(machine, "NEXT",
            s => s.CurrentState.Contains("region1.state2") && s.CurrentState.Contains("region2.stateB") && s.CurrentState.Contains("region3.final"),
            "all regions moved to second states");

        // Act - Reset all regions using multiple targets
        SendEventAndWait(machine, "RESET_ALL",
            s => s.CurrentState.Contains("region1.state1") && s.CurrentState.Contains("region2.stateA") && s.CurrentState.Contains("region3.initial"),
            "all regions reset to initial states");
    }

    //[Fact(Skip = "Multiple targets feature not yet implemented in XStateNet2 core engine")]
    [Fact]
    public void MultipleTargets_InNestedParallel_ShouldWork()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "active",
            "states": {
                "active": {
                    "type": "parallel",
                    "on": {
                        "EMERGENCY": {
                            "target": [
                                ".left.error",
                                ".right.stopped"
                            ]
                        }
                    },
                    "states": {
                        "left": {
                            "initial": "idle",
                            "states": {
                                "idle": {
                                    "on": { "START": "running" }
                                },
                                "running": {},
                                "error": {}
                            }
                        },
                        "right": {
                            "initial": "waiting",
                            "states": {
                                "waiting": {
                                    "on": { "START": "processing" }
                                },
                                "processing": {},
                                "stopped": {}
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Start both regions
        SendEventAndWait(machine, "START",
            s => s.CurrentState.Contains("left.running") && s.CurrentState.Contains("right.processing"),
            "both regions running");

        // Act - Emergency stop with multiple targets
        SendEventAndWait(machine, "EMERGENCY",
            s => s.CurrentState.Contains("left.error") && s.CurrentState.Contains("right.stopped"),
            "both regions in error/stopped states");
    }

    [Fact]
    public void MultipleTargets_WithActions_ShouldExecuteActions()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "type": "parallel",
            "on": {
                "RESET_ALL": {
                    "target": [".region1.state1", ".region2.stateA"],
                    "actions": ["resetAction"]
                }
            },
            "states": {
                "region1": {
                    "initial": "state1",
                    "states": {
                        "state1": {
                            "on": { "NEXT": "state2" }
                        },
                        "state2": {}
                    }
                },
                "region2": {
                    "initial": "stateA",
                    "states": {
                        "stateA": {
                            "on": { "NEXT": "stateB" }
                        },
                        "stateB": {}
                    }
                }
            }
        }
        """;

        bool resetExecuted = false;
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("resetAction", (ctx, _) => resetExecuted = true)
            .BuildAndStart();

        // Move both regions forward
        SendEventAndWait(machine, "NEXT",
            s => s.CurrentState.Contains("region1.state2") && s.CurrentState.Contains("region2.stateB"),
            "both regions in second states");

        // Act - Reset with action
        SendEventAndWait(machine, "RESET_ALL",
            s => s.CurrentState.Contains("region1.state1") && s.CurrentState.Contains("region2.stateA"),
            "both regions reset");

        // Assert action was executed
        Assert.True(resetExecuted);
    }

    [Fact]
    public void MultipleTargets_WithAbsolutePaths_ShouldWork()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "type": "parallel",
            "on": {
                "RESET_ALL": {
                    "target": ["#test.region1.state1", "#test.region2.stateA"]
                }
            },
            "states": {
                "region1": {
                    "initial": "state1",
                    "states": {
                        "state1": {
                            "on": { "NEXT": "state2" }
                        },
                        "state2": {}
                    }
                },
                "region2": {
                    "initial": "stateA",
                    "states": {
                        "stateA": {
                            "on": { "NEXT": "stateB" }
                        },
                        "stateB": {}
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Move regions forward
        SendEventAndWait(machine, "NEXT",
            s => s.CurrentState.Contains("region1.state2") && s.CurrentState.Contains("region2.stateB"),
            "both regions in second states");

        // Act - Reset using absolute paths
        SendEventAndWait(machine, "RESET_ALL",
            s => s.CurrentState.Contains("region1.state1") && s.CurrentState.Contains("region2.stateA"),
            "both regions reset using absolute paths");
    }

    [Fact]
    public void MultipleTargets_CrossingParallelBoundary_ShouldExitParallel()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "parallel",
            "states": {
                "parallel": {
                    "type": "parallel",
                    "on": {
                        "EXIT": {
                            "target": "#test.done"
                        }
                    },
                    "states": {
                        "region1": {
                            "initial": "state1",
                            "states": {
                                "state1": {}
                            }
                        },
                        "region2": {
                            "initial": "stateA",
                            "states": {
                                "stateA": {}
                            }
                        }
                    }
                },
                "done": {
                    "type": "final"
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Initial parallel state
        WaitForState(machine,
            s => s.CurrentState.Contains("region1.state1") && s.CurrentState.Contains("region2.stateA"),
            "parallel state active");

        // Act - Exit parallel state
        SendEventAndWait(machine, "EXIT",
            s => s.CurrentState == "done",
            "exited parallel state to done");
    }
}
