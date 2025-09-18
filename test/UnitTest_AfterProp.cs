
using Xunit;

using XStateNet;
using XStateNet.UnitTest;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace AdvancedFeatures;

public class AfterTests : IDisposable
{
    private ActionMap actions;
    private GuardMap guards;

    public AfterTests()
    {

        actions = new ActionMap
        {
            ["logEntryRed"] = [
                new("logEntryRed", (sm) => {                    
                    if(sm.ContextMap != null) {
                        sm.ContextMap["log"] += "Entering red; ";
                        StateMachine.Log("Entering Red; "); 
                    }
                })
            ],

            ["logExitRed"] = [
                new("logExitRed", (sm) => {
                    if(sm.ContextMap != null) {
                        sm.ContextMap["log"] += "Exiting red; ";
                        StateMachine.Log("Exiting Red; "); 
                    }
                })
            ],

            ["logTransitionAfterRedToGreen"] = [
                new("logTransitionAfterRedToGreen", (sm) => {
                    if(sm.ContextMap != null) {
                        sm.ContextMap["log"] += "After transition to green; ";
                        StateMachine.Log("After transition to green;} ");
                    }  
                })
            ],

            ["logEntryGreen"] = [new("logEntryGreen", 
                (sm) => {
                    if(sm.ContextMap != null) {
                        sm.ContextMap["log"] += "Entering green; ";
                        StateMachine.Log("Entering Green; ");
                    }
                })
            ],
        };

        guards = new GuardMap
        {
            ["isReady"] = new NamedGuard("isReady", (sm) =>
            {
                var isReadyValue = sm.ContextMap?["isReady"];
                if (isReadyValue is bool b) return b;
                return false;
            })
        };
    }

    public void Dispose()
    {
        // Cleanup resources if needed
    }

    [Fact]
    public async Task TestAfterTransition()
    {
        // Arrange
        // Use a unique ID to avoid conflicts when tests run in parallel
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

        _stateMachine = StateMachine.CreateFromScript(stateMachineJson, actions, guards);
        _stateMachine!.Start();


        // Initial state should be 'red'
        var cxtlog = _stateMachine?.ContextMap?["log"]?.ToString();

        Assert.Equal("Entering red; ", cxtlog);

        // Wait half the time of the 'after' delay
        await Task.Delay(450);

        // Assert that the 'after' transition has not occurred yet
        var currentStatesBefore = _stateMachine?.GetActiveStateString();
        Assert.Equal($"#{uniqueId}.red", currentStatesBefore);

        // Wait longer than the 'after' delay
        await Task.Delay(100);

        // State should now be 'green'
        _stateMachine?.PrintCurrentStatesString();
        var currentStates = _stateMachine?.GetActiveStateString();

        cxtlog = _stateMachine?.ContextMap?["log"]?.ToString();
        // Assert
        Assert.Equal($"#{uniqueId}.green", currentStates);
        Assert.Equal("Entering red; Exiting red; After transition to green; Entering green; ", cxtlog);
    }

    StateMachine? _stateMachine = null;

    [Fact]
    public async Task TestGuardedAfterTransition_FailsGuard()
    {
        // Arrange
        // Use a unique ID to avoid conflicts when tests run in parallel
        var uniqueId = $"trafficLight_{Guid.NewGuid():N}";
        var stateMachineJson = $@"
        {{
            'id': '{uniqueId}',
            'context': {{ 'isReady': false, 'log': '' }},
            'states': {{
                'red': {{
                    'entry': 'logEntryRed',
                    'exit': 'logExitRed',
                    'after': {{
                        '500': {{ 'target': 'green', 'guard': 'isReady', 'actions': 'logTransitionAfterRedToGreen' }}
                    }}
                }},
                'green': {{
                    'entry': [],
                    'exit': []
                }}
            }},
            'initial': 'red'
        }}";

        _stateMachine = (StateMachine)StateMachine.CreateFromScript(stateMachineJson, actions, guards).Start();

        // Wait for the after transition to occur
        await Task.Delay(600); // Wait longer than the 'after' delay

        _stateMachine.PrintCurrentStatesString(); // State should still be 'red'

        var currentState = _stateMachine!.GetActiveStateString();
        // Assert
        string? log = _stateMachine?.ContextMap?["log"]?.ToString();
        Assert.Equal($"#{uniqueId}.red", currentState);
        Assert.Equal("Entering red; ", log);
    }
}
