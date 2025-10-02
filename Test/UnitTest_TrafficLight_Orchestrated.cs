using Xunit;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BigStateMachine;

public class TrafficMachineOrchestrated : OrchestratorTestBase
{
    private List<string> _entryActions = new();
    private List<string> _tranActions = new();
    private List<string> _exitActions = new();
    private List<string> _noTargetActions = new();

    private Dictionary<string, Action<OrchestratedContext>> CreateActions1()
    {
        return new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logEntryRed"] = (ctx) => Console.WriteLine("...Entering red"),
            ["logExitRed"] = (ctx) => Console.WriteLine("...Exiting red"),
            ["logTransitionRedToGreen"] = (ctx) => Console.WriteLine("TransitionAction: red --> green"),
            ["logEntryLight"] = (ctx) => Console.WriteLine("Entering light"),
            ["logExitLight"] = (ctx) => Console.WriteLine("Exiting light"),
            ["logEntryPedestrian"] = (ctx) => Console.WriteLine("Entering pedestrian"),
            ["logExitPedestrian"] = (ctx) => Console.WriteLine("Exiting pedestrian"),
            ["logEntryCanWalk"] = (ctx) => Console.WriteLine("Entering canWalk"),
            ["logExitCanWalk"] = (ctx) => Console.WriteLine("Exiting canWalk"),
            ["logEntryCannotWalk"] = (ctx) => Console.WriteLine("Entering cannotWalk"),
            ["logExitCannotWalk"] = (ctx) => Console.WriteLine("Exiting cannotWalk"),
            ["logTransitionCanWalkToCannotWalk"] = (ctx) => Console.WriteLine("TransitionAction: canWalk --> cannotWalk"),
            ["logTransitionRedToGreen2"] = (ctx) => Console.WriteLine("TransitionAction2: red --> green"),
            ["logTransitionYellowToRed"] = (ctx) => Console.WriteLine("TransitionAction: yellow --> red"),
            ["logEntryYellow"] = (ctx) => Console.WriteLine("Entering yellow"),
            ["logExitYellow"] = (ctx) => Console.WriteLine("Exiting yellow"),
            ["logEntryGreen"] = (ctx) => Console.WriteLine("Entering green"),
            ["logExitGreen"] = (ctx) => Console.WriteLine("Exiting green"),
            ["logEntryBrightRed"] = (ctx) => Console.WriteLine("Entering red.bright"),
            ["logExitBrightRed"] = (ctx) => Console.WriteLine("Exiting red.bright"),
            ["logEntryDarkRed"] = (ctx) => Console.WriteLine("Entering red.dark"),
            ["logExitDarkRed"] = (ctx) => Console.WriteLine("Exiting red.dark"),
            ["logTransitionBrightRedToDark"] = (ctx) => Console.WriteLine("TransitionAction: bright --> dark"),
            ["logTransitionDarkRedToBright"] = (ctx) => Console.WriteLine("TransitionAction: dark --> bright"),
            ["logEntryBrightGreen"] = (ctx) => Console.WriteLine("Entering green.bright"),
            ["logExitBrightGreen"] = (ctx) => Console.WriteLine("Exiting green.bright"),
            ["logEntryDarkGreen"] = (ctx) => Console.WriteLine("Entering green.dark"),
            ["logExitDarkGreen"] = (ctx) => Console.WriteLine("Exiting green.dark"),
            ["logTransitionBrightGreenToDark"] = (ctx) => Console.WriteLine("TransitionAction: bright --> dark"),
            ["logTransitionDarkGreenToBright"] = (ctx) => Console.WriteLine("TransitionAction: dark --> bright"),
            ["logNoTargetAction"] = (ctx) => Console.WriteLine("No target action executed")
        };
    }

    private Dictionary<string, Action<OrchestratedContext>> CreateActions2()
    {
        return new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logEntryRed"] = (ctx) => _entryActions.Add("Entering red"),
            ["logExitRed"] = (ctx) => _exitActions.Add("Exiting red"),
            ["logTransitionRedToGreen"] = (ctx) => _tranActions.Add("TransitionAction: red --> green"),
            ["logEntryLight"] = (ctx) => _entryActions.Add("Entering light"),
            ["logExitLight"] = (ctx) => _exitActions.Add("Exiting light"),
            ["logEntryPedestrian"] = (ctx) => _entryActions.Add("Entering pedestrian"),
            ["logExitPedestrian"] = (ctx) => _exitActions.Add("Exiting pedestrian"),
            ["logEntryCanWalk"] = (ctx) => _entryActions.Add("Entering canWalk"),
            ["logExitCanWalk"] = (ctx) => _exitActions.Add("Exiting canWalk"),
            ["logEntryCannotWalk"] = (ctx) => _entryActions.Add("Entering cannotWalk"),
            ["logExitCannotWalk"] = (ctx) => _exitActions.Add("Exiting cannotWalk"),
            ["logTransitionCanWalkToCannotWalk"] = (ctx) => _tranActions.Add("TransitionAction: canWalk --> cannotWalk"),
            ["logTransitionRedToGreen2"] = (ctx) => _tranActions.Add("TransitionAction2: red --> green"),
            ["logTransitionYellowToRed"] = (ctx) => _tranActions.Add("TransitionAction: yellow --> red"),
            ["logEntryYellow"] = (ctx) => _entryActions.Add("Entering yellow"),
            ["logExitYellow"] = (ctx) => _exitActions.Add("Exiting yellow"),
            ["logEntryGreen"] = (ctx) => _entryActions.Add("Entering green"),
            ["logExitGreen"] = (ctx) => _exitActions.Add("Exiting green"),
            ["logEntryBrightRed"] = (ctx) => _entryActions.Add("Entering red.bright"),
            ["logExitBrightRed"] = (ctx) => _exitActions.Add("Exiting red.bright"),
            ["logEntryDarkRed"] = (ctx) => _entryActions.Add("Entering red.dark"),
            ["logExitDarkRed"] = (ctx) => _exitActions.Add("Exiting red.dark"),
            ["logTransitionBrightRedToDark"] = (ctx) => _tranActions.Add("TransitionAction: bright --> dark"),
            ["logTransitionDarkRedToBright"] = (ctx) => _tranActions.Add("TransitionAction: dark --> bright"),
            ["logEntryBrightGreen"] = (ctx) => _entryActions.Add("Entering green.bright"),
            ["logExitBrightGreen"] = (ctx) => _exitActions.Add("Exiting green.bright"),
            ["logEntryDarkGreen"] = (ctx) => _entryActions.Add("Entering green.dark"),
            ["logExitDarkGreen"] = (ctx) => _exitActions.Add("Exiting green.dark"),
            ["logTransitionBrightGreenToDark"] = (ctx) => _tranActions.Add("TransitionAction: bright --> dark"),
            ["logTransitionDarkGreenToBright"] = (ctx) => _tranActions.Add("TransitionAction: dark --> bright"),
            ["logNoTargetAction"] = (ctx) => _noTargetActions.Add("No target action executed")
        };
    }

    private Dictionary<string, Func<StateMachine, bool>> CreateGuards()
    {
        return new Dictionary<string, Func<StateMachine, bool>>
        {
            ["isReady"] = (sm) =>
            {
                if (sm.ContextMap != null && sm.ContextMap.TryGetValue("isReady", out var res))
                {
                    return (bool)(res ?? false);
                }
                throw new Exception("isReady not found in context");
            }
        };
    }

    [Fact]
    public async Task TestInitialState()
    {
        var machine = CreateMachine("trafficLight", TrafficLightJson, CreateActions1(), CreateGuards());
        await machine.StartAsync();

        // Set context through underlying machine (note: in pure orchestrator pattern, context would be in definition)
        var adapter = machine as PureStateMachineAdapter;
        if (adapter != null)
        {
            var underlying = adapter.GetUnderlying();
            if (underlying.ContextMap != null)
                underlying.ContextMap["isReady"] = true;
        }

        var currentState = machine.CurrentState;
        Assert.Contains("red.bright", currentState);
        Assert.Contains("cannotWalk", currentState);
    }

    [Fact]
    public async Task TestTransitionRedToGreen()
    {
        var machine = CreateMachine("trafficLight", TrafficLightJson, CreateActions1(), CreateGuards());
        await machine.StartAsync();

        var adapter = machine as PureStateMachineAdapter;
        if (adapter != null)
        {
            var underlying = adapter.GetUnderlying();
            if (underlying.ContextMap != null)
                underlying.ContextMap["isReady"] = true;
        }

        var result = await SendEventAsync("TEST", "trafficLight", "TIMER");

        Assert.True(result.Success);
        Assert.Contains("green.bright", result.NewState);
        Assert.Contains("cannotWalk", result.NewState);
    }

    [Fact]
    public async Task TestGuardCondition()
    {
        var machine = CreateMachine("trafficLight", TrafficLightJson, CreateActions1(), CreateGuards());
        await machine.StartAsync();

        var adapter = machine as PureStateMachineAdapter;
        if (adapter != null)
        {
            var underlying = adapter.GetUnderlying();
            if (underlying.ContextMap != null)
                underlying.ContextMap["isReady"] = false; // Guard will fail
        }

        var result = await SendEventAsync("TEST", "trafficLight", "TIMER");

        // Should remain in red state as guard condition fails
        Assert.Contains("red.bright", result.NewState);
        Assert.Contains("cannotWalk", result.NewState);
    }

    [Fact]
    public async Task TestEntryAndExitActions()
    {
        _entryActions.Clear();
        _exitActions.Clear();
        _tranActions.Clear();

        var machine = CreateMachine("trafficLight", TrafficLightJson, CreateActions2(), CreateGuards());
        await machine.StartAsync();

        var adapter = machine as PureStateMachineAdapter;
        if (adapter != null)
        {
            var underlying = adapter.GetUnderlying();
            if (underlying.ContextMap != null)
                underlying.ContextMap["isReady"] = true;
        }

        await SendEventAsync("TEST", "trafficLight", "TIMER");

        Assert.Contains("Exiting red.bright", _exitActions);
        Assert.Contains("Exiting red", _exitActions);
        Assert.Contains("TransitionAction: red --> green", _tranActions);
        Assert.Contains("Entering green", _entryActions);
        Assert.Contains("Entering green.bright", _entryActions);
    }

    [Fact]
    public async Task TestParallelStates()
    {
        var machine = CreateMachine("trafficLight", TrafficLightJson, CreateActions1(), CreateGuards());
        await machine.StartAsync();

        var result = await SendEventAsync("TEST", "trafficLight", "PUSH_BUTTON");

        Assert.Contains("canWalk", result.NewState);
        Assert.Contains("red", result.NewState);
    }

    [Fact]
    public async Task TestInvalidTransition()
    {
        var machine = CreateMachine("trafficLight", TrafficLightJson, CreateActions1(), CreateGuards());
        await machine.StartAsync();

        var result = await SendEventAsync("TEST", "trafficLight", "INVALID_EVENT");

        // Should remain in initial state
        Assert.Contains("red.bright", result.NewState);
        Assert.Contains("cannotWalk", result.NewState);
    }

    [Fact]
    public async Task TestNoTargetEvent()
    {
        _noTargetActions.Clear();

        var machine = CreateMachine("trafficLight", TrafficLightJson, CreateActions2(), CreateGuards());
        await machine.StartAsync();

        var adapter = machine as PureStateMachineAdapter;
        if (adapter != null)
        {
            var underlying = adapter.GetUnderlying();
            if (underlying.ContextMap != null)
                underlying.ContextMap["isReady"] = true;
        }

        await SendEventAsync("TEST", "trafficLight", "NO_TARGET");

        Assert.Contains("No target action executed", _noTargetActions);
    }

    [Fact]
    public async Task TestImplicitTargetTransition()
    {
        var machine = CreateMachine("trafficLight", TrafficLightJson, CreateActions1(), CreateGuards());
        await machine.StartAsync();

        var adapter = machine as PureStateMachineAdapter;
        if (adapter != null)
        {
            var underlying = adapter.GetUnderlying();
            if (underlying.ContextMap != null)
                underlying.ContextMap["isReady"] = true;
        }

        var result = await SendEventAsync("TEST", "trafficLight", "IMPLICIT_TARGET");

        Assert.Contains("yellow", result.NewState);
    }

    const string TrafficLightJson = @"{
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
}
