using Xunit;

using XStateNet;
using XStateNet.UnitTest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Transactions;

namespace AdvancedFeatures;

public class HistoryState : IDisposable
{
    StateMachine? _stateMachine;

    [Fact]
    public async Task ShallowHistory()
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

        _stateMachine = (StateMachine)StateMachine.CreateFromScript(stateMachineJson,
            new ActionMap(),
            new GuardMap()).Start();

        _stateMachine!.Send("TO_A2");
        var currentState = await _stateMachine!.SendAsyncWithState("TO_B");

        Assert.Equal("#testMachine.B", currentState.ToString());

        currentState = await _stateMachine!.SendAsyncWithState("TO_A");

        // When returning to A.hist, it should restore A.A2 for shallow history
        Assert.Equal("#testMachine.A.A2", currentState.ToString());
    }

    [Fact]
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

        _stateMachine = (StateMachine)StateMachine.CreateFromScript(stateMachineJson,
            new ActionMap(),
            new GuardMap()).Start();

        var currentState = _stateMachine!.GetActiveStateString();
        _stateMachine!.Send("TO_A1b");
        currentState = _stateMachine!.GetActiveStateString();
        _stateMachine!.Send("TO_B");
        currentState = _stateMachine!.GetActiveStateString(leafOnly : false);
        Assert.Equal("#testMachine.B;#testMachine.B.B1", currentState);
        _stateMachine!.Send("TO_A");
        currentState = _stateMachine!.GetActiveStateString(leafOnly: false);

        currentState.AssertEquivalence("#testMachine.A;#testMachine.A.A1;#testMachine.A.A1.A1b");
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}

