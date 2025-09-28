using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XStateNet.Tests.TestInfrastructure
{
    /// <summary>
    /// Priority levels for task execution
    /// </summary>
    public enum TaskPriority
    {
        Critical = 0,   // Highest priority - immediate execution
        High = 1,       // High priority - minimal delay
        Normal = 2,     // Default priority
        Low = 3,        // Low priority - can be delayed
        Background = 4  // Lowest priority - only when idle
    }

    /// <summary>
    /// Custom TaskScheduler that supports priority-based execution
    /// </summary>
    public class PriorityTaskScheduler : TaskScheduler, IDisposable
    {
        private readonly ConcurrentPriorityQueue<Task> _taskQueue = new();
        private readonly Thread[] _threads;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private int _delegatedTaskCount;

        public PriorityTaskScheduler(int maxConcurrency = 0)
        {
            if (maxConcurrency <= 0)
                maxConcurrency = Environment.ProcessorCount;

            _threads = new Thread[maxConcurrency];
            for (int i = 0; i < maxConcurrency; i++)
            {
                _threads[i] = new Thread(ProcessTasks)
                {
                    IsBackground = true,
                    Name = $"PriorityTaskThread_{i}",
                    Priority = ThreadPriority.AboveNormal
                };
                _threads[i].Start();
            }
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return _taskQueue.ToArray();
        }

        protected override void QueueTask(Task task)
        {
            var priority = GetTaskPriority(task);
            _taskQueue.Enqueue(task, priority);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            // Only allow inlining for critical priority tasks
            var priority = GetTaskPriority(task);
            if (priority == TaskPriority.Critical && Thread.CurrentThread.IsThreadPoolThread)
            {
                return TryExecuteTask(task);
            }
            return false;
        }

        private void ProcessTasks()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                if (_taskQueue.TryDequeue(out var task))
                {
                    TryExecuteTask(task);
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }

        private TaskPriority GetTaskPriority(Task task)
        {
            // Check if task has priority data attached via AsyncLocal or state
            if (task.AsyncState is TaskPriority priority)
                return priority;

            // Check current context for priority
            return TaskPriorityContext.Current;
        }

        public override int MaximumConcurrencyLevel => _threads.Length;

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            foreach (var thread in _threads)
            {
                thread.Join(TimeSpan.FromSeconds(1));
            }
            _cancellationTokenSource.Dispose();
        }
    }

    /// <summary>
    /// Priority queue implementation for tasks
    /// </summary>
    internal class ConcurrentPriorityQueue<T>
    {
        private readonly SortedDictionary<int, ConcurrentQueue<T>> _queues = new();
        private readonly object _lock = new();

        public void Enqueue(T item, TaskPriority priority)
        {
            var key = (int)priority;
            lock (_lock)
            {
                if (!_queues.ContainsKey(key))
                {
                    _queues[key] = new ConcurrentQueue<T>();
                }
                _queues[key].Enqueue(item);
            }
        }

        public bool TryDequeue(out T result)
        {
            lock (_lock)
            {
                foreach (var kvp in _queues)
                {
                    if (kvp.Value.TryDequeue(out result))
                    {
                        return true;
                    }
                }
            }
            result = default;
            return false;
        }

        public T[] ToArray()
        {
            lock (_lock)
            {
                var items = new List<T>();
                foreach (var queue in _queues.Values)
                {
                    items.AddRange(queue.ToArray());
                }
                return items.ToArray();
            }
        }
    }

    /// <summary>
    /// Provides async-local context for task priority
    /// </summary>
    public static class TaskPriorityContext
    {
        private static readonly AsyncLocal<TaskPriority?> _priority = new();

        public static TaskPriority Current
        {
            get => _priority.Value ?? TaskPriority.Normal;
            set => _priority.Value = value;
        }

        public static IDisposable SetPriority(TaskPriority priority)
        {
            var previousPriority = Current;
            Current = priority;
            return new PriorityScope(previousPriority);
        }

        private class PriorityScope : IDisposable
        {
            private readonly TaskPriority? _previousPriority;
            private readonly bool _wasSet;

            public PriorityScope(TaskPriority previousPriority)
            {
                _wasSet = _priority.Value.HasValue;
                _previousPriority = _wasSet ? previousPriority : null;
            }

            public void Dispose()
            {
                if (_wasSet)
                {
                    Current = _previousPriority.Value;
                }
                else
                {
                    _priority.Value = null; // Reset to unset state
                }
            }
        }
    }
}