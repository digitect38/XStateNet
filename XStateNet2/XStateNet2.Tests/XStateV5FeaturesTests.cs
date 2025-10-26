using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Comprehensive tests for XState V5 features supported in XStateNet2
/// This test suite demonstrates compatibility with XState V5 specification
/// </summary>
public class XStateV5FeaturesTests : XStateTestKit
{
    #region Multiple Targets

    [Fact]
    public void V5_MultipleTargets_ArrayFormat()
    {
        // XState V5 supports multiple targets as array: "target": ["state1", "state2"]
        var json = """
        {
            "id": "test",
            "type": "parallel",
            "on": {
                "RESET": {
                    "target": [".region1.idle", ".region2.idle"]
                }
            },
            "states": {
                "region1": {
                    "initial": "idle",
                    "states": {
                        "idle": { "on": { "START": "active" } },
                        "active": {}
                    }
                },
                "region2": {
                    "initial": "idle",
                    "states": {
                        "idle": { "on": { "START": "active" } },
                        "active": {}
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Move both regions to active
        SendEventAndWait(machine, "START",
            s => s.CurrentState.Contains("region1.active") && s.CurrentState.Contains("region2.active"),
            "both regions active");

        // Reset both regions using multiple targets
        SendEventAndWait(machine, "RESET",
            s => s.CurrentState.Contains("region1.idle") && s.CurrentState.Contains("region2.idle"),
            "both regions reset to idle");
    }

    [Fact]
    public void V5_MultipleTargets_SingleStringFormat()
    {
        // XState V5 supports single target as string: "target": "state"
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "START": { "target": "active" }
                    }
                },
                "active": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        SendEventAndWait(machine, "START",
            s => s.CurrentState == "active",
            "transitioned to active");
    }

    #endregion

    #region OnDone Transitions

    [Fact]
    public void V5_OnDone_StringFormat()
    {
        // XState V5 supports onDone as string: "onDone": "nextState"
        var json = """
        {
            "id": "test",
            "initial": "loading",
            "states": {
                "loading": {
                    "type": "parallel",
                    "states": {
                        "data": {
                            "initial": "fetching",
                            "states": {
                                "fetching": { "on": { "LOADED": "done" } },
                                "done": { "type": "final" }
                            }
                        },
                        "ui": {
                            "initial": "rendering",
                            "states": {
                                "rendering": { "on": { "RENDERED": "done" } },
                                "done": { "type": "final" }
                            }
                        }
                    },
                    "onDone": "ready"
                },
                "ready": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        WaitForState(machine, s => s.CurrentState.Contains("fetching"), "loading state");

        machine.Tell(new SendEvent("LOADED"));
        machine.Tell(new SendEvent("RENDERED"));

        WaitForState(machine, s => s.CurrentState == "ready", "ready state after both regions complete");
    }

    [Fact]
    public void V5_OnDone_ObjectFormat()
    {
        // XState V5 supports onDone as object: "onDone": { "target": "...", "actions": [...] }
        var json = """
        {
            "id": "test",
            "initial": "loading",
            "context": {
                "completed": false
            },
            "states": {
                "loading": {
                    "type": "parallel",
                    "states": {
                        "task1": {
                            "initial": "running",
                            "states": {
                                "running": { "on": { "DONE": "complete" } },
                                "complete": { "type": "final" }
                            }
                        },
                        "task2": {
                            "initial": "running",
                            "states": {
                                "running": { "on": { "DONE": "complete" } },
                                "complete": { "type": "final" }
                            }
                        }
                    },
                    "onDone": {
                        "target": "success",
                        "actions": ["markCompleted"]
                    }
                },
                "success": {}
            }
        }
        """;

        bool actionExecuted = false;
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("markCompleted", (ctx, _) => actionExecuted = true)
            .BuildAndStart();

        machine.Tell(new SendEvent("DONE"));
        machine.Tell(new SendEvent("DONE"));

        WaitForState(machine, s => s.CurrentState == "success", "success state");
        Assert.True(actionExecuted, "onDone action should have executed");
    }

    #endregion

    #region Always Transitions (Eventless)

    [Fact]
    public void V5_Always_EventlessTransitions()
    {
        // XState V5 supports "always" for eventless transitions
        var json = """
        {
            "id": "test",
            "initial": "checking",
            "context": {
                "value": 10
            },
            "states": {
                "checking": {
                    "always": [
                        {
                            "target": "high",
                            "cond": "isHigh"
                        },
                        {
                            "target": "low"
                        }
                    ]
                },
                "low": {},
                "high": {}
            }
        }
        """;

        // Test 1: High value - should go directly to high
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("isHigh", (ctx, _) => ctx.Get<int>("value") >= 10)
            .BuildAndStart();

        WaitForState(machine, s => s.CurrentState == "high", "high state via always transition");

        // Test 2: Low value - should go to low (default)
        Sys.Stop(machine);
        var json2 = """
        {
            "id": "test2",
            "initial": "checking",
            "context": {
                "value": 3
            },
            "states": {
                "checking": {
                    "always": [
                        {
                            "target": "high",
                            "cond": "isHigh"
                        },
                        {
                            "target": "low"
                        }
                    ]
                },
                "low": {},
                "high": {}
            }
        }
        """;

        machine = factory.FromJson(json2)
            .WithGuard("isHigh", (ctx, _) => ctx.Get<int>("value") >= 10)
            .BuildAndStart();

        WaitForState(machine, s => s.CurrentState == "low", "low state via always transition fallback");
    }

    #endregion

    #region Internal Transitions

    [Fact]
    public void V5_InternalTransitions()
    {
        // XState V5 supports internal transitions that don't exit/re-enter state
        var json = """
        {
            "id": "test",
            "initial": "active",
            "context": {
                "count": 0
            },
            "states": {
                "active": {
                    "entry": ["enterActive"],
                    "exit": ["exitActive"],
                    "on": {
                        "INTERNAL_UPDATE": {
                            "internal": true,
                            "actions": ["increment"]
                        },
                        "EXTERNAL_UPDATE": {
                            "target": "active",
                            "actions": ["increment"]
                        }
                    }
                }
            }
        }
        """;

        int entryCount = 0;
        int exitCount = 0;
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("enterActive", (ctx, _) => entryCount++)
            .WithAction("exitActive", (ctx, _) => exitCount++)
            .WithAction("increment", (ctx, _) =>
            {
                var count = ctx.Get<int>("count");
                ctx.Set("count", count + 1);
            })
            .BuildAndStart();

        WaitForState(machine, s => s.CurrentState == "active", "active state");
        Assert.Equal(1, entryCount); // Entry on start

        // Internal transition - no exit/entry
        machine.Tell(new SendEvent("INTERNAL_UPDATE"));
        WaitForState(machine, s =>
        {
            var value = s.Context["count"];
            int count = value is System.Text.Json.JsonElement element
                ? element.GetInt32()
                : Convert.ToInt32(value);
            return count == 1;
        }, "count incremented");
        Assert.Equal(1, entryCount); // No additional entry
        Assert.Equal(0, exitCount); // No exit

        // External transition - exit and re-entry
        machine.Tell(new SendEvent("EXTERNAL_UPDATE"));
        WaitForState(machine, s =>
        {
            var value = s.Context["count"];
            int count = value is System.Text.Json.JsonElement element
                ? element.GetInt32()
                : Convert.ToInt32(value);
            return count == 2;
        }, "count incremented again");
        Assert.Equal(2, entryCount); // Additional entry
        Assert.Equal(1, exitCount); // Exit occurred
    }

    #endregion

    // Note: "in" condition for guards is already tested in GuardsTests.InStateCondition_ChecksParallelRegion

    #region Final States

    [Fact]
    public void V5_FinalStates_ExplicitTransitions()
    {
        // XState V5 allows final states to have explicit transitions
        var json = """
        {
            "id": "test",
            "initial": "working",
            "states": {
                "working": {
                    "on": { "COMPLETE": "done" }
                },
                "done": {
                    "type": "final",
                    "on": {
                        "RESTART": "working"
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        SendEventAndWait(machine, "COMPLETE",
            s => s.CurrentState == "done",
            "final state");

        // Even though done is final, it can have explicit transitions
        SendEventAndWait(machine, "RESTART",
            s => s.CurrentState == "working",
            "restarted from final state");
    }

    #endregion

    #region History States

    [Fact]
    public void V5_HistoryStates_ShallowHistory()
    {
        // XState V5 supports history states for restoring previous state
        var json = """
        {
            "id": "test",
            "initial": "on",
            "states": {
                "on": {
                    "initial": "low",
                    "states": {
                        "low": { "on": { "INCREASE": "high" } },
                        "high": {},
                        "hist": {
                            "type": "history",
                            "target": "low"
                        }
                    },
                    "on": {
                        "TURN_OFF": "off"
                    }
                },
                "off": {
                    "on": {
                        "TURN_ON": "on.hist"
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        WaitForState(machine, s => s.CurrentState == "on.low", "initial low state");

        SendEventAndWait(machine, "INCREASE",
            s => s.CurrentState == "on.high",
            "high state");

        SendEventAndWait(machine, "TURN_OFF",
            s => s.CurrentState == "off",
            "turned off");

        // Turn back on - should restore to high (history)
        SendEventAndWait(machine, "TURN_ON",
            s => s.CurrentState == "on.high",
            "restored to high via history");
    }

    #endregion

    #region Delayed Transitions (After)

    [Fact]
    public void V5_DelayedTransitions_After()
    {
        // XState V5 supports "after" for time-based transitions
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": { "START": "running" }
                },
                "running": {
                    "after": {
                        "100": "timeout"
                    }
                },
                "timeout": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        SendEventAndWait(machine, "START",
            s => s.CurrentState == "running",
            "running state");

        // Wait for delayed transition
        WaitForState(machine, s => s.CurrentState == "timeout", "timeout state after delay");
    }

    #endregion

    #region Context and Actions

    [Fact]
    public void V5_Context_AssignActions()
    {
        // XState V5 supports context updates via actions
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "count": 0,
                "name": ""
            },
            "states": {
                "idle": {
                    "entry": ["initializeName"],
                    "on": {
                        "INCREMENT": {
                            "actions": ["incrementCount"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("initializeName", (ctx, _) => ctx.Set("name", "Test"))
            .WithAction("incrementCount", (ctx, _) =>
            {
                var count = ctx.Get<int>("count");
                ctx.Set("count", count + 1);
            })
            .BuildAndStart();

        WaitForState(machine, s =>
        {
            var name = s.Context["name"];
            string nameStr = name is System.Text.Json.JsonElement element
                ? element.GetString() ?? ""
                : name?.ToString() ?? "";
            return nameStr == "Test";
        }, "name initialized");

        machine.Tell(new SendEvent("INCREMENT"));
        WaitForState(machine, s =>
        {
            var count = s.Context["count"];
            int countInt = count is System.Text.Json.JsonElement element
                ? element.GetInt32()
                : Convert.ToInt32(count);
            return countInt == 1;
        }, "count incremented");
    }

    #endregion

    #region Absolute Paths

    [Fact]
    public void V5_AbsolutePaths_HashSyntax()
    {
        // XState V5 supports absolute paths with # syntax: "#machineId.stateName"
        var json = """
        {
            "id": "test",
            "initial": "stateA",
            "states": {
                "stateA": {
                    "initial": "nested1",
                    "states": {
                        "nested1": {
                            "on": {
                                "JUMP": "#test.stateB.nested3"
                            }
                        },
                        "nested2": {}
                    }
                },
                "stateB": {
                    "initial": "nested3",
                    "states": {
                        "nested3": {},
                        "nested4": {}
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        WaitForState(machine, s => s.CurrentState == "stateA.nested1", "initial nested state");

        // Jump to deeply nested state using absolute path
        SendEventAndWait(machine, "JUMP",
            s => s.CurrentState == "stateB.nested3",
            "jumped to absolute path");
    }

    #endregion

    #region JSON Format Flexibility

    [Fact]
    public void V5_JSONFormat_FlexibleTransitions()
    {
        // XState V5 supports multiple formats for transitions
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "EVENT1": "state1",
                        "EVENT2": { "target": "state2" },
                        "EVENT3": [
                            { "target": "state3", "cond": "condition1" },
                            { "target": "state4" }
                        ]
                    }
                },
                "state1": {},
                "state2": {},
                "state3": {},
                "state4": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("condition1", (ctx, _) => false)
            .BuildAndStart();

        // String format
        SendEventAndWait(machine, "EVENT1",
            s => s.CurrentState == "state1",
            "string format transition");

        // Object format
        Sys.Stop(machine);
        machine = factory.FromJson(json).WithGuard("condition1", (ctx, _) => false).BuildAndStart("test2");
        SendEventAndWait(machine, "EVENT2",
            s => s.CurrentState == "state2",
            "object format transition");

        // Array format with guards
        Sys.Stop(machine);
        machine = factory.FromJson(json).WithGuard("condition1", (ctx, _) => false).BuildAndStart("test3");
        SendEventAndWait(machine, "EVENT3",
            s => s.CurrentState == "state4",
            "array format with guard fallback");
    }

    #endregion
}
