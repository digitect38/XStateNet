using NUnit.Framework;
using XStateNet;
using XStateNet.UnitTest;
using System.Collections.Concurrent;
using System.Collections.Generic;
namespace AdvancedFeatures;

using ActionMap = ConcurrentDictionary<string, List<NamedAction>>;
using GuardMap = ConcurrentDictionary<string, NamedGuard>;

[TestFixture]
public class AfterTests
{
    private ActionMap actions;
    private GuardMap guards;
    //static string testLog = "";

    [SetUp]
    public void Setup()
    {
        actions = new ActionMap
        {
            ["logEntryRed"] = [new("logEntryRed", (sm) => sm.ContextMap["log"] += "Entering red; ")],
            ["logExitRed"] = [new("logExitRed", (sm) => sm.ContextMap["log"] += "Exiting red; ")],
            ["logTransitionAfterRedToGreen"] = [new("logTransitionAfterRedToGreen", (sm) => sm.ContextMap["log"] += "After transition to green; ")],
            ["logEntryGreen"] = [new("logEntryGreen", (sm) => sm.ContextMap["log"] += "Entering green; ")],
        };

        guards = new GuardMap
        {
            ["isReady"] = new NamedGuard("isReady", (sm) => (bool)sm.ContextMap["isReady"])
        };

    }
    [Test]
    public void TestAfterTransition()
    {
        // Arrange
        var stateMachineJson = @"
        {
            'id': 'trafficLight',
            'context': { 'isReady': true, 'log': '' },
            'states': {
                'red': {
                    'entry': [ 'logEntryRed' ],
                    'exit': [ 'logExitRed' ],
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

        _stateMachine = StateMachine.CreateFromScript(stateMachineJson, actions, guards).Start();


        // Initial state should be 'red'
        var cxtlog = _stateMachine.ContextMap["log"].ToString();

        Assert.AreEqual(cxtlog, "Entering red; ");

        cxtlog = _stateMachine.ContextMap["log"].ToString();

        // Wait half the time of the 'after' delay
        System.Threading.Thread.Sleep(450);

        // Assert that the 'after' transition has not occurred yet
        Assert.AreEqual(cxtlog, "Entering red; ");

        // Wait longer than the 'after' delay
        System.Threading.Thread.Sleep(100);

        // State should now be 'green'
        _stateMachine.PrintCurrentStatesString();
        var currentStates = _stateMachine.GetCurrentState();

        cxtlog = _stateMachine.ContextMap["log"].ToString();
        // Assert
        Assert.AreEqual(currentStates, "#trafficLight.green");
        Assert.AreEqual(cxtlog, "Entering red; Exiting red; After transition to green; Entering green; ");
    }

    StateMachine _stateMachine = null;

    [Test]
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

        var currentState = _stateMachine.GetCurrentState();
        // Assert
        string log = _stateMachine.ContextMap["log"].ToString();
        currentState.AssertEquivalence("#trafficLight.red");
        Assert.AreEqual(log, "Entering red; ");
    }
}