using System;
using XStateNet;

public class TestImplicitDebug
{
    public static void Main()
    {
        const string json = @"{
          id: 'trafficLight',
          type: 'parallel',
          context: {
            isReady: 'false',
            count: 0
          },
          states: {
            light: {
              initial: 'red',
              states: {
                red: {
                  on: {
                    IMPLICIT_TARGET: 'yellow'
                  },
                  initial: 'bright',
                  states: {
                    bright: {}
                  }
                },
                yellow: {},
                green: {}
              }
            },
            pedestrian: {
              initial: 'walk',
              states: {
                walk: {},
                wait: {},
                stop: {}
              }
            }
          }
        }";

        var stateMachine = new StateMachine();
        StateMachine.ParseStateMachine(stateMachine, json, false, null, null, null, null, null);
        stateMachine.Start();

        Console.WriteLine("Initial state: " + stateMachine.GetActiveStateNames());

        // Test Send (synchronous)
        var sendResult = stateMachine.Send("IMPLICIT_TARGET");
        Console.WriteLine("Send result: " + sendResult);

        // Reset
        stateMachine.Stop();
        stateMachine.Start();
        Console.WriteLine("\nReset to initial: " + stateMachine.GetActiveStateNames());

        // Test SendAsync
        var sendAsyncResult = stateMachine.SendAsync("IMPLICIT_TARGET").GetAwaiter().GetResult();
        Console.WriteLine("SendAsync result: " + sendAsyncResult);

        Console.WriteLine("\nDoes SendAsync result contain 'yellow'? " + sendAsyncResult.Contains("yellow"));
    }
}