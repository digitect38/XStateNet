using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet.Orchestration;
using XStateNet.Tests.TestInfrastructure;

namespace XStateNet.Tests
{
    //[TestPriority(TestPriority.Critical)]
    //[TestCaseOrderer("XStateNet.Tests.TestInfrastructure.PriorityOrderer", "XStateNet.Tests")]
    [Collection("TimingSensitive")]
    public class OrchestratedCircuitBreakerTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private EventBusOrchestrator? _orchestrator;

        public OrchestratedCircuitBreakerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task CircuitBreaker_StartsInClosedState()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var breaker = new OrchestratedCircuitBreaker("test", _orchestrator);

            // Act
            await breaker.StartAsync();

            // Assert
            Assert.Contains("closed", breaker.CurrentState);
        }

        [Fact]
        public async Task CircuitBreaker_OpensAfterFailureThreshold()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var breaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 3,
                openDuration: TimeSpan.FromMilliseconds(100));

            await breaker.StartAsync();

            // Event-driven waiting setup
            var openStateReached = new TaskCompletionSource<string>();
            breaker.StateTransitioned += (sender, args) =>
            {
                if (args.newState.Contains("open") && !args.newState.Contains("halfOpen"))
                    openStateReached.TrySetResult(args.newState);
            };

            // Act - Record failures up to threshold
            await breaker.RecordFailureAsync();
            Assert.Contains("closed", breaker.CurrentState);

            await breaker.RecordFailureAsync();
            Assert.Contains("closed", breaker.CurrentState);

            await breaker.RecordFailureAsync();

            // Wait for open state with timeout
            var openTask = await Task.WhenAny(openStateReached.Task, Task.Delay(500));
            Assert.True(openTask == openStateReached.Task, "Circuit should open within 500ms");

            // Grace period for property update
            await Task.Delay(10);

            // Assert
            Assert.Contains("open", breaker.CurrentState);
            Assert.DoesNotContain("halfOpen", breaker.CurrentState);
            Assert.Equal(3, breaker.FailureCount);
        }

        [Fact]
        public async Task CircuitBreaker_TransitionsToHalfOpenAfterTimeout()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var breaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 2,
                openDuration: TimeSpan.FromMilliseconds(100));

            await breaker.StartAsync();

            // Event-driven waiting setup
            var openStateReached = new TaskCompletionSource<string>();
            var halfOpenStateReached = new TaskCompletionSource<string>();
            breaker.StateTransitioned += (sender, args) =>
            {
                if (args.newState.Contains("open") && !args.newState.Contains("halfOpen"))
                    openStateReached.TrySetResult(args.newState);
                if (args.newState.Contains("halfOpen"))
                    halfOpenStateReached.TrySetResult(args.newState);
            };

            // Open the circuit
            await breaker.RecordFailureAsync();
            await breaker.RecordFailureAsync();

            // Wait for open state
            var openTask = await Task.WhenAny(openStateReached.Task, Task.Delay(500));
            Assert.True(openTask == openStateReached.Task, "Circuit should open within 500ms");

            // Grace period for property update
            await Task.Delay(10);
            Assert.Contains("open", breaker.CurrentState);

            // Act - Wait for half-open transition
            var halfOpenTask = await Task.WhenAny(halfOpenStateReached.Task, Task.Delay(500));
            Assert.True(halfOpenTask == halfOpenStateReached.Task, "Circuit should transition to half-open within 500ms");

            // Grace period for property update
            await Task.Delay(10);

            // Assert
            Assert.Contains("halfOpen", breaker.CurrentState);
        }

        [Fact]
        public async Task CircuitBreaker_ClosesAfterSuccessfulTestInHalfOpen()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var breaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 2,
                openDuration: TimeSpan.FromMilliseconds(100));

            await breaker.StartAsync();

            // Event-driven waiting setup
            var openStateReached = new TaskCompletionSource<string>();
            var halfOpenStateReached = new TaskCompletionSource<string>();
            var closedStateReached = new TaskCompletionSource<string>();
            breaker.StateTransitioned += (sender, args) =>
            {
                if (args.newState.Contains("open") && !args.newState.Contains("halfOpen"))
                    openStateReached.TrySetResult(args.newState);
                if (args.newState.Contains("halfOpen"))
                    halfOpenStateReached.TrySetResult(args.newState);
                if (args.newState.Contains("closed"))
                    closedStateReached.TrySetResult(args.newState);
            };

            // Open the circuit
            await breaker.RecordFailureAsync();
            await breaker.RecordFailureAsync();

            // Wait for open state
            var openTask = await Task.WhenAny(openStateReached.Task, Task.Delay(500));
            Assert.True(openTask == openStateReached.Task, "Circuit should open within 500ms");

            // Wait for half-open
            var halfOpenTask = await Task.WhenAny(halfOpenStateReached.Task, Task.Delay(500));
            Assert.True(halfOpenTask == halfOpenStateReached.Task, "Circuit should transition to half-open within 500ms");

            // Grace period for property update
            await Task.Delay(10);
            Assert.Contains("halfOpen", breaker.CurrentState);

            // Act - Successful test
            var result = await breaker.ExecuteAsync(async ct =>
            {
                await Task.Delay(1, ct);
                return true;
            });

            // Wait for closed state
            var closedTask = await Task.WhenAny(closedStateReached.Task, Task.Delay(500));
            Assert.True(closedTask == closedStateReached.Task, "Circuit should close within 500ms");

            // Grace period for property update
            await Task.Delay(10);

            // Assert
            Assert.True(result);
            Assert.Contains("closed", breaker.CurrentState);
            Assert.Equal(0, breaker.FailureCount); // Counters reset
        }

        [Fact]
        public async Task CircuitBreaker_ReOpensAfterFailedTestInHalfOpen()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var breaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 2,
                openDuration: TimeSpan.FromMilliseconds(100));

            await breaker.StartAsync();

            // Event-driven waiting setup
            var openStateReached = new TaskCompletionSource<string>();
            var halfOpenStateReached = new TaskCompletionSource<string>();
            var reopenedStateReached = new TaskCompletionSource<string>();
            breaker.StateTransitioned += (sender, args) =>
            {
                if (args.newState.Contains("open") && !args.newState.Contains("halfOpen"))
                {
                    // Distinguish first open from reopened
                    if (!openStateReached.Task.IsCompleted)
                        openStateReached.TrySetResult(args.newState);
                    else
                        reopenedStateReached.TrySetResult(args.newState);
                }
                if (args.newState.Contains("halfOpen"))
                    halfOpenStateReached.TrySetResult(args.newState);
            };

            // Open the circuit
            await breaker.RecordFailureAsync();
            await breaker.RecordFailureAsync();

            // Wait for open state
            var openTask = await Task.WhenAny(openStateReached.Task, Task.Delay(500));
            Assert.True(openTask == openStateReached.Task, "Circuit should open within 500ms");

            // Wait for half-open
            var halfOpenTask = await Task.WhenAny(halfOpenStateReached.Task, Task.Delay(500));
            Assert.True(halfOpenTask == halfOpenStateReached.Task, "Circuit should transition to half-open within 500ms");

            // Grace period for property update
            await Task.Delay(10);
            Assert.Contains("halfOpen", breaker.CurrentState);

            // Act - Failed test
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await breaker.ExecuteAsync<bool>(async ct =>
                {
                    await Task.Delay(1, ct);
                    throw new InvalidOperationException("Test failure");
                });
            });

            // Wait for reopen state
            var reopenTask = await Task.WhenAny(reopenedStateReached.Task, Task.Delay(500));
            Assert.True(reopenTask == reopenedStateReached.Task, "Circuit should reopen within 500ms");

            // Grace period for property update
            await Task.Delay(10);

            // Assert
            Assert.Contains("open", breaker.CurrentState);
            Assert.DoesNotContain("halfOpen", breaker.CurrentState);
        }

        [Fact]
        public async Task CircuitBreaker_RejectsWhenOpen()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var breaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 2);

            await breaker.StartAsync();

            // Event-driven waiting setup
            var openStateReached = new TaskCompletionSource<string>();
            breaker.StateTransitioned += (sender, args) =>
            {
                if (args.newState.Contains("open") && !args.newState.Contains("halfOpen"))
                    openStateReached.TrySetResult(args.newState);
            };

            // Open the circuit
            await breaker.RecordFailureAsync();
            await breaker.RecordFailureAsync();

            // Wait for open state
            var openTask = await Task.WhenAny(openStateReached.Task, Task.Delay(500));
            Assert.True(openTask == openStateReached.Task, "Circuit should open within 500ms");

            // Grace period for property update
            await Task.Delay(10);

            // Act & Assert
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
            {
                await breaker.ExecuteAsync(async ct => true);
            });
        }

        [Fact]
        public async Task CircuitBreaker_AllowsOnlyOneTestInHalfOpen()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var breaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 2,
                openDuration: TimeSpan.FromMilliseconds(100));

            await breaker.StartAsync();

            // Open and transition to half-open
            await breaker.RecordFailureAsync();
            await breaker.RecordFailureAsync();
            await Task.Delay(150);
            Assert.Contains("halfOpen", breaker.CurrentState);

            var testStarted = new TaskCompletionSource<bool>();
            var firstTestRunning = false;
            var secondRejected = false;

            // Act - Start first test that will take time
            var firstTest = Task.Run(async () =>
            {
                await breaker.ExecuteAsync(async ct =>
                {
                    firstTestRunning = true;
                    testStarted.SetResult(true);
                    await Task.Delay(100, ct);
                    return true;
                });
            });

            // Wait for first test to start
            await testStarted.Task;
            await Task.Delay(10);

            // Try second test - should be rejected
            try
            {
                await breaker.ExecuteAsync(async ct => true);
            }
            catch (CircuitBreakerOpenException)
            {
                secondRejected = true;
            }

            await firstTest;

            // Assert
            Assert.True(firstTestRunning, "First test should have run");
            Assert.True(secondRejected, "Second test should have been rejected");
        }

        [Fact]
        public async Task CircuitBreaker_ThreadSafe_ConcurrentFailures()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig { PoolSize = 4 });
            var breaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 10);

            await breaker.StartAsync();

            // Act - Record failures concurrently
            var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
            {
                await Task.Delay(Random.Shared.Next(1, 5));
                await breaker.RecordFailureAsync();
            }));

            await Task.WhenAll(tasks);
            await Task.Delay(100); // Allow event processing

            // Assert
            var stats = breaker.GetStats();
            _output.WriteLine($"Failure count: {stats.FailureCount}, State: {stats.State}");

            Assert.True(stats.FailureCount >= 10, "Should have recorded at least threshold failures");
            Assert.Contains("open", stats.State); // Should be open after threshold
        }

        [Fact]
        public async Task CircuitBreaker_ThreadSafe_ConcurrentExecutions()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig { PoolSize = 4 });
            var breaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 100); // High threshold so we don't trip

            await breaker.StartAsync();

            var successCount = 0;

            // Act - Execute many operations concurrently
            var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
            {
                try
                {
                    var result = await breaker.ExecuteAsync(async ct =>
                    {
                        await Task.Delay(Random.Shared.Next(1, 5), ct);
                        return i;
                    });
                    Interlocked.Increment(ref successCount);
                    return result;
                }
                catch
                {
                    return -1;
                }
            }));

            var results = await Task.WhenAll(tasks);
            await Task.Delay(100);

            // Assert
            Assert.Equal(50, successCount);
            Assert.Equal(50, breaker.SuccessCount);
            Assert.Contains("closed", breaker.CurrentState);
        }

        [Fact]
        public async Task CircuitBreaker_StateTransitionEvents()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var breaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 2,
                openDuration: TimeSpan.FromMilliseconds(100));

            var transitions = new System.Collections.Concurrent.ConcurrentBag<(string, string, string)>();
            var openStateReached = new TaskCompletionSource<string>();
            var halfOpenStateReached = new TaskCompletionSource<string>();
            var closedStateReached = new TaskCompletionSource<string>();

            breaker.StateTransitioned += (s, e) =>
            {
                transitions.Add(e);
                if (e.newState.Contains("open") && !e.newState.Contains("halfOpen"))
                    openStateReached.TrySetResult(e.newState);
                if (e.newState.Contains("halfOpen"))
                    halfOpenStateReached.TrySetResult(e.newState);
                // Only signal closed state if we came from halfOpen (not initial state)
                if (e.newState.Contains("closed") && e.oldState.Contains("halfOpen"))
                    closedStateReached.TrySetResult(e.newState);
            };

            await breaker.StartAsync();

            // Act - Trigger full cycle: Closed -> Open -> HalfOpen -> Closed
            await breaker.RecordFailureAsync();
            await breaker.RecordFailureAsync();

            // Wait for open state
            var openTask = await Task.WhenAny(openStateReached.Task, Task.Delay(500));
            Assert.True(openTask == openStateReached.Task, "Circuit should open within 500ms");

            // Wait for half-open state
            var halfOpenTask = await Task.WhenAny(halfOpenStateReached.Task, Task.Delay(500));
            Assert.True(halfOpenTask == halfOpenStateReached.Task, "Circuit should transition to half-open within 500ms");

            // Grace period to ensure halfOpen state is stable
            await Task.Delay(10);

            // Execute successful operation to close circuit
            await breaker.ExecuteAsync(ct => Task.FromResult(true));

            // Wait for closed state
            var closedTask = await Task.WhenAny(closedStateReached.Task, Task.Delay(1000));
            Assert.True(closedTask == closedStateReached.Task, "Circuit should close within 1000ms");

            // Grace period for property update
            await Task.Delay(10);

            // Assert - verify all expected transitions occurred
            Assert.Contains(transitions, t => t.Item1 == "closed" && t.Item2 == "open");
            Assert.Contains(transitions, t => t.Item1 == "open" && t.Item2 == "halfOpen");
            Assert.Contains(transitions, t => t.Item1 == "halfOpen" && t.Item2 == "closed");

            _output.WriteLine($"Transitions: {string.Join(", ", transitions.Select(t => $"{t.Item1}->{t.Item2}"))}");
        }

        public void Dispose()
        {
            _orchestrator?.Dispose();
        }
    }
}
