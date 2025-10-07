using XStateNet.Helpers;
using XStateNet.Orchestration;
using Xunit;
using CircuitBreakerOpenException = XStateNet.Orchestration.CircuitBreakerOpenException;

namespace XStateNet.Distributed.Tests.Resilience
{
    [Collection("TimingSensitive")]
    public class CircuitBreakerTests : IDisposable
    {
        private EventBusOrchestrator? _orchestrator;

        public void Dispose()
        {
            _orchestrator?.Dispose();
        }

        [Fact]
        public async Task CircuitBreaker_StartsInClosedState()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var circuitBreaker = new OrchestratedCircuitBreaker("test", _orchestrator);
            await circuitBreaker.StartAsync();

            // Assert
            Assert.Contains("closed", circuitBreaker.CurrentState);
        }

        [Fact]
        public async Task CircuitBreaker_OpensAfterThresholdFailures()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var circuitBreaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 3,
                openDuration: TimeSpan.FromMilliseconds(100));
            await circuitBreaker.StartAsync();
            var failureCount = 0;

            // Event-driven waiting setup
            var openStateReached = new TaskCompletionSource<string>();
            circuitBreaker.StateTransitioned += (sender, args) =>
            {
                if (args.newState.Contains("open") && !args.newState.Contains("halfOpen"))
                    openStateReached.TrySetResult(args.newState);
            };

            // Act
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await circuitBreaker.ExecuteAsync<bool>(async ct =>
                    {
                        await Task.Yield();
                        throw new InvalidOperationException("Test failure");
                    }, CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    failureCount++;
                }
            }

            // Wait for open state with timeout
            var openTask = await Task.WhenAny(openStateReached.Task, Task.Delay(500));
            Assert.True(openTask == openStateReached.Task, "Circuit breaker should open within 500ms");

            // Grace period for property update
            await Task.Yield();

            // Assert
            Assert.Equal(3, failureCount);
            Assert.Contains("open", circuitBreaker.CurrentState);
            Assert.DoesNotContain("halfOpen", circuitBreaker.CurrentState);
        }

        [Fact]
        public async Task CircuitBreaker_RejectsCallsWhenOpen()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var circuitBreaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 1,
                openDuration: TimeSpan.FromSeconds(1));
            await circuitBreaker.StartAsync();

            // Event-driven waiting setup
            var openStateReached = new TaskCompletionSource<string>();
            circuitBreaker.StateTransitioned += (sender, args) =>
            {
                if (args.newState.Contains("open") && !args.newState.Contains("halfOpen"))
                    openStateReached.TrySetResult(args.newState);
            };

            // Act - Open the circuit
            try
            {
                await circuitBreaker.ExecuteAsync<bool>(async ct =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("Test failure");
                }, CancellationToken.None);
            }
            catch { }

            // Wait for open state with timeout
            var openTask = await Task.WhenAny(openStateReached.Task, Task.Delay(500));
            Assert.True(openTask == openStateReached.Task, "Circuit breaker should open within 500ms");

            // Grace period for property update
            await Task.Yield();

            // Assert - Should reject calls
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
            {
                await circuitBreaker.ExecuteAsync(ct => Task.FromResult("test"), CancellationToken.None);
            });
        }

        [Fact]
        public async Task CircuitBreaker_TransitionsToHalfOpen_AfterBreakDuration()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var circuitBreaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 1,
                openDuration: TimeSpan.FromMilliseconds(100));
            await circuitBreaker.StartAsync();

            var openStateReached = new TaskCompletionSource<string>();
            var halfOpenStateReached = new TaskCompletionSource<string>();
            var closedStateReached = new TaskCompletionSource<string>();
            circuitBreaker.StateTransitioned += (sender, args) =>
            {
                if (args.newState.Contains("open") && !args.newState.Contains("halfOpen"))
                    openStateReached.TrySetResult(args.newState);
                if (args.newState.Contains("halfOpen"))
                    halfOpenStateReached.TrySetResult(args.newState);
                if (args.newState.Contains("closed"))
                    closedStateReached.TrySetResult(args.newState);
            };

            // Act - Open the circuit
            try
            {
                await circuitBreaker.ExecuteAsync<bool>(async ct =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("Test failure");
                }, CancellationToken.None);
            }
            catch { }

            // Wait for open state transition (with timeout)
            var openTask = await Task.WhenAny(openStateReached.Task, Task.Delay(500));
            Assert.True(openTask == openStateReached.Task, "Circuit breaker should transition to open state within 500ms");

            // Give the state machine time to update CurrentState property
            await Task.Yield();
            Assert.Contains("open", circuitBreaker.CurrentState);

            // Wait for half-open state transition (after openDuration of 100ms)
            var halfOpenTask = await Task.WhenAny(halfOpenStateReached.Task, Task.Delay(500));
            Assert.True(halfOpenTask == halfOpenStateReached.Task, "Circuit breaker should transition to half-open state within 500ms");

            // Give the state machine time to update CurrentState property
            await Task.Yield();

            // Attempt execution - should succeed now that circuit is half-open
            string? result = null;
            Exception? caughtException = null;

            try
            {
                result = await circuitBreaker.ExecuteAsync(ct => Task.FromResult("success"), CancellationToken.None);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Assert - should have succeeded
            Assert.Null(caughtException);
            Assert.NotNull(result);
            Assert.Equal("success", result);

            // Wait for closed state transition after successful test in half-open
            var closedTask = await Task.WhenAny(closedStateReached.Task, Task.Delay(500));
            Assert.True(closedTask == closedStateReached.Task, "Circuit breaker should transition to closed state after success in half-open within 500ms");
            Assert.Contains("closed", circuitBreaker.CurrentState);
        }

        [Fact]
        public async Task CircuitBreaker_ClosesAfterSuccessInHalfOpen()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var circuitBreaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 1,
                openDuration: TimeSpan.FromMilliseconds(100));
            await circuitBreaker.StartAsync();

            // Event-driven waiting setup
            var openStateReached = new TaskCompletionSource<string>();
            var halfOpenStateReached = new TaskCompletionSource<string>();
            var closedStateReached = new TaskCompletionSource<string>();

            circuitBreaker.StateTransitioned += (sender, args) =>
            {
                if (args.newState.Contains("open") && !args.newState.Contains("halfOpen"))
                    openStateReached.TrySetResult(args.newState);
                if (args.newState.Contains("halfOpen"))
                    halfOpenStateReached.TrySetResult(args.newState);
                if (args.newState.Contains("closed"))
                    closedStateReached.TrySetResult(args.newState);
            };

            // Open circuit
            try
            {
                await circuitBreaker.ExecuteAsync<bool>(async ct =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException();
                }, CancellationToken.None);
            }
            catch { }

            // Wait for open state
            var openTask = await Task.WhenAny(openStateReached.Task, Task.Delay(500));
            Assert.True(openTask == openStateReached.Task, "Circuit should open within 500ms");

            // Grace period for property update
            await Task.Yield();

            // Wait for circuit to transition to half-open
            var halfOpenTask = await Task.WhenAny(halfOpenStateReached.Task, Task.Delay(500));
            Assert.True(halfOpenTask == halfOpenStateReached.Task, "Circuit should transition to half-open within 500ms");

            // Grace period for property update
            await Task.Yield();

            // Execute successful call in half-open state
            var result = await circuitBreaker.ExecuteAsync(ct => Task.FromResult(1), CancellationToken.None);
            Assert.Equal(1, result);

            // Wait for closed state
            var closedTask = await Task.WhenAny(closedStateReached.Task, Task.Delay(500));
            Assert.True(closedTask == closedStateReached.Task, "Circuit should close within 500ms");

            // Grace period for property update
            await Task.Yield();

            // Assert
            Assert.Contains("closed", circuitBreaker.CurrentState);
        }

        [Fact]
        public async Task CircuitBreaker_ReopensOnFailureInHalfOpen()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var circuitBreaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 1,
                openDuration: TimeSpan.FromMilliseconds(100));
            await circuitBreaker.StartAsync();

            // Event-driven waiting setup
            var openStateReached = new TaskCompletionSource<string>();
            var halfOpenStateReached = new TaskCompletionSource<string>();
            var reopenedStateReached = new TaskCompletionSource<string>();

            circuitBreaker.StateTransitioned += (sender, args) =>
            {
                if (args.newState.Contains("open") && !args.newState.Contains("halfOpen"))
                {
                    // First open or reopened
                    if (!openStateReached.Task.IsCompleted)
                        openStateReached.TrySetResult(args.newState);
                    else
                        reopenedStateReached.TrySetResult(args.newState);
                }
                if (args.newState.Contains("halfOpen"))
                    halfOpenStateReached.TrySetResult(args.newState);
            };

            // Step 1: Open circuit with initial failure
            try
            {
                await circuitBreaker.ExecuteAsync<bool>(async ct =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("Initial failure");
                }, CancellationToken.None);
            }
            catch (InvalidOperationException) { /* Expected */ }

            // Wait for open state with timeout
            var openTask = await Task.WhenAny(openStateReached.Task, Task.Delay(500));
            Assert.True(openTask == openStateReached.Task, "Circuit should open within 500ms");

            // Poll for CurrentState to update (with timeout)
            var deadline = DateTime.UtcNow.AddMilliseconds(200);
            while (!circuitBreaker.CurrentState.Contains("open", StringComparison.OrdinalIgnoreCase)
                   && DateTime.UtcNow < deadline)
            {
                await Task.Yield();
            }
            Assert.Contains("open", circuitBreaker.CurrentState, StringComparison.OrdinalIgnoreCase);

            // Step 2: Wait for circuit to transition to half-open
            var halfOpenTask = await Task.WhenAny(halfOpenStateReached.Task, Task.Delay(500));
            Assert.True(halfOpenTask == halfOpenStateReached.Task, "Circuit should transition to half-open within 500ms");

            // Grace period for property update
            await Task.Yield();

            // Step 3: Fail in half-open state
            Exception? caughtException = null;
            try
            {
                await circuitBreaker.ExecuteAsync<bool>(async ct =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("Failure in half-open");
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Step 4: Verify the failure occurred
            Assert.NotNull(caughtException);
            Assert.IsType<InvalidOperationException>(caughtException);

            // Step 5: Wait for circuit to reopen (event-driven)
            var reopenTask = await Task.WhenAny(reopenedStateReached.Task, Task.Delay(500));
            Assert.True(reopenTask == reopenedStateReached.Task, "Circuit should reopen within 500ms");

            // Grace period for property update
            await Task.Yield();
            Assert.Contains("open", circuitBreaker.CurrentState);
        }

        [Fact]
        public async Task CircuitBreaker_StateChangedEvent_Fires()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var circuitBreaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 1);
            await circuitBreaker.StartAsync();

            (string oldState, string newState, string reason)? eventArgs = null;
            var eventFired = new TaskCompletionSource<bool>();

            circuitBreaker.StateTransitioned += (sender, args) =>
            {
                eventArgs = args;
                eventFired.TrySetResult(true);
            };

            // Act
            try
            {
                await circuitBreaker.ExecuteAsync<bool>(async ct =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException();
                }, CancellationToken.None);
            }
            catch { }

            // Wait for the event to fire
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                await eventFired.Task.WaitAsync(cts.Token);
            }

            // Assert
            Assert.NotNull(eventArgs);
            Assert.Contains("open", eventArgs.Value.newState);
        }

        [Fact]
        public async Task CircuitBreaker_ThreadSafe_ConcurrentOperations()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig { PoolSize = 4 });
            var circuitBreaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 5);
            await circuitBreaker.StartAsync();

            var successCount = 0;
            var failureCount = 0;

            // Act - Execute many concurrent operations
            var tasks = new Task[100];
            for (int i = 0; i < 100; i++)
            {
                var index = i;
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        if (index % 10 == 0) // 10% failure rate
                        {
                            await circuitBreaker.ExecuteAsync<bool>(async ct =>
                            {
                                await Task.Yield();
                                throw new InvalidOperationException();
                            }, CancellationToken.None);
                        }
                        else
                        {
                            await circuitBreaker.ExecuteAsync<bool>(async ct =>
                            {
                                await Task.Yield();
                                Interlocked.Increment(ref successCount);
                                return true;
                            }, CancellationToken.None);
                        }
                    }
                    catch
                    {
                        Interlocked.Increment(ref failureCount);
                    }
                });
            }

            await Task.WhenAll(tasks);
            await Task.Yield();

            // Assert - Should handle concurrent operations correctly
            Assert.True(successCount > 0);
            Assert.True(failureCount > 0);
        }
    }
}
