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
    public async Task TestInitialState()
    {
        var uniqueId = "TestInitialState_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        var initialState = stateMachine.GetActiveStateString();
        initialState.AssertEquivalence($"#{uniqueId}.idle");
    }

    [Fact]
    public async Task TestCardInserted()
    {
        var uniqueId = "TestCardInserted_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        var currentState = await stateMachine.SendAsyncWithState("CARD_INSERTED");
        currentState.AssertEquivalence($"#{uniqueId}.authenticating.enteringPin");
    }

    [Fact]
    public async Task TestPinEnteredCorrectly()
    {
        var uniqueId = "TestPinEnteredCorrectly_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        await stateMachine.SendAsync("PIN_ENTERED");
        var currentState = await stateMachine.SendAsyncWithState("PIN_CORRECT");
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.selectingTransaction;#{uniqueId}.operational.receipt.noReceipt");
    }

    [Fact]
    public async Task TestPinEnteredIncorrectly()
    {
        var uniqueId = "TestPinEnteredIncorrectly_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        await stateMachine.SendAsync("PIN_ENTERED");
        var currentState = await stateMachine.SendAsyncWithState("PIN_INCORRECT");
        currentState.AssertEquivalence($"#{uniqueId}.authenticating.enteringPin");
    }

    [Fact]
    public async Task TestWithdrawTransactionSuccess()
    {
        var uniqueId = "TestWithdrawTransactionSuccess_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("WITHDRAW");
        await stateMachine.SendAsync("AMOUNT_ENTERED");
        var currentState = await stateMachine.SendAsyncWithState("SUCCESS");
        currentState.AssertEquivalence($"#{uniqueId}.idle");
    }

    [Fact]
    public async Task TestDepositTransactionFailure()
    {
        var uniqueId = "TestDepositTransactionFailure_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("DEPOSIT");
        await stateMachine.SendAsync("AMOUNT_ENTERED");
        var currentState = await stateMachine.SendAsyncWithState("FAILURE");
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.depositing;#{uniqueId}.operational.receipt.noReceipt");
    }

    [Fact]
    public async Task TestCancelDuringTransaction()
    {
        var uniqueId = "TestCancelDuringTransaction_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        await stateMachine.SendAsync("WITHDRAW");
        var currentState = await stateMachine.SendAsyncWithState("CANCEL");
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.selectingTransaction;#{uniqueId}.operational.receipt.noReceipt");
    }

    [Fact]
    public async Task TestRequestReceipt()
    {
        var uniqueId = "TestRequestReceipt_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        await stateMachine.SendAsync("PIN_CORRECT");
        var currentState = await stateMachine.SendAsyncWithState("REQUEST_RECEIPT");
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.selectingTransaction;#{uniqueId}.operational.receipt.printingReceipt");
    }

    [Fact]
    public async Task TestReceiptPrinted()
    {
        var uniqueId = "TestReceiptPrinted_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        await stateMachine.SendAsync("REQUEST_RECEIPT");
        var currentState = await stateMachine.SendAsyncWithState("RECEIPT_PRINTED");
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.selectingTransaction;#{uniqueId}.operational.receipt.noReceipt");
    }

    [Fact]
    public async Task TestBalanceCheck()
    {
        var uniqueId = "TestBalanceCheck_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        await stateMachine.SendAsync("BALANCE");
        var currentState = await stateMachine.SendAsyncWithState("BALANCE_SHOWN");
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.selectingTransaction;#{uniqueId}.operational.receipt.noReceipt");
    }

    [Fact]
    public async Task TestNestedCancelDuringTransaction()
    {
        var uniqueId = "TestNestedCancelDuringTransaction_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        await stateMachine.SendAsync("WITHDRAW");
        var currentState = await stateMachine.SendAsyncWithState("CANCEL");
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.selectingTransaction;#{uniqueId}.operational.receipt.noReceipt");
    }

    [Fact]
    public async Task TestInvalidTransition()
    {
        var uniqueId = "TestInvalidTransition_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        await stateMachine.SendAsync("CARD_INSERTED");
        var currentState = await stateMachine.SendAsyncWithState("INVALID_EVENT");
        currentState.AssertEquivalence($"#{uniqueId}.authenticating.enteringPin"); // Should remain in the same state
    }

    [Fact]
    public async Task TestCancelAfterCardInserted()
    {
        var uniqueId = "TestCancelAfterCardInserted_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        await stateMachine.SendAsync("CARD_INSERTED");
        var currentState = await stateMachine.SendAsyncWithState("CANCEL");
        currentState.AssertEquivalence($"#{uniqueId}.idle");
    }

    [Fact]
    public async Task TestWithdrawFailureAndRetry()
    {
        var uniqueId = "TestWithdrawFailureAndRetry_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("WITHDRAW");
        await stateMachine.SendAsync("AMOUNT_ENTERED");
        var currentState = await stateMachine.SendAsyncWithState("FAILURE");
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.withdrawing;#{uniqueId}.operational.receipt.noReceipt");

        await stateMachine.SendAsync("AMOUNT_ENTERED");
        currentState = await stateMachine.SendAsyncWithState("SUCCESS");
        currentState.AssertEquivalence($"#{uniqueId}.idle");
    }

    [Fact]
    public async Task TestDepositFailureAndRetry()
    {
        var uniqueId = "TestDepositFailureAndRetry_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);
        stateMachine.Send("CARD_INSERTED");
        stateMachine.Send("PIN_ENTERED");
        stateMachine.Send("PIN_CORRECT");
        stateMachine.Send("DEPOSIT");
        await stateMachine.SendAsync("AMOUNT_ENTERED");
        var currentState = await stateMachine.SendAsyncWithState("FAILURE");
        currentState.AssertEquivalence($"#{uniqueId}.operational.transaction.depositing;#{uniqueId}.operational.receipt.noReceipt");

        await stateMachine.SendAsync("AMOUNT_ENTERED");
        currentState = await stateMachine.SendAsyncWithState("SUCCESS");
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
