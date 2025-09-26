using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace XStateNet;

/// <summary>
/// Ensures event handlers are executed sequentially in exact order
/// </summary>
public class SequentialEventExecutor<T> : IDisposable
{
    private readonly Channel<(T arg, TaskCompletionSource<bool> tcs)> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;
    private readonly OrderedEventHandler<T> _orderedHandler;

    public SequentialEventExecutor()
    {
        _orderedHandler = new OrderedEventHandler<T>();
        _channel = Channel.CreateUnbounded<(T, TaskCompletionSource<bool>)>(
            new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true,
                AllowSynchronousContinuations = false
            });

        _processingTask = ProcessEventsAsync(_cts.Token);
    }

    /// <summary>
    /// Subscribe with guaranteed sequential execution
    /// </summary>
    public IDisposable Subscribe(Action<T> handler, int priority = int.MaxValue)
    {
        return _orderedHandler.Subscribe(handler, priority);
    }

    /// <summary>
    /// Invoke handlers sequentially (blocks until all complete)
    /// </summary>
    public async Task InvokeAsync(T arg)
    {
        var tcs = new TaskCompletionSource<bool>();
        await _channel.Writer.WriteAsync((arg, tcs));
        await tcs.Task;
    }

    /// <summary>
    /// Invoke handlers sequentially (non-blocking)
    /// </summary>
    public void Invoke(T arg)
    {
        var tcs = new TaskCompletionSource<bool>();
        if (_channel.Writer.TryWrite((arg, tcs)))
        {
            // Fire and forget - handlers will execute in order
            _ = tcs.Task.ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    private async Task ProcessEventsAsync(CancellationToken ct)
    {
        await foreach (var (arg, tcs) in _channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                // Execute all handlers for this event in order
                _orderedHandler.Invoke(arg);
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.Complete();
        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch { }
        _cts.Dispose();
    }
}

/// <summary>
/// Alternative: Lock-based sequential executor for simpler scenarios
/// </summary>
public class SynchronizedEventHandler<T>
{
    private readonly OrderedEventHandler<T> _orderedHandler = new();
    private readonly object _executionLock = new();

    public IDisposable Subscribe(Action<T> handler, int priority = int.MaxValue)
    {
        return _orderedHandler.Subscribe(handler, priority);
    }

    /// <summary>
    /// Execute all handlers sequentially under lock
    /// </summary>
    public void Invoke(T arg)
    {
        lock (_executionLock)
        {
            _orderedHandler.Invoke(arg);
        }
    }
}