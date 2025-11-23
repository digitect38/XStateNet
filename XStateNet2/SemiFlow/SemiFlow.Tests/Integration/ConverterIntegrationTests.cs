using FluentAssertions;
using SemiFlow.Converter;
using SemiFlow.Converter.Models;
using System.Text.Json;
using Xunit;

namespace SemiFlow.Tests.Integration;

/// <summary>
/// Integration tests for full document conversion
/// </summary>
public class ConverterIntegrationTests
{
    private readonly SemiFlowToXStateConverter _converter = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public void Test047_ConvertMinimalWorkflow_ShouldSucceed()
    {
        // Arrange
        var semiFlow = new SemiFlowDocument
        {
            Name = "MinimalTest",
            Version = "1.0.0",
            Lanes = new List<Lane>
            {
                new Lane
                {
                    Id = "lane1",
                    Workflow = new Workflow
                    {
                        Id = "simple_workflow",
                        Steps = new List<Step>
                        {
                            new Step
                            {
                                Id = "step1",
                                Type = "action",
                                Action = "initialize",
                                Enabled = true
                            }
                        }
                    }
                }
            }
        };

        // Act
        var xstate = _converter.ConvertDocument(semiFlow);

        // Assert
        xstate.Should().NotBeNull();
        xstate.Id.Should().Be("simple_workflow");
        xstate.Initial.Should().Be("step1");
        xstate.States.Should().ContainKey("step1");
    }

    [Fact]
    public void Test048_ConvertMultiLaneWorkflow_ShouldCreateParallelMachine()
    {
        // Arrange
        var semiFlow = new SemiFlowDocument
        {
            Name = "MultiLaneTest",
            Version = "1.0.0",
            Lanes = new List<Lane>
            {
                new Lane
                {
                    Id = "lane1",
                    Workflow = new Workflow
                    {
                        Id = "wf1",
                        Steps = new List<Step>
                        {
                            new Step { Id = "s1", Type = "action", Action = "act1", Enabled = true }
                        }
                    }
                },
                new Lane
                {
                    Id = "lane2",
                    Workflow = new Workflow
                    {
                        Id = "wf2",
                        Steps = new List<Step>
                        {
                            new Step { Id = "s2", Type = "action", Action = "act2", Enabled = true }
                        }
                    }
                }
            }
        };

        // Act
        var xstate = _converter.ConvertDocument(semiFlow);

        // Assert
        xstate.Should().NotBeNull();
        xstate.Type.Should().Be("parallel");
        xstate.States.Should().ContainKey("lane1");
        xstate.States.Should().ContainKey("lane2");
    }

    [Fact]
    public void Test049_ConvertFromJson_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""name"": ""JsonTest"",
            ""version"": ""1.0.0"",
            ""lanes"": [
                {
                    ""id"": ""main"",
                    ""workflow"": {
                        ""id"": ""test_wf"",
                        ""steps"": [
                            {
                                ""id"": ""init"",
                                ""type"": ""action"",
                                ""action"": ""initialize""
                            }
                        ]
                    }
                }
            ]
        }";

        // Act
        var xstate = _converter.Convert(json);

        // Assert
        xstate.Should().NotBeNull();
        xstate.Id.Should().Be("test_wf");
    }

    [Fact]
    public void Test050_SerializeToJson_ShouldProduceValidJson()
    {
        // Arrange
        var semiFlow = new SemiFlowDocument
        {
            Name = "SerializeTest",
            Version = "1.0.0",
            Lanes = new List<Lane>
            {
                new Lane
                {
                    Id = "lane1",
                    Workflow = new Workflow
                    {
                        Id = "wf1",
                        Steps = new List<Step>
                        {
                            new Step { Id = "s1", Type = "action", Action = "act", Enabled = true }
                        }
                    }
                }
            }
        };
        var xstate = _converter.ConvertDocument(semiFlow);

        // Act
        var json = _converter.SerializeToJson(xstate);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("\"id\"");
        json.Should().Contain("\"states\"");
    }

    [Fact]
    public void Test051_ConvertComplexWorkflow_WithAllFeatures_ShouldSucceed()
    {
        // Arrange
        var semiFlow = new SemiFlowDocument
        {
            Name = "ComplexTest",
            Version = "1.0.0",
            Vars = new Dictionary<string, object>
            {
                ["count"] = 0
            },
            Constants = new Dictionary<string, object>
            {
                ["max"] = 10
            },
            Stations = new List<Station>
            {
                new Station { Id = "robot1", Role = "robot" }
            },
            Events = new List<EventDef>
            {
                new EventDef { Name = "READY", Type = "system" }
            },
            Metrics = new List<MetricDef>
            {
                new MetricDef { Name = "time", Type = "timer" }
            },
            Lanes = new List<Lane>
            {
                new Lane
                {
                    Id = "main",
                    Priority = 1,
                    Workflow = new Workflow
                    {
                        Id = "complex_wf",
                        Steps = new List<Step>
                        {
                            new Step { Id = "init", Type = "action", Action = "init", Enabled = true },
                            new Step
                            {
                                Id = "seq1",
                                Type = "sequence",
                                Enabled = true,
                                Steps = new List<Step>
                                {
                                    new Step { Id = "s1", Type = "action", Action = "a1", Enabled = true },
                                    new Step { Id = "s2", Type = "action", Action = "a2", Enabled = true }
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act
        var xstate = _converter.ConvertDocument(semiFlow);

        // Assert
        xstate.Should().NotBeNull();
        xstate.Context.Should().ContainKey("count");
        xstate.Context.Should().ContainKey("max");
        xstate.States.Should().ContainKey("init");
        xstate.States.Should().ContainKey("seq1");
    }

    [Fact]
    public void Test052_BuildContext_ShouldMergeAllVariables()
    {
        // Arrange
        var semiFlow = new SemiFlowDocument
        {
            Name = "ContextTest",
            Version = "1.0.0",
            Vars = new Dictionary<string, object> { ["var1"] = 1 },
            Constants = new Dictionary<string, object> { ["const1"] = 100 },
            Lanes = new List<Lane>
            {
                new Lane
                {
                    Id = "lane1",
                    Vars = new Dictionary<string, object> { ["laneVar"] = 2 },
                    Workflow = new Workflow
                    {
                        Id = "wf1",
                        Vars = new Dictionary<string, object> { ["wfVar"] = 3 },
                        Steps = new List<Step>()
                    }
                }
            }
        };

        // Act
        var xstate = _converter.ConvertDocument(semiFlow);

        // Assert
        xstate.Context.Should().ContainKey("var1");
        xstate.Context.Should().ContainKey("const1");
        xstate.Context.Should().ContainKey("lane_laneVar");
        xstate.Context.Should().ContainKey("workflow_wfVar");
    }

    [Fact]
    public void Test053_ConvertWithStations_ShouldIncludeInContext()
    {
        // Arrange
        var semiFlow = new SemiFlowDocument
        {
            Name = "StationTest",
            Version = "1.0.0",
            Stations = new List<Station>
            {
                new Station { Id = "robot1", Role = "robot", State = "idle" },
                new Station { Id = "platen1", Role = "platen", State = "idle" }
            },
            Lanes = new List<Lane>
            {
                new Lane
                {
                    Id = "lane1",
                    Workflow = new Workflow
                    {
                        Id = "wf1",
                        Steps = new List<Step>()
                    }
                }
            }
        };

        // Act
        var xstate = _converter.ConvertDocument(semiFlow);

        // Assert
        xstate.Context.Should().ContainKey("stations");
    }
}
