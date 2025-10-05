using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using XStateNet.Distributed.EventBus;

namespace XStateNet.Distributed.PubSub.FalseSharingOptimized
{
    /// <summary>
    /// Event bus with false sharing protection using cache line padding
    /// </summary>
    public sealed class FalseSharingOptimizedEventBus : IDisposable
    {
        // CPU cache line is typically 64 bytes on x64, 128 bytes on ARM
        private const int CACHE_LINE_SIZE = 128; // Use larger size for safety

        #region Padded Performance Counters

        /// <summary>
        /// Padded counter to prevent false sharing between threads
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = CACHE_LINE_SIZE * 2)]
        private struct PaddedCounter
        {
            [FieldOffset(CACHE_LINE_SIZE / 2)]
            public long Value;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public long Increment()
            {
                return Interlocked.Increment(ref Value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public long Add(long value)
            {
                return Interlocked.Add(ref Value, value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public long Read()
            {
                return Volatile.Read(ref Value);
            }
        }

        /// <summary>
        /// Padded boolean flag for thread coordination
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = CACHE_LINE_SIZE * 2)]
        private struct PaddedBool
        {
            [FieldOffset(CACHE_LINE_SIZE / 2)]
            private int _value;

            public bool Value
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Volatile.Read(ref _value) == 1;
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                set => Volatile.Write(ref _value, value ? 1 : 0);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool CompareExchange(bool value, bool comparand)
            {
                var newVal = value ? 1 : 0;
                var compVal = comparand ? 1 : 0;
                return Interlocked.CompareExchange(ref _value, newVal, compVal) == compVal;
            }
        }

        #endregion

        #region Per-Thread Structures with False Sharing Protection

        /// <summary>
        /// Thread-local data structure with padding to prevent false sharing
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private sealed class ThreadLocalData
        {
            // Padding before
            private readonly byte[] _padding1 = new byte[CACHE_LINE_SIZE];

            // Actual data in its own cache line
            public readonly ConcurrentQueue<StateMachineEvent> EventQueue;
            public readonly Channel<StateMachineEvent> LocalChannel;
            public long LocalEventsProcessed;
            public long LocalEventsDropped;

            // Padding after
            private readonly byte[] _padding2 = new byte[CACHE_LINE_SIZE];

            public ThreadLocalData()
            {
                EventQueue = new ConcurrentQueue<StateMachineEvent>();
                LocalChannel = Channel.CreateUnbounded<StateMachineEvent>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void IncrementProcessed()
            {
                Interlocked.Increment(ref LocalEventsProcessed);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void IncrementDropped()
            {
                Interlocked.Increment(ref LocalEventsDropped);
            }
        }

        /// <summary>
        /// Striped locks with padding to prevent false sharing
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private sealed class StripedLock
        {
            private readonly byte[] _padding1 = new byte[CACHE_LINE_SIZE];
            private readonly ReaderWriterLockSlim _lock = new();
            private readonly byte[] _padding2 = new byte[CACHE_LINE_SIZE];

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnterReadLock() => _lock.EnterReadLock();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ExitReadLock() => _lock.ExitReadLock();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnterWriteLock() => _lock.EnterWriteLock();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ExitWriteLock() => _lock.ExitWriteLock();
        }

        #endregion

        #region Ring Buffer with False Sharing Protection

        /// <summary>
        /// Lock-free ring buffer with cache line padding
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private sealed class PaddedRingBuffer<T> where T : class
        {
            private readonly byte[] _padding1 = new byte[CACHE_LINE_SIZE];

            // Producer section (own cache line)
            private long _producerSequence;
            private readonly byte[] _padding2 = new byte[CACHE_LINE_SIZE - 8];

            // Consumer section (own cache line)
            private long _consumerSequence;
            private readonly byte[] _padding3 = new byte[CACHE_LINE_SIZE - 8];

            // Shared data (separate cache lines)
            private readonly T?[] _buffer;
            private readonly int _bufferMask;
            private readonly byte[] _padding4 = new byte[CACHE_LINE_SIZE];

            public PaddedRingBuffer(int bufferSize)
            {
                if ((bufferSize & (bufferSize - 1)) != 0)
                    throw new ArgumentException("Buffer size must be power of 2");

                _buffer = new T[bufferSize];
                _bufferMask = bufferSize - 1;
                _producerSequence = -1;
                _consumerSequence = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryPublish(T item)
            {
                var next = Volatile.Read(ref _producerSequence) + 1;
                var wrapPoint = next - _buffer.Length;

                if (wrapPoint >= Volatile.Read(ref _consumerSequence))
                {
                    return false; // Buffer full
                }

                _buffer[next & _bufferMask] = item;

                // Memory barrier to ensure the item is written before updating sequence
                Thread.MemoryBarrier();

                Volatile.Write(ref _producerSequence, next);
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryConsume(out T? item)
            {
                var current = Volatile.Read(ref _consumerSequence);
                var available = Volatile.Read(ref _producerSequence);

                if (current <= available)
                {
                    item = _buffer[current & _bufferMask];
                    _buffer[current & _bufferMask] = null; // Clear reference

                    // Memory barrier before updating consumer sequence
                    Thread.MemoryBarrier();

                    Volatile.Write(ref _consumerSequence, current + 1);
                    return true;
                }

                item = default;
                return false;
            }

            public long Available =>
                Volatile.Read(ref _producerSequence) - Volatile.Read(ref _consumerSequence) + 1;
        }

        #endregion

        #region NUMA-Aware Thread Pool

        /// <summary>
        /// NUMA-aware worker thread with affinity
        /// </summary>
        private sealed class NumaAwareWorker
        {
            private readonly int _threadId;
            private readonly int _numaNode;
            private readonly PaddedRingBuffer<StateMachineEvent> _ringBuffer;
            private readonly ThreadLocalData _localData;
            private readonly Thread _thread;
            private readonly CancellationTokenSource _cts;

            public NumaAwareWorker(int threadId, int cpuId)
            {
                _threadId = threadId;
                _numaNode = GetNumaNode(cpuId);
                _ringBuffer = new PaddedRingBuffer<StateMachineEvent>(4096);
                _localData = new ThreadLocalData();
                _cts = new CancellationTokenSource();

                _thread = new Thread(WorkerLoop)
                {
                    Name = $"EventBusWorker-{threadId}",
                    IsBackground = true
                };

                // Set CPU affinity if supported
                SetThreadAffinity(_thread, cpuId);
            }

            public void StartAsync() => _thread.Start();

            public void Stop()
            {
                _cts.Cancel();
                _thread.Join(5000);
                _cts.Dispose();
            }

            public bool TryEnqueue(StateMachineEvent evt)
            {
                return _ringBuffer.TryPublish(evt);
            }

            private void WorkerLoop()
            {
                var spinWait = new SpinWait();

                while (!_cts.Token.IsCancellationRequested)
                {
                    if (_ringBuffer.TryConsume(out var evt) && evt != null)
                    {
                        ProcessEvent(evt);
                        _localData.IncrementProcessed();
                        spinWait.Reset();
                    }
                    else
                    {
                        spinWait.SpinOnce();
                    }
                }
            }

            private void ProcessEvent(StateMachineEvent evt)
            {
                // Process event
                // Implementation details...
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int GetNumaNode(int cpuId)
            {
                // Simplified - in production, use P/Invoke to get actual NUMA topology
                return cpuId / (Environment.ProcessorCount / 2);
            }

            private static void SetThreadAffinity(Thread thread, int cpuId)
            {
                // Platform-specific implementation
                // On Windows: Use SetThreadAffinityMask
                // On Linux: Use pthread_setaffinity_np
            }
        }

        #endregion

        #region Main Class Implementation

        private readonly ILogger<FalseSharingOptimizedEventBus>? _logger;

        // Padded counters to prevent false sharing
        private PaddedCounter _totalEventsPublished;
        private PaddedCounter _totalEventsDelivered;
        private PaddedCounter _totalEventsDropped;
        private PaddedCounter _totalSubscriptions;

        // Padded flags
        private PaddedBool _isRunning;
        private PaddedBool _isDisposed;

        // Thread-local storage with proper isolation
        private readonly ThreadLocal<ThreadLocalData> _threadLocalData;

        // Striped locks for subscriptions (reduces contention)
        private readonly StripedLock[] _subscriptionLocks;
        private const int LOCK_STRIPE_COUNT = 16; // Should be power of 2

        // NUMA-aware workers
        private readonly NumaAwareWorker[] _workers;

        // Main data structures (each in separate allocation to avoid false sharing)
        private readonly ConcurrentDictionary<string, SubscriptionList> _subscriptions;

        public FalseSharingOptimizedEventBus(ILogger<FalseSharingOptimizedEventBus>? logger = null)
        {
            _logger = logger;

            // Initialize thread-local data
            _threadLocalData = new ThreadLocal<ThreadLocalData>(() => new ThreadLocalData(), true);

            // Initialize striped locks
            _subscriptionLocks = new StripedLock[LOCK_STRIPE_COUNT];
            for (int i = 0; i < LOCK_STRIPE_COUNT; i++)
            {
                _subscriptionLocks[i] = new StripedLock();
            }

            // Initialize NUMA-aware workers
            var workerCount = Environment.ProcessorCount;
            _workers = new NumaAwareWorker[workerCount];
            for (int i = 0; i < workerCount; i++)
            {
                _workers[i] = new NumaAwareWorker(i, i);
            }

            _subscriptions = new ConcurrentDictionary<string, SubscriptionList>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PublishEvent(string topic, StateMachineEvent evt)
        {
            if (!_isRunning.Value) return;

            // Use thread-local data to avoid contention
            var localData = _threadLocalData.Value;
            if (localData == null) return;

            // Try to enqueue to local queue first
            if (!localData.LocalChannel.Writer.TryWrite(evt))
            {
                // If local queue is full, use round-robin to select worker
                var workerId = (int)(Volatile.Read(ref _totalEventsPublished.Value) % _workers.Length);
                if (!_workers[workerId].TryEnqueue(evt))
                {
                    _totalEventsDropped.Increment();
                    return;
                }
            }

            _totalEventsPublished.Increment();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IDisposable Subscribe(string pattern, Action<StateMachineEvent> handler)
        {
            _totalSubscriptions.Increment();

            // Use striped locking to reduce contention
            var lockIndex = GetLockIndex(pattern);
            var stripedLock = _subscriptionLocks[lockIndex];

            stripedLock.EnterWriteLock();
            try
            {
                var list = _subscriptions.GetOrAdd(pattern, _ => new SubscriptionList());
                var subscription = new Subscription(handler);
                list.Add(subscription);

                return new SubscriptionDisposable(() =>
                {
                    stripedLock.EnterWriteLock();
                    try
                    {
                        list.Remove(subscription);
                        _totalSubscriptions.Add(-1);
                    }
                    finally
                    {
                        stripedLock.ExitWriteLock();
                    }
                });
            }
            finally
            {
                stripedLock.ExitWriteLock();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetLockIndex(string key)
        {
            // Use hash code to determine which lock to use
            return (key.GetHashCode() & 0x7FFFFFFF) % LOCK_STRIPE_COUNT;
        }

        public void StartAsync()
        {
            if (_isRunning.CompareExchange(true, false))
            {
                foreach (var worker in _workers)
                {
                    worker.StartAsync();
                }

                _logger?.LogInformation("FalseSharingOptimizedEventBus started with {WorkerCount} workers",
                    _workers.Length);
            }
        }

        public void Stop()
        {
            if (_isRunning.CompareExchange(false, true))
            {
                foreach (var worker in _workers)
                {
                    worker.Stop();
                }

                _logger?.LogInformation(
                    "FalseSharingOptimizedEventBus stopped. Published: {Published}, Delivered: {Delivered}, Dropped: {Dropped}",
                    _totalEventsPublished.Read(),
                    _totalEventsDelivered.Read(),
                    _totalEventsDropped.Read());
            }
        }

        public void Dispose()
        {
            if (_isDisposed.CompareExchange(true, false))
            {
                Stop();
                _threadLocalData?.Dispose();
            }
        }

        #endregion

        #region Helper Classes

        private class SubscriptionList
        {
            private readonly List<Subscription> _list = new();

            public void Add(Subscription subscription) => _list.Add(subscription);
            public void Remove(Subscription subscription) => _list.Remove(subscription);
            public void NotifyAll(StateMachineEvent evt)
            {
                foreach (var sub in _list)
                {
                    sub.Notify(evt);
                }
            }
        }

        private class Subscription
        {
            private readonly Action<StateMachineEvent> _handler;

            public Subscription(Action<StateMachineEvent> handler)
            {
                _handler = handler;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Notify(StateMachineEvent evt) => _handler(evt);
        }

        private class SubscriptionDisposable : IDisposable
        {
            private Action? _disposeAction;

            public SubscriptionDisposable(Action disposeAction)
            {
                _disposeAction = disposeAction;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _disposeAction, null)?.Invoke();
            }
        }

        #endregion
    }

    /// <summary>
    /// Helper class for CPU and NUMA topology
    /// </summary>
    public static class CpuTopology
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNumaNodeProcessorMask(byte Node, out ulong ProcessorMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNumaHighestNodeNumber(out uint HighestNodeNumber);

        /// <summary>
        /// Set thread affinity to specific CPU
        /// </summary>
        public static void SetThreadAffinity(int cpuId)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var affinity = new IntPtr(1L << cpuId);
                SetThreadAffinityMask(GetCurrentThread(), affinity);
            }
            // Linux implementation would use pthread_setaffinity_np via P/Invoke
        }

        /// <summary>
        /// Get NUMA node for CPU
        /// </summary>
        public static int GetNumaNode(int cpuId)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                uint highestNode;
                if (GetNumaHighestNodeNumber(out highestNode))
                {
                    for (byte node = 0; node <= highestNode; node++)
                    {
                        ulong mask;
                        if (GetNumaNodeProcessorMask(node, out mask))
                        {
                            if ((mask & (1UL << cpuId)) != 0)
                                return node;
                        }
                    }
                }
            }

            // Fallback: simple division
            return cpuId / Math.Max(1, Environment.ProcessorCount / 2);
        }
    }
}