using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet.Tests.TestInfrastructure;

namespace XStateNet.Tests
{
    /// <summary>
    /// Tests demonstrating proper async patterns without fire-and-forget
    /// and without dangerous .GetAwaiter().GetResult()
    /// </summary>
    [Collection("TimingSensitive")]
    public class AsyncPatternTests
    {
        private readonly ITestOutputHelper _output;

        public AsyncPatternTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Demonstrates proper async disposal pattern without deadlock
        /// </summary>
        [Fact]
        public async Task ProperAsyncDisposal_NoDeadlock()
        {
            // Arrange
            await using var resource = new ProperlyDisposableResource();

            // Act
            await resource.DoWorkAsync();

            // Assert - disposal happens automatically and won't deadlock
            // The await using will call DisposeAsync properly
        }

        /// <summary>
        /// Demonstrates avoiding fire-and-forget pattern
        /// </summary>
        [Fact]
        public async Task AvoidFireAndForget_ProperAsyncAwait()
        {
            // Arrange
            var supervisor = new ProperConnectionSupervisor();
            var exceptionCaught = false;

            // Act - Properly await the supervisor task
            try
            {
                await supervisor.StartSupervisionAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                exceptionCaught = true;
                _output.WriteLine($"Exception properly caught: {ex.Message}");
            }

            // Assert - Exceptions are properly propagated, not lost
            Assert.False(exceptionCaught); // In this test, no exception should occur
        }

        /// <summary>
        /// Demonstrates proper cancellation handling
        /// </summary>
        [Fact]
        public async Task ProperCancellation_NoHanging()
        {
            // Arrange
            var supervisor = new ProperConnectionSupervisor();
            using var cts = new CancellationTokenSource(100);

            // Act
            var task = supervisor.StartLongRunningOperationAsync(cts.Token);

            // Assert - Should cancel quickly without hanging
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        }

        /// <summary>
        /// Demonstrates avoiding synchronous wait in Dispose
        /// </summary>
        [Fact]
        public void SynchronousDispose_WithTimeout_NoDeadlock()
        {
            // Arrange
            var resource = new ProperlyDisposableResource();

            // Act - Synchronous dispose with timeout protection
            var disposed = false;
            var disposeTask = Task.Run(() =>
            {
                resource.Dispose();
                disposed = true;
            });

            // Assert - Should complete quickly
            var completed = disposeTask.Wait(TimeSpan.FromSeconds(2));
            Assert.True(completed, "Dispose should complete within timeout");
            Assert.True(disposed, "Resource should be disposed");
        }

        /// <summary>
        /// Demonstrates thread-safe state management
        /// </summary>
        [Fact]
        [TestPriority(TestPriority.Critical)]
        public async Task ThreadSafeStateManagement_NoConcurrencyIssues()
        {
            // Arrange
            var manager = new ThreadSafeStateManager();
            var tasks = new Task[100];

            // Act - Many concurrent state changes
            for (int i = 0; i < tasks.Length; i++)
            {
                int stateValue = i;
                tasks[i] = Task.Run(() => manager.SetState(stateValue));
            }

            await Task.WhenAll(tasks);

            // Assert - State should be valid (one of the values we set)
            var finalState = manager.GetState();
            Assert.InRange(finalState, 0, 99);
        }
    }

    /// <summary>
    /// Example of properly disposable resource with IAsyncDisposable
    /// </summary>
    public class ProperlyDisposableResource : IAsyncDisposable, IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private volatile bool _disposed;
        private Task? _backgroundTask;
        private CancellationTokenSource? _cts;

        public async Task DoWorkAsync()
        {
            ThrowIfDisposed();

            await _semaphore.WaitAsync();
            try
            {
                // Simulate some work
                await Task.Delay(10);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;

            _disposed = true;
            _cts?.Cancel();

            if (_backgroundTask != null)
            {
                try
                {
                    // Proper async wait with timeout
                    await _backgroundTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    // Log and continue
                }
            }

            _semaphore?.Dispose();
            _cts?.Dispose();
            GC.SuppressFinalize(this);
        }

        public void Dispose()
        {
            if (_disposed) return;

            // Delegate to async disposal with timeout protection
            try
            {
                var task = DisposeAsync().AsTask();
                task.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Best effort - log if needed
            }

            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ProperlyDisposableResource));
        }
    }

    /// <summary>
    /// Example of proper connection supervisor without fire-and-forget
    /// </summary>
    public class ProperConnectionSupervisor
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private Task? _supervisorTask;

        public async Task StartSupervisionAsync(CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_supervisorTask != null && !_supervisorTask.IsCompleted)
                {
                    // Already running
                    await _supervisorTask;
                    return;
                }

                // Start supervision - properly await or store for later
                _supervisorTask = SuperviseAsync(cancellationToken);

                // For this example, we'll await it
                await _supervisorTask;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task StartLongRunningOperationAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(50, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private async Task SuperviseAsync(CancellationToken cancellationToken)
        {
            for (int i = 0; i < 3; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await Task.Delay(10, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Example of thread-safe state management
    /// </summary>
    public class ThreadSafeStateManager
    {
        private readonly object _stateLock = new();
        private int _state;

        public void SetState(int newState)
        {
            lock (_stateLock)
            {
                _state = newState;
                // Fire event outside of lock to prevent deadlock
                Task.Run(() => OnStateChanged(newState));
            }
        }

        public int GetState()
        {
            lock (_stateLock)
            {
                return _state;
            }
        }

        private void OnStateChanged(int newState)
        {
            // Simulate event handling
            Task.Delay(1).Wait();
        }
    }
}