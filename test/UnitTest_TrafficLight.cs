using NUnit.Framework;
using SharpState;
using SharpState.UnitTest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace AdvancedFeatures;

[TestFixture]
public class StateMachineTests
{
    private StateMachine _stateMachine;


    [SetUp]
    public void Setup()
    {
        var actions = new ConcurrentDictionary<string, List<NamedAction>>
        {
            ["logEntryRed"] = [new("logEntryRed", (sm) => Console.WriteLine("Entering red"))],
            ["logExitRed"] = [new("logExitRed", (sm) => Console.WriteLine("Exiting red"))],
            ["logTransitionRedToGreen"] = [new("logTransitionRedToGreen", (sm) => Console.WriteLine("TransitionAction: red --> green"))]
        };

        var guards = new ConcurrentDictionary<string, NamedGuard>
        {
            ["isReady"] = new NamedGuard("isReady", (sm) => (bool)_stateMachine.ContextMap["isReady"])
        };

        _stateMachine = StateMachine.CreateFromScript(json, actions, guards).Start();
        _stateMachine.ContextMap["isReady"] = true;
    }

    [Test]
    public void TestInitialState()
    {
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#trafficLight.light.red.bright;#trafficLight.pedestrian.cannotWalk");
    }

    [Test]
    public void TestTransitionRedToGreen()
    {
        _stateMachine.Send("TIMER");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#trafficLight.light.green.bright;#trafficLight.pedestrian.cannotWalk");
    }

    [Test]
    public void TestGuardCondition()
    {
        _stateMachine.ContextMap["isReady"] = false;
        _stateMachine.Send("TIMER");
        var currentState = _stateMachine.GetCurrentState();
        // Should remain in last state as guard condition fails
        currentState.AssertEquivalence("#trafficLight.light.red.bright;#trafficLight.pedestrian.cannotWalk");
    }

    [Test]
    public void TestEntryAndExitActions()
    {
        var entryActions = new List<string>();
        var tranActions = new List<string>();
        var exitActions = new List<string>();

        var actions = new ConcurrentDictionary<string, List<NamedAction>>
        {
            ["logEntryRed"] = [new("logEntryRed", (sm) => entryActions.Add("Entering red"))],
            ["logEntryBrightRed"] = [new("logEntryBrightRed", (sm) => entryActions.Add("Entering red.bright"))],
            ["logExitBrightRed"] = [new("logExitBrightRed", (sm) => exitActions.Add("Exiting red.bright"))],
            ["logExitRed"] = [new("logExitRed", (sm) => exitActions.Add("Exiting red"))],
            ["logTransitionRedToGreen"] = [new("logTransitionRedToGreen", (sm) => tranActions.Add("TransitionAction: red --> green"))],
            ["logEntryGreen"] = [new("logEntryGreen", (sm) => entryActions.Add("Entering green"))],
            ["logEntryBrightGreen"] = [new("logEntryBrightGreen", (sm) => entryActions.Add("Entering green.bright"))],
        };

        var guards = new ConcurrentDictionary<string, NamedGuard>
        {
            ["isReady"] = new NamedGuard("isReady", (sm) => (bool)_stateMachine.ContextMap["isReady"])
        };

        _stateMachine = StateMachine.CreateFromScript(json, actions, guards).Start();
        _stateMachine.ContextMap["isReady"] = true;

        _stateMachine.Send("TIMER");
        var currentState = _stateMachine.GetCurrentState();

        Assert.IsTrue(exitActions.Contains("Exiting red.bright"));
        Assert.IsTrue(exitActions.Contains("Exiting red"));
        Assert.IsTrue(tranActions.Contains("TransitionAction: red --> green"));
        Assert.IsTrue(entryActions.Contains("Entering green"));
        Assert.IsTrue(entryActions.Contains("Entering green.bright"));
    }

    [Test]
    public void TestParallelStates()
    {
        _stateMachine.Send("PUSH_BUTTON");
        var currentState = _stateMachine.GetCurrentState();
        Assert.IsTrue(currentState.Contains("canWalk"));
        Assert.IsTrue(currentState.Contains("red"));
    }

    [Test]
    public void TestNestedStates()
    {
        _stateMachine.Send("TIMER");
        _stateMachine.Send("DARKER");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#trafficLight.light.green.dark;#trafficLight.pedestrian.cannotWalk");
    }

    [Test]
    public void TestInvalidTransition()
    {
        _stateMachine.Send("INVALID_EVENT");
        var currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#trafficLight.light.red.bright;#trafficLight.pedestrian.cannotWalk");
    }
    [Test]
    public void TestAlwaysTransition()
    {
        var stateMachineJson = @"{
            'id': 'counter',
            'initial': 'smallNumber',
            'context': { 'count': 0 },
            'states': {
                'smallNumber': {
                    'always': { 'target': 'bigNumber', 'guard': 'isBigNumber' }
                },
                'bigNumber': {
                    'always': { 'target': 'smallNumber', 'guard': 'isSmallNumber' }
                }
            },
            'on': {
                'INCREMENT': { 'actions': ['incrementCount', 'checkCount'] },
                'DECREMENT': { 'actions': ['decrementCount', 'checkCount'] },
                'RESET': { 'actions': ['resetCount', 'checkCount'] }
            }
        }";

        var actions = new ConcurrentDictionary<string, List<NamedAction>>
        {
            ["incrementCount"] = [new("incrementCount", (sm) => _stateMachine.ContextMap["count"] = (int)_stateMachine.ContextMap["count"] + 1)],
            ["decrementCount"] = [new("decrementCount", (sm) => _stateMachine.ContextMap["count"] = (int)_stateMachine.ContextMap["count"] - 1)],
            ["resetCount"] = [new("resetCount", (sm) => _stateMachine.ContextMap["count"] = 0)],
            ["checkCount"] = [new("checkCount", (sm) => { })]
        };

        var guards = new ConcurrentDictionary<string, NamedGuard>
        {
            ["isBigNumber"] = new("isBigNumber", (sm) => (int)_stateMachine.ContextMap["count"] > 3),
            ["isSmallNumber"] = new("isSmallNumber", (sm) => (int)_stateMachine.ContextMap["count"] <= 3)
        };

        _stateMachine = StateMachine.CreateFromScript(stateMachineJson, actions, guards).Start();
        _stateMachine.ContextMap["count"] = 0;

        var currentState = _stateMachine.GetCurrentState();
        Assert.AreEqual("#counter.smallNumber", currentState);

        // Test incrementing to trigger always transition
        _stateMachine.Send("INCREMENT");
        _stateMachine.Send("INCREMENT");
        _stateMachine.Send("INCREMENT");
        _stateMachine.Send("INCREMENT");

        currentState = _stateMachine.GetCurrentState();
        Assert.AreEqual("#counter.bigNumber", currentState);

        _stateMachine.Send("DECREMENT");
        _stateMachine.Send("DECREMENT");
        _stateMachine.Send("DECREMENT");
        _stateMachine.Send("DECREMENT");

        currentState = _stateMachine.GetCurrentState();
        currentState.AssertEquivalence("#counter.smallNumber");
    }

    [Test]
    public void TestNoTargetEvent()
    {
        var noTargetActions = new List<string>();

        var actions = new ConcurrentDictionary<string, List<NamedAction>>
        {
            ["logEntryRed"] = [new("logEntryRed", (sm) => Console.WriteLine("Entering red"))],
            ["logExitRed"] = [new("logExitRed", (sm) => Console.WriteLine("Exiting red"))],
            ["logTransitionRedToGreen"] = [new("logTransitionRedToGreen", (sm) => Console.WriteLine("TransitionAction: red --> green"))],
            ["logNoTargetAction"] = [new("logNoTargetAction", (sm) => noTargetActions.Add("No target action executed"))]
        };

        var guards = new ConcurrentDictionary<string, NamedGuard>
        {
            ["isReady"] = new NamedGuard("isReady", (sm) => (bool)_stateMachine.ContextMap["isReady"])
        };

        _stateMachine = StateMachine.CreateFromScript(json, actions, guards).Start();
        _stateMachine.ContextMap["isReady"] = true;

        _stateMachine.Send("NO_TARGET");

        Assert.IsTrue(noTargetActions.Contains("No target action executed"));

    }
    [Test]
    public void TestImplicitTargetTransition()
    {
        var actions = new ConcurrentDictionary<string, List<NamedAction>>
        {
            ["logEntryRed"] = [new("logEntryRed", (sm) => Console.WriteLine("Entering red"))],
            ["logExitRed"] = [new("logExitRed", (sm) => Console.WriteLine("Exiting red"))],
            ["logEntryYellow"] = [new("logEntryYellow", (sm) => Console.WriteLine("Entering yellow"))],
            ["logExitYellow"] = [new("logExitYellow", (sm) => Console.WriteLine("Exiting yellow"))]
        };

        var guards = new ConcurrentDictionary<string, NamedGuard>
        {
            ["isReady"] = new NamedGuard("isReady", (sm) => (bool)_stateMachine.ContextMap["isReady"])
        };

        _stateMachine = StateMachine.CreateFromScript(json, actions, guards).Start();
        _stateMachine.ContextMap["isReady"] = true;

        // Send event to trigger the implicit target transition
        _stateMachine.Send("IMPLICIT_TARGET");

        var currentState = _stateMachine.GetCurrentState();
        Assert.IsTrue(currentState.Contains("yellow"), "Current state should contain 'yellow'");
    }
    [Test]
    public void TestShallowHistory()
    {
        var stateMachineJson = @"{
            'id': 'testMachine',
            'initial': 'A',
            'states': {
                'A': {
                    'initial': 'A1',
                    'states': {
                        'A1': {
                            'on': { 'TO_A2': 'A2' }
                        },
                        'A2': {
                            'on': { 'TO_A1': 'A1' }
                        },
                        'hist': { 
                            type : 'history',
                            'history':'shallow'
                        }
                    },
                    'on': { 'TO_B': 'B' }
                },
                'B': {
                    'on': { 'TO_A': 'A.hist' }
                }
            }
        }";

        _stateMachine = StateMachine.CreateFromScript(stateMachineJson,
            new ConcurrentDictionary<string, List<NamedAction>>(),
            new ConcurrentDictionary<string, NamedGuard>()).Start();

        _stateMachine.Send("TO_A2");
        _stateMachine.Send("TO_B");

        var currentState = _stateMachine.GetCurrentState();
        Assert.AreEqual("#testMachine.B", currentState);

        _stateMachine.Send("TO_A");

        currentState = _stateMachine.GetCurrentState();
        Assert.AreEqual("#testMachine.A.A2", currentState);
    }

    [Test]
    public void TestDeepHistory()
    {
        var stateMachineJson = @" {
            'id': 'testMachine',
            'initial': 'A',
              states : {         
                  'A': {
                      'initial': 'A1',              
                      'states': {
                          'hist' : {
                            type : 'history',
                            'history':'deep'
                          },   
                          'A1': {
                              'initial': 'A1a',
                              'states': {
                                  'A1a': {
                                      'on': { 'TO_A1b': 'A1b' }
                                  },
                                  'A1b': {}
                              }
                          },
                          'A2': {}
                      },
                      on: {
                         'TO_B': 'B'
                      }
                  },
                  'B': {
                      'on': { 'TO_A': 'A.hist' }
                  }
              }
          }";

        _stateMachine = StateMachine.CreateFromScript(stateMachineJson,
            new ConcurrentDictionary<string, List<NamedAction>>(),
            new ConcurrentDictionary<string, NamedGuard>()).Start();
        
        var currentState = _stateMachine.GetCurrentState();
        _stateMachine.Send("TO_A1b");
        currentState = _stateMachine.GetCurrentState();
        _stateMachine.Send("TO_B");        
        currentState = _stateMachine.GetCurrentState();
        Assert.AreEqual("#testMachine.B", currentState);
        _stateMachine.Send("TO_A");
        currentState = _stateMachine.GetCurrentState();

        currentState.AssertEquivalence("#testMachine.A.A1.A1b");
    }

    const string json = @"{
      id: 'trafficLight',
      type: 'parallel',
      context: {
        isReady: 'false',
        count: 0
      },
      states: {
        light: {
          initial: 'red',
          entry: [ 'logEntryLight' ],
          exit: [ 'logExitLight' ],
          states: {
            red: {
              entry: [ 'logEntryRed' ],
              exit: [ 'logExitRed' ],
              on: {
                TIMER: {
                  target: 'green',
                  guard: 'isReady',
                  actions: [ 'logTransitionRedToGreen', 'logTransitionRedToGreen2' ]
                },
                IMPLICIT_TARGET: 'yellow'
              },
              initial: 'bright',
              states: {
                bright: {
                  entry: [ 'logEntryBrightRed' ],
                  exit: [ 'logExitBrightRed' ],
                  on: {
                    DARKER: {
                      target: 'dark',
                      actions: [ 'logTransitionBrightRedToDark' ]
                    },
                    NO_TARGET: {
                      actions: [ 'logNoTargetAction' ]
                    }
                  }
                },
                dark: {
                  entry: [ 'logEntryDarkRed' ],
                  exit: [ 'logExitDarkRed' ],
                  on: {
                    BRIGHTER: {
                      target: 'bright',
                      actions: [ 'logTransitionDarkRedToBright' ]
                    }
                  }
                }
              }
            },
            yellow: {
              entry: [ 'logEntryYellow' ],
              exit: [ 'logExitYellow' ],
              on: {
                TIMER: {
                  target: 'red',
                  actions: [ 'logTransitionYellowToRed' ]
                }
              }
            },
            green: {
              entry: [ 'logEntryGreen' ],
              exit: [ 'logExitGreen' ],
              on: {
                TIMER: {
                  target: 'yellow',
                  actions: [ 'logTransitionGreenToYellow' ]
                }
              },
              initial: 'bright',
              states: {
                bright: {
                  entry: [ 'logEntryBrightGreen' ],
                  exit: [ 'logExitBrightGreen' ],
                  on: {
                    DARKER: {
                      target: 'dark',
                      actions: [ 'logTransitionBrightGreenToDark' ]
                    }
                  }
                },
                dark: {
                  entry: [ 'logEntryDarkGreen' ],
                  exit: [ 'logExitDarkGreen' ],
                  on: {
                    BRIGHTER: {
                      target: 'bright',
                      actions: [ 'logTransitionDarkGreenToBright' ]
                    }
                  }
                }
              }
            }
          }
        },
        pedestrian: {
          entry: [ 'logEntryPedestrian' ],
          exit: [ 'logExitPedestrian' ],
          initial: 'cannotWalk',
          states: {
            canWalk: {
              entry: [ 'logEntryCanWalk' ],
              exit: [ 'logExitCanWalk' ],
              on: {
                TIMER: {
                  target: 'cannotWalk',
                  actions: [ 'logTransitionCanWalkToCannotWalk' ]
                }
              }
            },
            cannotWalk: {
              entry: [ 'logEntryCannotWalk' ],
              exit: [ 'logExitCannotWalk' ],
              on: {
                PUSH_BUTTON: {
                  target: 'canWalk',
                  in: '#trafficLight.light.red',
                  actions: [ 'logTransitionCannotWalkToCanWalk' ]
                }
              }
            }
          }
        }
      }
    }

";
}
