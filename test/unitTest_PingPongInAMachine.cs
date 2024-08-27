﻿using NUnit.Framework;
using XStateNet;
using XStateNet.UnitTest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedFeatures;

[TestFixture]
public class IntraMachinePingPongStateMachinesTests
{
    private StateMachine _pingPongStateMachine;

    private ActionMap _actions;
    private GuardMap _guards;

    [SetUp]
    public void Setup()
    {
        _actions = new ()
        {
            ["sendToB"] = new List<NamedAction> { new NamedAction("sendToB", (sm) => send_to_b(sm)) },
            ["sendToA"] = new List<NamedAction> { new NamedAction("sendToA", (sm) => _pingPongStateMachine.Send("to_a")) }
        };

        _guards = new ();

        var stateMachineJson = PingPongMachine.PingPongStateMachineScript;

        _pingPongStateMachine = StateMachine.CreateFromScript(stateMachineJson, _actions, _guards).Start();
    }

    Action<StateMachine> send_to_b = (sm) => sm.Send("to_b");


    [Test]
    public async Task TestPingPongStateMachines()
    {
        // Initial states
        _pingPongStateMachine.GetActiveStateString().AssertEquivalence("#pingPongMachine.A.a;#pingPongMachine.B.a");

        // Wait for the ping-pong actions to occur
        await Task.Delay(1100);
        _pingPongStateMachine.GetActiveStateString().AssertEquivalence("#pingPongMachine.A.b;#pingPongMachine.B.b");

        await Task.Delay(1100);
        _pingPongStateMachine.GetActiveStateString().AssertEquivalence("#pingPongMachine.A.a;#pingPongMachine.B.a");
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

