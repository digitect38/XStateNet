using Xunit;

using XStateNet;
using XStateNet.UnitTest;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System;
namespace AdvancedFeatures;

public class MutualExclusionTests : IDisposable
{
    private StateMachine CreateStateMachine(string uniqueId)
    {
        var actionCallbacks = new ActionMap();
        var guardCallbacks = new GuardMap();

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

        var stateMachine = StateMachineFactory.CreateFromScript(jsonScript, threadSafe: false, false, actionCallbacks, guardCallbacks);
        stateMachine!.Start();
        return stateMachine;
    }

    [Fact]
    public void TestInitialState()
    {
        var uniqueId = "TestInitialState_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);

        stateMachine!.GetActiveStateNames().AssertEquivalence($"#{uniqueId}.shooter.wait;#{uniqueId}.trashCan.closed");
    }

    [Fact]
    public void TestTransitionShoot()
    {
        var uniqueId = "TestTransitionShoot_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);

        stateMachine!.Send("OPEN");
        stateMachine!.Send("SHOOT");

        stateMachine!.GetActiveStateNames().AssertEquivalence($"#{uniqueId}.shooter.shoot;#{uniqueId}.trashCan.open");
    }

    [Fact]
    public void TestTransitionCannotShoot()
    {
        var uniqueId = "TestTransitionCannotShoot_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);

        stateMachine!.Send("SHOOT");

        stateMachine!.GetActiveStateNames().AssertEquivalence($"#{uniqueId}.shooter.wait;#{uniqueId}.trashCan.closed");
    }


    [Fact]
    public void TestTransitionCannotClose()
    {
        var uniqueId = "TestTransitionCannotClose_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);

        // trashcan should not be closed while shooting!
        stateMachine!.Send("OPEN");
        stateMachine!.Send("SHOOT");
        stateMachine!.Send("CLOSE");

        stateMachine!.GetActiveStateNames().AssertEquivalence($"#{uniqueId}.shooter.shoot;#{uniqueId}.trashCan.open");
    }

    [Fact]
    public void TestTransitionCanClose()
    {
        var uniqueId = "TestTransitionCanClose_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);

        // trashcan can be closed after if shooting is done!
        stateMachine!.Send("OPEN");
        stateMachine!.Send("SHOOT");
        stateMachine!.Send("DONE");
        stateMachine!.Send("CLOSE");

        stateMachine!.GetActiveStateNames().AssertEquivalence($"#{uniqueId}.shooter.wait;#{uniqueId}.trashCan.closed");
    }

    [Fact]
    public void TestShootAndDoneTransition()
    {
        var uniqueId = "TestShootAndDoneTransition_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);

        stateMachine!.Send("OPEN");
        stateMachine!.Send("SHOOT");
        stateMachine!.Send("DONE");

        stateMachine!.GetActiveStateNames().AssertEquivalence($"#{uniqueId}.shooter.wait;#{uniqueId}.trashCan.open");
    }

    [Fact]
    public void TestOpenAndCloseTransition()
    {
        var uniqueId = "TestOpenAndCloseTransition_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);

        stateMachine!.Send("OPEN");
        stateMachine!.Send("CLOSE");
        var stateString = stateMachine!.GetActiveStateNames();
        stateMachine!.GetActiveStateNames().AssertEquivalence($"#{uniqueId}.shooter.wait;#{uniqueId}.trashCan.closed");
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}

