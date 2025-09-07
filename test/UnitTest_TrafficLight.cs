using Xunit;
using FluentAssertions;
using XStateNet;
using XStateNet.UnitTest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Transactions;

namespace BigStateMachine;

public class TrafficMachine : IDisposable
{
    private StateMachine? _stateMachine;

    List<string> entryActions = new();
    List<string> tranActions = new();
    List<string> exitActions = new();
    List<string> noTargetActions = new();
    ActionMap _actions1;
    ActionMap _actions2;
    GuardMap _guards;

    
    public TrafficMachine()
    {
        _actions1 = new ()
        {
            ["logEntryRed"] = [new("logEntryRed", (sm) => StateMachine.Log("...Entering red"))],
            ["logExitRed"] = [new("logExitRed", (sm) => StateMachine.Log("...Exiting red"))],
            ["logTransitionRedToGreen"] = [new("logTransitionRedToGreen", (sm) => StateMachine.Log("TransitionAction: red --> green"))],
            ["logEntryLight"] = [new("logEntryLight", (sm) => StateMachine.Log("Entering light"))],
            ["logExitLight"] = [new("logExitLight", (sm) => StateMachine.Log("Exiting light"))],
            ["logEntryPedestrian"] = [new("logEntryPedestrian", (sm) => StateMachine.Log("Entering pedestrian"))],
            ["logExitPedestrian"] = [new("logExitPedestrian", (sm) => StateMachine.Log("Exiting pedestrian"))],
            ["logEntryCanWalk"] = [new("logEntryCanWalk", (sm) => StateMachine.Log("Entering canWalk"))],
            ["logExitCanWalk"] = [new("logExitCanWalk", (sm) => StateMachine.Log("Exiting canWalk"))],
            ["logEntryCannotWalk"] = [new("logEntryCannotWalk", (sm) => StateMachine.Log("Entering cannotWalk"))],
            ["logExitCannotWalk"] = [new("logExitCannotWalk", (sm) => StateMachine.Log("Exiting cannotWalk"))],
            ["logTransitionCanWalkToCannotWalk"] = [new("logTransitionCanWalkToCannotWalk", (sm) => StateMachine.Log("TransitionAction: canWalk --> cannotWalk"))],
            ["logTransitionRedToGreen2"] = [new("logTransitionRedToGreen2", (sm) => StateMachine.Log("TransitionAction: red --> green2"))],
            ["logTransitionYellowToRed"] = [new("logTransitionYellowToRed", (sm) => StateMachine.Log("TransitionAction: yellow --> red"))],
            ["logEntryYellow"] = [new("logEntryYellow", (sm) => StateMachine.Log("Entering yellow"))],
            ["logExitYellow"] = [new("logExitYellow", (sm) => StateMachine.Log("Exiting yellow"))],
            ["logEntryGreen"] = [new("logEntryGreen", (sm) => StateMachine.Log("Entering green"))],
            ["logExitGreen"] = [new("logExitGreen", (sm) => StateMachine.Log("Exiting green"))],
            ["logEntryBrightRed"] = [new("logEntryBrightRed", (sm) => StateMachine.Log("Entering red.bright"))],
            ["logExitBrightRed"] = [new("logExitBrightRed", (sm) => StateMachine.Log("Exiting red.bright"))],
            ["logEntryDarkRed"] = [new("logEntryDarkRed", (sm) => StateMachine.Log("Entering red.dark"))],
            ["logExitDarkRed"] = [new("logExitDarkRed", (sm) => StateMachine.Log("Exiting red.dark"))],
            ["logTransitionBrightRedToDark"] = [new("logTransitionBrightRedToDark", (sm) => StateMachine.Log("TransitionAction: bright --> dark"))],
            ["logTransitionDarkRedToBright"] = [new("logTransitionDarkRedToBright", (sm) => StateMachine.Log("TransitionAction: dark --> bright"))],
            ["logEntryBrightGreen"] = [new("logEntryBrightGreen", (sm) => StateMachine.Log("Entering green.bright"))],
            ["logExitBrightGreen"] = [new("logExitBrightGreen", (sm) => StateMachine.Log("Exiting green.bright"))],
            ["logEntryDarkGreen"] = [new("logEntryDarkGreen", (sm) => StateMachine.Log("Entering green.dark"))],
            ["logExitDarkGreen"] = [new("logExitDarkGreen", (sm) => StateMachine.Log("Exiting green.dark"))],
            ["logTransitionBrightGreenToDark"] = [new("logTransitionBrightGreenToDark", (sm) => StateMachine.Log("TransitionAction: bright --> dark"))],
            ["logTransitionDarkGreenToBright"] = [new("logTransitionDarkGreenToBright", (sm) => StateMachine.Log("TransitionAction: dark --> bright"))],
            ["logNoTargetAction"] = [new("logNoTargetAction", (sm) => StateMachine.Log("No target action executed"))]
        };

        entryActions = new List<string>();
        tranActions = new List<string>();
        exitActions = new List<string>();

        _actions2 = new ()
        {
            ["logEntryRed"] = [new("logEntryRed", (sm) => entryActions.Add("Entering red"))],
            ["logExitRed"] = [new("logExitRed", (sm) => exitActions.Add("Exiting red"))],
            ["logTransitionRedToGreen"] = [new("logTransitionRedToGreen", (sm) => tranActions.Add("TransitionAction: red --> green"))],
            ["logEntryLight"] = [new("logEntryLight", (sm) => entryActions.Add("Entering light"))],
            ["logExitLight"] = [new("logExitLight", (sm) => exitActions.Add("Exiting light"))],
            ["logEntryPedestrian"] = [new("logEntryPedestrian", (sm) => entryActions.Add("Entering pedestrian"))],
            ["logExitPedestrian"] = [new("logExitPedestrian", (sm) => exitActions.Add("Exiting pedestrian"))],
            ["logEntryCanWalk"] = [new("logEntryCanWalk", (sm) => entryActions.Add("Entering canWalk"))],
            ["logExitCanWalk"] = [new("logExitCanWalk", (sm) => exitActions.Add("Exiting canWalk"))],
            ["logEntryCannotWalk"] = [new("logEntryCannotWalk", (sm) => entryActions.Add("Entering cannotWalk"))],
            ["logExitCannotWalk"] = [new("logExitCannotWalk", (sm) => exitActions.Add("Exiting cannotWalk"))],
            ["logTransitionCanWalkToCannotWalk"] = [new("logTransitionCanWalkToCannotWalk", (sm) => tranActions.Add("TransitionAction: canWalk --> cannotWalk"))],
            ["logTransitionRedToGreen2"] = [new("logTransitionRedToGreen2", (sm) => tranActions.Add("TransitionAction2: red --> green"))],
            ["logTransitionYellowToRed"] = [new("logTransitionYellowToRed", (sm) => tranActions.Add("TransitionAction: yellow --> red"))],
            ["logEntryYellow"] = [new("logEntryYellow", (sm) => entryActions.Add("Entering yellow"))],
            ["logExitYellow"] = [new("logExitYellow", (sm) => exitActions.Add("Exiting yellow"))],
            ["logEntryGreen"] = [new("logEntryGreen", (sm) => entryActions.Add("Entering green"))],
            ["logExitGreen"] = [new("logExitGreen", (sm) => exitActions.Add("Exiting green"))],
            ["logEntryBrightRed"] = [new("logEntryBrightRed", (sm) => entryActions.Add("Entering red.bright"))],
            ["logExitBrightRed"] = [new("logExitBrightRed", (sm) => exitActions.Add("Exiting red.bright"))],
            ["logEntryDarkRed"] = [new("logEntryDarkRed", (sm) => entryActions.Add("Entering red.dark"))],
            ["logExitDarkRed"] = [new("logExitDarkRed", (sm) => exitActions.Add("Exiting red.dark"))],
            ["logTransitionBrightRedToDark"] = [new("logTransitionBrightRedToDark", (sm) => tranActions.Add("TransitionAction: bright --> dark"))],
            ["logTransitionDarkRedToBright"] = [new("logTransitionDarkRedToBright", (sm) => tranActions.Add("TransitionAction: dark --> bright"))],
            ["logEntryBrightGreen"] = [new("logEntryBrightGreen", (sm) => entryActions.Add("Entering green.bright"))],
            ["logExitBrightGreen"] = [new("logExitBrightGreen", (sm) => exitActions.Add("Exiting green.bright"))],
            ["logEntryDarkGreen"] = [new("logEntryDarkGreen", (sm) => entryActions.Add("Entering green.dark"))],
            ["logExitDarkGreen"] = [new("logExitDarkGreen", (sm) => exitActions.Add("Exiting green.dark"))],
            ["logTransitionBrightGreenToDark"] = [new("logTransitionBrightGreenToDark", (sm) => tranActions.Add("TransitionAction: bright --> dark"))],
            ["logTransitionDarkGreenToBright"] = [new("logTransitionDarkGreenToBright", (sm) => tranActions.Add("TransitionAction: dark --> bright"))],
            ["logNoTargetAction"] = [new("logNoTargetAction", (sm) => noTargetActions.Add("No target action executed"))] // No target action is a tansition action
        };

        _guards = new GuardMap
        {
            ["isReady"] = new ("isReady", IsReady)
        };

    }

    Func<StateMachine, bool> IsReady = (sm) => 
    {
        object? res;
        if (sm.ContextMap != null && sm.ContextMap.TryGetValue("isReady", out res))
        {
            return (bool)(res ?? false);
        }
        else
        {
            throw new Exception("isReady not found in context");
        }
    };
    /*
    bool IsReady(StateMachine sm)
    {
        return (bool)sm.ContextMap["isReady"];
    }
    */

    [Fact]
    public void TestInitialState()
    {
        _stateMachine = StateMachine.CreateFromScript(json, _actions1, _guards).Start();
        if (_stateMachine.ContextMap != null)
            _stateMachine!.ContextMap!["isReady"] = true;
        var currentState = _stateMachine!.GetActiveStateString();
        currentState.AssertEquivalence("#trafficLight.light.red.bright;#trafficLight.pedestrian.cannotWalk");
    }

    [Fact]
    public void TestTransitionRedToGreen()
    {
        _stateMachine = StateMachine.CreateFromScript(json, _actions1, _guards).Start();
        if(_stateMachine.ContextMap != null)
            _stateMachine!.ContextMap!["isReady"] = true;
        _stateMachine!.Send("TIMER");
        var currentState = _stateMachine!.GetActiveStateString();
        currentState.AssertEquivalence("#trafficLight.light.green.bright;#trafficLight.pedestrian.cannotWalk");
    }

    [Fact]
    public void TestGuardCondition()
    {
        _stateMachine = StateMachine.CreateFromScript(json, _actions1, _guards).Start();
        if (_stateMachine.ContextMap != null)
            _stateMachine!.ContextMap!["isReady"] = false;
        _stateMachine!.Send("TIMER");
        var currentState = _stateMachine!.GetActiveStateString();
        // Should remain in last state as guard condition fails
        currentState.AssertEquivalence("#trafficLight.light.red.bright;#trafficLight.pedestrian.cannotWalk");
    }

    [Fact]
    public void TestEntryAndExitActions()
    {
        _stateMachine = StateMachine.CreateFromScript(json, _actions2, _guards).Start();
        if (_stateMachine.ContextMap != null)
            _stateMachine!.ContextMap!["isReady"] = true;
        
        _stateMachine!.Send("TIMER");
        var currentState = _stateMachine!.GetActiveStateString();

        exitActions.Should().Contain("Exiting red.bright");
        exitActions.Should().Contain("Exiting red");
        tranActions.Should().Contain("TransitionAction: red --> green");
        entryActions.Should().Contain("Entering green");
        entryActions.Should().Contain("Entering green.bright");
    }

    [Fact]
    public void TestParallelStates()
    {
        _stateMachine = StateMachine.CreateFromScript(json, _actions1, _guards).Start();
        _stateMachine!.Send("PUSH_BUTTON");
        var currentState = _stateMachine!.GetActiveStateString();
        currentState.Should().Contain("canWalk");
        currentState.Should().Contain("red");
    }

    [Fact]
    public void TestNestedStates()
    {
        _stateMachine = StateMachine.CreateFromScript(json, _actions1, _guards).Start();
        if (_stateMachine.ContextMap != null)
            _stateMachine!.ContextMap!["isReady"] = true;
        _stateMachine!.Send("TIMER");
        //_stateMachine.Send("DARKER");
        var currentState = _stateMachine!.GetActiveStateString();
        //currentState.AssertEquivalence("#trafficLight.light.green.dark;#trafficLight.pedestrian.cannotWalk");
    }

    [Fact]
    public void TestInvalidTransition()
    {
        _stateMachine = StateMachine.CreateFromScript(json, _actions1, _guards).Start();
        _stateMachine!.Send("INVALID_EVENT");
        var currentState = _stateMachine!.GetActiveStateString();
        currentState.AssertEquivalence("#trafficLight.light.red.bright;#trafficLight.pedestrian.cannotWalk");
    }

    [Fact]
    public void TestNoTargetEvent()
    {
        _stateMachine = StateMachine.CreateFromScript(json, _actions2, _guards).Start();
        if (_stateMachine.ContextMap != null)
            _stateMachine!.ContextMap!["isReady"] = true;

        _stateMachine!.Send("NO_TARGET");

        noTargetActions.Should().Contain("No target action executed");

    }
    [Fact]
    public void TestImplicitTargetTransition()
    {
        _stateMachine = StateMachine.CreateFromScript(json, _actions1, _guards).Start();
        if (_stateMachine.ContextMap != null)
            _stateMachine!.ContextMap!["isReady"] = true;

        // Send event to trigger the implicit target transition
        _stateMachine!.Send("IMPLICIT_TARGET");

        var currentState = _stateMachine!.GetActiveStateString();
        currentState.Should().Contain("yellow", "Current state should contain 'yellow'");
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
    }";
    
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}




