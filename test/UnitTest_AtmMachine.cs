using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using Xunit;

namespace ComplexMachine;

public class AtmStateMachineTests : OrchestratorTestBase
{
    private IPureStateMachine? _currentMachine;

    StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

    /// <summary>
    /// Helper method to send event and wait for state condition deterministically
    /// </summary>
    private async Task SendEventAndWaitAsync(
        IPureStateMachine machine,
        string eventName,
        Func<string, bool> stateCondition,
        string description = "state condition",
        int timeoutMs = 5000)
    {
        await SendEventAsync("TEST", machine, eventName);

        var startTime = DateTime.UtcNow;
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            if (stateCondition(machine.CurrentState))
            {
                return;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException($"Expected {description}, but got state: {machine.CurrentState}");
    }

    private async Task<IPureStateMachine> CreateStateMachine(string uniqueId)
    {
        // Define actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logEntry"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                // Log entry
            },
            ["logExit"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                // Log exit
            }
        };

        var jsonScript = GetJson(uniqueId);

        // Create and start machine using orchestrator
        _currentMachine = CreateMachine(uniqueId, jsonScript, actions);
        await _currentMachine.StartAsync();
        return _currentMachine;
    }

    [Fact]
    public async Task TestInitialState()
    {
        var stateMachine = await CreateStateMachine("atm");
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("idle", currentState);
    }

    [Fact]
    public async Task TestCardInserted()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "state to contain 'enteringPin'");
    }

    [Fact]
    public async Task TestPinEnteredCorrectly()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_ENTERED",
            s => s.Contains("verifyingPin"), "verifyingPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_CORRECT",
            s => s.Contains("selectingTransaction") && s.Contains("noReceipt"),
            "selectingTransaction with noReceipt");
    }

    [Fact]
    public async Task TestPinEnteredIncorrectly()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_ENTERED",
            s => s.Contains("verifyingPin"), "verifyingPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_INCORRECT",
            s => s.Contains("enteringPin"), "enteringPin");
    }

    [Fact]
    public async Task TestWithdrawTransactionSuccess()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_ENTERED",
            s => s.Contains("verifyingPin"), "verifyingPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_CORRECT",
            s => s.Contains("selectingTransaction"), "selectingTransaction");
        await SendEventAndWaitAsync(stateMachine, "WITHDRAW",
            s => s.Contains("withdrawing"), "withdrawing");
        await SendEventAndWaitAsync(stateMachine, "AMOUNT_ENTERED",
            s => s.Contains("processingWithdrawal"), "processingWithdrawal");
        await SendEventAndWaitAsync(stateMachine, "SUCCESS",
            s => s.Contains("idle"), "idle");
    }

    [Fact]
    public async Task TestDepositTransactionFailure()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_ENTERED",
            s => s.Contains("verifyingPin"), "verifyingPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_CORRECT",
            s => s.Contains("selectingTransaction"), "selectingTransaction");
        await SendEventAndWaitAsync(stateMachine, "DEPOSIT",
            s => s.Contains("depositing"), "depositing");
        await SendEventAndWaitAsync(stateMachine, "AMOUNT_ENTERED",
            s => s.Contains("processingDeposit"), "processingDeposit");
        await SendEventAndWaitAsync(stateMachine, "FAILURE",
            s => s.Contains("depositing") && s.Contains("noReceipt"),
            "depositing with noReceipt");
    }

    [Fact]
    public async Task TestCancelDuringTransaction()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_ENTERED",
            s => s.Contains("verifyingPin"), "verifyingPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_CORRECT",
            s => s.Contains("selectingTransaction"), "selectingTransaction");
        await SendEventAndWaitAsync(stateMachine, "WITHDRAW",
            s => s.Contains("withdrawing"), "withdrawing");
        await SendEventAndWaitAsync(stateMachine, "CANCEL",
            s => s.Contains("selectingTransaction") && s.Contains("noReceipt"),
            "selectingTransaction with noReceipt");
    }

    [Fact]
    public async Task TestRequestReceipt()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_ENTERED",
            s => s.Contains("verifyingPin"), "verifyingPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_CORRECT",
            s => s.Contains("selectingTransaction"), "selectingTransaction");
        await SendEventAndWaitAsync(stateMachine, "REQUEST_RECEIPT",
            s => s.Contains("selectingTransaction") && s.Contains("printingReceipt"),
            "selectingTransaction with printingReceipt");
    }

    [Fact]
    public async Task TestReceiptPrinted()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_ENTERED",
            s => s.Contains("verifyingPin"), "verifyingPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_CORRECT",
            s => s.Contains("selectingTransaction"), "selectingTransaction");
        await SendEventAndWaitAsync(stateMachine, "REQUEST_RECEIPT",
            s => s.Contains("printingReceipt"), "printingReceipt");
        await SendEventAndWaitAsync(stateMachine, "RECEIPT_PRINTED",
            s => s.Contains("selectingTransaction") && s.Contains("noReceipt"),
            "selectingTransaction with noReceipt");
    }

    [Fact]
    public async Task TestBalanceCheck()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_ENTERED",
            s => s.Contains("verifyingPin"), "verifyingPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_CORRECT",
            s => s.Contains("selectingTransaction"), "selectingTransaction");
        await SendEventAndWaitAsync(stateMachine, "BALANCE",
            s => s.Contains("checkingBalance"), "checkingBalance");
        await SendEventAndWaitAsync(stateMachine, "BALANCE_SHOWN",
            s => s.Contains("selectingTransaction") && s.Contains("noReceipt"),
            "selectingTransaction with noReceipt");
    }

    [Fact]
    public async Task TestNestedCancelDuringTransaction()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_ENTERED",
            s => s.Contains("verifyingPin"), "verifyingPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_CORRECT",
            s => s.Contains("selectingTransaction"), "selectingTransaction");
        await SendEventAndWaitAsync(stateMachine, "WITHDRAW",
            s => s.Contains("withdrawing"), "withdrawing");
        await SendEventAndWaitAsync(stateMachine, "CANCEL",
            s => s.Contains("selectingTransaction") && s.Contains("noReceipt"),
            "selectingTransaction with noReceipt");
    }

    [Fact]
    public async Task TestInvalidTransition()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");

        // Send invalid event and verify state didn't change
        await SendEventAsync("TEST", stateMachine, "INVALID_EVENT");
        await Task.Delay(10); // Small delay to ensure event is processed
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("enteringPin", currentState); // Should remain in the same state
    }

    [Fact]
    public async Task TestCancelAfterCardInserted()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");
        await SendEventAndWaitAsync(stateMachine, "CANCEL",
            s => s.Contains("idle"), "idle");
    }

    [Fact]
    public async Task TestWithdrawFailureAndRetry()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_ENTERED",
            s => s.Contains("verifyingPin"), "verifyingPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_CORRECT",
            s => s.Contains("selectingTransaction"), "selectingTransaction");
        await SendEventAndWaitAsync(stateMachine, "WITHDRAW",
            s => s.Contains("withdrawing"), "withdrawing");
        await SendEventAndWaitAsync(stateMachine, "AMOUNT_ENTERED",
            s => s.Contains("processingWithdrawal"), "processingWithdrawal");
        await SendEventAndWaitAsync(stateMachine, "FAILURE",
            s => s.Contains("withdrawing") && s.Contains("noReceipt"),
            "withdrawing with noReceipt");

        // Retry succeeds
        await SendEventAndWaitAsync(stateMachine, "AMOUNT_ENTERED",
            s => s.Contains("processingWithdrawal"), "processingWithdrawal");
        await SendEventAndWaitAsync(stateMachine, "SUCCESS",
            s => s.Contains("idle"), "idle");
    }

    [Fact]
    public async Task TestDepositFailureAndRetry()
    {
        var stateMachine = await CreateStateMachine("atm");
        await SendEventAndWaitAsync(stateMachine, "CARD_INSERTED",
            s => s.Contains("enteringPin"), "enteringPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_ENTERED",
            s => s.Contains("verifyingPin"), "verifyingPin");
        await SendEventAndWaitAsync(stateMachine, "PIN_CORRECT",
            s => s.Contains("selectingTransaction"), "selectingTransaction");
        await SendEventAndWaitAsync(stateMachine, "DEPOSIT",
            s => s.Contains("depositing"), "depositing");
        await SendEventAndWaitAsync(stateMachine, "AMOUNT_ENTERED",
            s => s.Contains("processingDeposit"), "processingDeposit");
        await SendEventAndWaitAsync(stateMachine, "FAILURE",
            s => s.Contains("depositing") && s.Contains("noReceipt"),
            "depositing with noReceipt");

        // Retry succeeds
        await SendEventAndWaitAsync(stateMachine, "AMOUNT_ENTERED",
            s => s.Contains("processingDeposit"), "processingDeposit");
        await SendEventAndWaitAsync(stateMachine, "SUCCESS",
            s => s.Contains("idle"), "idle");
    }

    private string GetJson(string uniqueId) => @"{
          id: 'atm',
          initial: 'idle',
          states: {
            idle: {
              on: { CARD_INSERTED: 'authenticating' }
            },
            authenticating: {
              initial: 'enteringPin',
              states: {
                enteringPin: {
                  on: {
                    PIN_ENTERED: 'verifyingPin',
                    CANCEL: '#atm.idle'
                  }
                },
                verifyingPin: {
                  on: {
                    PIN_CORRECT: '#atm.operational',
                    PIN_INCORRECT: 'enteringPin'
                  }
                }
              }
            },
            operational: {
              type: 'parallel',
              states: {
                transaction: {
                  initial: 'selectingTransaction',
                  states: {
                    selectingTransaction: {
                      on: {
                        WITHDRAW: 'withdrawing',
                        DEPOSIT: 'depositing',
                        BALANCE: 'checkingBalance',
                        CANCEL: '#atm.idle'
                      }
                    },
                    withdrawing: {
                      on: {
                        AMOUNT_ENTERED: 'processingWithdrawal',
                        CANCEL: 'selectingTransaction'
                      }
                    },
                    processingWithdrawal: {
                      on: {
                        SUCCESS: '#atm.idle',
                        FAILURE: 'withdrawing'
                      }
                    },
                    depositing: {
                      on: {
                        AMOUNT_ENTERED: 'processingDeposit',
                        CANCEL: 'selectingTransaction'
                      }
                    },
                    processingDeposit: {
                      on: {
                        SUCCESS: '#atm.idle',
                        FAILURE: 'depositing'
                      }
                    },
                    checkingBalance: {
                      on: {
                        BALANCE_SHOWN: 'selectingTransaction',
                        CANCEL: 'selectingTransaction'
                      }
                    }
                  }
                },
                receipt: {
                  initial: 'noReceipt',
                  states: {
                    noReceipt: {
                      on: { REQUEST_RECEIPT: 'printingReceipt' }
                    },
                    printingReceipt: {
                      on: { RECEIPT_PRINTED: 'noReceipt' }
                    }
                  }
                }
              }
            }
          }
        }";
}
