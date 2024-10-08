using NUnit.Framework;
using SharpState;
using SharpState.UnitTest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
namespace ComplexMachine;

[TestFixture]
public class AtmStateMachineTests
{
    private StateMachine _stateMachine;
    private ConcurrentDictionary<string, List<NamedAction>> _actions;
    private ConcurrentDictionary<string, NamedGuard> _guards;

    [SetUp]
    public void Setup()
    {
        // Define actions
        _actions = new ConcurrentDictionary<string, List<NamedAction>>
        {
            ["logTransition"] = [new NamedAction("logTransition", (sm) => Console.WriteLine("Transitioning..."))]
        };

        // Define guards (if any, for example purposes we keep it simple)
        _guards = new ConcurrentDictionary<string, NamedGuard>();

        // Load state machine from JSON
        var json = Script;
        _stateMachine = StateMachine.CreateFromScript(json, _actions, _guards).Start();
    }

    [Test]
    public void TestInitialState()
    {
        var initialState = _stateMachine.GetCurrentState();
        "#atm.idle".AssertEquivalence(initialState);
    }

    [Test]
    public void TestCardInserted()
    {
        _stateMachine.Send("CARD_INSERTED");
        var currentState = _stateMachine.GetCurrentState();
        "#atm.authenticating.enteringPin".AssertEquivalence(currentState);
    }

    [Test]
    public void TestPinEnteredCorrectly()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        var currentState = _stateMachine.GetCurrentState();
        "#atm.operational.transaction.selectingTransaction;#atm.operational.receipt.noReceipt".AssertEquivalence(currentState);
    }

    [Test]
    public void TestPinEnteredIncorrectly()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_INCORRECT");
        var currentState = _stateMachine.GetCurrentState();
        "#atm.authenticating.enteringPin".AssertEquivalence(currentState);
    }

    [Test]
    public void TestWithdrawTransactionSuccess()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("WITHDRAW");
        _stateMachine.Send("AMOUNT_ENTERED");
        _stateMachine.Send("SUCCESS");
        var currentState = _stateMachine.GetCurrentState();
        Assert.AreEqual("#atm.idle", currentState);
    }

    [Test]
    public void TestDepositTransactionFailure()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("DEPOSIT");
        _stateMachine.Send("AMOUNT_ENTERED");
        _stateMachine.Send("FAILURE");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.depositing;#atm.operational.receipt.noReceipt");
    }

    [Test]
    public void TestCancelDuringTransaction()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("WITHDRAW");
        _stateMachine.Send("CANCEL");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.selectingTransaction;#atm.operational.receipt.noReceipt");
    }

    [Test]
    public void TestRequestReceipt()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("REQUEST_RECEIPT");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.selectingTransaction;#atm.operational.receipt.printingReceipt");
    }

    [Test]
    public void TestReceiptPrinted()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("REQUEST_RECEIPT");
        _stateMachine.Send("RECEIPT_PRINTED");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.selectingTransaction;#atm.operational.receipt.noReceipt");
    }

    string Script =>
        @"{
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

[TestFixture]
public class AtmStateMachineTests2
{
    private StateMachine _stateMachine;
    private ConcurrentDictionary<string, List<NamedAction>> _actions;
    private ConcurrentDictionary<string, NamedGuard> _guards;

    [SetUp]
    public void Setup()
    {
        // Define actions
        _actions = new ConcurrentDictionary<string, List<NamedAction>>
        {
            ["logEntry"] = [new NamedAction("logEntry", (sm) => Console.WriteLine("Entering state"))],
            ["logExit"] = [new NamedAction("logExit", (sm) => Console.WriteLine("Exiting state"))]
        };

        // Define guards
        _guards = new ConcurrentDictionary<string, NamedGuard>();

        // Load state machine from JSON
        _stateMachine = StateMachine.CreateFromScript(json, _actions, _guards).Start();
    }

    [Test]
    public void TestInitialState()
    {
        var initialState = _stateMachine.GetCurrentState();
        initialState.AssertEquivalence("#atm.idle");
    }

    [Test]
    public void TestCardInserted()
    {
        _stateMachine.Send("CARD_INSERTED");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.authenticating.enteringPin");
    }

    [Test]
    public void TestPinEnteredCorrectly()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.selectingTransaction;#atm.operational.receipt.noReceipt");
    }

    [Test]
    public void TestPinEnteredIncorrectly()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_INCORRECT");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.authenticating.enteringPin");
    }

    [Test]
    public void TestWithdrawTransactionSuccess()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("WITHDRAW");
        _stateMachine.Send("AMOUNT_ENTERED");
        _stateMachine.Send("SUCCESS");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.idle");
    }

    [Test]
    public void TestDepositTransactionFailure()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("DEPOSIT");
        _stateMachine.Send("AMOUNT_ENTERED");
        _stateMachine.Send("FAILURE");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.depositing;#atm.operational.receipt.noReceipt");
    }

    [Test]
    public void TestCancelDuringTransaction()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("WITHDRAW");
        _stateMachine.Send("CANCEL");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.selectingTransaction;#atm.operational.receipt.noReceipt");
    }

    [Test]
    public void TestRequestReceipt()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("REQUEST_RECEIPT");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.selectingTransaction;#atm.operational.receipt.printingReceipt");
    }

    [Test]
    public void TestReceiptPrinted()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("REQUEST_RECEIPT");
        _stateMachine.Send("RECEIPT_PRINTED");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.selectingTransaction;#atm.operational.receipt.noReceipt");
    }

    [Test]
    public void TestBalanceCheck()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("BALANCE");
        _stateMachine.Send("BALANCE_SHOWN");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.selectingTransaction;#atm.operational.receipt.noReceipt");
    }

    [Test]
    public void TestNestedCancelDuringTransaction()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("WITHDRAW");
        _stateMachine.Send("CANCEL");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.selectingTransaction;#atm.operational.receipt.noReceipt");
    }

    [Test]
    public void TestInvalidTransition()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("INVALID_EVENT");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.authenticating.enteringPin"); // Should remain in the same state
    }

    [Test]
    public void TestCancelAfterCardInserted()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("CANCEL");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.idle");
    }

    [Test]
    public void TestWithdrawFailureAndRetry()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("WITHDRAW");
        _stateMachine.Send("AMOUNT_ENTERED");
        _stateMachine.Send("FAILURE");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.withdrawing;#atm.operational.receipt.noReceipt");

        _stateMachine.Send("AMOUNT_ENTERED");
        _stateMachine.Send("SUCCESS");
        currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.idle");
    }

    [Test]
    public void TestDepositFailureAndRetry()
    {
        _stateMachine.Send("CARD_INSERTED");
        _stateMachine.Send("PIN_ENTERED");
        _stateMachine.Send("PIN_CORRECT");
        _stateMachine.Send("DEPOSIT");
        _stateMachine.Send("AMOUNT_ENTERED");
        _stateMachine.Send("FAILURE");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.operational.transaction.depositing;#atm.operational.receipt.noReceipt");

        _stateMachine.Send("AMOUNT_ENTERED");
        _stateMachine.Send("SUCCESS");
        currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#atm.idle");
    }

    const string json = @"{
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
