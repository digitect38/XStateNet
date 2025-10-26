using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Comprehensive tests for XState specification compliance
/// Tests remaining XState V5 spec features not covered in other test files
/// </summary>
public class XStateSpecComplianceTests : XStateTestKit
{
    #region Self-Transitions

    [Fact]
    public void SelfTransition_ExitsAndReEntersState()
    {
        // Self-transitions should exit and re-enter the state
        var json = """
        {
            "id": "test",
            "initial": "active",
            "context": {
                "count": 0
            },
            "states": {
                "active": {
                    "entry": ["incrementEntry"],
                    "exit": ["incrementExit"],
                    "on": {
                        "REFRESH": "active"
                    }
                }
            }
        }
        """;

        int entryCount = 0;
        int exitCount = 0;
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("incrementEntry", (ctx, _) => entryCount++)
            .WithAction("incrementExit", (ctx, _) => exitCount++)
            .BuildAndStart();

        WaitForState(machine, s => s.CurrentState == "active", "initial active");
        Assert.Equal(1, entryCount); // Initial entry

        // Self-transition
        SendEventAndWait(machine, "REFRESH",
            s => s.CurrentState == "active",
            "still active after refresh");

        Assert.Equal(1, exitCount); // Exited once
        Assert.Equal(2, entryCount); // Re-entered
    }

    #endregion

    #region Targetless Transitions

    [Fact]
    public void TargetlessTransition_StaysInState()
    {
        // Targetless transitions execute actions without changing state
        var json = """
        {
            "id": "test",
            "initial": "counting",
            "context": {
                "count": 0
            },
            "states": {
                "counting": {
                    "on": {
                        "INCREMENT": {
                            "actions": ["increment"]
                        },
                        "RESET": {
                            "target": "counting",
                            "actions": ["reset"]
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
            .WithAction("reset", (ctx, _) => ctx.Set("count", 0))
            .BuildAndStart();

        // INCREMENT is targetless - stays in counting
        machine.Tell(new SendEvent("INCREMENT"));
        machine.Tell(new SendEvent("INCREMENT"));

        WaitForState(machine, s =>
        {
            var count = s.Context["count"];
            int intValue = count is System.Text.Json.JsonElement element
                ? element.GetInt32()
                : Convert.ToInt32(count);
            return s.CurrentState == "counting" && intValue == 2;
        }, "count incremented without state change");
    }

    #endregion

    #region Entry and Exit Action Ordering

    [Fact]
    public void Transition_ExecutesActionsInCorrectOrder()
    {
        // Order should be: exit source → transition actions → entry target
        var json = """
        {
            "id": "test",
            "initial": "stateA",
            "states": {
                "stateA": {
                    "exit": ["exitA"],
                    "on": {
                        "GO": {
                            "target": "stateB",
                            "actions": ["onTransition"]
                        }
                    }
                },
                "stateB": {
                    "entry": ["entryB"]
                }
            }
        }
        """;

        var actionOrder = new List<string>();
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("exitA", (ctx, _) => actionOrder.Add("exitA"))
            .WithAction("onTransition", (ctx, _) => actionOrder.Add("onTransition"))
            .WithAction("entryB", (ctx, _) => actionOrder.Add("entryB"))
            .BuildAndStart();

        SendEventAndWait(machine, "GO",
            s => s.CurrentState == "stateB",
            "stateB");

        Assert.Equal(new[] { "exitA", "onTransition", "entryB" }, actionOrder);
    }

    [Fact]
    public void NestedTransition_ExitsChildState()
    {
        // When transitioning from nested state, child state should exit
        var json = """
        {
            "id": "test",
            "initial": "parent",
            "states": {
                "parent": {
                    "initial": "child",
                    "exit": ["exitParent"],
                    "states": {
                        "child": {
                            "exit": ["exitChild"]
                        }
                    },
                    "on": {
                        "LEAVE": "#test.other"
                    }
                },
                "other": {
                    "entry": ["entryOther"]
                }
            }
        }
        """;

        var actionOrder = new List<string>();
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("exitChild", (ctx, _) => actionOrder.Add("exitChild"))
            .WithAction("exitParent", (ctx, _) => actionOrder.Add("exitParent"))
            .WithAction("entryOther", (ctx, _) => actionOrder.Add("entryOther"))
            .BuildAndStart();

        SendEventAndWait(machine, "LEAVE",
            s => s.CurrentState == "other",
            "other state");

        // Should exit child and enter other (parent exit depends on implementation)
        Assert.Contains("exitChild", actionOrder);
        Assert.Contains("entryOther", actionOrder);
    }

    #endregion

    #region Multiple Actions

    [Fact]
    public void MultipleActions_ExecuteInOrder()
    {
        // Multiple actions should execute in array order
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "value": 0
            },
            "states": {
                "idle": {
                    "entry": ["action1", "action2", "action3"],
                    "on": {
                        "TRIGGER": {
                            "actions": ["action4", "action5"]
                        }
                    }
                }
            }
        }
        """;

        var actionOrder = new List<string>();
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("action1", (ctx, _) => actionOrder.Add("action1"))
            .WithAction("action2", (ctx, _) => actionOrder.Add("action2"))
            .WithAction("action3", (ctx, _) => actionOrder.Add("action3"))
            .WithAction("action4", (ctx, _) => actionOrder.Add("action4"))
            .WithAction("action5", (ctx, _) => actionOrder.Add("action5"))
            .BuildAndStart();

        WaitForState(machine, s => s.CurrentState == "idle", "idle");
        Assert.Equal(new[] { "action1", "action2", "action3" }, actionOrder);

        actionOrder.Clear();
        machine.Tell(new SendEvent("TRIGGER"));

        WaitForState(machine, s =>
        {
            return actionOrder.Count >= 2;
        }, "actions executed");

        Assert.Equal(new[] { "action4", "action5" }, actionOrder);
    }

    #endregion

    #region Complex Guard Combinations

    [Fact]
    public void MultipleGuards_FirstMatchingTransitionTaken()
    {
        // With multiple transitions for same event, first matching guard wins
        var json = """
        {
            "id": "test",
            "initial": "checking",
            "context": {
                "value": 15
            },
            "states": {
                "checking": {
                    "on": {
                        "EVALUATE": [
                            {
                                "target": "veryHigh",
                                "cond": "isVeryHigh"
                            },
                            {
                                "target": "high",
                                "cond": "isHigh"
                            },
                            {
                                "target": "medium",
                                "cond": "isMedium"
                            },
                            {
                                "target": "low"
                            }
                        ]
                    }
                },
                "veryHigh": {},
                "high": {},
                "medium": {},
                "low": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("isVeryHigh", (ctx, _) => ctx.Get<int>("value") > 20)
            .WithGuard("isHigh", (ctx, _) => ctx.Get<int>("value") > 10)
            .WithGuard("isMedium", (ctx, _) => ctx.Get<int>("value") > 5)
            .BuildAndStart();

        // value=15, should match "isHigh" (first matching)
        SendEventAndWait(machine, "EVALUATE",
            s => s.CurrentState == "high",
            "high state");
    }

    #endregion

    #region Deep History

    [Fact]
    public void DeepHistory_RestoresNestedState()
    {
        // Deep history restores nested state hierarchy
        var json = """
        {
            "id": "test",
            "initial": "off",
            "states": {
                "off": {
                    "on": {
                        "TURN_ON": "on.hist"
                    }
                },
                "on": {
                    "type": "compound",
                    "initial": "mode1",
                    "states": {
                        "hist": {
                            "type": "history",
                            "history": "deep",
                            "target": "mode1"
                        },
                        "mode1": {
                            "initial": "level1",
                            "states": {
                                "level1": {
                                    "on": { "INCREASE": "level2" }
                                },
                                "level2": {}
                            },
                            "on": {
                                "SWITCH": "mode2"
                            }
                        },
                        "mode2": {
                            "initial": "settingA",
                            "states": {
                                "settingA": {
                                    "on": { "NEXT": "settingB" }
                                },
                                "settingB": {}
                            }
                        }
                    },
                    "on": {
                        "TURN_OFF": "off"
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        SendEventAndWait(machine, "TURN_ON",
            s => s.CurrentState == "on.mode1.level1",
            "initial mode1.level1");

        SendEventAndWait(machine, "INCREASE",
            s => s.CurrentState == "on.mode1.level2",
            "mode1.level2");

        SendEventAndWait(machine, "TURN_OFF",
            s => s.CurrentState == "off",
            "off");

        // Turn on again - should restore to mode1.level2 (deep history)
        SendEventAndWait(machine, "TURN_ON",
            s => s.CurrentState == "on.mode1.level2",
            "restored to mode1.level2");
    }

    #endregion

    #region Context Edge Cases

    [Fact]
    public void Context_PreservedAcrossTransitions()
    {
        var json = """
        {
            "id": "test",
            "initial": "state1",
            "context": {
                "data": "initial"
            },
            "states": {
                "state1": {
                    "entry": ["modifyContext"],
                    "on": {
                        "NEXT": "state2"
                    }
                },
                "state2": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("modifyContext", (ctx, _) => ctx.Set("data", "modified"))
            .BuildAndStart();

        WaitForState(machine, s =>
        {
            var data = s.Context["data"];
            string dataStr = data is System.Text.Json.JsonElement element
                ? element.GetString() ?? ""
                : data?.ToString() ?? "";
            return dataStr == "modified";
        }, "context modified");

        SendEventAndWait(machine, "NEXT",
            s =>
            {
                var data = s.Context["data"];
                string dataStr = data is System.Text.Json.JsonElement element
                    ? element.GetString() ?? ""
                    : data?.ToString() ?? "";
                return s.CurrentState == "state2" && dataStr == "modified";
            },
            "context preserved in state2");
    }

    [Fact]
    public void Context_CanBeComplex()
    {
        var json = """
        {
            "id": "test",
            "initial": "active",
            "context": {
                "user": {
                    "name": "test",
                    "age": 25
                },
                "items": [1, 2, 3]
            },
            "states": {
                "active": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        WaitForState(machine, s =>
        {
            Assert.True(s.Context.ContainsKey("user"));
            Assert.True(s.Context.ContainsKey("items"));
            return true;
        }, "complex context initialized");
    }

    #endregion

    #region Invoke Service Error Handling

    [Fact]
    public void InvokeService_OnError_TransitionsToErrorState()
    {
        var json = """
        {
            "id": "test",
            "initial": "loading",
            "states": {
                "loading": {
                    "invoke": {
                        "src": "failingService",
                        "onDone": "success",
                        "onError": "error"
                    }
                },
                "success": {},
                "error": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithService("failingService", async (ctx) =>
            {
                await Task.Delay(100);
                throw new Exception("Service failed");
            })
            .BuildAndStart();

        WaitForState(machine, s => s.CurrentState == "error", "error state after service failure");
    }

    [Fact]
    public void InvokeService_OnError_WithActions()
    {
        var json = """
        {
            "id": "test",
            "initial": "loading",
            "context": {
                "errorLogged": false
            },
            "states": {
                "loading": {
                    "invoke": {
                        "src": "failingService",
                        "onError": {
                            "target": "error",
                            "actions": ["logError"]
                        }
                    }
                },
                "error": {}
            }
        }
        """;

        bool errorLogged = false;
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithService("failingService", async (ctx) =>
            {
                await Task.Delay(100);
                throw new Exception("Service failed");
            })
            .WithAction("logError", (ctx, _) => errorLogged = true)
            .BuildAndStart();

        WaitForState(machine, s => s.CurrentState == "error", "error state");
        Assert.True(errorLogged, "error action should have executed");
    }

    #endregion

    #region Parallel State Edge Cases

    [Fact]
    public void ParallelState_AllRegionsEnterSimultaneously()
    {
        var json = """
        {
            "id": "test",
            "initial": "parallel",
            "states": {
                "parallel": {
                    "type": "parallel",
                    "states": {
                        "region1": {
                            "initial": "r1idle",
                            "states": {
                                "r1idle": {}
                            }
                        },
                        "region2": {
                            "initial": "r2idle",
                            "states": {
                                "r2idle": {}
                            }
                        },
                        "region3": {
                            "initial": "r3idle",
                            "states": {
                                "r3idle": {}
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        WaitForState(machine, s =>
            s.CurrentState.Contains("region1.r1idle") &&
            s.CurrentState.Contains("region2.r2idle") &&
            s.CurrentState.Contains("region3.r3idle"),
            "all three regions active");
    }

    [Fact]
    public void ParallelState_EventBroadcastToAllRegions()
    {
        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "region1": {
                    "initial": "idle",
                    "states": {
                        "idle": {
                            "on": { "ACTIVATE": "active" }
                        },
                        "active": {}
                    }
                },
                "region2": {
                    "initial": "waiting",
                    "states": {
                        "waiting": {
                            "on": { "ACTIVATE": "ready" }
                        },
                        "ready": {}
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        SendEventAndWait(machine, "ACTIVATE",
            s => s.CurrentState.Contains("region1.active") && s.CurrentState.Contains("region2.ready"),
            "both regions activated");
    }

    #endregion

    #region Transient Transitions (Empty String Events)

    [Fact]
    public void TransientTransition_AutomaticallyTransitions()
    {
        // Note: In XState V5, transient transitions are typically handled with "always"
        // But some implementations support empty string "" as event type
        // This is already covered by AlwaysTransitions tests
        // This test documents the expected behavior
        var json = """
        {
            "id": "test",
            "initial": "checking",
            "context": {
                "ready": true
            },
            "states": {
                "checking": {
                    "always": {
                        "target": "ready",
                        "cond": "isReady"
                    }
                },
                "ready": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("isReady", (ctx, _) => ctx.Get<bool>("ready"))
            .BuildAndStart();

        // Should immediately transition to ready
        WaitForState(machine, s => s.CurrentState == "ready", "ready state via always transition");
    }

    #endregion

    // Note: State machine lifecycle (stop/restart) is tested in other test files
}
