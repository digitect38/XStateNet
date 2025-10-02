using Xunit;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using XStateNet.UnitTest;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedFeatures;

public class HistoryState : OrchestratorTestBase
{
    private IPureStateMachine? _currentMachine;

    [Fact]
    public async Task ShallowHistory()
    {
        var uniqueId = $"testMachine_{Guid.NewGuid():N}";

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
        }";

        var actions = new Dictionary<string, Action<OrchestratedContext>>();
        var guards = new Dictionary<string, Func<StateMachine, bool>>();

        _currentMachine = CreateMachine(uniqueId, stateMachineJson, actions, guards);
        await _currentMachine.StartAsync();

        await SendEventAsync("TEST", uniqueId, "TO_A2");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "TO_B");
        await Task.Delay(100);
        var currentState = _currentMachine.CurrentState;
        Assert.Contains("B", currentState);

        await SendEventAsync("TEST", uniqueId, "TO_A");
        await Task.Delay(100);
        currentState = _currentMachine.CurrentState;
        // When returning to A.hist, it should restore A.A2 for shallow history
        Assert.Contains("A2", currentState);
    }

    [Fact]
    public async Task DeepHistory()
    {
        var uniqueId = $"testMachine_{Guid.NewGuid():N}";

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
          }";

        var actions = new Dictionary<string, Action<OrchestratedContext>>();
        var guards = new Dictionary<string, Func<StateMachine, bool>>();

        _currentMachine = CreateMachine(uniqueId, stateMachineJson, actions, guards);
        await _currentMachine.StartAsync();

        await SendEventAsync("TEST", uniqueId, "TO_A1b");
        await Task.Delay(100);
        await SendEventAsync("TEST", uniqueId, "TO_B");
        await Task.Delay(100);
        var currentState = _currentMachine.CurrentState;
        Assert.Contains("B", currentState);

        await SendEventAsync("TEST", uniqueId, "TO_A");
        await Task.Delay(100);
        currentState = _currentMachine.CurrentState;
        // When returning to A.hist (deep), it should restore A.A1.A1b
        Assert.Contains("A1b", currentState);
    }
}
