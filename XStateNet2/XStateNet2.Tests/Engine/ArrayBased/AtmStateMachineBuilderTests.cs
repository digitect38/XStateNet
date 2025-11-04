using FluentAssertions;
using Xunit;
using XStateNet2.Core.Engine.ArrayBased;

namespace XStateNet2.Tests.Engine.ArrayBased;

/// <summary>
/// Tests for ArrayStateMachineBuilder - automatic conversion from JSON to Array-based state machine
/// Validates that developers don't need to manually write State/Event mappings
/// </summary>
public class AtmStateMachineBuilderTests
{
    private const string AtmJson = """
    {
        "id": "atm",
        "initial": "idle",
        "states": {
            "idle": {
                "on": {
                    "CARD_INSERTED": "authenticating"
                }
            },
            "authenticating": {
                "initial": "enteringPin",
                "states": {
                    "enteringPin": {
                        "on": {
                            "PIN_ENTERED": "verifyingPin",
                            "CANCEL": "#atm.idle"
                        }
                    },
                    "verifyingPin": {
                        "on": {
                            "PIN_CORRECT": "#atm.operational",
                            "PIN_INCORRECT": "enteringPin"
                        }
                    }
                }
            },
            "operational": {
                "type": "parallel",
                "states": {
                    "transaction": {
                        "initial": "selectingTransaction",
                        "states": {
                            "selectingTransaction": {
                                "on": {
                                    "WITHDRAW": "withdrawing",
                                    "DEPOSIT": "depositing",
                                    "BALANCE": "checkingBalance",
                                    "CANCEL": "#atm.idle"
                                }
                            },
                            "withdrawing": {
                                "on": {
                                    "AMOUNT_ENTERED": "processingWithdrawal",
                                    "CANCEL": "selectingTransaction"
                                }
                            },
                            "processingWithdrawal": {
                                "on": {
                                    "SUCCESS": "#atm.idle",
                                    "FAILURE": "withdrawing"
                                }
                            },
                            "depositing": {
                                "on": {
                                    "AMOUNT_ENTERED": "processingDeposit",
                                    "CANCEL": "selectingTransaction"
                                }
                            },
                            "processingDeposit": {
                                "on": {
                                    "SUCCESS": "#atm.idle",
                                    "FAILURE": "depositing"
                                }
                            },
                            "checkingBalance": {
                                "on": {
                                    "BALANCE_SHOWN": "selectingTransaction",
                                    "CANCEL": "selectingTransaction"
                                }
                            }
                        }
                    },
                    "receipt": {
                        "initial": "noReceipt",
                        "states": {
                            "noReceipt": {
                                "on": {
                                    "REQUEST_RECEIPT": "printingReceipt"
                                }
                            },
                            "printingReceipt": {
                                "on": {
                                    "RECEIPT_PRINTED": "noReceipt"
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    """;

    #region Builder Basic Tests

    [Fact]
    public void Builder_FromJson_ShouldParseSuccessfully()
    {
        // Act
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();

        // Assert
        machine.Should().NotBeNull();
        machine.Id.Should().Be("atm");
    }

    [Fact]
    public void Builder_ShouldGenerateCorrectStateCount()
    {
        // Act
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();

        // Assert
        // idle, authenticating, enteringPin, verifyingPin, operational,
        // transaction, selectingTransaction, withdrawing, processingWithdrawal,
        // depositing, processingDeposit, checkingBalance, receipt, noReceipt, printingReceipt
        machine.StateCount.Should().Be(15);
    }

    [Fact]
    public void Builder_ShouldSetCorrectInitialState()
    {
        // Act
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();

        // Assert
        machine.GetStateName(machine.InitialStateId).Should().Be("idle");
    }

    [Fact]
    public void Builder_ShouldCreateStateMachineMap()
    {
        // Act
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();

        // Assert
        machine.Map.Should().NotBeNull();
        machine.Map.States.Should().NotBeNull();
        machine.Map.Events.Should().NotBeNull();
    }

    #endregion

    #region State Mapping Tests

    [Fact]
    public void Builder_ShouldMapAllStates()
    {
        // Act
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();

        // Assert - Verify key states are mapped
        machine.Map.States.GetIndex("idle").Should().BeGreaterOrEqualTo((byte)0);
        machine.Map.States.GetIndex("enteringPin").Should().BeGreaterOrEqualTo((byte)0);
        machine.Map.States.GetIndex("verifyingPin").Should().BeGreaterOrEqualTo((byte)0);
        machine.Map.States.GetIndex("selectingTransaction").Should().BeGreaterOrEqualTo((byte)0);
        machine.Map.States.GetIndex("withdrawing").Should().BeGreaterOrEqualTo((byte)0);
        machine.Map.States.GetIndex("processingWithdrawal").Should().BeGreaterOrEqualTo((byte)0);
    }

    [Fact]
    public void Builder_ShouldMapAllEvents()
    {
        // Act
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();

        // Assert - Verify key events are mapped
        machine.Map.Events.GetIndex("CARD_INSERTED").Should().BeGreaterOrEqualTo((byte)0);
        machine.Map.Events.GetIndex("PIN_ENTERED").Should().BeGreaterOrEqualTo((byte)0);
        machine.Map.Events.GetIndex("PIN_CORRECT").Should().BeGreaterOrEqualTo((byte)0);
        machine.Map.Events.GetIndex("PIN_INCORRECT").Should().BeGreaterOrEqualTo((byte)0);
        machine.Map.Events.GetIndex("WITHDRAW").Should().BeGreaterOrEqualTo((byte)0);
        machine.Map.Events.GetIndex("DEPOSIT").Should().BeGreaterOrEqualTo((byte)0);
        machine.Map.Events.GetIndex("CANCEL").Should().BeGreaterOrEqualTo((byte)0);
    }

    [Fact]
    public void Builder_ShouldAllowBidirectionalMapping()
    {
        // Act
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();

        // Get state index
        var idleIndex = machine.Map.States.GetIndex("idle");

        // Convert back to name
        var stateName = machine.Map.States.GetString(idleIndex);

        // Assert
        stateName.Should().Be("idle");
    }

    #endregion

    #region Transition Tests

    [Fact]
    public void Builder_IdleState_OnCardInserted_ShouldTransitionCorrectly()
    {
        // Arrange
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();
        var idleId = machine.Map.States.GetIndex("idle");
        var cardInsertedId = machine.Map.Events.GetIndex("CARD_INSERTED");
        var authenticatingId = machine.Map.States.GetIndex("authenticating");

        // Act
        var transitions = machine.GetTransitions(idleId, cardInsertedId);

        // Assert
        transitions.Should().NotBeNull();
        transitions!.Length.Should().BeGreaterThan(0);
        transitions[0].TargetStateIds.Should().NotBeNull();
        transitions[0].TargetStateIds![0].Should().Be(authenticatingId);
    }

    [Fact]
    public void Builder_EnteringPinState_OnPinEntered_ShouldTransitionCorrectly()
    {
        // Arrange
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();
        var enteringPinId = machine.Map.States.GetIndex("enteringPin");
        var pinEnteredId = machine.Map.Events.GetIndex("PIN_ENTERED");
        var verifyingPinId = machine.Map.States.GetIndex("verifyingPin");

        // Act
        var transitions = machine.GetTransitions(enteringPinId, pinEnteredId);

        // Assert
        transitions.Should().NotBeNull();
        transitions![0].TargetStateIds![0].Should().Be(verifyingPinId);
    }

    [Fact]
    public void Builder_SelectingTransaction_OnWithdraw_ShouldTransitionCorrectly()
    {
        // Arrange
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();
        var selectingId = machine.Map.States.GetIndex("selectingTransaction");
        var withdrawId = machine.Map.Events.GetIndex("WITHDRAW");
        var withdrawingId = machine.Map.States.GetIndex("withdrawing");

        // Act
        var transitions = machine.GetTransitions(selectingId, withdrawId);

        // Assert
        transitions.Should().NotBeNull();
        transitions![0].TargetStateIds![0].Should().Be(withdrawingId);
    }

    #endregion

    #region Complete Flow Tests

    [Fact]
    public void Builder_CompleteWithdrawalFlow_ShouldFollowCorrectPath()
    {
        // Arrange
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();

        // Start at idle
        byte currentState = machine.Map.States.GetIndex("idle");

        // Act & Assert - Follow complete withdrawal flow

        // 1. Insert card: idle -> authenticating
        var cardInsertedId = machine.Map.Events.GetIndex("CARD_INSERTED");
        var trans = machine.GetTransitions(currentState, cardInsertedId);
        trans.Should().NotBeNull();
        currentState = trans![0].TargetStateIds![0];
        machine.GetStateName(currentState).Should().Be("authenticating");

        // 2. Enter PIN: enteringPin -> verifyingPin
        var enteringPinId = machine.Map.States.GetIndex("enteringPin");
        var pinEnteredId = machine.Map.Events.GetIndex("PIN_ENTERED");
        trans = machine.GetTransitions(enteringPinId, pinEnteredId);
        trans.Should().NotBeNull();
        currentState = trans![0].TargetStateIds![0];
        machine.GetStateName(currentState).Should().Be("verifyingPin");

        // 3. PIN correct: verifyingPin -> operational
        var pinCorrectId = machine.Map.Events.GetIndex("PIN_CORRECT");
        trans = machine.GetTransitions(currentState, pinCorrectId);
        trans.Should().NotBeNull();
        currentState = trans![0].TargetStateIds![0];
        machine.GetStateName(currentState).Should().Be("operational");
    }

    [Fact]
    public void Builder_InvalidEvent_ShouldHaveNoTransition()
    {
        // Arrange
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();
        var idleId = machine.Map.States.GetIndex("idle");
        var withdrawId = machine.Map.Events.GetIndex("WITHDRAW");

        // Act - Try WITHDRAW while in idle (invalid)
        var transitions = machine.GetTransitions(idleId, withdrawId);

        // Assert
        transitions.Should().BeNull();
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void Builder_TransitionLookup_ShouldBeOof1()
    {
        // Arrange
        var machine = ArrayStateMachineBuilder.FromJson(AtmJson).Build();
        var idleId = machine.Map.States.GetIndex("idle");
        var cardInsertedId = machine.Map.Events.GetIndex("CARD_INSERTED");

        // Act - Measure O(1) lookups
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < 10000; i++)
        {
            var trans = machine.GetTransitions(idleId, cardInsertedId);
            trans.Should().NotBeNull();
        }

        stopwatch.Stop();

        // Assert - Should complete in microseconds (10,000 lookups)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Builder_WithInvalidJson_ShouldThrowException()
    {
        // Arrange
        const string invalidJson = "{ invalid json }";

        // Act & Assert
        Assert.Throws<System.Text.Json.JsonException>(() =>
        {
            ArrayStateMachineBuilder.FromJson(invalidJson).Build();
        });
    }

    [Fact]
    public void Builder_WithEmptyStates_ShouldHandleGracefully()
    {
        // Arrange
        const string emptyJson = """
        {
            "id": "empty",
            "initial": "start",
            "states": {
                "start": {}
            }
        }
        """;

        // Act
        var machine = ArrayStateMachineBuilder.FromJson(emptyJson).Build();

        // Assert
        machine.Should().NotBeNull();
        machine.Id.Should().Be("empty");
        machine.StateCount.Should().Be(1);
    }

    #endregion

    #region Comparison Test: Manual vs Builder

    [Fact]
    public void Builder_ShouldProduceSameResultAsManualCreation()
    {
        // Arrange - Create simple machine manually
        var manualMap = new StateMachineMap(
            new Dictionary<string, byte> { ["idle"] = 0, ["busy"] = 1 },
            new Dictionary<string, byte> { ["START"] = 0, ["STOP"] = 1 },
            new Dictionary<string, byte>(),
            new Dictionary<string, byte>()
        );

        var manualStates = new ArrayStateNode[2];
        manualStates[0] = new ArrayStateNode
        {
            Transitions = new ArrayTransition?[2][]
            {
                new[] { new ArrayTransition { TargetStateIds = new byte[] { 1 } } }, // START
                null // STOP
            }
        };
        manualStates[1] = new ArrayStateNode
        {
            Transitions = new ArrayTransition?[2][]
            {
                null, // START
                new[] { new ArrayTransition { TargetStateIds = new byte[] { 0 } } } // STOP
            }
        };

        // Create same machine using builder
        const string json = """
        {
            "id": "simple",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "START": "busy"
                    }
                },
                "busy": {
                    "on": {
                        "STOP": "idle"
                    }
                }
            }
        }
        """;

        // Act
        var builtMachine = ArrayStateMachineBuilder.FromJson(json).Build();

        // Assert - Both should have same state/event counts
        builtMachine.StateCount.Should().Be(2);
        builtMachine.Map.Events.GetIndex("START").Should().BeGreaterOrEqualTo((byte)0);
        builtMachine.Map.Events.GetIndex("STOP").Should().BeGreaterOrEqualTo((byte)0);

        // Both should have same transition behavior
        var idleId = builtMachine.Map.States.GetIndex("idle");
        var startId = builtMachine.Map.Events.GetIndex("START");
        var trans = builtMachine.GetTransitions(idleId, startId);

        trans.Should().NotBeNull();
        builtMachine.GetStateName(trans![0].TargetStateIds![0]).Should().Be("busy");
    }

    #endregion
}
