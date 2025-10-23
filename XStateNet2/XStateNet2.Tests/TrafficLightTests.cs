using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Builder;

namespace XStateNet2.Tests;

/// <summary>
/// Comprehensive traffic light state machine tests covering:
/// - Parallel states (light + pedestrian)
/// - Nested states (red.bright, red.dark, green.bright, green.dark)
/// - Guards (isReady condition)
/// - Entry/Exit actions
/// - Transition actions
/// - No-target actions
/// - Implicit target transitions
/// </summary>
public class TrafficLightTests : TestKit
{
    private readonly List<string> _entryActions = new();
    private readonly List<string> _exitActions = new();
    private readonly List<string> _tranActions = new();
    private readonly List<string> _noTargetActions = new();

    private const string TrafficLightJson = """
    {
      "id": "trafficLight",
      "type": "parallel",
      "context": {
        "isReady": false,
        "count": 0
      },
      "states": {
        "light": {
          "initial": "red",
          "entry": ["logEntryLight"],
          "exit": ["logExitLight"],
          "states": {
            "red": {
              "entry": ["logEntryRed"],
              "exit": ["logExitRed"],
              "on": {
                "TIMER": {
                  "target": "green",
                  "cond": "isReady",
                  "actions": ["logTransitionRedToGreen", "logTransitionRedToGreen2"]
                },
                "IMPLICIT_TARGET": "yellow"
              },
              "initial": "bright",
              "states": {
                "bright": {
                  "entry": ["logEntryBrightRed"],
                  "exit": ["logExitBrightRed"],
                  "on": {
                    "DARKER": {
                      "target": "dark",
                      "actions": ["logTransitionBrightRedToDark"]
                    },
                    "NO_TARGET": {
                      "actions": ["logNoTargetAction"]
                    }
                  }
                },
                "dark": {
                  "entry": ["logEntryDarkRed"],
                  "exit": ["logExitDarkRed"],
                  "on": {
                    "BRIGHTER": {
                      "target": "bright",
                      "actions": ["logTransitionDarkRedToBright"]
                    }
                  }
                }
              }
            },
            "yellow": {
              "entry": ["logEntryYellow"],
              "exit": ["logExitYellow"],
              "on": {
                "TIMER": {
                  "target": "red",
                  "actions": ["logTransitionYellowToRed"]
                }
              }
            },
            "green": {
              "entry": ["logEntryGreen"],
              "exit": ["logExitGreen"],
              "on": {
                "TIMER": {
                  "target": "yellow",
                  "actions": ["logTransitionGreenToYellow"]
                }
              },
              "initial": "bright",
              "states": {
                "bright": {
                  "entry": ["logEntryBrightGreen"],
                  "exit": ["logExitBrightGreen"],
                  "on": {
                    "DARKER": {
                      "target": "dark",
                      "actions": ["logTransitionBrightGreenToDark"]
                    }
                  }
                },
                "dark": {
                  "entry": ["logEntryDarkGreen"],
                  "exit": ["logExitDarkGreen"],
                  "on": {
                    "BRIGHTER": {
                      "target": "bright",
                      "actions": ["logTransitionDarkGreenToBright"]
                    }
                  }
                }
              }
            }
          }
        },
        "pedestrian": {
          "entry": ["logEntryPedestrian"],
          "exit": ["logExitPedestrian"],
          "initial": "cannotWalk",
          "states": {
            "canWalk": {
              "entry": ["logEntryCanWalk"],
              "exit": ["logExitCanWalk"],
              "on": {
                "TIMER": {
                  "target": "cannotWalk",
                  "actions": ["logTransitionCanWalkToCannotWalk"]
                }
              }
            },
            "cannotWalk": {
              "entry": ["logEntryCannotWalk"],
              "exit": ["logExitCannotWalk"],
              "on": {
                "PUSH_BUTTON": {
                  "target": "canWalk",
                  "cond": "inRedLight",
                  "actions": ["logTransitionCannotWalkToCanWalk"]
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
        var machine = factory.FromJson(TrafficLightJson)
            .WithContext("isReady", true)
            .WithGuard("isReady", (ctx, _) => ctx.Get<bool>("isReady"))
            .WithGuard("inRedLight", (ctx, _) => true) // Simplified for this test
            .RegisterBasicActions()
            .BuildAndStart();

        // Act - GetState Ask ensures all initialization is complete
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("red", snapshot.CurrentState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bright", snapshot.CurrentState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannotWalk", snapshot.CurrentState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestTransitionRedToGreen()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(TrafficLightJson)
            .WithContext("isReady", true)
            .WithGuard("isReady", (ctx, _) => ctx.Get<bool>("isReady"))
            .WithGuard("inRedLight", (ctx, _) => true)
            .RegisterBasicActions()
            .BuildAndStart();

        // Act - Tell event then Ask for state (ensures event is processed)
        machine.Tell(new SendEvent("TIMER"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("green", snapshot.CurrentState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bright", snapshot.CurrentState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestGuardCondition()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(TrafficLightJson)
            .WithContext("isReady", false) // Guard will fail
            .WithGuard("isReady", (ctx, _) => ctx.Get<bool>("isReady"))
            .WithGuard("inRedLight", (ctx, _) => true)
            .RegisterBasicActions()
            .BuildAndStart();

        // Act - Tell event then Ask for state (ensures event is processed)
        machine.Tell(new SendEvent("TIMER"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Should remain in red because guard condition fails
        Assert.Contains("red", snapshot.CurrentState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestEntryAndExitActions()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(TrafficLightJson)
            .WithContext("isReady", true)
            .WithGuard("isReady", (ctx, _) => ctx.Get<bool>("isReady"))
            .WithGuard("inRedLight", (ctx, _) => true)
            .WithAction("logEntryLight", (ctx, _) => _entryActions.Add("Entering light"))
            .WithAction("logExitLight", (ctx, _) => _exitActions.Add("Exiting light"))
            .WithAction("logEntryRed", (ctx, _) => _entryActions.Add("Entering red"))
            .WithAction("logExitRed", (ctx, _) => _exitActions.Add("Exiting red"))
            .WithAction("logEntryBrightRed", (ctx, _) => _entryActions.Add("Entering red.bright"))
            .WithAction("logExitBrightRed", (ctx, _) => _exitActions.Add("Exiting red.bright"))
            .WithAction("logEntryGreen", (ctx, _) => _entryActions.Add("Entering green"))
            .WithAction("logEntryBrightGreen", (ctx, _) => _entryActions.Add("Entering green.bright"))
            .WithAction("logTransitionRedToGreen", (ctx, _) => _tranActions.Add("TransitionAction: red --> green"))
            .WithAction("logTransitionRedToGreen2", (ctx, _) => _tranActions.Add("TransitionAction2: red --> green"))
            .RegisterBasicActions()
            .BuildAndStart();

        // Act - Tell event then Ask for state (ensures event is processed)
        machine.Tell(new SendEvent("TIMER"));
        await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("Exiting red.bright", _exitActions);
        Assert.Contains("Exiting red", _exitActions);
        Assert.Contains("TransitionAction: red --> green", _tranActions);
        Assert.Contains("Entering green", _entryActions);
        Assert.Contains("Entering green.bright", _entryActions);
    }

    [Fact]
    public async Task TestParallelStates()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(TrafficLightJson)
            .WithGuard("inRedLight", (ctx, _) => true)
            .RegisterBasicActions()
            .BuildAndStart();

        // Act - Tell event then Ask for state (ensures event is processed)
        machine.Tell(new SendEvent("PUSH_BUTTON"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Both parallel regions should be active
        Assert.Contains("canWalk", snapshot.CurrentState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("red", snapshot.CurrentState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestNestedStates()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(TrafficLightJson)
            .WithContext("isReady", true)
            .WithGuard("isReady", (ctx, _) => ctx.Get<bool>("isReady"))
            .WithGuard("inRedLight", (ctx, _) => true)
            .RegisterBasicActions()
            .BuildAndStart();

        // Act - Tell event then Ask for state (ensures event is processed)
        machine.Tell(new SendEvent("TIMER")); // red -> green
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - Should be in nested green.bright state
        Assert.Contains("green", snapshot.CurrentState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bright", snapshot.CurrentState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestInvalidTransition()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(TrafficLightJson)
            .WithGuard("isReady", (ctx, _) => ctx.Get<bool>("isReady"))
            .WithGuard("inRedLight", (ctx, _) => true)
            .RegisterBasicActions()
            .BuildAndStart();

        // Get initial state (ensures initialization is complete)
        var before = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Act - Tell invalid event then Ask for state (ensures event is processed)
        machine.Tell(new SendEvent("INVALID_EVENT"));
        var after = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert - State should remain unchanged
        Assert.Equal(before.CurrentState, after.CurrentState);
        Assert.Contains("red", after.CurrentState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestNoTargetAction()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(TrafficLightJson)
            .WithContext("isReady", true)
            .WithGuard("isReady", (ctx, _) => ctx.Get<bool>("isReady"))
            .WithGuard("inRedLight", (ctx, _) => true)
            .WithAction("logNoTargetAction", (ctx, _) => _noTargetActions.Add("No target action executed"))
            .RegisterBasicActions()
            .BuildAndStart();

        // Act - Tell event then Ask for state (ensures event is processed)
        machine.Tell(new SendEvent("NO_TARGET"));
        await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("No target action executed", _noTargetActions);
    }

    [Fact]
    public async Task TestImplicitTargetTransition()
    {
        // Arrange
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(TrafficLightJson)
            .WithContext("isReady", true)
            .WithGuard("isReady", (ctx, _) => ctx.Get<bool>("isReady"))
            .WithGuard("inRedLight", (ctx, _) => true)
            .RegisterBasicActions()
            .BuildAndStart();

        // Act - Tell event then Ask for state (ensures event is processed)
        machine.Tell(new SendEvent("IMPLICIT_TARGET"));
        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("yellow", snapshot.CurrentState, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Extension methods for registering basic no-op actions for tests
/// </summary>
public static class TrafficLightTestExtensions
{
    public static MachineBuilder RegisterBasicActions(this MachineBuilder builder)
    {
        var basicActions = new[]
        {
            "logEntryLight", "logExitLight", "logEntryPedestrian", "logExitPedestrian",
            "logEntryCanWalk", "logExitCanWalk", "logEntryCannotWalk", "logExitCannotWalk",
            "logEntryRed", "logExitRed", "logEntryYellow", "logExitYellow",
            "logEntryGreen", "logExitGreen", "logEntryBrightRed", "logExitBrightRed",
            "logEntryDarkRed", "logExitDarkRed", "logEntryBrightGreen", "logExitBrightGreen",
            "logEntryDarkGreen", "logExitDarkGreen", "logTransitionRedToGreen",
            "logTransitionRedToGreen2", "logTransitionYellowToRed", "logTransitionGreenToYellow",
            "logTransitionBrightRedToDark", "logTransitionDarkRedToBright",
            "logTransitionBrightGreenToDark", "logTransitionDarkGreenToBright",
            "logTransitionCanWalkToCannotWalk", "logTransitionCannotWalkToCanWalk",
            "logNoTargetAction"
        };

        foreach (var action in basicActions)
        {
            // Only register if not already registered - allows tests to override specific actions
            if (!builder.HasAction(action))
            {
                builder = builder.WithAction(action, (ctx, _) => { }); // No-op
            }
        }

        return builder;
    }
}
