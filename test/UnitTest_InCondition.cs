using Xunit;

using XStateNet;
using XStateNet.UnitTest;
using System.Collections.Concurrent;
using System.Collections.Generic;
namespace AdvancedFeatures;

public class InConditionTests : IDisposable
{
    private StateMachine _stateMachine;

    private ActionMap _actions;
    private GuardMap _guards;

    public InConditionTests()
    {
        _actions = new ();
        _guards = new ();

        var stateMachineJson = InConditionStateMachineWithParallel.InConditionStateMachineScript;

        _stateMachine = (StateMachine)StateMachine.CreateFromScript(stateMachineJson, _actions, _guards).Start();
    }

    [Fact]
    public void TestInConditionWithParallelStateMet()
    {
        // Check initial states
        var currentState = _stateMachine!.GetActiveStateString();
        currentState.AssertEquivalence("#inConditionMachine.stateA.subStateA1;#inConditionMachine.stateB.subStateB1");

        // Transition stateA.subStateA1 to stateA.subStateA2
        _stateMachine!.Send("EVENT1");
        currentState = _stateMachine!.GetActiveStateString();
        currentState.AssertEquivalence("#inConditionMachine.stateA.subStateA2;#inConditionMachine.stateB.subStateB1");
        
        // Transition stateB.subStateB1 to stateB.subStateB2
        _stateMachine!.Send("EVENT2");
        currentState = _stateMachine!.GetActiveStateString();
        currentState.AssertEquivalence("#inConditionMachine.stateA.subStateA2;#inConditionMachine.stateB.subStateB2");

        // Check InCondition
        _stateMachine!.Send("CHECK_IN_CONDITION");
        currentState = _stateMachine!.GetActiveStateString();
        currentState.AssertEquivalence("#inConditionMachine.stateA.subStateA2;#inConditionMachine.stateB.finalState");
        }

    [Fact]
    public void TestInConditionWithParallelStateNotMet()
    {
        // Check initial states
        var currentState = _stateMachine!.GetActiveStateString();
        currentState.AssertEquivalence("#inConditionMachine.stateA.subStateA1;#inConditionMachine.stateB.subStateB1");

        // Transition stateB.subStateB1 to stateB.subStateB2
        _stateMachine!.Send("EVENT2");
        currentState = _stateMachine!.GetActiveStateString();
        currentState.AssertEquivalence("#inConditionMachine.stateA.subStateA1;#inConditionMachine.stateB.subStateB2");

        // Check InCondition, should not transition
        _stateMachine!.Send("CHECK_IN_CONDITION");
        currentState = _stateMachine!.GetActiveStateString();
        currentState.AssertEquivalence("#inConditionMachine.stateA.subStateA1;#inConditionMachine.stateB.subStateB2");
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}

public static class InConditionStateMachineWithParallel
{
    public static string InConditionStateMachineScript => @"
        {
            'id': 'inConditionMachine',
            'initial': 'parallelState',
            'type' : 'parallel',
            'states': {                
                'stateA': {
                    'initial': 'subStateA1',
                    'states': {
                        'subStateA1': {
                            'on': { 'EVENT1': 'subStateA2' }
                        },
                        'subStateA2': {}
                    }
                },
                'stateB': {
                    'initial': 'subStateB1',
                    'states': {
                        'subStateB1': {
                            'on': { 'EVENT2': 'subStateB2' }
                        },
                        'subStateB2': {
                            'on': { 
                                'CHECK_IN_CONDITION': { 
                                    'target': 'finalState', 
                                    'in': '#inConditionMachine.stateA.subStateA2' 
                                } 
                            }
                        },
                        'finalState': {}
                    }
                }
            }
        }";
}



