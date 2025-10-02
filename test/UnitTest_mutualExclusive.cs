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
        var uniqueId = $"TestInitialState_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("wait", currentState);
        Assert.Contains("closed", currentState);
    }

    [Fact]
    public async Task TestTransitionShoot()
    {
        var uniqueId = $"TestTransitionShoot_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);

        await SendEventAsync("TEST", uniqueId, "OPEN");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "SHOOT");
        await Task.Delay(100);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("shoot", currentState);
        Assert.Contains("open", currentState);
    }

    [Fact]
    public async Task TestTransitionCannotShoot()
    {
        var uniqueId = $"TestTransitionCannotShoot_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);

        await SendEventAsync("TEST", uniqueId, "SHOOT");
        await Task.Delay(100);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("wait", currentState);
        Assert.Contains("closed", currentState);
    }

    [Fact]
    public async Task TestTransitionCannotClose()
    {
        var uniqueId = $"TestTransitionCannotClose_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);

        // trashcan should not be closed while shooting!
        await SendEventAsync("TEST", uniqueId, "OPEN");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "SHOOT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "CLOSE");
        await Task.Delay(100);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("shoot", currentState);
        Assert.Contains("open", currentState);
    }

    [Fact]
    public async Task TestTransitionCanClose()
    {
        var uniqueId = $"TestTransitionCanClose_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);

        // trashcan can be closed after if shooting is done!
        await SendEventAsync("TEST", uniqueId, "OPEN");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "SHOOT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "DONE");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "CLOSE");
        await Task.Delay(100);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("wait", currentState);
        Assert.Contains("closed", currentState);
    }

    [Fact]
    public async Task TestShootAndDoneTransition()
    {
        var uniqueId = $"TestShootAndDoneTransition_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);

        await SendEventAsync("TEST", uniqueId, "OPEN");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "SHOOT");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "DONE");
        await Task.Delay(100);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("wait", currentState);
        Assert.Contains("open", currentState);
    }

    [Fact]
    public async Task TestOpenAndCloseTransition()
    {
        var uniqueId = $"TestOpenAndCloseTransition_{Guid.NewGuid():N}";
        var stateMachine = await CreateStateMachine(uniqueId);

        await SendEventAsync("TEST", uniqueId, "OPEN");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "CLOSE");
        await Task.Delay(100);

        var currentState = _currentMachine!.CurrentState;
        Assert.Contains("wait", currentState);
        Assert.Contains("closed", currentState);
    }
}
