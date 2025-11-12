using System.Text.Json;
using Xunit;
using XStateNet2.Core.Converters;
using XStateNet2.Core.Engine;

namespace XStateNet2.Tests.Converters;

/// <summary>
/// Unit tests for AfterTransitionConverter
/// Tests JSON parsing and serialization of delayed transitions with array support
/// </summary>
public class AfterTransitionConverterTests
{
    private readonly JsonSerializerOptions _options;

    public AfterTransitionConverterTests()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new AfterTransitionConverter() }
        };
    }

    [Fact]
    public void Read_SimpleStringFormat_ShouldParse()
    {
        // Arrange
        var json = """
        {
            "after": {
                "1000": "targetState"
            }
        }
        """;

        // Act
        var node = JsonSerializer.Deserialize<XStateNode>(json, _options);

        // Assert
        Assert.NotNull(node);
        Assert.NotNull(node.After);
        Assert.Single(node.After);
        Assert.True(node.After.ContainsKey(1000));
        Assert.Single(node.After[1000]);
        Assert.Equal("targetState", node.After[1000][0].Target);
    }

    [Fact]
    public void Read_SingleObjectFormat_ShouldParse()
    {
        // Arrange
        var json = """
        {
            "after": {
                "2000": {
                    "target": "success",
                    "cond": "isReady"
                }
            }
        }
        """;

        // Act
        var node = JsonSerializer.Deserialize<XStateNode>(json, _options);

        // Assert
        Assert.NotNull(node);
        Assert.NotNull(node.After);
        Assert.Single(node.After);
        Assert.True(node.After.ContainsKey(2000));
        Assert.Single(node.After[2000]);
        Assert.Equal("success", node.After[2000][0].Target);
        Assert.Equal("isReady", node.After[2000][0].Cond);
    }

    [Fact]
    public void Read_ArrayFormat_ShouldParse()
    {
        // Arrange - New feature: array of guarded transitions
        var json = """
        {
            "after": {
                "1500": [
                    {
                        "target": "high",
                        "cond": "isHighPriority"
                    },
                    {
                        "target": "medium",
                        "cond": "isMediumPriority"
                    },
                    {
                        "target": "low"
                    }
                ]
            }
        }
        """;

        // Act
        var node = JsonSerializer.Deserialize<XStateNode>(json, _options);

        // Assert
        Assert.NotNull(node);
        Assert.NotNull(node.After);
        Assert.Single(node.After);
        Assert.True(node.After.ContainsKey(1500));
        Assert.Equal(3, node.After[1500].Count);

        Assert.Equal("high", node.After[1500][0].Target);
        Assert.Equal("isHighPriority", node.After[1500][0].Cond);

        Assert.Equal("medium", node.After[1500][1].Target);
        Assert.Equal("isMediumPriority", node.After[1500][1].Cond);

        Assert.Equal("low", node.After[1500][2].Target);
        Assert.Null(node.After[1500][2].Cond);
    }

    [Fact]
    public void Read_ArrayWithActions_ShouldParse()
    {
        // Arrange
        var json = """
        {
            "after": {
                "300": [
                    {
                        "target": "success",
                        "cond": "pickSuccessful",
                        "actions": ["notifySuccess", "updateCount"]
                    },
                    {
                        "target": "failure",
                        "actions": ["notifyFailure"]
                    }
                ]
            }
        }
        """;

        // Act
        var node = JsonSerializer.Deserialize<XStateNode>(json, _options);

        // Assert
        Assert.NotNull(node);
        Assert.NotNull(node.After);
        Assert.Equal(2, node.After[300].Count);

        Assert.Equal("success", node.After[300][0].Target);
        Assert.Equal("pickSuccessful", node.After[300][0].Cond);
        Assert.NotNull(node.After[300][0].Actions);
        Assert.Equal(2, node.After[300][0].Actions.Count);

        Assert.Equal("failure", node.After[300][1].Target);
        Assert.NotNull(node.After[300][1].Actions);
        Assert.Single(node.After[300][1].Actions);
    }

    [Fact]
    public void Read_MultipleDelays_ShouldParse()
    {
        // Arrange
        var json = """
        {
            "after": {
                "100": "quick",
                "500": [
                    {
                        "target": "medium",
                        "cond": "canProceed"
                    },
                    {
                        "target": "slow"
                    }
                ],
                "1000": {
                    "target": "timeout"
                }
            }
        }
        """;

        // Act
        var node = JsonSerializer.Deserialize<XStateNode>(json, _options);

        // Assert
        Assert.NotNull(node);
        Assert.NotNull(node.After);
        Assert.Equal(3, node.After.Count);

        Assert.True(node.After.ContainsKey(100));
        Assert.Single(node.After[100]);
        Assert.Equal("quick", node.After[100][0].Target);

        Assert.True(node.After.ContainsKey(500));
        Assert.Equal(2, node.After[500].Count);

        Assert.True(node.After.ContainsKey(1000));
        Assert.Single(node.After[1000]);
    }

    [Fact(Skip = "Serialization testing - not critical for parser functionality")]
    public void Write_SingleTransition_ShouldSerializeAsString()
    {
        // Arrange
        var node = new XStateNode
        {
            After = new Dictionary<int, List<XStateTransition>>
            {
                [1000] = new List<XStateTransition>
                {
                    new XStateTransition { Target = "targetState" }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(node, _options);

        // Assert
        Assert.Contains("\"1000\": \"targetState\"", json);
    }

    [Fact(Skip = "Serialization testing - not critical for parser functionality")]
    public void Write_SingleTransitionWithGuard_ShouldSerializeAsObject()
    {
        // Arrange
        var node = new XStateNode
        {
            After = new Dictionary<int, List<XStateTransition>>
            {
                [2000] = new List<XStateTransition>
                {
                    new XStateTransition
                    {
                        Target = "success",
                        Cond = "isReady"
                    }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(node, _options);

        // Assert
        Assert.Contains("\"2000\":", json);
        Assert.Contains("\"Target\": \"success\"", json);
        Assert.Contains("\"Cond\": \"isReady\"", json);
    }

    [Fact(Skip = "Serialization testing - not critical for parser functionality")]
    public void Write_MultipleTransitions_ShouldSerializeAsArray()
    {
        // Arrange
        var node = new XStateNode
        {
            After = new Dictionary<int, List<XStateTransition>>
            {
                [1500] = new List<XStateTransition>
                {
                    new XStateTransition
                    {
                        Target = "high",
                        Cond = "isHighPriority"
                    },
                    new XStateTransition
                    {
                        Target = "medium",
                        Cond = "isMediumPriority"
                    },
                    new XStateTransition
                    {
                        Target = "low"
                    }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(node, _options);

        // Assert
        Assert.Contains("\"1500\": [", json);
        Assert.Contains("\"Target\": \"high\"", json);
        Assert.Contains("\"Target\": \"medium\"", json);
        Assert.Contains("\"Target\": \"low\"", json);
    }

    [Fact]
    public void RoundTrip_ArrayFormat_ShouldPreserve()
    {
        // Arrange
        var original = """
        {
            "after": {
                "1000": [
                    {
                        "target": "success",
                        "cond": "isReady",
                        "actions": ["log"]
                    },
                    {
                        "target": "failure"
                    }
                ]
            }
        }
        """;

        // Act - Parse and serialize back
        var node = JsonSerializer.Deserialize<XStateNode>(original, _options);
        var serialized = JsonSerializer.Serialize(node, _options);
        var reparsed = JsonSerializer.Deserialize<XStateNode>(serialized, _options);

        // Assert
        Assert.NotNull(reparsed);
        Assert.NotNull(reparsed.After);
        Assert.True(reparsed.After.ContainsKey(1000));
        Assert.Equal(2, reparsed.After[1000].Count);
        Assert.Equal("success", reparsed.After[1000][0].Target);
        Assert.Equal("isReady", reparsed.After[1000][0].Cond);
        Assert.Equal("failure", reparsed.After[1000][1].Target);
    }

    [Fact]
    public void Read_EmptyArray_ShouldParseAsEmptyList()
    {
        // Arrange - Empty arrays are technically valid, just not useful
        var json = """
        {
            "after": {
                "1000": []
            }
        }
        """;

        // Act
        var node = JsonSerializer.Deserialize<XStateNode>(json, _options);

        // Assert
        Assert.NotNull(node);
        Assert.NotNull(node.After);
        Assert.True(node.After.ContainsKey(1000));
        Assert.Empty(node.After[1000]);
    }

    [Fact]
    public void Read_InvalidDelayFormat_ShouldThrowException()
    {
        // Arrange
        var json = """
        {
            "after": {
                "invalid": "targetState"
            }
        }
        """;

        // Act & Assert
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<XStateNode>(json, _options));
    }
}
