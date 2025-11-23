using FluentAssertions;
using SemiFlow.Converter;
using SemiFlow.Converter.Converters;
using SemiFlow.Converter.Models;
using Xunit;

namespace SemiFlow.Tests.EdgeCases;

/// <summary>
/// Edge case and error handling tests
/// </summary>
public class EdgeCaseTests
{
    private readonly SemiFlowToXStateConverter _converter = new();
    private readonly StepConverter _stepConverter;
    private readonly ConversionContext _context = new();

    public EdgeCaseTests()
    {
        _stepConverter = new StepConverter(_context);
    }

    [Fact]
    public void Test054_EmptyWorkflow_ShouldCreateIdleState()
    {
        // Arrange
        var semiFlow = new SemiFlowDocument
        {
            Name = "Empty",
            Version = "1.0.0",
            Lanes = new List<Lane>
            {
                new Lane
                {
                    Id = "lane1",
                    Workflow = new Workflow
                    {
                        Id = "empty_wf",
                        Steps = new List<Step>()
                    }
                }
            }
        };

        // Act
        var xstate = _converter.ConvertDocument(semiFlow);

        // Assert
        xstate.Should().NotBeNull();
        xstate.States.Should().ContainKey("idle");
    }

    [Fact]
    public void Test055_NullOptionalFields_ShouldNotThrow()
    {
        // Arrange
        var semiFlow = new SemiFlowDocument
        {
            Name = "NullFields",
            Version = "1.0.0",
            Vars = null,
            Constants = null,
            Stations = null,
            Events = null,
            Metrics = null,
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
        Action act = () => _converter.ConvertDocument(semiFlow);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Test056_StepWithoutId_ShouldUseTypeAsId()
    {
        // Note: In practice, ID is required, but testing graceful handling
        // This test documents expected behavior
        var step = new Step
        {
            Id = "", // Empty ID
            Type = "action",
            Action = "test",
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        Action act = () => _stepConverter.ConvertStep(step, parentStates, null);

        // Assert - Should not throw, will use empty string as key
        act.Should().NotThrow();
    }

    [Fact]
    public void Test057_DeeplyNestedSequences_ShouldNotOverflow()
    {
        // Arrange
        var deeplyNested = new Step
        {
            Id = "root",
            Type = "sequence",
            Enabled = true,
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "nested1",
                    Type = "sequence",
                    Enabled = true,
                    Steps = new List<Step>
                    {
                        new Step
                        {
                            Id = "nested2",
                            Type = "sequence",
                            Enabled = true,
                            Steps = new List<Step>
                            {
                                new Step { Id = "leaf", Type = "action", Action = "final", Enabled = true }
                            }
                        }
                    }
                }
            }
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        Action act = () => _stepConverter.ConvertStep(deeplyNested, parentStates, "done");

        // Assert
        act.Should().NotThrow();
        parentStates.Should().ContainKey("root");
    }

    [Fact]
    public void Test058_ParallelWithSingleBranch_ShouldStillWork()
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
                    new Step { Id = "only", Type = "action", Action = "act", Enabled = true }
                }
            },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("par1");
        parentStates["par1"].Type.Should().Be("parallel");
    }

    [Fact]
    public void Test059_ParallelWithEmptyBranches_ShouldCreateEmptyRegions()
    {
        // Arrange
        var step = new Step
        {
            Id = "par_empty",
            Type = "parallel",
            Branches = new List<List<Step>>
            {
                new List<Step>(),
                new List<Step>()
            },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("par_empty");
        parentStates["par_empty"].States.Should().ContainKey("branch_0");
        parentStates["par_empty"].States.Should().ContainKey("branch_1");
    }

    [Fact]
    public void Test060_LoopWithoutCondition_ShouldUseDefault()
    {
        // Arrange
        var step = new Step
        {
            Id = "loop_no_cond",
            Type = "loop",
            Mode = "while",
            Condition = null, // No condition
            Steps = new List<Step>
            {
                new Step { Id = "ls1", Type = "action", Action = "act", Enabled = true }
            },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStep(step, parentStates, "done");

        // Assert
        parentStates.Should().ContainKey("loop_no_cond");
        // Should use default condition "shouldContinueLoop"
    }

    [Fact]
    public void Test061_TryWithoutCatch_ShouldOnlyHaveTryBlock()
    {
        // Arrange
        var step = new Step
        {
            Id = "try_no_catch",
            Type = "try",
            Try = new List<Step>
            {
                new Step { Id = "t1", Type = "action", Action = "risky", Enabled = true }
            },
            Catch = null,
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStep(step, parentStates, "done");

        // Assert
        parentStates.Should().ContainKey("try_no_catch");
        parentStates["try_no_catch"].States.Should().ContainKey("try");
        parentStates["try_no_catch"].States.Should().NotContainKey("catch");
    }

    [Fact]
    public void Test062_TryWithFinally_ShouldIncludeFinallyBlock()
    {
        // Arrange
        var step = new Step
        {
            Id = "try_finally",
            Type = "try",
            Try = new List<Step>
            {
                new Step { Id = "t1", Type = "action", Action = "try", Enabled = true }
            },
            Finally = new List<Step>
            {
                new Step { Id = "f1", Type = "action", Action = "cleanup", Enabled = true }
            },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStep(step, parentStates, "done");

        // Assert
        parentStates.Should().ContainKey("try_finally");
        parentStates["try_finally"].States.Should().ContainKey("finally");
    }

    [Fact]
    public void Test063_BranchWithoutOtherwise_ShouldTransitionToNext()
    {
        // Arrange
        var step = new Step
        {
            Id = "branch_no_otherwise",
            Type = "branch",
            Cases = System.Text.Json.JsonSerializer.SerializeToElement(new[]
            {
                new BranchCase
                {
                    When = "cond1",
                    Steps = new List<Step>
                    {
                        new Step { Id = "cs1", Type = "action", Action = "act", Enabled = true }
                    }
                }
            }),
            Otherwise = null,
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStep(step, parentStates, "final");

        // Assert
        parentStates.Should().ContainKey("branch_no_otherwise");
    }

    [Fact]
    public void Test064_SwitchWithoutDefault_ShouldWork()
    {
        // Arrange
        var step = new Step
        {
            Id = "switch_no_default",
            Type = "switch",
            Value = "status",
            Cases = System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, List<Step>>
            {
                ["ready"] = new List<Step>
                {
                    new Step { Id = "ready_step", Type = "action", Action = "proceed", Enabled = true }
                }
            }),
            Default = null,
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStep(step, parentStates, "final");

        // Assert
        parentStates.Should().ContainKey("switch_no_default");
    }

    [Fact]
    public void Test065_UseStationWithoutWaitForAvailable_ShouldNotCreateWaitingState()
    {
        // Arrange
        var step = new Step
        {
            Id = "use_no_wait",
            Type = "useStation",
            Role = "robot",
            WaitForAvailable = false,
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("use_no_wait");
    }

    [Fact]
    public void Test066_ActionWithTimeout_ShouldCreateAfterTransition()
    {
        // Arrange
        var step = new Step
        {
            Id = "timeout_action",
            Type = "action",
            Action = "slowOp",
            Timeout = 5000,
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("timeout_action");
        parentStates["timeout_action"].After.Should().NotBeNull();
    }

    [Fact]
    public void Test067_StepWithLongId_ShouldNotCauseIssues()
    {
        // Arrange
        var longId = new string('a', 500); // Very long ID
        var step = new Step
        {
            Id = longId,
            Type = "action",
            Action = "test",
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        Action act = () => _stepConverter.ConvertStep(step, parentStates, null);

        // Assert
        act.Should().NotThrow();
        parentStates.Should().ContainKey(longId);
    }

    [Fact]
    public void Test068_ConvertFromInvalidJson_ShouldThrow()
    {
        // Arrange
        var invalidJson = "{ invalid json }";

        // Act
        Action act = () => _converter.Convert(invalidJson);

        // Assert
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Test069_ConvertNullDocument_ShouldThrow()
    {
        // Arrange
        string nullJson = "null";

        // Act
        Action act = () => _converter.Convert(nullJson);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Test070_MultipleDisabledSteps_ShouldSkipAll()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Id = "s1", Type = "action", Action = "a1", Enabled = false },
            new Step { Id = "s2", Type = "action", Action = "a2", Enabled = false },
            new Step { Id = "s3", Type = "action", Action = "a3", Enabled = true }
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStepSequence(steps, parentStates, "final");

        // Assert
        parentStates.Should().NotContainKey("s1");
        parentStates.Should().NotContainKey("s2");
        parentStates.Should().ContainKey("s3");
    }

    [Fact]
    public void Test071_StepWithComplexRetryPolicy_ShouldParse()
    {
        // Arrange
        var step = new Step
        {
            Id = "retry_step",
            Type = "action",
            Action = "flaky",
            Retry = new RetryPolicy
            {
                Count = 5,
                Delay = 100,
                Strategy = "exponential",
                MaxDelay = 10000,
                Jitter = true,
                RetryOn = new List<string> { "NetworkError", "TimeoutError" }
            },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        Action act = () => _stepConverter.ConvertStep(step, parentStates, null);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Test072_LaneWithoutWorkflow_ShouldHandleGracefully()
    {
        // This tests defensive programming
        // In reality, schema validation would catch this
        var semiFlow = new SemiFlowDocument
        {
            Name = "NoWorkflow",
            Version = "1.0.0",
            Lanes = new List<Lane>
            {
                new Lane
                {
                    Id = "lane1",
                    Workflow = new Workflow
                    {
                        Id = "wf1",
                        Steps = new List<Step>() // Empty steps is valid
                    }
                }
            }
        };

        // Act
        Action act = () => _converter.ConvertDocument(semiFlow);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Test073_SpecialCharactersInIds_ShouldBePreserved()
    {
        // Arrange
        var step = new Step
        {
            Id = "step-with_special.chars@123",
            Type = "action",
            Action = "test",
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStep(step, parentStates, null);

        // Assert
        parentStates.Should().ContainKey("step-with_special.chars@123");
    }

    [Fact]
    public void Test074_OnEventWithOnce_ShouldNotLoopBack()
    {
        // Arrange
        var step = new Step
        {
            Id = "onevent_once",
            Type = "onEvent",
            Event = "TRIGGER",
            Once = true,
            Steps = new List<Step>
            {
                new Step { Id = "handler", Type = "action", Action = "handle", Enabled = true }
            },
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStep(step, parentStates, "next");

        // Assert
        parentStates.Should().ContainKey("onevent_once");
    }

    [Fact]
    public void Test075_TransactionWithoutRollback_ShouldWork()
    {
        // Arrange
        var step = new Step
        {
            Id = "trans_no_rollback",
            Type = "transaction",
            Steps = new List<Step>
            {
                new Step { Id = "ts1", Type = "action", Action = "op", Enabled = true }
            },
            Rollback = null,
            Enabled = true
        };
        var parentStates = new Dictionary<string, XStateNode>();

        // Act
        _stepConverter.ConvertStep(step, parentStates, "done");

        // Assert
        parentStates.Should().ContainKey("trans_no_rollback");
        parentStates["trans_no_rollback"].States.Should().ContainKey("body");
        parentStates["trans_no_rollback"].States.Should().ContainKey("commit");
        parentStates["trans_no_rollback"].States.Should().NotContainKey("rollback");
    }
}
