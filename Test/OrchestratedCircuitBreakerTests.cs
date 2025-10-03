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

            // Act - Record failures up to threshold
            await breaker.RecordFailureAsync();
            await Task.Delay(10); // Allow event processing
            Assert.Contains("closed", breaker.CurrentState);

            await breaker.RecordFailureAsync();
            await Task.Delay(10);
            Assert.Contains("closed", breaker.CurrentState);

            await breaker.RecordFailureAsync();
            await Task.Delay(50); // Allow state transition

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

            // Open the circuit
            await breaker.RecordFailureAsync();
            await breaker.RecordFailureAsync();
            await Task.Delay(50);
            Assert.Contains("open", breaker.CurrentState);

            // Act - Wait for timeout
            await Task.Delay(150);

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

            // Open the circuit
            await breaker.RecordFailureAsync();
            await breaker.RecordFailureAsync();
            await Task.Delay(50);

            // Wait for half-open
            await Task.Delay(150);
            Assert.Contains("halfOpen", breaker.CurrentState);

            // Act - Successful test
            var result = await breaker.ExecuteAsync(async ct =>
            {
                await Task.Delay(1, ct);
                return true;
            });

            await Task.Delay(50);

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

            // Open the circuit
            await breaker.RecordFailureAsync();
            await breaker.RecordFailureAsync();
            await Task.Delay(50);

            // Wait for half-open
            await Task.Delay(150);
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

            await Task.Delay(50);

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

            // Open the circuit
            await breaker.RecordFailureAsync();
            await breaker.RecordFailureAsync();
            await Task.Delay(50);

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
            breaker.StateTransitioned += (s, e) => transitions.Add(e);

            await breaker.StartAsync();

            // Act - Trigger full cycle: Closed -> Open -> HalfOpen -> Closed
            await breaker.RecordFailureAsync();
            await breaker.RecordFailureAsync();
            await Task.Delay(50); // Open

            await Task.Delay(150); // HalfOpen

            await breaker.ExecuteAsync(async ct => true);
            await Task.Delay(50); // Closed

            // Assert
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
