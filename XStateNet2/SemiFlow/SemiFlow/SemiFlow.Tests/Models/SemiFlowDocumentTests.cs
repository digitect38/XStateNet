using FluentAssertions;
using SemiFlow.Converter.Models;
using System.Text.Json;
using XStateNet2.Core.Engine;
using Xunit;

namespace SemiFlow.Tests.Models;

/// <summary>
/// Tests for SemiFlow document model parsing and validation
/// </summary>
public class SemiFlowDocumentTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Test001_ParseMinimalDocument_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""name"": ""TestWorkflow"",
            ""version"": ""1.0.0"",
            ""lanes"": [
                {
                    ""id"": ""lane1"",
                    ""workflow"": {
                        ""id"": ""wf1"",
                        ""steps"": [
                            {
                                ""id"": ""step1"",
                                ""type"": ""action"",
                                ""action"": ""doSomething""
                            }
                        ]
                    }
                }
            ]
        }";

        // Act
        var doc = JsonSerializer.Deserialize<SemiFlowDocument>(json, _jsonOptions);

        // Assert
        doc.Should().NotBeNull();
        doc!.Name.Should().Be("TestWorkflow");
        doc.Version.Should().Be("1.0.0");
        doc.Lanes.Should().HaveCount(1);
        doc.Lanes[0].Id.Should().Be("lane1");
    }

    [Fact]
    public void Test002_ParseDocumentWithConstants_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test"",
            ""version"": ""1.0"",
            ""constants"": {
                ""max_retries"": 3,
                ""timeout_ms"": 5000
            },
            ""lanes"": [
                {
                    ""id"": ""l1"",
                    ""workflow"": {
                        ""id"": ""w1"",
                        ""steps"": []
                    }
                }
            ]
        }";

        // Act
        var doc = JsonSerializer.Deserialize<SemiFlowDocument>(json, _jsonOptions);

        // Assert
        doc!.Constants.Should().NotBeNull();
        doc.Constants.Should().ContainKey("max_retries");
        doc.Constants!["max_retries"].ToString().Should().Be("3");
    }

    [Fact]
    public void Test003_ParseDocumentWithStations_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test"",
            ""version"": ""1.0"",
            ""stations"": [
                {
                    ""id"": ""robot_1"",
                    ""role"": ""robot"",
                    ""kind"": ""dedicated"",
                    ""capacity"": 1,
                    ""state"": ""idle""
                }
            ],
            ""lanes"": [
                {
                    ""id"": ""l1"",
                    ""workflow"": {
                        ""id"": ""w1"",
                        ""steps"": []
                    }
                }
            ]
        }";

        // Act
        var doc = JsonSerializer.Deserialize<SemiFlowDocument>(json, _jsonOptions);

        // Assert
        doc!.Stations.Should().NotBeNull();
        doc.Stations.Should().HaveCount(1);
        doc.Stations![0].Id.Should().Be("robot_1");
        doc.Stations[0].Role.Should().Be("robot");
        doc.Stations[0].Kind.Should().Be("dedicated");
        doc.Stations[0].Capacity.Should().Be(1);
    }

    [Fact]
    public void Test004_ParseDocumentWithEvents_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test"",
            ""version"": ""1.0"",
            ""events"": [
                {
                    ""name"": ""WAFER_READY"",
                    ""type"": ""wafer"",
                    ""description"": ""Wafer is ready""
                }
            ],
            ""lanes"": [
                {
                    ""id"": ""l1"",
                    ""workflow"": {
                        ""id"": ""w1"",
                        ""steps"": []
                    }
                }
            ]
        }";

        // Act
        var doc = JsonSerializer.Deserialize<SemiFlowDocument>(json, _jsonOptions);

        // Assert
        doc!.Events.Should().NotBeNull();
        doc.Events.Should().HaveCount(1);
        doc.Events![0].Name.Should().Be("WAFER_READY");
        doc.Events[0].Type.Should().Be("wafer");
    }

    [Fact]
    public void Test005_ParseDocumentWithMetrics_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test"",
            ""version"": ""1.0"",
            ""metrics"": [
                {
                    ""name"": ""cycle_time"",
                    ""type"": ""timer"",
                    ""unit"": ""ms"",
                    ""aggregation"": ""avg""
                }
            ],
            ""lanes"": [
                {
                    ""id"": ""l1"",
                    ""workflow"": {
                        ""id"": ""w1"",
                        ""steps"": []
                    }
                }
            ]
        }";

        // Act
        var doc = JsonSerializer.Deserialize<SemiFlowDocument>(json, _jsonOptions);

        // Assert
        doc!.Metrics.Should().NotBeNull();
        doc.Metrics.Should().HaveCount(1);
        doc.Metrics![0].Name.Should().Be("cycle_time");
        doc.Metrics[0].Type.Should().Be("timer");
        doc.Metrics[0].Aggregation.Should().Be("avg");
    }

    [Fact]
    public void Test006_ParseMultipleLanes_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""name"": ""MultiLane"",
            ""version"": ""1.0"",
            ""lanes"": [
                {
                    ""id"": ""lane1"",
                    ""priority"": 1,
                    ""workflow"": {
                        ""id"": ""wf1"",
                        ""steps"": []
                    }
                },
                {
                    ""id"": ""lane2"",
                    ""priority"": 2,
                    ""workflow"": {
                        ""id"": ""wf2"",
                        ""steps"": []
                    }
                }
            ]
        }";

        // Act
        var doc = JsonSerializer.Deserialize<SemiFlowDocument>(json, _jsonOptions);

        // Assert
        doc!.Lanes.Should().HaveCount(2);
        doc.Lanes[0].Priority.Should().Be(1);
        doc.Lanes[1].Priority.Should().Be(2);
    }

    [Fact]
    public void Test007_ParseResourceGroups_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test"",
            ""version"": ""1.0"",
            ""resourceGroups"": [
                {
                    ""id"": ""polishers"",
                    ""resources"": [""p1"", ""p2""],
                    ""strategy"": ""roundRobin""
                }
            ],
            ""lanes"": [
                {
                    ""id"": ""l1"",
                    ""workflow"": {
                        ""id"": ""w1"",
                        ""steps"": []
                    }
                }
            ]
        }";

        // Act
        var doc = JsonSerializer.Deserialize<SemiFlowDocument>(json, _jsonOptions);

        // Assert
        doc!.ResourceGroups.Should().NotBeNull();
        doc.ResourceGroups.Should().HaveCount(1);
        doc.ResourceGroups![0].Id.Should().Be("polishers");
        doc.ResourceGroups[0].Resources.Should().Contain("p1");
        doc.ResourceGroups[0].Strategy.Should().Be("roundRobin");
    }

    [Fact]
    public void Test008_ParseGlobalHandlers_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test"",
            ""version"": ""1.0"",
            ""globalHandlers"": {
                ""onError"": [
                    {
                        ""id"": ""log_error"",
                        ""type"": ""action"",
                        ""action"": ""logError""
                    }
                ]
            },
            ""lanes"": [
                {
                    ""id"": ""l1"",
                    ""workflow"": {
                        ""id"": ""w1"",
                        ""steps"": []
                    }
                }
            ]
        }";

        // Act
        var doc = JsonSerializer.Deserialize<SemiFlowDocument>(json, _jsonOptions);

        // Assert
        doc!.GlobalHandlers.Should().NotBeNull();
        doc.GlobalHandlers!.OnError.Should().NotBeNull();
        doc.GlobalHandlers.OnError.Should().HaveCount(1);
        doc.GlobalHandlers.OnError![0].Id.Should().Be("log_error");
    }
}
