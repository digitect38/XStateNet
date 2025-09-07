using Xunit;
using FluentAssertions;
using XStateNet;
using XStateNet.UnitTest;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
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
                return isReadyValue != null && isReadyValue.GetType() == typeof(JTokenType) && (bool)isReadyValue;
            })
        };
    }

    public void Dispose()
    {
        // Cleanup resources if needed
    }

    [Fact]
    public void TestAfterTransition()
    {
        // Arrange
        var stateMachineJson = @"
        {
            'id': 'trafficLight',
            'context': { 'isReady': true, 'log': '' },
            'states': {
                'red': {
                    'entry': ['logEntryRed'],
                    'exit': ['logExitRed'],
                    'after': {
                        '500': { 'target': 'green', 'actions': [ 'logTransitionAfterRedToGreen' ] }
                    }
                },
                'green': {
                    'entry': ['logEntryGreen'],
                    'exit': []
                }
            },
            'initial': 'red'
        }";

        _stateMachine = StateMachine.CreateFromScript(stateMachineJson, actions, guards);
        _stateMachine!.Start();


        // Initial state should be 'red'
        var cxtlog = _stateMachine?.ContextMap?["log"]?.ToString();

        cxtlog.Should().Be("Entering red; ");

        cxtlog = _stateMachine?.ContextMap?["log"]?.ToString();

        // Wait half the time of the 'after' delay
        System.Threading.Thread.Sleep(450);

        // Assert that the 'after' transition has not occurred yet
        cxtlog.Should().Be("Entering red; ");

        // Wait longer than the 'after' delay
        System.Threading.Thread.Sleep(100);

        // State should now be 'green'
        _stateMachine?.PrintCurrentStatesString();
        var currentStates = _stateMachine?.GetActiveStateString();

        cxtlog = _stateMachine?.ContextMap?["log"]?.ToString();
        // Assert
        currentStates.Should().Be("#trafficLight.green");
        cxtlog.Should().Be("Entering red; Exiting red; After transition to green; Entering green; ");
    }

    StateMachine? _stateMachine = null;

    [Fact]
    public void TestGuardedAfterTransition_FailsGuard()
    {
        // Arrange
        var stateMachineJson = @"
        {
            'id': 'trafficLight',
            'context': { 'isReady': false, 'log': '' },
            'states': {
                'red': {
                    'entry': [ 'logEntryRed' ],
                    'exit': [ 'logExitRed' ],
                    'after': {
                        '500': { 'target': 'green', 'guard': 'isReady', 'actions': [ 'logTransitionAfterRedToGreen' ] }
                    }
                },
                'green': {
                    'entry': [],
                    'exit': []
                }
            },
            'initial': 'red'
        }";

        // Wait for the after transition to occur
        System.Threading.Thread.Sleep(600); // Wait longer than the 'after' delay

        _stateMachine = StateMachine.CreateFromScript(stateMachineJson, actions, guards).Start();

        _stateMachine.PrintCurrentStatesString(); // State should still be 'red'

        var currentState = _stateMachine!.GetActiveStateString();
        // Assert
        string? log = _stateMachine?.ContextMap?["log"]?.ToString();
        currentState.AssertEquivalence("#trafficLight.red");
        log.Should().Be("Entering red; ");
    }
}