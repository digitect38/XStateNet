using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Resilience;

class TestCircuitBreaker
{
    static async Task Main()
    {
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            BreakDuration = TimeSpan.FromSeconds(30),
            SuccessCountInHalfOpen = 2
        };

        var cb = new CircuitBreaker("test", options);
        Console.WriteLine($"Initial state: {cb.State}");

        // Try to cause 3 failures
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await cb.ExecuteAsync(async () =>
                {
                    await Task.Yield();
                    throw new Exception($"Failure {i + 1}");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Caught: {ex.Message}, State: {cb.State}");
            }
        }

        Console.WriteLine($"Final state: {cb.State}");

        // Try one more call - should be rejected
        try
        {
            await cb.ExecuteAsync(async () =>
            {
                await Task.Yield();
                return "Should not get here";
            });
        }
        catch (CircuitBreakerOpenException)
        {
            Console.WriteLine("Circuit breaker is open - call rejected (expected)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected exception: {ex.Message}");
        }
    }
}