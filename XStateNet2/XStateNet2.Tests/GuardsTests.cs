using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for guards (conditional transitions) and in-state conditions
/// Guards determine whether a transition should be taken based on conditions
/// </summary>
public class GuardsTests : TestKit
{
    [Fact]
    public async Task Guard_AllowsTransitionWhenTrue()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "value": 10
            },
            "states": {
                "idle": {
                    "on": {
                        "CHECK": {
                            "target": "active",
                            "cond": "isHighValue"
                        }
                    }
                },
                "active": {
                    "entry": ["onActive"]
                }
            }
        }
        """;

        bool activeEntered = false;
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("isHighValue", (ctx, _) => ctx.Get<int>("value") > 5)
            .WithAction("onActive", (ctx, _) => activeEntered = true)
            .BuildAndStart();

        // Act
        machine.Tell(new SendEvent("CHECK"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Guard passes, transition should occur
        Assert.Equal("active", snapshot.CurrentState);
        Assert.True(activeEntered);
    }

    [Fact]
    public async Task Guard_BlocksTransitionWhenFalse()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "value": 3
            },
            "states": {
                "idle": {
                    "on": {
                        "CHECK": {
                            "target": "active",
                            "cond": "isHighValue"
                        }
                    }
                },
                "active": {
                    "entry": ["onActive"]
                }
            }
        }
        """;

        bool activeEntered = false;
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("isHighValue", (ctx, _) => ctx.Get<int>("value") > 5)
            .WithAction("onActive", (ctx, _) => activeEntered = true)
            .BuildAndStart();

        // Act
        machine.Tell(new SendEvent("CHECK"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Guard fails, should stay in idle
        Assert.Equal("idle", snapshot.CurrentState);
        Assert.False(activeEntered);
    }

    [Fact]
    public async Task Guard_AccessesEventData()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "LOGIN": {
                            "target": "authenticated",
                            "cond": "isValidUser"
                        }
                    }
                },
                "authenticated": {
                    "entry": ["onAuthenticated"]
                }
            }
        }
        """;

        bool authenticated = false;
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("isValidUser", (ctx, eventData) =>
            {
                // Check event data for valid credentials
                if (eventData is IDictionary<string, object> data)
                {
                    return data.ContainsKey("username") && data.ContainsKey("password");
                }
                return false;
            })
            .WithAction("onAuthenticated", (ctx, _) => authenticated = true)
            .BuildAndStart();

        // Act - Send with invalid data
        machine.Tell(new SendEvent("LOGIN", new { username = "test" }));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Equal("idle", snapshot.CurrentState);
        Assert.False(authenticated);

        // Act - Send with valid data
        machine.Tell(new SendEvent("LOGIN", new Dictionary<string, object>
        {
            ["username"] = "test",
            ["password"] = "pass"
        }));
        snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Should transition with valid data
        Assert.Equal("authenticated", snapshot.CurrentState);
        Assert.True(authenticated);
    }

    [Fact]
    public async Task MultipleGuards_FirstMatchingTransitionTaken()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "value": 15
            },
            "states": {
                "idle": {
                    "on": {
                        "CHECK": [
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

        // Act
        machine.Tell(new SendEvent("CHECK"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Should match "isHigh" (first matching guard)
        Assert.Equal("high", snapshot.CurrentState);
    }

    [Fact]
    public async Task GuardWithoutCondition_AlwaysTaken()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "value": 1
            },
            "states": {
                "idle": {
                    "on": {
                        "CHECK": [
                            {
                                "target": "high",
                                "cond": "isHigh"
                            },
                            {
                                "target": "fallback"
                            }
                        ]
                    }
                },
                "high": {},
                "fallback": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("isHigh", (ctx, _) => ctx.Get<int>("value") > 10)
            .BuildAndStart();

        // Act
        machine.Tell(new SendEvent("CHECK"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Should take fallback (no guard = always true)
        Assert.Equal("fallback", snapshot.CurrentState);
    }

    [Fact]
    public async Task Guard_OnAlwaysTransition()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "checking",
            "context": {
                "ready": true
            },
            "states": {
                "checking": {
                    "always": [
                        {
                            "target": "ready",
                            "cond": "isReady"
                        },
                        {
                            "target": "notReady"
                        }
                    ]
                },
                "ready": {
                    "entry": ["onReady"]
                },
                "notReady": {
                    "entry": ["onNotReady"]
                }
            }
        }
        """;

        bool readyEntered = false;
        bool notReadyEntered = false;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("isReady", (ctx, _) => ctx.Get<bool>("ready"))
            .WithAction("onReady", (ctx, _) => readyEntered = true)
            .WithAction("onNotReady", (ctx, _) => notReadyEntered = true)
            .BuildAndStart();

        // Act - Always transition should fire automatically
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal("ready", snapshot.CurrentState);
        Assert.True(readyEntered);
        Assert.False(notReadyEntered);
    }

    [Fact]
    public async Task Guard_OnAfterTransition()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "waiting",
            "context": {
                "canProceed": true
            },
            "states": {
                "waiting": {
                    "after": {
                        "200": {
                            "target": "success",
                            "cond": "canProceed"
                        },
                        "250": "failure"
                    }
                },
                "success": {
                    "entry": ["onSuccess"]
                },
                "failure": {
                    "entry": ["onFailure"]
                }
            }
        }
        """;

        bool successEntered = false;
        bool failureEntered = false;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("canProceed", (ctx, _) => ctx.Get<bool>("canProceed"))
            .WithAction("onSuccess", (ctx, _) => successEntered = true)
            .WithAction("onFailure", (ctx, _) => failureEntered = true)
            .BuildAndStart();

        // Act - Wait for after transition
        await AwaitAssertAsync(() =>
        {
            var result = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1)).Result;
            Assert.True(result.CurrentState == "success" || result.CurrentState == "failure");
        }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));

        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Guard passes, should transition to success
        Assert.Equal("success", snapshot.CurrentState);
        Assert.True(successEntered);
        Assert.False(failureEntered);
    }

    [Fact]
    public async Task Guard_UpdatedByAction_AffectsSubsequentTransition()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "count": 0,
                "canProceed": false
            },
            "states": {
                "idle": {
                    "on": {
                        "INCREMENT": {
                            "actions": ["increment", "checkCount"]
                        },
                        "PROCEED": {
                            "target": "active",
                            "cond": "canProceed"
                        }
                    }
                },
                "active": {
                    "entry": ["onActive"]
                }
            }
        }
        """;

        bool activeEntered = false;
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("increment", (ctx, _) =>
            {
                var count = ctx.Get<int>("count");
                ctx.Set("count", count + 1);
            })
            .WithAction("checkCount", (ctx, _) =>
            {
                var count = ctx.Get<int>("count");
                if (count >= 3)
                {
                    ctx.Set("canProceed", true);
                }
            })
            .WithGuard("canProceed", (ctx, _) => ctx.Get<bool>("canProceed"))
            .WithAction("onActive", (ctx, _) => activeEntered = true)
            .BuildAndStart();

        // Act - Try to proceed (should fail)
        machine.Tell(new SendEvent("PROCEED"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Equal("idle", snapshot.CurrentState);
        Assert.False(activeEntered);

        // Increment count to enable guard
        machine.Tell(new SendEvent("INCREMENT"));
        machine.Tell(new SendEvent("INCREMENT"));
        machine.Tell(new SendEvent("INCREMENT"));

        snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Equal(3, snapshot.Context["count"]);
        Assert.Equal(true, snapshot.Context["canProceed"]);

        // Try to proceed again (should succeed)
        machine.Tell(new SendEvent("PROCEED"));
        snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal("active", snapshot.CurrentState);
        Assert.True(activeEntered);
    }

    [Fact(Skip = "In-state conditions not yet implemented in XStateNet2")]
    public async Task InStateCondition_ChecksParallelRegion()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "type": "parallel",
            "states": {
                "regionA": {
                    "initial": "A1",
                    "states": {
                        "A1": {
                            "on": {
                                "NEXT_A": "A2"
                            }
                        },
                        "A2": {}
                    }
                },
                "regionB": {
                    "initial": "B1",
                    "states": {
                        "B1": {
                            "on": {
                                "NEXT_B": "B2"
                            }
                        },
                        "B2": {
                            "on": {
                                "CHECK": {
                                    "target": "B_complete",
                                    "in": "#test.regionA.A2"
                                }
                            }
                        },
                        "B_complete": {
                            "entry": ["onComplete"]
                        }
                    }
                }
            }
        }
        """;

        bool completeEntered = false;
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("onComplete", (ctx, _) => completeEntered = true)
            .BuildAndStart();

        // Get initial state
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Contains("regionA.A1", snapshot.CurrentState);
        Assert.Contains("regionB.B1", snapshot.CurrentState);

        // Act - Move regionB to B2
        machine.Tell(new SendEvent("NEXT_B"));
        await Task.Delay(100);
        snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Contains("regionB.B2", snapshot.CurrentState);

        // Try CHECK (should fail because regionA is still in A1)
        machine.Tell(new SendEvent("CHECK"));
        await Task.Delay(100);
        snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Contains("regionB.B2", snapshot.CurrentState);
        Assert.False(completeEntered);

        // Move regionA to A2
        machine.Tell(new SendEvent("NEXT_A"));
        await Task.Delay(100);
        snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Contains("regionA.A2", snapshot.CurrentState);

        // Try CHECK again (should succeed now)
        machine.Tell(new SendEvent("CHECK"));
        await Task.Delay(100);
        snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Should transition to B_complete
        Assert.Contains("regionB.B_complete", snapshot.CurrentState);
        Assert.True(completeEntered);
    }

    [Fact]
    public async Task Guard_WithComplexLogic()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "age": 25,
                "hasLicense": true,
                "hasInsurance": true
            },
            "states": {
                "idle": {
                    "on": {
                        "DRIVE": {
                            "target": "driving",
                            "cond": "canDrive"
                        },
                        "WALK": "walking"
                    }
                },
                "driving": {
                    "entry": ["onDriving"]
                },
                "walking": {}
            }
        }
        """;

        bool drivingEntered = false;
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithGuard("canDrive", (ctx, _) =>
            {
                var age = ctx.Get<int>("age");
                var hasLicense = ctx.Get<bool>("hasLicense");
                var hasInsurance = ctx.Get<bool>("hasInsurance");
                return age >= 18 && hasLicense && hasInsurance;
            })
            .WithAction("onDriving", (ctx, _) => drivingEntered = true)
            .BuildAndStart();

        // Act
        machine.Tell(new SendEvent("DRIVE"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - All conditions met
        Assert.Equal("driving", snapshot.CurrentState);
        Assert.True(drivingEntered);
    }
}
