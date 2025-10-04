using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet.Semi.Transport;
using XStateNet.Orchestration;
using Microsoft.Extensions.Logging;

namespace XStateNet.Tests
{
    public class ResilientHsmsConnectionTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogger<ImprovedResilientHsmsConnection> _logger;
        private readonly EventBusOrchestrator _orchestrator;

        public ResilientHsmsConnectionTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = new TestLogger<ImprovedResilientHsmsConnection>(output);
            _orchestrator = new EventBusOrchestrator();
        }

        public Task InitializeAsync() => Task.CompletedTask;
        public Task DisposeAsync()
        {
            _orchestrator?.Dispose();
            return Task.CompletedTask;
        }

        [SkippableNetworkFact]
        public async Task ImprovedConnection_ProperAsyncDisposal_NoDeadlock()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5000);
            var connection = new ImprovedResilientHsmsConnection(endpoint, HsmsConnection.HsmsConnectionMode.Active, _orchestrator, _logger);

            // Don't actually try to connect - just test disposal

            // Act & Assert - should complete without deadlock
            var disposeTask = connection.DisposeAsync().AsTask();
            var completed = await Task.WhenAny(disposeTask, Task.Delay(2000)) == disposeTask;

            Assert.True(completed, "DisposeAsync should complete within timeout");
        }

        [SkippableNetworkFact]
        public async Task ImprovedConnection_MultipleConnectAttempts_ReturnsConsistentResult()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5001);
            await using var connection = new ImprovedResilientHsmsConnection(endpoint, HsmsConnection.HsmsConnectionMode.Active, _orchestrator, _logger);
            connection.MaxRetryAttempts = 0;  // No retries - fail immediately
            connection.RetryDelayMs = 1;  // Minimal delay
            connection.CircuitBreakerThreshold = 20; // High threshold to avoid opening

            // Act - Multiple parallel connect attempts
            var tasks = new Task<bool>[5];

            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                        return await connection.ConnectAsync(cts.Token);
                    }
                    catch { return false; }
                });
            }

            var results = await Task.WhenAll(tasks);

            // Assert - All should return the same result (false since can't connect)
            Assert.All(results, r => Assert.False(r));
        }

        [SkippableNetworkFact]
        public async Task ImprovedConnection_CancellationDuringConnect_ProperlyCancels()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5002);
            await using var connection = new ImprovedResilientHsmsConnection(endpoint, HsmsConnection.HsmsConnectionMode.Active, _orchestrator, _logger);
            // Use very short delays to make test fast but still testable
            connection.MaxRetryAttempts = 1;
            connection.RetryDelayMs = 10;

            // Act
            using var cts = new CancellationTokenSource();

            // Start connection attempt
            var connectTask = connection.ConnectAsync(cts.Token);

            // Give it a tiny bit of time to start, then cancel
            await Task.Delay(5);
            cts.Cancel();

            // Assert - Should either return false or throw cancellation exception
            bool cancelled = false;
            try
            {
                var result = await connectTask.WaitAsync(TimeSpan.FromSeconds(1));
                Assert.False(result); // If it completes, it should be false
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                // This is expected - cancellation worked
            }
            catch (TimeoutException)
            {
                Assert.True(false, "Connection attempt did not complete within timeout - may be hanging");
            }

            // Either way is acceptable - cancelled or returned false
            _output.WriteLine($"Connection attempt {(cancelled ? "was cancelled" : "returned false")} as expected");
        }

        [SkippableNetworkFact]
        public async Task ImprovedConnection_StateTransitions_AreThreadSafe()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5003);
            await using var connection = new ImprovedResilientHsmsConnection(endpoint, HsmsConnection.HsmsConnectionMode.Active, _orchestrator, _logger);

            var stateChanges = new System.Collections.Concurrent.ConcurrentBag<ImprovedResilientHsmsConnection.ConnectionState>();
            connection.StateChanged += (s, state) => stateChanges.Add(state);

            // Act - Try to connect (will fail but should trigger state changes)
            connection.MaxRetryAttempts = 0;  // No retries
            connection.RetryDelayMs = 1;
            connection.CircuitBreakerThreshold = 10; // Prevent circuit from opening

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try
            {
                await connection.ConnectAsync(cts.Token);
            }
            catch
            {
                // Expected to fail or timeout
            }

            // Wait a bit for state changes to propagate
            await Task.Delay(100);

            // Assert - Should have recorded state transitions
            Assert.NotEmpty(stateChanges);
            Assert.Contains(ImprovedResilientHsmsConnection.ConnectionState.Connecting, stateChanges);
        }

        [SkippableNetworkFact]
        public async Task ImprovedConnection_DisposedConnection_ThrowsObjectDisposedException()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5004);
            var connection = new ImprovedResilientHsmsConnection(endpoint, HsmsConnection.HsmsConnectionMode.Active, _orchestrator, _logger);

            // Act
            await connection.DisposeAsync();

            // Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await connection.ConnectAsync());

            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await connection.SendMessageAsync(new HsmsMessage()));
        }

        [SkippableNetworkFact]
        public async Task ImprovedConnection_CircuitBreaker_OpensAfterThreshold()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5005);
            await using var connection = new ImprovedResilientHsmsConnection(endpoint, HsmsConnection.HsmsConnectionMode.Active, _orchestrator, _logger);

            connection.CircuitBreakerThreshold = 2;
            connection.CircuitBreakerDuration = TimeSpan.FromMilliseconds(100);
            connection.MaxRetryAttempts = 0;  // No retries
            connection.RetryDelayMs = 1;

            var circuitOpened = false;
            connection.StateChanged += (s, state) =>
            {
                if (state == ImprovedResilientHsmsConnection.ConnectionState.CircuitOpen)
                    circuitOpened = true;
            };

            // Act - Multiple failed attempts should open circuit
            for (int i = 0; i < 3; i++)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                try
                {
                    await connection.ConnectAsync(cts.Token);
                }
                catch
                {
                    // Expected failures
                }
                await Task.Delay(10);  // Small delay between attempts
            }

            // Wait for state propagation
            await Task.Delay(200);

            // Assert
            Assert.True(circuitOpened, "Circuit breaker should have opened after threshold");
        }

        [SkippableNetworkFact]
        public async Task ImprovedConnection_ConcurrentDisposal_HandledGracefully()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5006);
            var connection = new ImprovedResilientHsmsConnection(endpoint, HsmsConnection.HsmsConnectionMode.Active, _orchestrator, _logger);

            // Act - Multiple concurrent disposal attempts
            var tasks = new Task[10];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(async () => await connection.DisposeAsync());
            }

            // Assert - Should complete without errors
            var allCompleted = await Task.WhenAll(tasks).ContinueWith(t => !t.IsFaulted);
            Assert.True(allCompleted, "All disposal attempts should complete without errors");
        }

        [SkippableNetworkFact]
        public async Task ImprovedConnection_SynchronousDispose_CompletesWithTimeout()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5007);
            var connection = new ImprovedResilientHsmsConnection(endpoint, HsmsConnection.HsmsConnectionMode.Active, _orchestrator, _logger);

            // Don't try to connect - just test disposal

            // Act & Assert - Synchronous dispose should complete quickly
            var disposeTask = Task.Run(() => connection.Dispose());
            var completed = await Task.WhenAny(disposeTask, Task.Delay(2000)) == disposeTask;

            Assert.True(completed, "Synchronous Dispose should complete within timeout");
        }

        private class TestLogger<T> : ILogger<T>
        {
            private readonly ITestOutputHelper _output;

            public TestLogger(ITestOutputHelper output)
            {
                _output = output;
            }

            public IDisposable BeginScope<TState>(TState state) => new NullScope();
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
                if (exception != null)
                    _output.WriteLine($"Exception: {exception}");
            }

            private class NullScope : IDisposable
            {
                public void Dispose() { }
            }
        }
    }
}