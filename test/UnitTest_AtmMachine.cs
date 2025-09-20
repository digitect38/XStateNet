using Xunit;

using XStateNet;
using XStateNet.UnitTest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
namespace ComplexMachine;

public class AtmStateMachineTests : IDisposable
{
    private StateMachine CreateStateMachine(string uniqueId)
    {
        // Define actions
        var actions = new ActionMap()
        {
            ["logEntry"] = [new NamedAction("logEntry", (sm) => StateMachine.Log("Entering state"))],
            ["logExit"] = [new NamedAction("logExit", (sm) => StateMachine.Log("Exiting state"))]
        };

        // Define guards
        var guards = new GuardMap();

        var jsonScript = GetJson(uniqueId);

        // Load state machine from JSON
        var stateMachine = (StateMachine)StateMachine.CreateFromScript(jsonScript, actions, guards).Start();
        return stateMachine;
    }

    public void Dispose()
    {
        // Cleanup resources if needed
    }

    [Fact]
    public void TestInitialState()
    {
        var uniqueId = "TestInitialState_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        var initialState = stateMachine.GetActiveStateString();
        initialState.AssertEquivalence($"#{uniqueId}.idle");
    }

    [Fact]
    public void TestCardInserted()
    {
        var uniqueId = "TestCardInserted_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.authenticating.enteringPin");
    }

    [Fact]
    public void TestPinEnteredCorrectly()
    {
        var uniqueId = "TestPinEnteredCorrectly_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.selectingTransaction;#{uniqueId}.operational.receipt.noReceipt");
    }

    [Fact]
    public void TestPinEnteredIncorrectly()
    {
        var uniqueId = "TestPinEnteredIncorrectly_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_INCORRECT");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.authenticating.enteringPin");
    }

    [Fact]
    public void TestWithdrawTransactionSuccess()
    {
        var uniqueId = "TestWithdrawTransactionSuccess_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("WITHDRAW");
        stateMachine.Send("AMOUNT_ENTERED");
        stateMachine.Send("SUCCESS");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.idle");
    }

    [Fact]
    public void TestDepositTransactionFailure()
    {
        var uniqueId = "TestDepositTransactionFailure_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("DEPOSIT");
        stateMachine.Send("AMOUNT_ENTERED");
        stateMachine.Send("FAILURE");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.depositing;#{uniqueId}.operational.receipt.noReceipt");
    }

    [Fact]
    public void TestCancelDuringTransaction()
    {
        var uniqueId = "TestCancelDuringTransaction_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("WITHDRAW");
        stateMachine.Send("CANCEL");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.selectingTransaction;#{uniqueId}.operational.receipt.noReceipt");
    }

    [Fact]
    public void TestRequestReceipt()
    {
        var uniqueId = "TestRequestReceipt_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("REQUEST_RECEIPT");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.selectingTransaction;#{uniqueId}.operational.receipt.printingReceipt");
    }

    [Fact]
    public void TestReceiptPrinted()
    {
        var uniqueId = "TestReceiptPrinted_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("REQUEST_RECEIPT");
        stateMachine.Send("RECEIPT_PRINTED");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.selectingTransaction;#{uniqueId}.operational.receipt.noReceipt");
    }

    [Fact]
    public void TestBalanceCheck()
    {
        var uniqueId = "TestBalanceCheck_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("BALANCE");
        stateMachine.Send("BALANCE_SHOWN");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.selectingTransaction;#{uniqueId}.operational.receipt.noReceipt");
    }

    [Fact]
    public void TestNestedCancelDuringTransaction()
    {
        var uniqueId = "TestNestedCancelDuringTransaction_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("WITHDRAW");
        stateMachine.Send("CANCEL");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.selectingTransaction;#{uniqueId}.operational.receipt.noReceipt");
    }

    [Fact]
    public void TestInvalidTransition()
    {
        var uniqueId = "TestInvalidTransition_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("INVALID_EVENT");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.authenticating.enteringPin"); // Should remain in the same state
    }

    [Fact]
    public void TestCancelAfterCardInserted()
    {
        var uniqueId = "TestCancelAfterCardInserted_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("CANCEL");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.idle");
    }

    [Fact]
    public void TestWithdrawFailureAndRetry()
    {
        var uniqueId = "TestWithdrawFailureAndRetry_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("WITHDRAW");
        stateMachine.Send("AMOUNT_ENTERED");
        stateMachine.Send("FAILURE");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.withdrawing;#{uniqueId}.operational.receipt.noReceipt");

        stateMachine.Send("AMOUNT_ENTERED");
        stateMachine.Send("SUCCESS");
        currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.idle");
    }

    [Fact]
    public void TestDepositFailureAndRetry()
    {
        var uniqueId = "TestDepositFailureAndRetry_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("DEPOSIT");
        stateMachine.Send("AMOUNT_ENTERED");
        stateMachine.Send("FAILURE");
        var currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.depositing;#{uniqueId}.operational.receipt.noReceipt");

        stateMachine.Send("AMOUNT_ENTERED");
        stateMachine.Send("SUCCESS");
        currentState = stateMachine.GetActiveStateString();
        currentState.AssertEquivalence($"#{uniqueId}.idle");
    }

    private string GetJson(string uniqueId) => @$"{{
          id: '{uniqueId}',
          initial: 'idle',
          states: {{
            idle: {{
              on: {{ CARD_INSERTED: 'authenticating' }}
            }},
            authenticating: {{
              initial: 'enteringPin',
              states: {{
                enteringPin: {{
                  on: {{
                    PIN_ENTERED: 'verifyingPin',
                    CANCEL: '#{uniqueId}.idle'
                  }}
                }},
                verifyingPin: {{
                  on: {{
                    PIN_CORRECT: '#{uniqueId}.operational',
                    PIN_INCORRECT: 'enteringPin'
                  }}
                }}
              }}
            }},
            operational: {{
              type: 'parallel',
              states: {{
                transaction: {{
                  initial: 'selectingTransaction',
                  states: {{
                    selectingTransaction: {{
                      on: {{
                        WITHDRAW: 'withdrawing',
                        DEPOSIT: 'depositing',
                        BALANCE: 'checkingBalance',
                        CANCEL: '#{uniqueId}.idle'
                      }}
                    }},
                    withdrawing: {{
                      on: {{
                        AMOUNT_ENTERED: 'processingWithdrawal',
                        CANCEL: 'selectingTransaction'
                      }}
                    }},
                    processingWithdrawal: {{
                      on: {{
                        SUCCESS: '#{uniqueId}.idle',
                        FAILURE: 'withdrawing'
                      }}
                    }},
                    depositing: {{
                      on: {{
                        AMOUNT_ENTERED: 'processingDeposit',
                        CANCEL: 'selectingTransaction'
                      }}
                    }},
                    processingDeposit: {{
                      on: {{
                        SUCCESS: '#{uniqueId}.idle',
                        FAILURE: 'depositing'
                      }}
                    }},
                    checkingBalance: {{
                      on: {{
                        BALANCE_SHOWN: 'selectingTransaction',
                        CANCEL: 'selectingTransaction'
                      }}
                    }}
                  }}
                }},
                receipt: {{
                  initial: 'noReceipt',
                  states: {{
                    noReceipt: {{
                      on: {{ REQUEST_RECEIPT: 'printingReceipt' }}
                    }},
                    printingReceipt: {{
                      on: {{ RECEIPT_PRINTED: 'noReceipt' }}
                    }}
                  }}
                }}
              }}
            }}
          }}
        }}";
}
