using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Resilience;

class TestCircuitBreaker
{
    static async Task Main()
    {
        Console.WriteLine("Testing XStateNetCircuitBreaker...");

        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            BreakDuration = TimeSpan.FromMilliseconds(100)
        };

        var cb = new XStateNetCircuitBreaker("test", options);

        Console.WriteLine($"Initial state: {cb.State}");

        // Try to cause 3 failures
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await cb.ExecuteAsync(async () =>
                {
                    await Task.Delay(1);
                    throw new InvalidOperationException($"Failure {i + 1}");
                });
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Caught exception {i + 1}: {ex.Message}");
            }
        }

        Console.WriteLine($"State after 3 failures: {cb.State}");

        // Should be open now
        if (cb.State != CircuitState.Open)
        {
            Console.WriteLine("ERROR: Circuit breaker should be open but is " + cb.State);
        }
        else
        {
            Console.WriteLine("SUCCESS: Circuit breaker is open as expected");
        }
    }
}
