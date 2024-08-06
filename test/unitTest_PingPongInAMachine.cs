using NUnit.Framework;
using SharpState;
using SharpState.UnitTest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedFeatures;

[TestFixture]
public class IntraMachinePingPongStateMachinesTests
{
    private StateMachine _pingPongStateMachine;

    private ConcurrentDictionary<string, List<NamedAction>> _actions;
    private ConcurrentDictionary<string, NamedGuard> _guards;

    [SetUp]
    public void Setup()
    {
        _actions = new ConcurrentDictionary<string, List<NamedAction>>
        {
            ["sendToB"] = new List<NamedAction> { new NamedAction("sendToB", (sm) => send_to_b(sm)) },
            ["sendToA"] = new List<NamedAction> { new NamedAction("sendToA", (sm) => _pingPongStateMachine.Send("to_a")) }
        };

        _guards = new ConcurrentDictionary<string, NamedGuard>();

        var stateMachineJson = PingPongMachine.PingPongStateMachineScript;

        _pingPongStateMachine = StateMachine.CreateFromScript(stateMachineJson, _actions, _guards).Start();
    }

    Action<StateMachine> send_to_b = (sm) => sm.Send("to_b");


    [Test]
    public async Task TestPingPongStateMachines()
    {
        // Initial states
        _pingPongStateMachine.GetCurrentState().AssertEquivalence("#pingPongMachine.A.a;#pingPongMachine.B.a");

        // Wait for the ping-pong actions to occur
        await Task.Delay(1100);
        _pingPongStateMachine.GetCurrentState().AssertEquivalence("#pingPongMachine.A.b;#pingPongMachine.B.b");

        await Task.Delay(1100);
        _pingPongStateMachine.GetCurrentState().AssertEquivalence("#pingPongMachine.A.a;#pingPongMachine.B.a");
    }
}
public static class PingPongMachine
{
    public static string PingPongStateMachineScript => @"
    {
        'id': 'pingPongMachine',
        'type': 'parallel',
        'states': {
            'A': {
                'initial': 'a',
                'states': {
                    'a': {
                        'after': {
                            '1000': {
                                'target': 'b',
                                'actions': ['sendToB']
                            }
                        }
                    },
                    'b': {
                        'on': {
                            'to_a': {
                                'target': 'a'
                            }
                        }
                    }
                }
            },
            'B': {
                'initial': 'a',
                'states': {
                    'a': {
                        'on': {
                            'to_b': {
                                'target': 'b'
                            }
                        }
                    },
                    'b': {
                        'after': {
                            '1000': {
                                'target': 'a',
                                'actions': ['sendToA']
                            }
                        }
                    }
                }
            }
        }
    }";
}

