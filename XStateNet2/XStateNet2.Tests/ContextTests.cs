using Akka.Actor;
using Akka.TestKit.Xunit2;
using System.Text.Json;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for context operations and assign actions
/// Tests reading, writing, and updating state machine context
/// </summary>
public class ContextTests : TestKit
{
    private static long GetContextInt(object? value)
    {
        if (value is JsonElement element)
        {
            return element.GetInt64();
        }
        return Convert.ToInt64(value);
    }

    private static string GetContextString(object? value)
    {
        if (value is JsonElement element)
        {
            return element.GetString() ?? "";
        }
        return value?.ToString() ?? "";
    }

    private static bool GetContextBool(object? value)
    {
        if (value is JsonElement element)
        {
            return element.GetBoolean();
        }
        return Convert.ToBoolean(value);
    }

    [Fact]
    public async Task Context_InitialValues_ShouldBeSet()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "count": 0,
                "name": "test",
                "enabled": true
            },
            "states": {
                "idle": {}
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(0L, GetContextInt(snapshot.Context["count"]));
        Assert.Equal("test", GetContextString(snapshot.Context["name"]));
        Assert.True(GetContextBool(snapshot.Context["enabled"]));
    }

    [Fact]
    public async Task Context_AssignAction_ShouldUpdateValues()
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
                        "UPDATE": {
                            "actions": [{
                                "type": "assign",
                                "assignment": {
                                    "count": 5
                                }
                            }]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act
        machine.Tell(new SendEvent("UPDATE"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(5L, GetContextInt(snapshot.Context["count"]));
    }

    [Fact]
    public async Task Context_MultipleAssigns_ShouldApplyInOrder()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "a": 1,
                "b": 2,
                "c": 3
            },
            "states": {
                "idle": {
                    "on": {
                        "UPDATE": {
                            "actions": [
                                {
                                    "type": "assign",
                                    "assignment": {
                                        "a": 10
                                    }
                                },
                                {
                                    "type": "assign",
                                    "assignment": {
                                        "b": 20
                                    }
                                },
                                {
                                    "type": "assign",
                                    "assignment": {
                                        "c": 30
                                    }
                                }
                            ]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act
        machine.Tell(new SendEvent("UPDATE"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(10L, GetContextInt(snapshot.Context["a"]));
        Assert.Equal(20L, GetContextInt(snapshot.Context["b"]));
        Assert.Equal(30L, GetContextInt(snapshot.Context["c"]));
    }

    [Fact]
    public async Task Context_CustomActions_CanModifyContext()
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
                            "actions": ["increment"]
                        },
                        "DOUBLE": {
                            "actions": ["double"]
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
            .WithAction("double", (ctx, _) =>
            {
                var count = ctx.Get<int>("count");
                ctx.Set("count", count * 2);
            })
            .BuildAndStart();

        // Act - Increment twice
        machine.Tell(new SendEvent("INCREMENT"));
        machine.Tell(new SendEvent("INCREMENT"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        Assert.Equal(2, snapshot.Context["count"]);

        // Act - Double
        machine.Tell(new SendEvent("DOUBLE"));
        snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(4, snapshot.Context["count"]);
    }

    [Fact]
    public async Task Context_WithEventData_ShouldBeAccessible()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "lastValue": null
            },
            "states": {
                "idle": {
                    "on": {
                        "UPDATE": {
                            "actions": ["saveValue"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("saveValue", (ctx, eventData) =>
            {
                // Event data should be accessible here
                ctx.Set("lastValue", eventData);
            })
            .BuildAndStart();

        // Act
        machine.Tell(new SendEvent("UPDATE", new { value = 42 }));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.NotNull(snapshot.Context["lastValue"]);
    }

    [Fact]
    public async Task Context_EntryActions_CanSetContext()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "initialized": false
            },
            "states": {
                "idle": {
                    "entry": ["initialize"],
                    "on": {
                        "START": "active"
                    }
                },
                "active": {
                    "entry": ["activate"]
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("initialize", (ctx, _) => ctx.Set("initialized", true))
            .WithAction("activate", (ctx, _) => ctx.Set("active", true))
            .BuildAndStart();

        // Get initial state
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));
        Assert.Equal(true, snapshot.Context["initialized"]);

        // Act
        machine.Tell(new SendEvent("START"));
        snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(true, snapshot.Context["active"]);
    }

    [Fact]
    public async Task Context_GuardsCanReadContext()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "count": 0,
                "threshold": 5
            },
            "states": {
                "idle": {
                    "on": {
                        "INCREMENT": {
                            "actions": ["increment"]
                        },
                        "CHECK": [
                            {
                                "target": "success",
                                "cond": "isAboveThreshold"
                            },
                            {
                                "target": "failure"
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
            .WithGuard("isAboveThreshold", (ctx, _) =>
            {
                var count = ctx.Get<int>("count");
                var threshold = ctx.Get<int>("threshold");
                return count > threshold;
            })
            .WithAction("increment", (ctx, _) =>
            {
                var count = ctx.Get<int>("count");
                ctx.Set("count", count + 1);
            })
            .BuildAndStart();

        // Act - Increment to 3, check (should fail)
        machine.Tell(new SendEvent("INCREMENT"));
        machine.Tell(new SendEvent("INCREMENT"));
        machine.Tell(new SendEvent("INCREMENT"));
        machine.Tell(new SendEvent("CHECK"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal("failure", snapshot.CurrentState);
    }

    [Fact]
    public async Task Context_ComplexObjects_ShouldBeSupported()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "context": {
                "user": null,
                "items": []
            },
            "states": {
                "idle": {
                    "on": {
                        "SET_USER": {
                            "actions": ["setUser"]
                        },
                        "ADD_ITEM": {
                            "actions": ["addItem"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithAction("setUser", (ctx, _) =>
            {
                ctx.Set("user", new { name = "Alice", age = 30 });
            })
            .WithAction("addItem", (ctx, _) =>
            {
                var items = ctx.Get<List<string>>("items") ?? new List<string>();
                items.Add("item");
                ctx.Set("items", items);
            })
            .BuildAndStart();

        // Act
        machine.Tell(new SendEvent("SET_USER"));
        machine.Tell(new SendEvent("ADD_ITEM"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.NotNull(snapshot.Context["user"]);
        Assert.NotNull(snapshot.Context["items"]);
    }
}
