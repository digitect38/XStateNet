using System;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Distributed.Resilience;

class TestCircuitBreakerDebug
{
    static async Task Main()
    {
        Console.WriteLine("Testing Circuit Breaker State Machine...\n");

        // Create a simple test state machine
        var config = @"{
            id: 'TestCircuitBreaker',
            initial: 'closed',
            states: {
                closed: {
                    on: {
                        FAIL: {
                            target: 'open',
                            actions: 'logFail'
                        }
                    }
                },
                open: {
                    type: 'final'
                }
            }
        }";

        var actionMap = new ActionMap();
        actionMap["logFail"] = new List<NamedAction>
        {
            new NamedAction("logFail", (sm) =>
            {
                Console.WriteLine("[ACTION] logFail executed!");
            })
        };

        var guardMap = new GuardMap();

        try
        {
            var stateMachine = StateMachine.CreateFromScript(config, actionMap, guardMap);
            stateMachine.Start();

            Console.WriteLine($"Initial state: {stateMachine.GetActiveStateString()}");

            // Send FAIL event
            Console.WriteLine("\nSending FAIL event...");
            stateMachine.Send("FAIL");

            Console.WriteLine($"State after FAIL: {stateMachine.GetActiveStateString()}");

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