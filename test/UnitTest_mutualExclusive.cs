using Xunit;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using XStateNet.UnitTest;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedFeatures;

public class MutualExclusionTests : OrchestratorTestBase
{
    private IPureStateMachine? _currentMachine;

    StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

    private async Task<IPureStateMachine> CreateStateMachine(string uniqueId)
    {
        var actions = new Dictionary<string, Action<OrchestratedContext>>();
        var guards = new Dictionary<string, Func<StateMachine, bool>>();

        string jsonScript = @$"
        {{
            id: '{uniqueId}',
            type: 'parallel',
            states: {{
                shooter: {{
                    initial: 'wait',
                    states: {{
                        wait: {{
                            on: {{
                                SHOOT: {{
                                    target: 'shoot',
                                    in: '#{uniqueId}.trashCan.open'
                                }}
                            }}
                        }},
                        shoot: {{
                            on: {{
                                DONE: 'wait'
                            }}
                        }}
                    }}
                }},
                trashCan: {{
                    initial: 'closed',
                    states: {{
                        open: {{
                            on: {{
                                CLOSE: {{
                                    target: 'closed',
                                    in: '#{uniqueId}.shooter.wait'
                                }}
                            }}
                        }},
                        closed: {{
                            on: {{
                                OPEN: 'open'
                            }}
                        }}
                    }}
                }}
            }}
        }}";

        _currentMachine = CreateMachine(uniqueId, jsonScript, actions, guards);
        await _currentMachine.StartAsync();
        return _currentMachine;
    }

    [Fact]
    public async Task TestInitialState()
    {
        var stateMachine = await CreateStateMachine("uniqueId");

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("wait", currentState);
        Assert.Contains("closed", currentState);
    }

    [Fact]
    public async Task TestTransitionShoot()
    {
        var stateMachine = await CreateStateMachine("uniqueId");

        await SendEventAsync("TEST", stateMachine, "OPEN");
        await Task.Delay(100);
        await SendEventAsync("TEST", stateMachine, "SHOOT");
        await Task.Delay(100);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("shoot", currentState);
        Assert.Contains("open", currentState);
    }

    [Fact]
    public async Task TestTransitionCannotShoot()
    {
        var stateMachine = await CreateStateMachine("uniqueId");

        await SendEventAsync("TEST", stateMachine, "SHOOT");
        await Task.Delay(100);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("wait", currentState);
        Assert.Contains("closed", currentState);
    }

    [Fact]
    public async Task TestTransitionCannotClose()
    {
        var stateMachine = await CreateStateMachine("uniqueId");

        // trashcan should not be closed while shooting!
        await SendEventAsync("TEST", stateMachine, "OPEN");
        await Task.Delay(100);
        await SendEventAsync("TEST", stateMachine, "SHOOT");
        await Task.Delay(100);
        await SendEventAsync("TEST", stateMachine, "CLOSE");
        await Task.Delay(100);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("shoot", currentState);
        Assert.Contains("open", currentState);
    }

    [Fact]
    public async Task TestTransitionCanClose()
    {
        var stateMachine = await CreateStateMachine("uniqueId");

        // trashcan can be closed after if shooting is done!
        await SendEventAsync("TEST", stateMachine, "OPEN");
        await Task.Delay(100);
        await SendEventAsync("TEST", stateMachine, "SHOOT");
        await Task.Delay(100);
        await SendEventAsync("TEST", stateMachine, "DONE");
        await Task.Delay(100);
        await SendEventAsync("TEST", stateMachine, "CLOSE");
        await Task.Delay(100);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("wait", currentState);
        Assert.Contains("closed", currentState);
    }

    [Fact]
    public async Task TestShootAndDoneTransition()
    {
        var stateMachine = await CreateStateMachine("uniqueId");

        await SendEventAsync("TEST", stateMachine, "OPEN");
        await Task.Delay(100);
        await SendEventAsync("TEST", stateMachine, "SHOOT");
        await Task.Delay(100);
        await SendEventAsync("TEST", stateMachine, "DONE");
        await Task.Delay(100);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("wait", currentState);
        Assert.Contains("open", currentState);
    }

    [Fact]
    public async Task TestOpenAndCloseTransition()
    {
        var stateMachine = await CreateStateMachine("uniqueId");

        await SendEventAsync("TEST", stateMachine, "OPEN");
        await Task.Delay(100);
        await SendEventAsync("TEST", stateMachine, "CLOSE");
        await Task.Delay(100);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("wait", currentState);
        Assert.Contains("closed", currentState);
    }
}
