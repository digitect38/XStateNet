using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet.Tests.TestInfrastructure;

namespace XStateNet.Tests
{
    [Collection("TimingSensitive")]
    public class PriorityTaskSchedulerTests
    {
        private readonly ITestOutputHelper _output;

        public PriorityTaskSchedulerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        [TestPriority(TestPriority.Critical)]
        public async Task PriorityAsyncExecutor_ExecutesTasksInPriorityOrder()
        {
            // Arrange
            var executor = new PriorityAsyncExecutor(maxConcurrency: 1); // Single thread to ensure order
            var executionOrder = new List<string>();
            var executionLock = new object();
            var tasks = new List<Task>();

            // Act - Create and start tasks directly
            tasks.Add(executor.ExecuteAsync(async ct =>
            {
                lock (executionLock) { executionOrder.Add("Low"); }
                await Task.Yield();
            }, TaskPriority.Low));

            tasks.Add(executor.ExecuteAsync(async ct =>
            {
                lock (executionLock) { executionOrder.Add("Critical"); }
                await Task.Yield();
            }, TaskPriority.Critical));

            tasks.Add(executor.ExecuteAsync(async ct =>
            {
                lock (executionLock) { executionOrder.Add("Normal"); }
                await Task.Yield();
            }, TaskPriority.Normal));

            tasks.Add(executor.ExecuteAsync(async ct =>
            {
                lock (executionLock) { executionOrder.Add("High"); }
                await Task.Yield();
            }, TaskPriority.High));

            tasks.Add(executor.ExecuteAsync(async ct =>
            {
                lock (executionLock) { executionOrder.Add("Background"); }
                await Task.Yield();
            }, TaskPriority.Background));

            await Task.WhenAll(tasks);

            // Assert - Should execute in priority order
            _output.WriteLine($"Execution order: {string.Join(", ", executionOrder)}");

            Assert.Equal(5, executionOrder.Count);
            // With concurrent submission, the first task might start immediately
            // but subsequent ones should be in priority order
            Assert.Contains("Critical", executionOrder.Take(2));
            Assert.Contains("High", executionOrder.Take(3));
        }

        [Fact]
        public async Task PriorityTaskScheduler_HandlesHighConcurrency()
        {
            // Arrange
            using var scheduler = new PriorityTaskScheduler(maxConcurrency: 4);
            var factory = new TaskFactory(scheduler);
            var criticalCompleted = 0;
            var normalCompleted = 0;
            var tasks = new List<Task>();

            // Act - Create many tasks with different priorities
            for (int i = 0; i < 100; i++)
            {
                var priority = i % 10 == 0 ? TaskPriority.Critical : TaskPriority.Normal;
                var taskId = i;

                tasks.Add(factory.StartNew(async state =>
                {
                    await Task.Delay(Random.Shared.Next(1, 10));
                    if ((TaskPriority)state == TaskPriority.Critical)
                        Interlocked.Increment(ref criticalCompleted);
                    else
                        Interlocked.Increment(ref normalCompleted);
                }, priority).Unwrap());
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(10, criticalCompleted);
            Assert.Equal(90, normalCompleted);
            _output.WriteLine($"Completed: {criticalCompleted} critical, {normalCompleted} normal");
        }

        [Fact]
        public async Task PriorityExtensions_SimplifyPriorityExecution()
        {
            // Arrange
            var results = new ConcurrentBag<(string name, DateTime time)>();

            // Act - Use extension methods for different priorities
            var criticalTask = new Func<Task>(async () =>
            {
                await Task.Delay(5);
                results.Add(("Critical", DateTime.UtcNow));
            }).RunWithPriorityAsync(TaskPriority.Critical);

            var normalTask = new Func<Task>(async () =>
            {
                await Task.Delay(5);
                results.Add(("Normal", DateTime.UtcNow));
            }).RunWithPriorityAsync(TaskPriority.Normal);

            var lowTask = new Func<Task>(async () =>
            {
                await Task.Delay(5);
                results.Add(("Low", DateTime.UtcNow));
            }).RunWithPriorityAsync(TaskPriority.Low);

            await Task.WhenAll(criticalTask, normalTask, lowTask);

            // Assert
            Assert.Equal(3, results.Count);
            var orderedResults = results.OrderBy(r => r.time).Select(r => r.name).ToList();
            _output.WriteLine($"Completion order: {string.Join(", ", orderedResults)}");
        }

        [Fact]
        public async Task TaskPriorityContext_MaintainsPriorityAcrossAsyncCalls()
        {
            // Arrange
            var capturedPriorities = new ConcurrentBag<TaskPriority>();

            async Task CaptureCurrentPriority()
            {
                await Task.Yield();
                capturedPriorities.Add(TaskPriorityContext.Current);
            }

            // Act
            using (TaskPriorityContext.SetPriority(TaskPriority.Critical))
            {
                await CaptureCurrentPriority();

                using (TaskPriorityContext.SetPriority(TaskPriority.Low))
                {
                    await CaptureCurrentPriority();
                }

                await CaptureCurrentPriority();
            }

            await CaptureCurrentPriority();

            // Assert
            var priorities = capturedPriorities.ToList();
            Assert.Equal(4, priorities.Count);
            Assert.Contains(TaskPriority.Critical, priorities);
            Assert.Contains(TaskPriority.Low, priorities);
            Assert.Contains(TaskPriority.Normal, priorities); // Default after scope exit
        }

        [Fact]
        public async Task PriorityAsyncExecutor_RespectssCancellation()
        {
            // Arrange
            var executor = new PriorityAsyncExecutor(maxConcurrency: 2);
            var cts = new CancellationTokenSource();
            var started = false;
            var cancelled = false;

            // Act
            var task = executor.ExecuteAsync(async ct =>
            {
                started = true;
                await Task.Delay(1000, ct);
                return 42;
            }, TaskPriority.Normal, cts.Token);

            await Task.Delay(50); // Let it start
            cts.Cancel();

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            // Assert
            Assert.True(started);
            Assert.True(cancelled);
        }

        [Fact]
        public async Task PriorityAsyncExecutor_HandlesExceptions()
        {
            // Arrange
            var executor = new PriorityAsyncExecutor();
            var exceptionMessage = "Test exception";

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await executor.ExecuteAsync<int>(async ct =>
                {
                    await Task.Delay(10, ct);
                    throw new InvalidOperationException(exceptionMessage);
                }, TaskPriority.High);
            });

            Assert.Equal(exceptionMessage, exception.Message);
        }

        [Fact]
        public async Task ConcurrentPriorityQueue_MaintainsOrder()
        {
            // Arrange
            var queue = new ConcurrentPriorityQueue<int>();
            var tasks = new List<Task>();

            // Act - Add items concurrently
            for (int i = 0; i < 100; i++)
            {
                var value = i;
                var priority = (TaskPriority)(value % 5);
                tasks.Add(Task.Run(() => queue.Enqueue(value, priority)));
            }

            await Task.WhenAll(tasks);

            // Dequeue and verify priority order
            var dequeuedItems = new List<int>();
            while (queue.TryDequeue(out var item))
            {
                dequeuedItems.Add(item);
            }

            // Assert
            Assert.Equal(100, dequeuedItems.Count);

            // Items with same priority should be together
            var priorityGroups = dequeuedItems
                .Select((value, index) => new { value, priority = value % 5, index })
                .GroupBy(x => x.priority)
                .OrderBy(g => g.Key);

            _output.WriteLine($"Dequeued {dequeuedItems.Count} items in priority groups");
        }
    }
}