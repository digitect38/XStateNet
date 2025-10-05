using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Distributed.Resilience;

class Program
{
    static async Task Main()
    {
        Console.WriteLine("Testing Circuit Breaker State Machine...\n");

        // Create a test state machine with guards similar to CircuitBreaker
        var config = @"{
            id: 'TestCircuitBreaker',
            initial: 'closed',
            states: {
                closed: {
                    on: {
                        FAIL: [
                            {
                                target: 'open',
                                cond: 'shouldOpen',
                                actions: ['increment', 'notifyOpen']
                            },
                            {
                                actions: ['increment']
                            }
                        ]
                    }
                },
                open: {
                    type: 'final'
                }
            }
        }";

        var counter = 0;
        var threshold = 3;

        var actionMap = new ActionMap();
        actionMap["increment"] = new List<NamedAction>
        {
            new NamedAction("increment", (sm) =>
            {
                counter++;
                Console.WriteLine($"[ACTION] increment executed! counter = {counter}");
            })
        };

        actionMap["notifyOpen"] = new List<NamedAction>
        {
            new NamedAction("notifyOpen", (sm) =>
            {
                Console.WriteLine("[ACTION] notifyOpen executed!");
            })
        };

        var guardMap = new GuardMap();
        guardMap["shouldOpen"] = new NamedGuard("shouldOpen", (sm) =>
        {
            var nextCount = counter + 1;
            var result = nextCount >= threshold;
            Console.WriteLine($"[GUARD] shouldOpen: current={counter}, next={nextCount}, threshold={threshold}, result={result}");
            return result;
        });

        try
        {
            var stateMachine = StateMachine.CreateFromScript(config, actionMap, guardMap);
            await stateMachine.StartAsync();

            Console.WriteLine($"Initial state: {stateMachine.GetActiveStateString()}");

            // Send multiple FAIL events
            for (int i = 1; i <= 3; i++)
            {
                Console.WriteLine($"\n--- Sending FAIL event #{i} ---");
                stateMachine.Send("FAIL");
                Console.WriteLine($"State after FAIL #{i}: {stateMachine.GetActiveStateString()}");
            }

            if (stateMachine.GetActiveStateString().Contains("open"))
            {
                Console.WriteLine("\nSUCCESS: State machine transitioned to open!");
            }
            else
            {
                Console.WriteLine("\nFAILURE: State machine did not transition!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}