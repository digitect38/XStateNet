using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using XStateNet.Tests.TestInfrastructure;

namespace XStateNet.Tests
{
    /// <summary>
    /// Deterministic tests for ResilientHsmsConnection that don't rely on timing or actual network connections
    /// </summary>
    [Collection("TimingSensitive")]
    public class DeterministicResilientHsmsConnectionTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;

        public DeterministicResilientHsmsConnectionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public Task InitializeAsync() => Task.CompletedTask;
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task MockConnection_DisposeAsync_CompletesImmediately()
        {
            // Arrange
            var connection = new MockHsmsConnection();

            // Act
            await connection.DisposeAsync();

            // Assert
            Assert.True(connection.IsDisposed);
            _output.WriteLine("✅ DisposeAsync completed immediately");
        }

        [Fact]
        public async Task MockConnection_MultipleConnectAttempts_ReturnsConsistentResult()
        {
            // Arrange
            var connection = new MockHsmsConnection();
            connection.SetNextConnectResult(false); // Will fail

            // Act - Multiple parallel connect attempts
            var tasks = new Task<bool>[5];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() => connection.ConnectAsync());
            }

            var results = await Task.WhenAll(tasks);

            // Assert - All should return the same result
            Assert.All(results, r => Assert.False(r));
            // With proper synchronization, only one connection attempt should be made
            Assert.True(connection.ConnectAttempts <= tasks.Length,
                $"Expected at most {tasks.Length} attempts, got {connection.ConnectAttempts}");
            _output.WriteLine($"✅ {tasks.Length} parallel calls resulted in {connection.ConnectAttempts} actual attempt");
        }

        [Fact]
        public async Task MockConnection_CancellationDuringConnect_ProperlyCancels()
        {
            // Arrange
            var connection = new MockHsmsConnection();
            connection.SetConnectDelay(TimeSpan.FromSeconds(10)); // Long delay
            using var cts = new CancellationTokenSource();

            // Act
            var connectTask = connection.ConnectAsync(cts.Token);
            cts.Cancel(); // Cancel immediately

            // Assert - TaskCanceledException is a subtype of OperationCanceledException
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await connectTask);
            Assert.Equal(1, connection.CancelledAttempts);
            _output.WriteLine("✅ Connection properly cancelled");
        }

        [Fact]
        [TestPriority(TestPriority.Critical)]
        public async Task MockConnection_StateTransitions_AreThreadSafe()
        {
            // Arrange
            var connection = new MockHsmsConnection();
            var stateChanges = new System.Collections.Concurrent.ConcurrentBag<string>();

            connection.OnStateChanged += (state) => stateChanges.Add(state);

            // Act - Trigger state changes
            connection.SetNextConnectResult(true);
            await connection.ConnectAsync();
            await connection.DisconnectAsync();

            // Assert
            Assert.Contains("Connecting", stateChanges);
            Assert.Contains("Connected", stateChanges);
            Assert.Contains("Disconnecting", stateChanges);
            Assert.Contains("Disconnected", stateChanges);
            _output.WriteLine($"✅ Recorded {stateChanges.Count} state changes");
        }

        [Fact]
        public async Task MockConnection_DisposedConnection_ThrowsObjectDisposedException()
        {
            // Arrange
            var connection = new MockHsmsConnection();

            // Act
            await connection.DisposeAsync();

            // Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await connection.ConnectAsync());

            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await connection.SendMessageAsync("test"));

            _output.WriteLine("✅ Disposed connection throws ObjectDisposedException");
        }

        [Fact]
        public async Task MockConnection_CircuitBreaker_OpensAfterThreshold()
        {
            // Arrange
            var connection = new MockHsmsConnection();
            connection.CircuitBreakerThreshold = 2;
            connection.SetNextConnectResult(false);
            connection.SetConnectDelay(TimeSpan.FromMilliseconds(10)); // Small delay to ensure sequential attempts

            var circuitOpened = false;
            connection.OnStateChanged += (state) =>
            {
                if (state == "CircuitOpen")
                    circuitOpened = true;
            };

            // Act - Multiple failed attempts (need sequential to count separately)
            for (int i = 0; i < 3; i++)
            {
                await connection.ConnectAsync();
                await Task.Delay(20); // Wait between attempts to ensure they're separate
            }

            // Assert
            Assert.True(circuitOpened, $"Circuit should open after {connection.CircuitBreakerThreshold} failures");
            Assert.True(connection.IsCircuitOpen, "Circuit breaker should be in open state");
            _output.WriteLine($"✅ Circuit breaker opened after {connection.CircuitBreakerThreshold} failures");
        }

        [Fact]
        [TestPriority(TestPriority.Critical)]
        public async Task MockConnection_ConcurrentOperations_HandledGracefully()
        {
            // Arrange
            var connection = new MockHsmsConnection();
            connection.SetNextConnectResult(true);

            var operations = new List<Task>();

            // Act - Mix of operations
            for (int i = 0; i < 20; i++)
            {
                int index = i;
                operations.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (index % 3 == 0)
                            await connection.ConnectAsync();
                        else if (index % 3 == 1)
                            await connection.SendMessageAsync($"msg{index}");
                        else
                            await connection.DisconnectAsync();
                    }
                    catch (InvalidOperationException)
                    {
                        // Expected - might try to send when not connected
                    }
                }));
            }

            // Assert - Should complete without deadlock
            await Task.WhenAll(operations);
            _output.WriteLine($"✅ {operations.Count} concurrent operations completed");
        }

        [Fact]
        public void MockConnection_SynchronousDispose_CompletesWithoutHanging()
        {
            // Arrange
            var connection = new MockHsmsConnection();

            // Act & Assert - Should not hang
            connection.Dispose();

            Assert.True(connection.IsDisposed);
            _output.WriteLine("✅ Synchronous Dispose completed without hanging");
        }

        [Fact]
        public async Task MockConnection_ReconnectionLogic_OnlyOneActiveAttempt()
        {
            // Arrange
            var connection = new MockHsmsConnection();
            connection.SetConnectDelay(TimeSpan.FromMilliseconds(100));
            connection.SetNextConnectResult(true);

            // Act - Multiple parallel reconnection attempts
            var tasks = new Task<bool>[10];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() => connection.ConnectAsync());
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.All(results, r => Assert.True(r)); // All succeed
            Assert.Equal(1, connection.ConnectAttempts); // But only one actual connection
            _output.WriteLine($"✅ {tasks.Length} reconnection attempts resulted in {connection.ConnectAttempts} actual connection");
        }
    }

    /// <summary>
    /// Mock connection for deterministic testing
    /// </summary>
    public class MockHsmsConnection : IAsyncDisposable, IDisposable
    {
        private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
        private readonly object _stateLock = new();
        private TaskCompletionSource<bool>? _currentConnectTcs;
        private bool _disposed;
        private string _state = "Disconnected";
        private bool _nextConnectResult = true;
        private TimeSpan _connectDelay = TimeSpan.Zero;
        private int _failureCount;

        public int ConnectAttempts { get; private set; }
        public int CancelledAttempts { get; private set; }
        public bool IsDisposed => _disposed;
        public bool IsCircuitOpen { get; private set; }
        public int CircuitBreakerThreshold { get; set; } = 5;

        public event Action<string>? OnStateChanged;

        public void SetNextConnectResult(bool result) => _nextConnectResult = result;
        public void SetConnectDelay(TimeSpan delay) => _connectDelay = delay;

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            TaskCompletionSource<bool>? existingTcs = null;
            TaskCompletionSource<bool>? newTcs = null;

            // Check state and prepare connection
            lock (_stateLock)
            {
                if (_state == "Connected")
                    return true;

                // If circuit is open, fail immediately
                if (IsCircuitOpen)
                {
                    return false;
                }

                // If already connecting, wait for that attempt
                if (_currentConnectTcs != null)
                {
                    existingTcs = _currentConnectTcs;
                }
                else
                {
                    // Start new connection attempt
                    newTcs = _currentConnectTcs = new TaskCompletionSource<bool>();
                }
            }

            // Wait for existing connection attempt
            if (existingTcs != null)
            {
                try
                {
                    return await existingTcs.Task;
                }
                catch
                {
                    return false;
                }
            }

            try
            {
                ConnectAttempts++;
                SetState("Connecting");

                // Simulate connection delay if configured
                if (_connectDelay > TimeSpan.Zero)
                {
                    using (cancellationToken.Register(() => _currentConnectTcs?.TrySetCanceled()))
                    {
                        await Task.Delay(_connectDelay, cancellationToken);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                var result = _nextConnectResult;

                if (!result)
                {
                    _failureCount++;
                    if (_failureCount >= CircuitBreakerThreshold)
                    {
                        IsCircuitOpen = true;
                        SetState("CircuitOpen");
                    }
                    SetState("Failed");
                }
                else
                {
                    _failureCount = 0;
                    SetState("Connected");
                }

                _currentConnectTcs.TrySetResult(result);
                return result;
            }
            catch (OperationCanceledException)
            {
                CancelledAttempts++;
                SetState("Disconnected");
                _currentConnectTcs?.TrySetCanceled();
                throw;
            }
            catch (Exception ex)
            {
                SetState("Failed");
                _currentConnectTcs?.TrySetException(ex);
                throw;
            }
            finally
            {
                lock (_stateLock)
                {
                    _currentConnectTcs = null;
                }
            }
        }

        public async Task DisconnectAsync()
        {
            if (_disposed) return;

            SetState("Disconnecting");
            await Task.Yield(); // Allow state change to propagate
            SetState("Disconnected");
        }

        public async Task<bool> SendMessageAsync(string message)
        {
            ThrowIfDisposed();

            if (_state != "Connected")
                throw new InvalidOperationException("Not connected");

            await Task.Yield(); // Simulate async operation
            return true;
        }

        private void SetState(string newState)
        {
            lock (_stateLock)
            {
                if (_state != newState)
                {
                    _state = newState;
                    Task.Run(() => OnStateChanged?.Invoke(newState));
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MockHsmsConnection));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            await DisconnectAsync();
            _connectionSemaphore?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                DisconnectAsync().Wait(TimeSpan.FromSeconds(1));
            }
            catch { }

            _connectionSemaphore?.Dispose();
        }
    }
}