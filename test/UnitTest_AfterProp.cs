using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using Xunit;

namespace AdvancedFeatures;

public class AfterTests : OrchestratorTestBase
{
    private IPureStateMachine? _currentMachine;

    private Dictionary<string, Action<OrchestratedContext>> CreateActions()
    {
        StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

        return new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logEntryRed"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    underlying.ContextMap["log"] += "Entering red; ";
                }
            },
            ["logExitRed"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    underlying.ContextMap["log"] += "Exiting red; ";
                }
            },
            ["logTransitionAfterRedToGreen"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    underlying.ContextMap["log"] += "After transition to green; ";
                }
            },
            ["logEntryGreen"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    underlying.ContextMap["log"] += "Entering green; ";
                }
            }
        };
    }

    private Dictionary<string, Func<StateMachine, bool>> CreateGuards()
    {
        return new Dictionary<string, Func<StateMachine, bool>>
        {
            ["isReady"] = (sm) =>
            {
                var isReadyValue = sm.ContextMap?["isReady"];
                if (isReadyValue is bool b) return b;
                return false;
            }
        };
    }

    [Fact]
    public async Task TestAfterTransition()
    {
        // Arrange
        var uniqueId = $"trafficLight_{Guid.NewGuid():N}";
        var stateMachineJson = @"{
            'id': '" + uniqueId + @"',
            'context': { 'isReady': true, 'log': '' },
            'states': {
                'red': {
                    'entry': 'logEntryRed',
                    'exit': 'logExitRed',
                    'after': {
                        '500': { 'target': 'green', 'actions': 'logTransitionAfterRedToGreen' }
                    }
                },
                'green': {
                    'entry': 'logEntryGreen',
                    'exit': []
                }
            },
            'initial': 'red'
        }";

        _currentMachine = CreateMachine(uniqueId, stateMachineJson, CreateActions(), CreateGuards());
        await _currentMachine.StartAsync();

        // Get underlying for context access
        var underlying = (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

        // Initial state should be 'red'
        var cxtlog = underlying?.ContextMap?["log"]?.ToString();
        Assert.Equal("Entering red; ", cxtlog);

        // Wait half the time of the 'after' delay
        await Task.Delay(450);

        // Assert that the 'after' transition has not occurred yet
        var currentStatesBefore = _currentMachine.CurrentState;
        Assert.Contains("red", currentStatesBefore);

        // Wait longer than the 'after' delay
        await Task.Delay(100);

        // State should now be 'green'
        var currentStates = _currentMachine.CurrentState;
        cxtlog = underlying?.ContextMap?["log"]?.ToString();

        // Assert
        Assert.Contains("green", currentStates);
        Assert.Equal("Entering red; Exiting red; After transition to green; Entering green; ", cxtlog);
    }

    [Fact]
    public async Task TestGuardedAfterTransition_FailsGuard()
    {
        // Arrange
        var stateMachineJson = @"
        {
            'id': 'trafficLight',
            'context': { 'isReady': false, 'log': '' },
            'states': {
                'red': {
                    'entry': 'logEntryRed',
                    'exit': 'logExitRed',
                    'after': {
                        '500': { 'target': 'green', 'guard': 'isReady', 'actions': 'logTransitionAfterRedToGreen' }
                    }
                },
                'green': {
                    'entry': [],
                    'exit': []
                }
            },
            'initial': 'red'
        }";

        _currentMachine = CreateMachine("trafficLight", stateMachineJson, CreateActions(), CreateGuards());
        await _currentMachine.StartAsync();

        var underlying = (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

        // Wait for the after transition to occur
        await Task.Delay(600); // Wait longer than the 'after' delay

        var currentState = _currentMachine.CurrentState;

        // Assert - should still be in red because guard failed
        string? log = underlying?.ContextMap?["log"]?.ToString();
        Assert.Contains("red", currentState);
        Assert.Equal("Entering red; ", log);
    }
}
