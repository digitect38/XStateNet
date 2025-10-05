using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using XStateNet.GPU.Core;

namespace XStateNet.GPU
{
    /// <summary>
    /// Manages a pool of state machines running on GPU
    /// Each state machine instance runs on a GPU core
    /// </summary>
    public class GPUStateMachinePool : IDisposable
    {
        private Context _context;
        private Accelerator _accelerator;
        private MemoryBuffer1D<GPUStateMachineInstance, Stride1D.Dense> _instanceBuffer;
        private MemoryBuffer1D<TransitionEntry, Stride1D.Dense> _transitionBuffer;
        private MemoryBuffer1D<GPUEvent, Stride1D.Dense> _eventBuffer;
        private MemoryBuffer1D<int, Stride1D.Dense> _stateHistogram;

        private GPUStateMachineDefinition _definition;
        private int _instanceCount;
        private GPUStateMachineInstance[] _hostInstances;
        private readonly ConcurrentQueue<GPUEvent> _pendingEvents;
        private bool _disposed;

        // Compiled kernels
        private Action<Index1D, ArrayView<GPUStateMachineInstance>, ArrayView<GPUEvent>,
            ArrayView<TransitionEntry>, int, int, int> _processEventsKernel;
        private Action<Index1D, ArrayView<GPUStateMachineInstance>, ArrayView<int>> _collectStatsKernel;

        public int InstanceCount => _instanceCount;
        public string AcceleratorName => _accelerator.Name;
        public long AvailableMemory => _accelerator.MemorySize;
        public int MaxThreadsPerGroup => _accelerator.MaxNumThreadsPerGroup;
        public int WarpSize => _accelerator.WarpSize;

        public GPUStateMachinePool()
        {
            _pendingEvents = new ConcurrentQueue<GPUEvent>();
        }

        /// <summary>
        /// Initialize the GPU context and accelerator
        /// </summary>
        public async Task InitializeAsync(
            int instanceCount,
            GPUStateMachineDefinition definition,
            AcceleratorType preferredType = AcceleratorType.Cuda)
        {
            _instanceCount = instanceCount;
            _definition = definition;

            // Initialize ILGPU context
            _context = Context.CreateDefault();

            // Select accelerator based on preference
            _accelerator = SelectAccelerator(preferredType);

            // Compile kernels
            _processEventsKernel = _accelerator.LoadAutoGroupedStreamKernel<
                Index1D,
                ArrayView<GPUStateMachineInstance>,
                ArrayView<GPUEvent>,
                ArrayView<TransitionEntry>,
                int, int, int>(
                GPUStateMachineKernels.ProcessEventsKernel);

            _collectStatsKernel = _accelerator.LoadAutoGroupedStreamKernel<
                Index1D,
                ArrayView<GPUStateMachineInstance>,
                ArrayView<int>>(
                GPUStateMachineKernels.CollectStatsKernel);

            // Allocate GPU memory
            await AllocateBuffersAsync();

            // Initialize instances
            await InitializeInstancesAsync();

            Console.WriteLine($"GPU State Machine Pool initialized:");
            Console.WriteLine($"  Accelerator: {_accelerator.Name}");
            Console.WriteLine($"  Type: {_accelerator.AcceleratorType}");
            Console.WriteLine($"  Memory: {_accelerator.MemorySize / (1024 * 1024)} MB");
            Console.WriteLine($"  Max Threads/Group: {_accelerator.MaxNumThreadsPerGroup}");
            Console.WriteLine($"  Warp Size: {_accelerator.WarpSize}");
            Console.WriteLine($"  Instances: {_instanceCount}");
            Console.WriteLine($"  States: {_definition.StateCount}");
            Console.WriteLine($"  Events: {_definition.EventTypeCount}");
        }

        private Accelerator SelectAccelerator(AcceleratorType preferredType)
        {
            // Try to get a CUDA accelerator first
            foreach (var device in _context.Devices)
            {
                if (device.AcceleratorType == AcceleratorType.Cuda)
                {
                    return device.CreateAccelerator(_context);
                }
            }

            // Fallback to CPU
            return _context.CreateCPUAccelerator(0);
        }

        private async Task AllocateBuffersAsync()
        {
            // Allocate instance buffer
            _instanceBuffer = _accelerator.Allocate1D<GPUStateMachineInstance>(_instanceCount);

            // Allocate transition table buffer
            _transitionBuffer = _accelerator.Allocate1D<TransitionEntry>(_definition.TransitionTable.Length);
            _transitionBuffer.CopyFromCPU(_definition.TransitionTable);

            // Allocate event buffer (same size as instances for batch processing)
            _eventBuffer = _accelerator.Allocate1D<GPUEvent>(_instanceCount);

            // Allocate histogram buffer for statistics
            _stateHistogram = _accelerator.Allocate1D<int>(_definition.StateCount);

            // Keep a host copy for quick access
            _hostInstances = new GPUStateMachineInstance[_instanceCount];

            await Task.CompletedTask;
        }

        private async Task InitializeInstancesAsync()
        {
            // Initialize all instances to initial state
            for (int i = 0; i < _instanceCount; i++)
            {
                _hostInstances[i] = new GPUStateMachineInstance
                {
                    InstanceId = i,
                    CurrentState = 0, // Initial state
                    PreviousState = -1,
                    LastEvent = -1,
                    LastTransitionTime = DateTime.UtcNow.Ticks,
                    ErrorCount = 0,
                    Flags = 0
                };
            }

            // Copy to GPU
            _instanceBuffer.CopyFromCPU(_hostInstances);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Send an event to a specific state machine instance
        /// </summary>
        public void SendEvent(int instanceId, string eventName, int data1 = 0, int data2 = 0)
        {
            if (instanceId < 0 || instanceId >= _instanceCount)
                throw new ArgumentOutOfRangeException(nameof(instanceId));

            var eventId = _definition.GetEventId(eventName);
            if (eventId < 0)
                throw new ArgumentException($"Unknown event: {eventName}");

            _pendingEvents.Enqueue(new GPUEvent
            {
                InstanceId = instanceId,
                EventType = eventId,
                EventData1 = data1,
                EventData2 = data2,
                Timestamp = DateTime.UtcNow.Ticks
            });
        }

        /// <summary>
        /// Process all pending events in parallel on GPU
        /// </summary>
        public async Task ProcessEventsAsync()
        {
            if (_pendingEvents.IsEmpty) return;

            // Prepare event batch
            var events = new GPUEvent[_instanceCount];
            for (int i = 0; i < _instanceCount; i++)
            {
                events[i] = new GPUEvent { InstanceId = -1 }; // Mark as no event
            }

            // Fill in actual events
            int processedCount = 0;
            while (_pendingEvents.TryDequeue(out var evt) && processedCount < _instanceCount)
            {
                events[evt.InstanceId] = evt;
                processedCount++;
            }

            // Copy events to GPU
            _eventBuffer.CopyFromCPU(events);

            // Launch kernel
            _processEventsKernel(
                _instanceCount,
                _instanceBuffer.View,
                _eventBuffer.View,
                _transitionBuffer.View,
                _definition.TransitionTable.Length,
                _definition.StateCount,
                _definition.EventTypeCount);

            // Wait for completion
            _accelerator.Synchronize();

            // Copy results back to host
            _instanceBuffer.CopyToCPU(_hostInstances);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Get the current state of a specific instance
        /// </summary>
        public string GetState(int instanceId)
        {
            if (instanceId < 0 || instanceId >= _instanceCount)
                throw new ArgumentOutOfRangeException(nameof(instanceId));

            var stateId = _hostInstances[instanceId].CurrentState;
            return _definition.StateNames[stateId];
        }

        /// <summary>
        /// Get statistics about state distribution
        /// </summary>
        public async Task<ConcurrentDictionary<string, int>> GetStateDistributionAsync()
        {
            // Clear histogram
            _stateHistogram.MemSetToZero();

            // Run statistics kernel
            _collectStatsKernel(
                _instanceCount,
                _instanceBuffer.View,
                _stateHistogram.View);

            _accelerator.Synchronize();

            // Copy histogram to host
            var histogram = new int[_definition.StateCount];
            _stateHistogram.CopyToCPU(histogram);

            // Convert to dictionary
            var result = new ConcurrentDictionary<string, int>();
            for (int i = 0; i < _definition.StateCount; i++)
            {
                if (histogram[i] > 0)
                {
                    result[_definition.StateNames[i]] = histogram[i];
                }
            }

            return result;
        }

        /// <summary>
        /// Batch send events to multiple instances
        /// </summary>
        public async Task SendEventBatchAsync(
            int[] instanceIds,
            string[] eventNames,
            int[] data1 = null,
            int[] data2 = null)
        {
            if (instanceIds.Length != eventNames.Length)
                throw new ArgumentException("Arrays must have same length");

            for (int i = 0; i < instanceIds.Length; i++)
            {
                SendEvent(
                    instanceIds[i],
                    eventNames[i],
                    data1?[i] ?? 0,
                    data2?[i] ?? 0);
            }

            await ProcessEventsAsync();
        }

        /// <summary>
        /// Get performance metrics
        /// </summary>
        public GPUPerformanceMetrics GetMetrics()
        {
            return new GPUPerformanceMetrics
            {
                InstanceCount = _instanceCount,
                StateCount = _definition.StateCount,
                EventTypeCount = _definition.EventTypeCount,
                TransitionCount = _definition.TransitionTable.Length,
                MemoryUsed = CalculateMemoryUsage(),
                AcceleratorType = _accelerator.AcceleratorType.ToString(),
                MaxParallelism = _accelerator.MaxNumThreadsPerGroup
            };
        }

        private long CalculateMemoryUsage()
        {
            long bytes = 0;
            bytes += _instanceCount * System.Runtime.InteropServices.Marshal.SizeOf<GPUStateMachineInstance>();
            bytes += _definition.TransitionTable.Length * System.Runtime.InteropServices.Marshal.SizeOf<TransitionEntry>();
            bytes += _instanceCount * System.Runtime.InteropServices.Marshal.SizeOf<GPUEvent>();
            bytes += _definition.StateCount * sizeof(int);
            return bytes;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _instanceBuffer?.Dispose();
            _transitionBuffer?.Dispose();
            _eventBuffer?.Dispose();
            _stateHistogram?.Dispose();
            _accelerator?.Dispose();
            _context?.Dispose();

            _disposed = true;
        }
    }

    public class GPUPerformanceMetrics
    {
        public int InstanceCount { get; set; }
        public int StateCount { get; set; }
        public int EventTypeCount { get; set; }
        public int TransitionCount { get; set; }
        public long MemoryUsed { get; set; }
        public string AcceleratorType { get; set; }
        public int MaxParallelism { get; set; }

        public override string ToString()
        {
            return $"GPU State Machines: {InstanceCount}, " +
                   $"Memory: {MemoryUsed / (1024 * 1024)}MB, " +
                   $"Max Parallelism: {MaxParallelism}, " +
                   $"Type: {AcceleratorType}";
        }
    }
}