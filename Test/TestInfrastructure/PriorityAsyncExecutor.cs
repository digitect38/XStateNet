using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace XStateNet.Tests.TestInfrastructure
{
    /// <summary>
    /// Provides priority-based async execution using Channels
    /// </summary>
    public class PriorityAsyncExecutor : IDisposable
    {
        private readonly Channel<PriorityWorkItem>[] _channels;
        private readonly Task _processingTask;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly SemaphoreSlim _concurrencyLimiter;

        public PriorityAsyncExecutor(int maxConcurrency = 0)
        {
            if (maxConcurrency <= 0)
                maxConcurrency = Environment.ProcessorCount;

            _concurrencyLimiter = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            // Create separate channels for each priority level
            var priorityCount = Enum.GetValues<TaskPriority>().Length;
            _channels = new Channel<PriorityWorkItem>[priorityCount];

            for (int i = 0; i < priorityCount; i++)
            {
                _channels[i] = Channel.CreateUnbounded<PriorityWorkItem>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            }

            _processingTask = ProcessItemsAsync(_cancellationTokenSource.Token);
        }

        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            TaskPriority priority = TaskPriority.Normal,
            CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var workItem = new PriorityWorkItem(
                async ct =>
                {
                    try
                    {
                        var result = await operation(ct);
                        tcs.SetResult(result);
                    }
                    catch (OperationCanceledException)
                    {
                        tcs.SetCanceled();
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                },
                priority,
                cancellationToken
            );

            var channel = _channels[(int)priority];
            if (!channel.Writer.TryWrite(workItem))
            {
                tcs.SetException(new InvalidOperationException("Failed to queue work item"));
            }

            return tcs.Task;
        }

        public async Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            TaskPriority priority = TaskPriority.Normal,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async ct =>
            {
                await operation(ct);
                return true;
            }, priority, cancellationToken);
        }

        private async Task ProcessItemsAsync(CancellationToken cancellationToken)
        {
            // Use only one processor loop to maintain strict priority ordering
            // The semaphore will control actual concurrency
            await ProcessorLoop(cancellationToken);
        }

        private async Task ProcessorLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PriorityWorkItem workItem = null;

                // Check channels in priority order
                for (int priority = 0; priority < _channels.Length; priority++)
                {
                    if (_channels[priority].Reader.TryRead(out workItem))
                    {
                        break;
                    }
                }

                if (workItem == null)
                {
                    // Wait for any channel to have data
                    await Task.Delay(1, cancellationToken);
                    continue;
                }

                await _concurrencyLimiter.WaitAsync(cancellationToken);
                try
                {
                    await workItem.ExecuteAsync();
                }
                finally
                {
                    _concurrencyLimiter.Release();
                }
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();

            foreach (var channel in _channels)
            {
                channel.Writer.TryComplete();
            }

            try
            {
                _processingTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch { }

            _cancellationTokenSource.Dispose();
            _concurrencyLimiter.Dispose();
        }

        private class PriorityWorkItem
        {
            private readonly Func<CancellationToken, Task> _operation;
            private readonly CancellationToken _cancellationToken;
            private int _executed = 0;

            public TaskPriority Priority { get; }

            public PriorityWorkItem(
                Func<CancellationToken, Task> operation,
                TaskPriority priority,
                CancellationToken cancellationToken)
            {
                _operation = operation;
                Priority = priority;
                _cancellationToken = cancellationToken;
            }

            public async Task ExecuteAsync()
            {
                // Ensure we only execute once
                if (Interlocked.CompareExchange(ref _executed, 1, 0) != 0)
                    return;

                if (_cancellationToken.IsCancellationRequested)
                    return;

                await _operation(_cancellationToken);
            }
        }
    }

    /// <summary>
    /// Extension methods for priority-based task execution
    /// </summary>
    public static class PriorityTaskExtensions
    {
        private static readonly PriorityAsyncExecutor DefaultExecutor = new(Environment.ProcessorCount * 2);

        public static Task<T> RunWithPriorityAsync<T>(
            this Func<Task<T>> operation,
            TaskPriority priority = TaskPriority.Normal)
        {
            return DefaultExecutor.ExecuteAsync(ct => operation(), priority);
        }

        public static Task RunWithPriorityAsync(
            this Func<Task> operation,
            TaskPriority priority = TaskPriority.Normal)
        {
            return DefaultExecutor.ExecuteAsync(ct => operation(), priority);
        }

        public static Task<T> RunWithPriorityAsync<T>(
            this Func<CancellationToken, Task<T>> operation,
            TaskPriority priority = TaskPriority.Normal,
            CancellationToken cancellationToken = default)
        {
            return DefaultExecutor.ExecuteAsync(operation, priority, cancellationToken);
        }

        public static Task RunWithPriorityAsync(
            this Func<CancellationToken, Task> operation,
            TaskPriority priority = TaskPriority.Normal,
            CancellationToken cancellationToken = default)
        {
            return DefaultExecutor.ExecuteAsync(operation, priority, cancellationToken);
        }
    }
}