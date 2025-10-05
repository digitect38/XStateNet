using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using Xunit;

namespace AdvancedFeatures;

public class InConditionTests : OrchestratorTestBase
{
    private IPureStateMachine? _currentMachine;

    StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

    [Fact]
    public async Task TestInConditionWithParallelStateMet()
    {

        var stateMachineJson = InConditionStateMachineWithParallel.InConditionStateMachineScript;

        var actions = new Dictionary<string, Action<OrchestratedContext>>();
        var guards = new Dictionary<string, Func<StateMachine, bool>>();

        _currentMachine = CreateMachine("uniqueId", stateMachineJson, actions, guards);
        await _currentMachine.StartAsync();

        // Check initial states
        var currentState = _currentMachine.CurrentState;
        Assert.Contains("subStateA1", currentState);
        Assert.Contains("subStateB1", currentState);

        // Transition stateA.subStateA1 to stateA.subStateA2
        await SendEventAsync("TEST", _currentMachine, "EVENT1");
        await Task.Delay(100);
        currentState = _currentMachine.CurrentState;
        Assert.Contains("subStateA2", currentState);
        Assert.Contains("subStateB1", currentState);

        // Transition stateB.subStateB1 to stateB.subStateB2
        await SendEventAsync("TEST", _currentMachine, "EVENT2");
        await Task.Delay(100);
        currentState = _currentMachine.CurrentState;
        Assert.Contains("subStateA2", currentState);
        Assert.Contains("subStateB2", currentState);

        // Check InCondition
        await SendEventAsync("TEST", _currentMachine, "CHECK_IN_CONDITION");
        await Task.Delay(100);
        currentState = _currentMachine.CurrentState;
        Assert.Contains("subStateA2", currentState);
        Assert.Contains("finalState", currentState);
    }

    [Fact]
    public async Task TestInConditionWithParallelStateNotMet()
    {
        var stateMachineJson = InConditionStateMachineWithParallel.InConditionStateMachineScript;

        var actions = new Dictionary<string, Action<OrchestratedContext>>();
        var guards = new Dictionary<string, Func<StateMachine, bool>>();

        _currentMachine = CreateMachine("uniqueId", stateMachineJson, actions, guards);
        await _currentMachine.StartAsync();

        // Check initial states
        var currentState = _currentMachine.CurrentState;
        Assert.Contains("subStateA1", currentState);
        Assert.Contains("subStateB1", currentState);

        // Transition stateB.subStateB1 to stateB.subStateB2
        await SendEventAsync("TEST", _currentMachine, "EVENT2");
        await Task.Delay(100);
        currentState = _currentMachine.CurrentState;
        Assert.Contains("subStateA1", currentState);
        Assert.Contains("subStateB2", currentState);

        // Check InCondition, should not transition
        await SendEventAsync("TEST", _currentMachine, "CHECK_IN_CONDITION");
        await Task.Delay(100);
        currentState = _currentMachine.CurrentState;
        Assert.Contains("subStateA1", currentState);
        Assert.Contains("subStateB2", currentState);
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
