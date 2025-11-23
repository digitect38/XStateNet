using FluentAssertions;
using SemiFlow.Converter.Models;
using System.Text.Json;
using XStateNet2.Core.Engine;
using Xunit;

namespace SemiFlow.Tests.Models;

/// <summary>
/// Tests for all step type models
/// </summary>
public class StepModelTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Test009_ParseActionStep_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""step1"",
            ""type"": ""action"",
            ""action"": ""doSomething"",
            ""async"": true
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Id.Should().Be("step1");
        step.Type.Should().Be("action");
        step.Action.Should().Be("doSomething");
        step.Async.Should().BeTrue();
    }

    [Fact]
    public void Test010_ParseUseStationStep_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""use_robot"",
            ""type"": ""useStation"",
            ""role"": ""robot"",
            ""waitForAvailable"": true,
            ""maxWaitTime"": 10000
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Type.Should().Be("useStation");
        step.Role.Should().Be("robot");
        step.WaitForAvailable.Should().BeTrue();
        step.MaxWaitTime.Should().Be(10000);
    }

    [Fact]
    public void Test011_ParseReserveStep_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""reserve_res"",
            ""type"": ""reserve"",
            ""resources"": [""res1"", ""res2""],
            ""priority"": 5
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Type.Should().Be("reserve");
        step.Resources.Should().Contain("res1");
        step.Resources.Should().Contain("res2");
        step.Priority.Should().Be(5);
    }

    [Fact]
    public void Test012_ParseParallelStep_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""par1"",
            ""type"": ""parallel"",
            ""branches"": [
                [
                    {
                        ""id"": ""b1s1"",
                        ""type"": ""action"",
                        ""action"": ""act1""
                    }
                ],
                [
                    {
                        ""id"": ""b2s1"",
                        ""type"": ""action"",
                        ""action"": ""act2""
                    }
                ]
            ],
            ""wait"": ""all""
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Type.Should().Be("parallel");
        step.Branches.Should().HaveCount(2);
        step.Wait.Should().Be("all");
    }

    [Fact]
    public void Test013_ParseLoopStep_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""loop1"",
            ""type"": ""loop"",
            ""mode"": ""while"",
            ""condition"": ""hasMore"",
            ""maxIterations"": 100,
            ""steps"": [
                {
                    ""id"": ""ls1"",
                    ""type"": ""action"",
                    ""action"": ""process""
                }
            ]
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Type.Should().Be("loop");
        step.Mode.Should().Be("while");
        step.Condition.Should().Be("hasMore");
        step.MaxIterations.Should().Be(100);
        step.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void Test014_ParseBranchStep_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""branch1"",
            ""type"": ""branch"",
            ""cases"": [
                {
                    ""when"": ""condition1"",
                    ""steps"": [
                        {
                            ""id"": ""cs1"",
                            ""type"": ""action"",
                            ""action"": ""act1""
                        }
                    ]
                }
            ]
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Type.Should().Be("branch");
        step.Cases.Should().NotBeNull();
    }

    [Fact]
    public void Test015_ParseWaitStep_DurationBased_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""wait1"",
            ""type"": ""wait"",
            ""duration"": 5000
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Type.Should().Be("wait");
        step.Duration.Should().Be(5000);
    }

    [Fact]
    public void Test016_ParseWaitStep_ConditionBased_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""wait2"",
            ""type"": ""wait"",
            ""until"": ""resourceReady"",
            ""pollInterval"": 100
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Type.Should().Be("wait");
        step.Until.Should().Be("resourceReady");
        step.PollInterval.Should().Be(100);
    }

    [Fact]
    public void Test017_ParseConditionStep_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""cond1"",
            ""type"": ""condition"",
            ""expect"": ""isReady"",
            ""message"": ""System not ready""
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Type.Should().Be("condition");
        step.Expect.Should().Be("isReady");
        step.Message.Should().Be("System not ready");
    }

    [Fact]
    public void Test018_ParseTryStep_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""try1"",
            ""type"": ""try"",
            ""try"": [
                {
                    ""id"": ""t1"",
                    ""type"": ""action"",
                    ""action"": ""risky""
                }
            ],
            ""catch"": [
                {
                    ""id"": ""c1"",
                    ""type"": ""action"",
                    ""action"": ""handleError""
                }
            ]
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Type.Should().Be("try");
        step.Try.Should().HaveCount(1);
        step.Catch.Should().HaveCount(1);
    }

    [Fact]
    public void Test019_ParseEmitEventStep_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""emit1"",
            ""type"": ""emitEvent"",
            ""event"": ""PROCESS_COMPLETE"",
            ""async"": true
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Type.Should().Be("emitEvent");
        step.Event.Should().Be("PROCESS_COMPLETE");
        step.Async.Should().BeTrue();
    }

    [Fact]
    public void Test020_ParseCollectMetricStep_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""metric1"",
            ""type"": ""collectMetric"",
            ""metric"": ""cycle_time"",
            ""value"": ""elapsed""
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Type.Should().Be("collectMetric");
        step.Metric.Should().Be("cycle_time");
        step.Value.Should().Be("elapsed");
    }

    [Fact]
    public void Test021_ParseStepWithRetryPolicy_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""step1"",
            ""type"": ""action"",
            ""action"": ""doIt"",
            ""retry"": {
                ""count"": 3,
                ""delay"": 1000,
                ""strategy"": ""exponential"",
                ""maxDelay"": 10000,
                ""jitter"": true
            }
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Retry.Should().NotBeNull();
        step.Retry!.Count.Should().Be(3);
        step.Retry.Delay.Should().Be(1000);
        step.Retry.Strategy.Should().Be("exponential");
        step.Retry.Jitter.Should().BeTrue();
    }

    [Fact]
    public void Test022_ParseStepWithTimeout_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""step1"",
            ""type"": ""action"",
            ""action"": ""doIt"",
            ""timeout"": 30000,
            ""onTimeout"": [
                {
                    ""id"": ""timeout_handler"",
                    ""type"": ""action"",
                    ""action"": ""handleTimeout""
                }
            ]
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Timeout.Should().Be(30000);
        step.OnTimeout.Should().HaveCount(1);
    }

    [Fact]
    public void Test023_ParseDisabledStep_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""step1"",
            ""type"": ""action"",
            ""action"": ""doIt"",
            ""enabled"": false
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Test024_ParseStepWithTags_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""id"": ""step1"",
            ""type"": ""action"",
            ""action"": ""doIt"",
            ""tags"": [""critical"", ""monitored""]
        }";

        // Act
        var step = JsonSerializer.Deserialize<Step>(json, _jsonOptions);

        // Assert
        step.Should().NotBeNull();
        step!.Tags.Should().Contain("critical");
        step.Tags.Should().Contain("monitored");
    }
}
