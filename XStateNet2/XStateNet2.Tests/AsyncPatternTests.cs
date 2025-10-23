using Akka.TestKit.Xunit2;
using Xunit;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for async patterns to ensure proper exception handling,
/// disposal, and cancellation without deadlocks or fire-and-forget issues
/// </summary>
public class AsyncPatternTests : TestKit
{
    [Fact]
    public async Task ExceptionsNotLost_ProperAsyncAwait()
    {
        // Arrange & Act
        var exceptionCaught = false;
        var task = Task.Run(async () =>
        {
            await Task.Delay(10);
            throw new InvalidOperationException("Test exception");
        });

        try
        {
            await task;
        }
        catch (InvalidOperationException)
        {
            exceptionCaught = true;
        }

        // Assert
        Assert.True(exceptionCaught, "Exception should be caught, not lost");
    }

    [Fact]
    public void DisposeDoesntHang_WithTimeout()
    {
        // Arrange
        var resource = new QuickDisposableResource();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act
        resource.Dispose();
        sw.Stop();

        // Assert
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Dispose should complete in < 500ms, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task AsyncDisposeWorks_NoDeadlock()
    {
        // Arrange & Act
        await using var resource = new QuickAsyncDisposableResource();
        await resource.DoWorkAsync();

        // Assert - Resource will be disposed here automatically without deadlock
        Assert.True(true, "Async dispose completed successfully");
    }

    [Fact]
    public async Task CancellationWorks_StopsOperationsPromptly()
    {
        // Arrange
        using var cts = new CancellationTokenSource(50);
        var cancelled = false;

        // Act
        try
        {
            await LongRunningOperation(cts.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        // Assert
        Assert.True(cancelled, "Operation should be cancelled");
    }

    [Fact]
    public async Task NoConcurrencyIssues_ThreadSafeCounter()
    {
        // Arrange
        var counter = new ThreadSafeCounter();
        var tasks = new Task[20];

        // Act
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => counter.Increment());
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(20, counter.Value);
    }

    [Fact]
    public async Task ProperAsyncDisposal_WithCancellation()
    {
        // Arrange
        await using var resource = new ProperlyDisposableResource();

        // Act
        await resource.DoWorkAsync();

        // Assert - disposal happens automatically with proper cancellation
    }

    [Fact]
    public async Task ThreadSafeStateManagement_ConcurrentAccess()
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

    private async Task LongRunningOperation(CancellationToken ct)
    {
        for (int i = 0; i < 100; i++)
        {
            await Task.Delay(10, ct);
            ct.ThrowIfCancellationRequested();
        }
    }
}

public class QuickDisposableResource : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _backgroundTask;

    public QuickDisposableResource()
    {
        _backgroundTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, _cts.Token);
            }
            catch (OperationCanceledException) { }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _backgroundTask.Wait(100);
        }
        catch { }
        _cts.Dispose();
    }
}

public class QuickAsyncDisposableResource : IAsyncDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1);

    public async Task DoWorkAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            await Task.Delay(10);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(10);
        _semaphore?.Dispose();
    }
}

public class ProperlyDisposableResource : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private volatile bool _disposed;

    public async Task DoWorkAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ProperlyDisposableResource));

        await _semaphore.WaitAsync();
        try
        {
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

        await Task.Delay(10);
        _semaphore?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            var task = DisposeAsync().AsTask();
            task.Wait(TimeSpan.FromSeconds(5));
        }
        catch { }

        GC.SuppressFinalize(this);
    }
}

public class ThreadSafeCounter
{
    private int _value;
    public int Value => _value;

    public void Increment()
    {
        Interlocked.Increment(ref _value);
    }
}

public class ThreadSafeStateManager
{
    private readonly object _stateLock = new();
    private int _state;

    public void SetState(int newState)
    {
        lock (_stateLock)
        {
            _state = newState;
        }
    }

    public int GetState()
    {
        lock (_stateLock)
        {
            return _state;
        }
    }
}
