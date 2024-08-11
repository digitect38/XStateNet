﻿using NUnit.Framework;
using XStateNet;
using XStateNet.UnitTest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Transactions;

namespace AdvancedFeatures;

[TestFixture]
public class HistoryState
{
    StateMachine _stateMachine;

    [Test]
    public void ShallowHistory()
    {
        var stateMachineJson = @"{
            'id': 'testMachine',
            'initial': 'A',
            'states': {
                'A': {
                    'initial': 'A1',
                    'states': {
                        'A1': {
                            'on': { 'TO_A2': 'A2' }
                        },
                        'A2': {
                            'on': { 'TO_A1': 'A1' }
                        },
                        'hist': { 
                            type : 'history',
                            'history':'shallow'
                        }
                    },
                    'on': { 'TO_B': 'B' }
                },
                'B': {
                    'on': { 'TO_A': 'A.hist' }
                }
            }
        }"
        ;

        _stateMachine = StateMachine.CreateFromScript(stateMachineJson,
            new ConcurrentDictionary<string, List<NamedAction>>(),
            new ConcurrentDictionary<string, NamedGuard>()).Start();

        _stateMachine.Send("TO_A2");
        _stateMachine.Send("TO_B");

        var currentState = _stateMachine.GetActiveStateString();
        Assert.AreEqual("#testMachine.B", currentState);

        _stateMachine.Send("TO_A");

        currentState = _stateMachine.GetActiveStateString(leafOnly: false);
        Assert.AreEqual("#testMachine.A;#testMachine.A.A2", currentState);
    }

    [Test]
    public void DeepHistory()
    {
        var stateMachineJson = @" {
            'id': 'testMachine',
            'initial': 'A',
              states : {         
                  'A': {
                      'initial': 'A1',              
                      'states': {
                          'hist' : {
                            type : 'history',
                            'history':'deep'
                          },   
                          'A1': {
                              'initial': 'A1a',
                              'states': {
                                  'A1a': {
                                      'on': { 'TO_A1b': 'A1b' }
                                  },
                                  'A1b': {}
                              }
                          },
                          'A2': {}
                      },
                      on: {
                         'TO_B': 'B'
                      }
                  },

                  'B': {
                      'on': { 'TO_A': 'A.hist' },
                        initial : 'B1',
                        states : {
                            'B1': {},
                            'B2': {}
                        }
                  }
              }
          }"
        ;

        _stateMachine = StateMachine.CreateFromScript(stateMachineJson,
            new ConcurrentDictionary<string, List<NamedAction>>(),
            new ConcurrentDictionary<string, NamedGuard>()).Start();

        var currentState = _stateMachine.GetActiveStateString();
        _stateMachine.Send("TO_A1b");
        currentState = _stateMachine.GetActiveStateString();
        _stateMachine.Send("TO_B");
        currentState = _stateMachine.GetActiveStateString(leafOnly : false);
        Assert.AreEqual("#testMachine.B;#testMachine.B.B1", currentState);
        _stateMachine.Send("TO_A");
        currentState = _stateMachine.GetActiveStateString(leafOnly: false);

        currentState.AssertEquivalence("#testMachine.A;#testMachine.A.A1;#testMachine.A.A1.A1b");
    }
}