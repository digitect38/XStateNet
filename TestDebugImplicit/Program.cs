using System;
using System.Threading.Tasks;
using XStateNet;

class Program
{
    static async Task Main()
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
        Console.WriteLine("\n=== Testing Send ===");
        var sendResult = stateMachine.Send("IMPLICIT_TARGET");
        Console.WriteLine("Send result: " + sendResult);
        Console.WriteLine("Contains 'yellow': " + sendResult.Contains("yellow"));

        // Reset
        stateMachine.Stop();
        stateMachine.Start();
        Console.WriteLine("\n=== Reset ===");
        Console.WriteLine("State after reset: " + stateMachine.GetActiveStateNames());

        // Test SendAsync
        Console.WriteLine("\n=== Testing SendAsync ===");
        var sendAsyncResult = await stateMachine.SendAsync("IMPLICIT_TARGET");
        Console.WriteLine("SendAsync result: " + sendAsyncResult);
        Console.WriteLine("Contains 'yellow': " + sendAsyncResult.Contains("yellow"));

        // Compare
        Console.WriteLine("\n=== Comparison ===");
        Console.WriteLine("Send:      " + sendResult);
        Console.WriteLine("SendAsync: " + sendAsyncResult);
        Console.WriteLine("Match: " + (sendResult == sendAsyncResult));
    }
}