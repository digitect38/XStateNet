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

        _stateMachine = (StateMachine)StateMachineFactory.CreateFromScript(stateMachineJson,
            threadSafe: false,
            true,
            new ActionMap(),
            new GuardMap()).Start();

        await _stateMachine!.SendAsync("TO_A2");
        var currentState = await _stateMachine!.SendAsync("TO_B");
        Assert.Contains(".B", currentState);

        currentState = await _stateMachine!.SendAsync("TO_A");
        // When returning to A.hist, it should restore A.A2 for shallow history
        Assert.Contains(".A.A2", currentState);
    }

    [Fact]
    public async void DeepHistory()
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

        _stateMachine = (StateMachine)StateMachineFactory.CreateFromScript(stateMachineJson,
            threadSafe: false,
            true,
            new ActionMap(),
            new GuardMap());
        var machineId = _stateMachine!.machineId;
        await _stateMachine.StartAsync();
        await _stateMachine!.WaitForStateAsync(".A1a");
        await _stateMachine!.SendAsync("TO_A1b");
        await _stateMachine!.WaitForStateAsync(".A1b");
        string currentState = await _stateMachine!.SendAsync("TO_B");
        Assert.Contains("B.B1", currentState);

        currentState = await _stateMachine!.SendAsync("TO_A");
        Assert.Contains("A.A1.A1b", currentState);
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}

