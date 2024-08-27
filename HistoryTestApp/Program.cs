using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using XStateNet;

// main
class Program
{
    static void Main()
    {
        HistoryStateTest historyStateTest = new HistoryStateTest();
        historyStateTest.DeepHistory();
    }



}

public class HistoryStateTest
{
    StateMachine stateMachine;

    static void Log(string message) => Console.WriteLine(message);

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

        stateMachine = StateMachine.CreateFromScript(stateMachineJson,
            new ActionMap(),
            new GuardMap()).Start();

        stateMachine.Send("TO_A2");
        stateMachine.Send("TO_B");

        var currentState = stateMachine.GetActiveStateString();


        stateMachine.Send("TO_A");

        currentState = stateMachine.GetActiveStateString(leafOnly: false);

    }


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

        stateMachine = StateMachine.CreateFromScript(stateMachineJson,
            new ActionMap(),
            new GuardMap()).Start();

        var currentState = stateMachine.GetActiveStateString();
        stateMachine.Send("TO_A1b");
        currentState = stateMachine.GetActiveStateString();
        stateMachine.Send("TO_B");
        currentState = stateMachine.GetActiveStateString(leafOnly: false);
        /*
        stateMachine.Send("TO_A");
        currentState = stateMachine.GetActiveStateString(leafOnly: false);
        */
        //currentState.AssertEquivalence("#testMachine.A;#testMachine.A.A1;#testMachine.A.A1.A1b");



#if false
        Log("==========================================================================");

        var path1 = stateMachine.GetFullTransitionSinglePath("#testMachine.B", "#testMachine.A.hist");
        var exitPathCsv1 = path1.exitSinglePath.ToCsvString(stateMachine);
        var entryPathCsv1 = path1.entrySinglePath.ToCsvString(stateMachine);

        Log($"entryPath: {exitPathCsv1}");
        Log($"exitPath: {entryPathCsv1}");

        Log("==========================================================================");

        string? firstExit = path1.exitSinglePath.First();
        string firstEntry = path1.entrySinglePath.First();

        if (stateMachine != null)
        {
            stateMachine.TransitUp(firstExit?.ToState(stateMachine) as RealState);
            stateMachine.TransitDown(firstEntry.ToState(stateMachine) as RealState, "#testMachine.A.hist");
        }
#else
        stateMachine.TransitFull("#testMachine.B", "#testMachine.A.hist");
#endif
        currentState = stateMachine.GetActiveStateString(leafOnly: false);
        Log($"currentState: {currentState}");

    }
}
