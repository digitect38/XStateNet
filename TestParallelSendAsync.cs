using System;
using System.Threading.Tasks;
using XStateNet;

public class TestParallelSendAsync
{
    public static async Task Main()
    {
        Console.WriteLine("Testing parallel state SendAsync fix...\n");

        // Create a state machine with parallel states
        var json = @"{
            'id': 'parallelTest',
            'initial': 'parallel',
            'states': {
                'parallel': {
                    'type': 'parallel',
                    'states': {
                        'region1': {
                            'initial': 'r1_state1',
                            'states': {
                                'r1_state1': {
                                    'on': {
                                        'NEXT': 'r1_state2'
                                    }
                                },
                                'r1_state2': {}
                            }
                        },
                        'region2': {
                            'initial': 'r2_state1',
                            'states': {
                                'r2_state1': {
                                    'on': {
                                        'NEXT': 'r2_state2'
                                    }
                                },
                                'r2_state2': {}
                            }
                        }
                    }
                }
            }
        }";

        var machine = new StateMachine();
        StateMachine.ParseStateMachine(machine, json, false, null, null, null, null, null);
        machine.Start();

        Console.WriteLine("Initial state: " + machine.GetActiveStateNames());

        // Test Send (synchronous)
        Console.WriteLine("\n--- Testing Send('NEXT') ---");
        var sendResult = machine.Send("NEXT");
        Console.WriteLine("Send('NEXT') returned: " + sendResult);
        Console.WriteLine("Expected: parallel.region1.r1_state2,parallel.region2.r2_state2");

        // Reset to initial state
        machine.Stop();
        machine.Start();
        Console.WriteLine("\n--- Reset to initial: " + machine.GetActiveStateNames());

        // Test SendAsync (should now properly wait for all parallel transitions)
        Console.WriteLine("\n--- Testing SendAsync('NEXT') ---");
        var sendAsyncResult = await machine.SendAsync("NEXT");
        Console.WriteLine("SendAsync('NEXT') returned: " + sendAsyncResult);
        Console.WriteLine("Expected: parallel.region1.r1_state2,parallel.region2.r2_state2");

        // Check if they match
        Console.WriteLine("\n--- Results Comparison ---");
        Console.WriteLine("Send result:      " + sendResult);
        Console.WriteLine("SendAsync result: " + sendAsyncResult);
        Console.WriteLine("Results match: " + (sendResult == sendAsyncResult));

        if (sendResult == sendAsyncResult)
        {
            Console.WriteLine("\n✓ SUCCESS: SendAsync now properly waits for all parallel state transitions!");
        }
        else
        {
            Console.WriteLine("\n✗ FAILURE: SendAsync still doesn't match Send result");
        }
    }
}