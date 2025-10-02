using Xunit;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ComplexMachine;

public class AtmStateMachineTests : OrchestratorTestBase
{
    private IPureStateMachine? _currentMachine;

    StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

    private async Task<IPureStateMachine> CreateStateMachine(string uniqueId)
    {
        // Define actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logEntry"] = (ctx) => {
                var underlying = GetUnderlying();
                // Log entry
            },
            ["logExit"] = (ctx) => {
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
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("idle", currentState);
    }

    [Fact]
    public async Task TestCardInserted()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("enteringPin", currentState);
    }

    [Fact]
    public async Task TestPinEnteredCorrectly()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_CORRECT");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("selectingTransaction", currentState);
        Assert.Contains("noReceipt", currentState);
    }

    [Fact]
    public async Task TestPinEnteredIncorrectly()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_INCORRECT");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("enteringPin", currentState);
    }

    [Fact]
    public async Task TestWithdrawTransactionSuccess()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_CORRECT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "WITHDRAW");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "AMOUNT_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "SUCCESS");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("idle", currentState);
    }

    [Fact]
    public async Task TestDepositTransactionFailure()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_CORRECT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "DEPOSIT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "AMOUNT_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "FAILURE");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("depositing", currentState);
        Assert.Contains("noReceipt", currentState);
    }

    [Fact]
    public async Task TestCancelDuringTransaction()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_CORRECT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "WITHDRAW");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "CANCEL");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("selectingTransaction", currentState);
        Assert.Contains("noReceipt", currentState);
    }

    [Fact]
    public async Task TestRequestReceipt()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_CORRECT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "REQUEST_RECEIPT");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("selectingTransaction", currentState);
        Assert.Contains("printingReceipt", currentState);
    }

    [Fact]
    public async Task TestReceiptPrinted()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_CORRECT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "REQUEST_RECEIPT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "RECEIPT_PRINTED");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("selectingTransaction", currentState);
        Assert.Contains("noReceipt", currentState);
    }

    [Fact]
    public async Task TestBalanceCheck()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_CORRECT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "BALANCE");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "BALANCE_SHOWN");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("selectingTransaction", currentState);
        Assert.Contains("noReceipt", currentState);
    }

    [Fact]
    public async Task TestNestedCancelDuringTransaction()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_CORRECT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "WITHDRAW");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "CANCEL");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("selectingTransaction", currentState);
        Assert.Contains("noReceipt", currentState);
    }

    [Fact]
    public async Task TestInvalidTransition()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "INVALID_EVENT");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("enteringPin", currentState); // Should remain in the same state
    }

    [Fact]
    public async Task TestCancelAfterCardInserted()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "CANCEL");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("idle", currentState);
    }

    [Fact]
    public async Task TestWithdrawFailureAndRetry()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_CORRECT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "WITHDRAW");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "AMOUNT_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "FAILURE");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("withdrawing", currentState);
        Assert.Contains("noReceipt", currentState);

        await SendEventAsync("TEST", uniqueId, "AMOUNT_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "SUCCESS");
        await Task.Delay(100);
        currentState = _currentMachine!.CurrentState;
        Assert.Contains("idle", currentState);
    }

    [Fact]
    public async Task TestDepositFailureAndRetry()
    {
        var uniqueId = $"atm_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);
        await SendEventAsync("TEST", uniqueId, "CARD_INSERTED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "PIN_CORRECT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "DEPOSIT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "AMOUNT_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "FAILURE");
        await Task.Delay(100);
        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("depositing", currentState);
        Assert.Contains("noReceipt", currentState);

        await SendEventAsync("TEST", uniqueId, "AMOUNT_ENTERED");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "SUCCESS");
        await Task.Delay(100);
        currentState = _currentMachine!.CurrentState;
        Assert.Contains("idle", currentState);
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
