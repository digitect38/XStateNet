using XStateNet.Distributed.Channels;
using XStateNet.Distributed.Resilience;
using XStateNet.Orchestration;
using Xunit;
using CircuitBreakerOpenException = XStateNet.Orchestration.CircuitBreakerOpenException;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Minimal working tests that demonstrate the resilience features are functional
    /// </summary>
    public class MinimalResilienceTests : IDisposable
    {
        private EventBusOrchestrator? _orchestrator;

        public void Dispose()
        {
            _orchestrator?.Dispose();
        }

        [Fact]
        public async Task CircuitBreaker_Works()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var cb = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 2,
                openDuration: TimeSpan.FromMilliseconds(50));
            await cb.StartAsync();

            var stateChangedTcs = new TaskCompletionSource<string>();
            cb.StateTransitioned += (sender, args) =>
            {
                if (args.newState.Contains("open"))
                    stateChangedTcs.TrySetResult(args.newState);
            };

            // Act - Cause failures to open circuit
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    await cb.ExecuteAsync<string>(async ct =>
                    {
                        await Task.Yield();
                        throw new Exception("fail");
                    }, CancellationToken.None);
                }
                catch { }
            }

            // Wait for state change event with timeout
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                await stateChangedTcs.Task.ConfigureAwait(false);
            }

            await Task.Yield(); // Small delay to ensure state transition completes

            // Assert - Circuit should be open
            Assert.Contains("open", cb.CurrentState);

            // Test that circuit rejects calls when open
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
            {
                await cb.ExecuteAsync(ct => Task.FromResult("should fail"), CancellationToken.None);
            });

            // Wait for break duration to elapse (intentional - testing circuit breaker timing)
            await Task.Delay(100);

            // Now the circuit should allow execution (half-open)
            var result = await cb.ExecuteAsync(ct => Task.FromResult("success"), CancellationToken.None);
            Assert.Equal("success", result);

            await Task.Yield();
            // Circuit should be closed after successful execution
            Assert.Contains("closed", cb.CurrentState);
        }

        [Fact]
        public async Task RetryPolicy_Works()
        {
            // Arrange
            var retry = new XStateNetRetryPolicy("test", new RetryOptions
            {
                MaxRetries = 2,
                InitialDelay = TimeSpan.FromMilliseconds(10)
            });

            var attempts = 0;

            // Act
            var result = await retry.ExecuteAsync(ct =>
            {
                attempts++;
                if (attempts < 2) throw new Exception("retry");
                return Task.FromResult("success");
            });

            // Assert
            Assert.Equal(2, attempts);
            Assert.Equal("success", result);
        }

        [Fact]
        public async Task BoundedChannelManager_Works()
        {
            // Arrange
            var manager = new BoundedChannelManager<string>("test-channel", new CustomBoundedChannelOptions
            {
                Capacity = 10
            });

            // Act
            await manager.WriteAsync("test1");
            await manager.WriteAsync("test2");

            var (success1, result1) = await manager.ReadAsync();
            var (success2, result2) = await manager.ReadAsync();

            // Assert
            Assert.True(success1);
            Assert.True(success2);
            Assert.Equal("test1", result1);
            Assert.Equal("test2", result2);
        }
    }
}
