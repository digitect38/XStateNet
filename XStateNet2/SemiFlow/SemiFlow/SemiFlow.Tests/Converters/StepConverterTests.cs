using FluentAssertions;
using SemiFlow.Converter.Converters;
using SemiFlow.Converter.Models;
using XStateNet2.Core.Engine;
using Xunit;

namespace SemiFlow.Tests.Converters;

/// <summary>
/// Tests for StepConverter - all 19 step types
/// </summary>
public class StepConverterTests
{
    private readonly ConversionContext _context = new();
    private readonly StepConverter _converter;

    public StepConverterTests()
    {
        _converter = new StepConverter(_context);
    }

    [Fact]
    public void Test025_ConvertActionStep_ShouldCreateStateWithEntry()
    {
        // Arrange
        var step = new Step
        {
            Id = "step1",
            Type = "action",
            Action = "doSomething",
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        var stateId = _converter.ConvertStep(step, parentStates, "next_state");

        // Assert
        stateId.Should().Be("step1");
        parentStates.Should().ContainKey("step1");
        parentStates["step1"].Entry.Should().NotBeNull();
    }

    [Fact]
    public void Test026_ConvertActionStep_WithAsync_ShouldUseAlways()
    {
        // Arrange
        var step = new Step
        {
            Id = "async_action",
            Type = "action",
            Action = "asyncOp",
            Async = true,
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates["async_action"].Always.Should().NotBeNull();
        parentStates["async_action"].Always.Should().HaveCount(1);
    }

    [Fact]
    public void Test027_ConvertReserveStep_ShouldCreateState()
    {
        // Arrange
        var step = new Step
        {
            Id = "reserve1",
            Type = "reserve",
            Resources = new List<string> { "res1", "res2" },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        var stateId = _converter.ConvertStep(step, parentStates, "next");

        // Assert
        stateId.Should().Be("reserve1");
        parentStates.Should().ContainKey("reserve1");
    }

    [Fact]
    public void Test028_ConvertReleaseStep_ShouldCreateState()
    {
        // Arrange
        var step = new Step
        {
            Id = "release1",
            Type = "release",
            Resources = new List<string> { "res1" },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        var stateId = _converter.ConvertStep(step, parentStates, "next");

        // Assert
        stateId.Should().Be("release1");
        parentStates.Should().ContainKey("release1");
    }

    [Fact]
    public void Test029_ConvertUseStationStep_ShouldCreateNestedStates()
    {
        // Arrange
        var step = new Step
        {
            Id = "use_robot",
            Type = "useStation",
            Role = "robot",
            WaitForAvailable = true,
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("use_robot");
        parentStates["use_robot"].States.Should().NotBeNull();
        parentStates["use_robot"].States.Should().ContainKey("acquiring");
        parentStates["use_robot"].States.Should().ContainKey("using");
    }

    [Fact]
    public void Test030_ConvertSequenceStep_ShouldCreateCompoundState()
    {
        // Arrange
        var step = new Step
        {
            Id = "seq1",
            Type = "sequence",
            Steps = new List<Step>
            {
                new Step { Id = "s1", Type = "action", Action = "act1", Enabled = true },
                new Step { Id = "s2", Type = "action", Action = "act2", Enabled = true }
            },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "final");

        // Assert
        parentStates.Should().ContainKey("seq1");
        parentStates["seq1"].States.Should().NotBeNull();
        parentStates["seq1"].States.Should().ContainKey("s1");
        parentStates["seq1"].States.Should().ContainKey("s2");
        parentStates["seq1"].Initial.Should().Be("s1");
    }

    [Fact]
    public void Test031_ConvertParallelStep_ShouldCreateParallelState()
    {
        // Arrange
        var step = new Step
        {
            Id = "par1",
            Type = "parallel",
            Branches = new List<List<Step>>
            {
                new List<Step>
                {
                    new Step { Id = "b1s1", Type = "action", Action = "act1", Enabled = true }
                },
                new List<Step>
                {
                    new Step { Id = "b2s1", Type = "action", Action = "act2", Enabled = true }
                }
            },
            Wait = "all",
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("par1");
        parentStates["par1"].Type.Should().Be("parallel");
        parentStates["par1"].States.Should().ContainKey("branch_0");
        parentStates["par1"].States.Should().ContainKey("branch_1");
    }

    [Fact]
    public void Test032_ConvertLoopStep_ShouldCreateLoopState()
    {
        // Arrange
        var step = new Step
        {
            Id = "loop1",
            Type = "loop",
            Mode = "while",
            Condition = "hasMore",
            Steps = new List<Step>
            {
                new Step { Id = "ls1", Type = "action", Action = "process", Enabled = true }
            },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "done");

        // Assert
        parentStates.Should().ContainKey("loop1");
        parentStates["loop1"].States.Should().ContainKey("loop_body");
    }

    [Fact]
    public void Test033_ConvertWaitStep_Duration_ShouldUseAfter()
    {
        // Arrange
        var step = new Step
        {
            Id = "wait1",
            Type = "wait",
            Duration = 5000,
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("wait1");
        parentStates["wait1"].After.Should().NotBeNull();
    }

    [Fact]
    public void Test034_ConvertWaitStep_Condition_ShouldUseAlways()
    {
        // Arrange
        var step = new Step
        {
            Id = "wait2",
            Type = "wait",
            Until = "resourceReady",
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("wait2");
        parentStates["wait2"].Always.Should().NotBeNull();
    }

    [Fact]
    public void Test035_ConvertConditionStep_ShouldUseGuard()
    {
        // Arrange
        var step = new Step
        {
            Id = "cond1",
            Type = "condition",
            Expect = "isReady",
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("cond1");
        parentStates["cond1"].Always.Should().NotBeNull();
        parentStates["cond1"].Always![0].Guard.Should().Be("isReady");
    }

    [Fact]
    public void Test036_ConvertBranchStep_ShouldCreateBranchStates()
    {
        // Arrange
        var step = new Step
        {
            Id = "branch1",
            Type = "branch",
            Cases = System.Text.Json.JsonSerializer.SerializeToElement(new[]
            {
                new BranchCase
                {
                    When = "cond1",
                    Steps = new List<Step>
                    {
                        new Step { Id = "cs1", Type = "action", Action = "act1", Enabled = true }
                    }
                }
            }),
            Otherwise = new List<Step>
            {
                new Step { Id = "os1", Type = "action", Action = "other", Enabled = true }
            },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "final");

        // Assert
        parentStates.Should().ContainKey("branch1");
        parentStates["branch1"].States.Should().ContainKey("entry");
    }

    [Fact]
    public void Test037_ConvertCallStep_ShouldUseInvoke()
    {
        // Arrange
        var step = new Step
        {
            Id = "call1",
            Type = "call",
            Target = "subWorkflow",
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("call1");
        parentStates["call1"].Invoke.Should().NotBeNull();
        parentStates["call1"].Invoke!.Src.Should().Be("subWorkflow");
    }

    [Fact]
    public void Test038_ConvertTryStep_ShouldCreateTryCatchStates()
    {
        // Arrange
        var step = new Step
        {
            Id = "try1",
            Type = "try",
            Try = new List<Step>
            {
                new Step { Id = "t1", Type = "action", Action = "risky", Enabled = true }
            },
            Catch = new List<Step>
            {
                new Step { Id = "c1", Type = "action", Action = "handleError", Enabled = true }
            },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "done");

        // Assert
        parentStates.Should().ContainKey("try1");
        parentStates["try1"].States.Should().ContainKey("try");
        parentStates["try1"].States.Should().ContainKey("catch");
    }

    [Fact]
    public void Test039_ConvertEmitEventStep_ShouldCreateState()
    {
        // Arrange
        var step = new Step
        {
            Id = "emit1",
            Type = "emitEvent",
            Event = "PROCESS_COMPLETE",
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("emit1");
        parentStates["emit1"].Entry.Should().NotBeNull();
    }

    [Fact]
    public void Test040_ConvertOnEventStep_ShouldCreateEventHandler()
    {
        // Arrange
        var step = new Step
        {
            Id = "onEvent1",
            Type = "onEvent",
            Event = "WAFER_READY",
            Steps = new List<Step>
            {
                new Step { Id = "h1", Type = "action", Action = "handleWafer", Enabled = true }
            },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("onEvent1");
        parentStates["onEvent1"].States.Should().ContainKey("waiting");
        parentStates["onEvent1"].States.Should().ContainKey("handling");
    }

    [Fact]
    public void Test041_ConvertCollectMetricStep_ShouldCreateState()
    {
        // Arrange
        var step = new Step
        {
            Id = "metric1",
            Type = "collectMetric",
            Metric = "cycle_time",
            Value = "elapsed",
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("metric1");
    }

    [Fact]
    public void Test042_ConvertRaceStep_ShouldCreateParallelState()
    {
        // Arrange
        var step = new Step
        {
            Id = "race1",
            Type = "race",
            Branches = new List<List<Step>>
            {
                new List<Step>
                {
                    new Step { Id = "r1s1", Type = "action", Action = "fast", Enabled = true }
                },
                new List<Step>
                {
                    new Step { Id = "r2s1", Type = "action", Action = "slow", Enabled = true }
                }
            },
            CancelOthers = true,
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "winner");

        // Assert
        parentStates.Should().ContainKey("race1");
        parentStates["race1"].Type.Should().Be("parallel");
        parentStates["race1"].States.Should().ContainKey("race_0");
        parentStates["race1"].States.Should().ContainKey("race_1");
    }

    [Fact]
    public void Test043_ConvertTransactionStep_ShouldCreateTransactionStates()
    {
        // Arrange
        var step = new Step
        {
            Id = "trans1",
            Type = "transaction",
            Steps = new List<Step>
            {
                new Step { Id = "ts1", Type = "action", Action = "op1", Enabled = true },
                new Step { Id = "ts2", Type = "action", Action = "op2", Enabled = true }
            },
            Rollback = new List<Step>
            {
                new Step { Id = "rb1", Type = "action", Action = "undo", Enabled = true }
            },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStep(step, parentStates, "done");

        // Assert
        parentStates.Should().ContainKey("trans1");
        parentStates["trans1"].States.Should().ContainKey("body");
        parentStates["trans1"].States.Should().ContainKey("commit");
        parentStates["trans1"].States.Should().ContainKey("rollback");
    }

    [Fact]
    public void Test044_ConvertDisabledStep_ShouldReturnNextState()
    {
        // Arrange
        var step = new Step
        {
            Id = "disabled_step",
            Type = "action",
            Action = "skip",
            Enabled = false
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        var stateId = _converter.ConvertStep(step, parentStates, "next_state");

        // Assert
        stateId.Should().Be("next_state");
        parentStates.Should().NotContainKey("disabled_step");
    }

    [Fact]
    public void Test045_ConvertStepSequence_EmptyList_ShouldCreateEmptyState()
    {
        // Arrange
        var steps = new List<Step>();
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStepSequence(steps, parentStates, "final");

        // Assert
        parentStates.Should().ContainKey("empty");
    }

    [Fact]
    public void Test046_ConvertStepSequence_MultipleSteps_ShouldLinkThem()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Id = "step1", Type = "action", Action = "act1", Enabled = true },
            new Step { Id = "step2", Type = "action", Action = "act2", Enabled = true },
            new Step { Id = "step3", Type = "action", Action = "act3", Enabled = true }
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _converter.ConvertStepSequence(steps, parentStates, "final");

        // Assert
        parentStates.Should().ContainKey("step1");
        parentStates.Should().ContainKey("step2");
        parentStates.Should().ContainKey("step3");
    }
}
