using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for ATM state machine
/// Covers authentication, parallel states, and transaction processing
/// </summary>
public class AtmStateMachineTests : XStateTestKit
{

    private string GetAtmJson() => """
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

    [Fact]
    public async Task TestInitialState()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal("idle", snapshot.CurrentState);
    }

    [Fact]
    public void TestCardInserted()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act & Assert
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"),
            "state to contain 'enteringPin'");
    }

    [Fact]
    public void TestPinEnteredCorrectly()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"),
            "state to contain 'enteringPin'");
        SendEventAndWait(machine, "PIN_ENTERED",
            s => s.CurrentState.Contains("verifyingPin"),
            "state to contain 'verifyingPin'");
        SendEventAndWait(machine, "PIN_CORRECT",
            s => s.CurrentState.Contains("selectingTransaction") && s.CurrentState.Contains("noReceipt"),
            "state to contain both 'selectingTransaction' and 'noReceipt'");
    }

    [Fact]
    public void TestPinEnteredIncorrectly()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"),
            "state to contain 'enteringPin'");
        SendEventAndWait(machine, "PIN_ENTERED",
            s => s.CurrentState.Contains("verifyingPin"),
            "state to contain 'verifyingPin'");
        SendEventAndWait(machine, "PIN_INCORRECT",
            s => s.CurrentState.Contains("enteringPin"),
            "state to return to 'enteringPin'");
    }

    [Fact]
    public void TestWithdrawTransactionSuccess()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"), "enteringPin");
        SendEventAndWait(machine, "PIN_ENTERED",
            s => s.CurrentState.Contains("verifyingPin"), "verifyingPin");
        SendEventAndWait(machine, "PIN_CORRECT",
            s => s.CurrentState.Contains("selectingTransaction"), "selectingTransaction");
        SendEventAndWait(machine, "WITHDRAW",
            s => s.CurrentState.Contains("withdrawing"), "withdrawing");
        SendEventAndWait(machine, "AMOUNT_ENTERED",
            s => s.CurrentState.Contains("processingWithdrawal"), "processingWithdrawal");
        SendEventAndWait(machine, "SUCCESS",
            s => s.CurrentState == "idle", "idle");
    }

    [Fact]
    public void TestDepositTransactionFailure()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"), "enteringPin");
        SendEventAndWait(machine, "PIN_ENTERED",
            s => s.CurrentState.Contains("verifyingPin"), "verifyingPin");
        SendEventAndWait(machine, "PIN_CORRECT",
            s => s.CurrentState.Contains("selectingTransaction"), "selectingTransaction");
        SendEventAndWait(machine, "DEPOSIT",
            s => s.CurrentState.Contains("depositing"), "depositing");
        SendEventAndWait(machine, "AMOUNT_ENTERED",
            s => s.CurrentState.Contains("processingDeposit"), "processingDeposit");
        SendEventAndWait(machine, "FAILURE",
            s => s.CurrentState.Contains("depositing") && s.CurrentState.Contains("noReceipt"),
            "depositing with noReceipt");
    }

    [Fact]
    public void TestCancelDuringTransaction()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"), "enteringPin");
        SendEventAndWait(machine, "PIN_ENTERED",
            s => s.CurrentState.Contains("verifyingPin"), "verifyingPin");
        SendEventAndWait(machine, "PIN_CORRECT",
            s => s.CurrentState.Contains("selectingTransaction"), "selectingTransaction");
        SendEventAndWait(machine, "WITHDRAW",
            s => s.CurrentState.Contains("withdrawing"), "withdrawing");
        SendEventAndWait(machine, "CANCEL",
            s => s.CurrentState.Contains("selectingTransaction") && s.CurrentState.Contains("noReceipt"),
            "selectingTransaction with noReceipt");
    }

    [Fact]
    public void TestRequestReceipt()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"), "enteringPin");
        SendEventAndWait(machine, "PIN_ENTERED",
            s => s.CurrentState.Contains("verifyingPin"), "verifyingPin");
        SendEventAndWait(machine, "PIN_CORRECT",
            s => s.CurrentState.Contains("selectingTransaction"), "selectingTransaction");
        SendEventAndWait(machine, "REQUEST_RECEIPT",
            s => s.CurrentState.Contains("selectingTransaction") && s.CurrentState.Contains("printingReceipt"),
            "selectingTransaction with printingReceipt");
    }

    [Fact]
    public void TestReceiptPrinted()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"), "enteringPin");
        SendEventAndWait(machine, "PIN_ENTERED",
            s => s.CurrentState.Contains("verifyingPin"), "verifyingPin");
        SendEventAndWait(machine, "PIN_CORRECT",
            s => s.CurrentState.Contains("selectingTransaction"), "selectingTransaction");
        SendEventAndWait(machine, "REQUEST_RECEIPT",
            s => s.CurrentState.Contains("printingReceipt"), "printingReceipt");
        SendEventAndWait(machine, "RECEIPT_PRINTED",
            s => s.CurrentState.Contains("selectingTransaction") && s.CurrentState.Contains("noReceipt"),
            "selectingTransaction with noReceipt");
    }

    [Fact]
    public void TestBalanceCheck()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"), "enteringPin");
        SendEventAndWait(machine, "PIN_ENTERED",
            s => s.CurrentState.Contains("verifyingPin"), "verifyingPin");
        SendEventAndWait(machine, "PIN_CORRECT",
            s => s.CurrentState.Contains("selectingTransaction"), "selectingTransaction");
        SendEventAndWait(machine, "BALANCE",
            s => s.CurrentState.Contains("checkingBalance"), "checkingBalance");
        SendEventAndWait(machine, "BALANCE_SHOWN",
            s => s.CurrentState.Contains("selectingTransaction") && s.CurrentState.Contains("noReceipt"),
            "selectingTransaction with noReceipt");
    }

    [Fact]
    public void TestNestedCancelDuringTransaction()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"), "enteringPin");
        SendEventAndWait(machine, "PIN_ENTERED",
            s => s.CurrentState.Contains("verifyingPin"), "verifyingPin");
        SendEventAndWait(machine, "PIN_CORRECT",
            s => s.CurrentState.Contains("selectingTransaction"), "selectingTransaction");
        SendEventAndWait(machine, "WITHDRAW",
            s => s.CurrentState.Contains("withdrawing"), "withdrawing");
        SendEventAndWait(machine, "CANCEL",
            s => s.CurrentState.Contains("selectingTransaction") && s.CurrentState.Contains("noReceipt"),
            "selectingTransaction with noReceipt");
    }

    [Fact]
    public void TestInvalidTransition()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"), "enteringPin");

        // Send invalid event and verify state didn't change
        machine.Tell(new SendEvent("INVALID_EVENT"));
        WaitForState(machine,
            s => s.CurrentState.Contains("enteringPin"),
            "state to remain in 'enteringPin'");
    }

    [Fact]
    public void TestCancelAfterCardInserted()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"), "enteringPin");
        SendEventAndWait(machine, "CANCEL",
            s => s.CurrentState == "idle", "idle");
    }

    [Fact]
    public void TestWithdrawFailureAndRetry()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act - First attempt fails
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"), "enteringPin");
        SendEventAndWait(machine, "PIN_ENTERED",
            s => s.CurrentState.Contains("verifyingPin"), "verifyingPin");
        SendEventAndWait(machine, "PIN_CORRECT",
            s => s.CurrentState.Contains("selectingTransaction"), "selectingTransaction");
        SendEventAndWait(machine, "WITHDRAW",
            s => s.CurrentState.Contains("withdrawing"), "withdrawing");
        SendEventAndWait(machine, "AMOUNT_ENTERED",
            s => s.CurrentState.Contains("processingWithdrawal"), "processingWithdrawal");
        SendEventAndWait(machine, "FAILURE",
            s => s.CurrentState.Contains("withdrawing") && s.CurrentState.Contains("noReceipt"),
            "withdrawing with noReceipt");

        // Act - Retry succeeds
        SendEventAndWait(machine, "AMOUNT_ENTERED",
            s => s.CurrentState.Contains("processingWithdrawal"), "processingWithdrawal");
        SendEventAndWait(machine, "SUCCESS",
            s => s.CurrentState == "idle", "idle");
    }

    [Fact]
    public void TestDepositFailureAndRetry()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetAtmJson())
            .BuildAndStart();

        // Act - First attempt fails
        SendEventAndWait(machine, "CARD_INSERTED",
            s => s.CurrentState.Contains("enteringPin"), "enteringPin");
        SendEventAndWait(machine, "PIN_ENTERED",
            s => s.CurrentState.Contains("verifyingPin"), "verifyingPin");
        SendEventAndWait(machine, "PIN_CORRECT",
            s => s.CurrentState.Contains("selectingTransaction"), "selectingTransaction");
        SendEventAndWait(machine, "DEPOSIT",
            s => s.CurrentState.Contains("depositing"), "depositing");
        SendEventAndWait(machine, "AMOUNT_ENTERED",
            s => s.CurrentState.Contains("processingDeposit"), "processingDeposit");
        SendEventAndWait(machine, "FAILURE",
            s => s.CurrentState.Contains("depositing") && s.CurrentState.Contains("noReceipt"),
            "depositing with noReceipt");

        // Act - Retry succeeds
        SendEventAndWait(machine, "AMOUNT_ENTERED",
            s => s.CurrentState.Contains("processingDeposit"), "processingDeposit");
        SendEventAndWait(machine, "SUCCESS",
            s => s.CurrentState == "idle", "idle");
    }
}
