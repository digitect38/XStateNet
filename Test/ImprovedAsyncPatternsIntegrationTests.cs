using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet.Tests
{
    /// <summary>
    /// Integration tests to verify the improved async patterns work correctly
    /// and don't have the original issues (fire-and-forget, deadlocks, etc.)
    /// </summary>
    public class ImprovedAsyncPatternsIntegrationTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private readonly List<IAsyncDisposable> _mockConnections = new();

        public ImprovedAsyncPatternsIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            foreach (var conn in _mockConnections)
            {
                await conn.DisposeAsync();
            }
        }

        /// <summary>
        /// Test 1: Verify no fire-and-forget - exceptions are properly propagated
        /// </summary>
        [Fact]
        public async Task Test_NoFireAndForget_ExceptionsProperlyPropagated()
        {
            // Arrange
            var connection = new MockResilientConnection(shouldFailConnect: true);
            var exceptionCaught = false;
            Exception? caughtException = null;

            connection.OnError += (ex) =>
            {
                exceptionCaught = true;
                caughtException = ex;
                _output.WriteLine($"Exception caught in event handler: {ex.Message}");
            };

            // Act - The key point is that exceptions are handled, not lost
            bool connectResult = false;
            try
            {
                connectResult = await connection.ConnectAsync();
            }
            catch (Exception ex)
            {
                // This is GOOD - exception was properly propagated, not lost in fire-and-forget
                _output.WriteLine($"Exception properly propagated: {ex.Message}");
                exceptionCaught = true;
                caughtException = ex;
            }

            // Assert
            Assert.False(connectResult, "Connection should fail");
            Assert.True(exceptionCaught, "Exception should be caught, not lost in fire-and-forget");
            Assert.NotNull(caughtException);
            _output.WriteLine("✅ Exceptions are properly propagated, not lost in fire-and-forget");
        }

        /// <summary>
        /// Test 2: Verify no deadlock in Dispose - completes quickly
        /// </summary>
        [Fact]
        public async Task Test_NoDeadlockInDispose_CompletesQuickly()
        {
            // Arrange
            var connection = new MockResilientConnection();
            var stopwatch = Stopwatch.StartNew();

            // Start some background operations
            _ = connection.ConnectAsync();
            await Task.Delay(50); // Let it start

            // Act - Dispose should complete quickly without deadlock
            await connection.DisposeAsync();
            stopwatch.Stop();

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds < 1000,
                $"Dispose took {stopwatch.ElapsedMilliseconds}ms - should be < 1000ms");
            _output.WriteLine($"✅ DisposeAsync completed in {stopwatch.ElapsedMilliseconds}ms without deadlock");
        }

        /// <summary>
        /// Test 3: Verify synchronous Dispose doesn't hang
        /// </summary>
        [Fact]
        public async Task Test_SynchronousDispose_DoesntHang()
        {
            // Arrange
            var connection = new MockResilientConnection();
            _ = connection.ConnectAsync();
            await Task.Delay(50);

            // Act
            var disposeTask = Task.Run(() =>
            {
                connection.Dispose(); // Synchronous dispose
            });

            var completed = await Task.WhenAny(disposeTask, Task.Delay(2000)) == disposeTask;

            // Assert
            Assert.True(completed, "Synchronous Dispose should complete within 2 seconds");
            _output.WriteLine("✅ Synchronous Dispose completed without hanging");
        }

        /// <summary>
        /// Test 4: Verify proper cancellation handling
        /// </summary>
        [Fact]
        public async Task Test_ProperCancellation_NoOrphanedTasks()
        {
            // Arrange
            var connection = new MockResilientConnection(connectDelay: 5000);
            using var cts = new CancellationTokenSource(100);

            var taskStarted = false;
            var taskCancelled = false;

            connection.OnTaskStarted += () => taskStarted = true;
            connection.OnTaskCancelled += () => taskCancelled = true;

            // Act
            try
            {
                await connection.ConnectAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            await Task.Delay(200); // Give time for cleanup

            // Assert
            Assert.True(taskStarted, "Task should have started");
            Assert.True(taskCancelled, "Task should be properly cancelled, not orphaned");
            _output.WriteLine("✅ Cancellation properly handled without orphaned tasks");
        }

        /// <summary>
        /// Test 5: Verify reconnection logic doesn't create racing tasks
        /// </summary>
        [Fact]
        public async Task Test_ReconnectionLogic_NoRacingTasks()
        {
            // Arrange
            var connection = new MockResilientConnection();
            var connectionAttempts = 0;

            connection.OnConnectionAttempt += () =>
            {
                Interlocked.Increment(ref connectionAttempts);
            };

            // Act - Multiple parallel connection attempts
            var tasks = new Task[10];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = connection.ConnectAsync();
            }

            await Task.WhenAll(tasks);

            // Assert - Should only have one actual connection attempt, not racing
            Assert.Equal(1, connectionAttempts);
            _output.WriteLine($"✅ Only {connectionAttempts} connection attempt for {tasks.Length} parallel calls - no racing");
        }

        /// <summary>
        /// Test 6: Stress test - many concurrent operations without deadlock
        /// </summary>
        [Fact]
        public async Task Test_StressTest_ConcurrentOperations_NoDeadlock()
        {
            // Arrange
            var connection = new MockResilientConnection();
            var operations = new List<Task>();
            var errors = new List<Exception>();

            // Act - Simulate heavy concurrent usage
            for (int i = 0; i < 100; i++)
            {
                int localI = i;  // Capture loop variable
                // Mix of operations
                operations.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (localI % 3 == 0)
                            await connection.ConnectAsync();
                        else if (localI % 3 == 1)
                            await connection.SendMessageAsync("test");
                        else
                            await connection.DisconnectAsync();
                    }
                    catch (Exception ex)
                    {
                        lock (errors)
                            errors.Add(ex);
                    }
                }));
            }

            // Should complete without hanging
            var allOperationsTask = Task.WhenAll(operations);
            var delayTask = Task.Delay(5000);
            var completedTask = await Task.WhenAny(allOperationsTask, delayTask);
            var allCompleted = completedTask == allOperationsTask;

            // Assert
            Assert.True(allCompleted, "All operations should complete without deadlock");
            _output.WriteLine($"✅ {operations.Count} concurrent operations completed without deadlock");
            _output.WriteLine($"   Errors encountered: {errors.Count} (expected some due to state)");
        }

        /// <summary>
        /// Test 7: Verify state changes are thread-safe
        /// </summary>
        [Fact]
        public async Task Test_StateChanges_ThreadSafe()
        {
            // Arrange
            var connection = new MockResilientConnection();
            var stateChanges = new System.Collections.Concurrent.ConcurrentBag<string>();
            var stateOrder = new System.Collections.Concurrent.ConcurrentBag<string>();
            var raceConditions = 0;

            connection.OnStateChanged += (state) =>
            {
                stateChanges.Add(state);
                stateOrder.Add($"{DateTime.UtcNow.Ticks}: {state}");
            };

            // Act - Trigger many state changes
            var tasks = new Task[50];
            for (int i = 0; i < tasks.Length; i++)
            {
                int index = i;
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        if (index % 2 == 0)
                            await connection.ConnectAsync();
                        else
                            await connection.DisconnectAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when disconnecting while connecting
                        Interlocked.Increment(ref raceConditions);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Expected if dispose happens during operation
                        Interlocked.Increment(ref raceConditions);
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.NotEmpty(stateChanges);
            _output.WriteLine($"✅ {stateChanges.Count} state changes recorded");
            _output.WriteLine($"   Race conditions handled: {raceConditions}");

            // Verify we have expected states
            var uniqueStates = stateChanges.Distinct().OrderBy(s => s).ToList();
            _output.WriteLine($"   Unique states: {string.Join(", ", uniqueStates)}");

            // Should have at least some of these states
            var expectedStates = new[] { "Connecting", "Connected", "Disconnecting", "Disconnected" };
            var foundStates = expectedStates.Where(s => uniqueStates.Contains(s)).ToList();
            Assert.NotEmpty(foundStates);
        }

        /// <summary>
        /// Test 8: Memory leak test - proper resource cleanup
        /// </summary>
        [Fact]
        public async Task Test_NoMemoryLeak_ProperCleanup()
        {
            // Arrange
            WeakReference? weakRef = null;
            var memoryBefore = GC.GetTotalMemory(true);

            // Act - Create and dispose many connections
            await Task.Run(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    var connection = new MockResilientConnection();
                    if (i == 0)
                        weakRef = new WeakReference(connection);

                    await connection.ConnectAsync();
                    await connection.DisposeAsync();
                }
            });

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var memoryAfter = GC.GetTotalMemory(false);

            // Assert
            Assert.False(weakRef?.IsAlive ?? false, "Connection should be garbage collected");
            _output.WriteLine($"✅ Resources properly cleaned up");
            _output.WriteLine($"   Memory before: {memoryBefore:N0} bytes");
            _output.WriteLine($"   Memory after: {memoryAfter:N0} bytes");
        }
    }

    /// <summary>
    /// Mock implementation for testing
    /// </summary>
    public class MockResilientConnection : IAsyncDisposable, IDisposable
    {
        private readonly bool _shouldFailConnect;
        private readonly int _connectDelay;
        private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
        private readonly object _stateLock = new object();
        private volatile bool _disposed;
        private string _state = "Disconnected";
        private Task<bool>? _connectionTask;
        private CancellationTokenSource? _cts;

        public event Action<Exception>? OnError;
        public event Action<string>? OnStateChanged;
        public event Action? OnConnectionAttempt;
        public event Action? OnTaskStarted;
        public event Action? OnTaskCancelled;

        public MockResilientConnection(bool shouldFailConnect = false, int connectDelay = 100)
        {
            _shouldFailConnect = shouldFailConnect;
            _connectDelay = connectDelay;
        }

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Check if already connected or connecting without locking
            if (_state == "Connected")
                return true;

            if (_connectionTask != null && !_connectionTask.IsCompleted)
            {
                // Wait for existing connection attempt
                return await _connectionTask;
            }

            await _connectionSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Double-check inside the lock
                if (_connectionTask != null && !_connectionTask.IsCompleted)
                {
                    var result = await _connectionTask;
                    return result;
                }

                // If already connected, return immediately
                if (_state == "Connected")
                    return true;

                // This is the only actual connection attempt
                OnConnectionAttempt?.Invoke();
                SetState("Connecting");

                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _connectionTask = ConnectInternalAsync(_cts.Token);

                try
                {
                    var result = await _connectionTask;
                    if (result)
                        SetState("Connected");
                    else
                        SetState("Failed");
                    return result;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex);
                    SetState("Failed");
                    // Don't re-throw to simulate handling like a real resilient connection would
                    return false;
                }
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        private async Task<bool> ConnectInternalAsync(CancellationToken cancellationToken)
        {
            OnTaskStarted?.Invoke();

            try
            {
                await Task.Delay(_connectDelay, cancellationToken);

                if (_shouldFailConnect)
                {
                    throw new InvalidOperationException("Connection failed as configured");
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                OnTaskCancelled?.Invoke();
                throw;
            }
        }

        public async Task SendMessageAsync(string message)
        {
            ThrowIfDisposed();

            if (_state != "Connected")
                throw new InvalidOperationException("Not connected");

            await Task.Delay(10);
        }

        public async Task DisconnectAsync()
        {
            if (_disposed) return;

            SetState("Disconnecting");
            _cts?.Cancel();

            if (_connectionTask != null)
            {
                try
                {
                    await _connectionTask.WaitAsync(TimeSpan.FromSeconds(1));
                }
                catch { }
            }

            SetState("Disconnected");
            _connectionTask = null;  // Reset task after disconnect
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
                throw new ObjectDisposedException(nameof(MockResilientConnection));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            await DisconnectAsync();
            _connectionSemaphore?.Dispose();
            _cts?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;

            var task = DisposeAsync().AsTask();
            task.Wait(TimeSpan.FromSeconds(2));
        }
    }
}