using ILGPU;
using ILGPU.Runtime;
using XStateNet.GPU.Core;

namespace XStateNet.GPU.Tests
{
    public class GPUKernelTests : IDisposable
    {
        private Context _context;
        private Accelerator _accelerator;

        public GPUKernelTests()
        {
            _context = Context.CreateDefault();
            // Use the first available device (CPU fallback)
            var device = _context.Devices.First();
            _accelerator = device.CreateAccelerator(_context);
        }

        public void Dispose()
        {
            _accelerator?.Dispose();
            _context?.Dispose();
        }

        [Fact]
        public void ProcessEventsKernel_ProcessesSingleEvent()
        {
            // Arrange
            var instances = new GPUStateMachineInstance[]
            {
                new GPUStateMachineInstance
                {
                    InstanceId = 0,
                    CurrentState = 0,
                    PreviousState = -1,
                    LastEvent = -1,
                    LastTransitionTime = DateTime.UtcNow.Ticks,
                    ErrorCount = 0,
                    Flags = 0
                }
            };

            var events = new GPUEvent[]
            {
                new GPUEvent
                {
                    InstanceId = 0,
                    EventType = 0,
                    EventData1 = 0,
                    EventData2 = 0,
                    Timestamp = DateTime.UtcNow.Ticks
                }
            };

            var transitions = new TransitionEntry[]
            {
                new TransitionEntry { FromState = 0, EventType = 0, ToState = 1, ActionId = 0, GuardId = 0 }
            };

            // Allocate GPU buffers
            using var instanceBuffer = _accelerator.Allocate1D(instances);
            using var eventBuffer = _accelerator.Allocate1D(events);
            using var transitionBuffer = _accelerator.Allocate1D(transitions);

            // Compile and execute kernel
            var kernel = _accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<GPUStateMachineInstance>, ArrayView<GPUEvent>,
                ArrayView<TransitionEntry>, int, int, int>(
                GPUStateMachineKernels.ProcessEventsKernel);

            kernel(1, instanceBuffer.View, eventBuffer.View, transitionBuffer.View,
                transitions.Length, 2, 1);

            _accelerator.Synchronize();

            // Copy back results
            var result = new GPUStateMachineInstance[1];
            instanceBuffer.CopyToCPU(result);

            // Assert
            Assert.Equal(1, result[0].CurrentState);
            Assert.Equal(0, result[0].PreviousState);
            Assert.Equal(0, result[0].LastEvent);
        }

        [Fact]
        public void CollectStatsKernel_GeneratesCorrectHistogram()
        {
            // Arrange
            var instances = new GPUStateMachineInstance[]
            {
                new GPUStateMachineInstance { InstanceId = 0, CurrentState = 0 },
                new GPUStateMachineInstance { InstanceId = 1, CurrentState = 1 },
                new GPUStateMachineInstance { InstanceId = 2, CurrentState = 0 },
                new GPUStateMachineInstance { InstanceId = 3, CurrentState = 2 },
                new GPUStateMachineInstance { InstanceId = 4, CurrentState = 1 },
            };

            var histogram = new int[3]; // 3 states

            // Allocate GPU buffers
            using var instanceBuffer = _accelerator.Allocate1D(instances);
            using var histogramBuffer = _accelerator.Allocate1D(histogram);

            // Compile and execute kernel
            var kernel = _accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<GPUStateMachineInstance>, ArrayView<int>>(
                GPUStateMachineKernels.CollectStatsKernel);

            kernel(instances.Length, instanceBuffer.View, histogramBuffer.View);

            _accelerator.Synchronize();

            // Copy back results
            histogramBuffer.CopyToCPU(histogram);

            // Assert
            Assert.Equal(2, histogram[0]); // State 0: 2 instances
            Assert.Equal(2, histogram[1]); // State 1: 2 instances
            Assert.Equal(1, histogram[2]); // State 2: 1 instance
        }

        [Fact]
        public void EvaluateGuardsKernel_EvaluatesGuardsCorrectly()
        {
            // Arrange
            var instances = new GPUStateMachineInstance[]
            {
                new GPUStateMachineInstance { InstanceId = 0, ErrorCount = 1 },
                new GPUStateMachineInstance { InstanceId = 1, ErrorCount = 5 },
                new GPUStateMachineInstance { InstanceId = 2, ErrorCount = 2 },
                new GPUStateMachineInstance { InstanceId = 3, ErrorCount = 10 },
            };

            var guardResults = new int[4];

            // Allocate GPU buffers
            using var instanceBuffer = _accelerator.Allocate1D(instances);
            using var guardBuffer = _accelerator.Allocate1D(guardResults);

            // Compile and execute kernel
            var kernel = _accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<GPUStateMachineInstance>, ArrayView<int>>(
                GPUStateMachineKernels.EvaluateGuardsKernel);

            kernel(instances.Length, instanceBuffer.View, guardBuffer.View);

            _accelerator.Synchronize();

            // Copy back results
            guardBuffer.CopyToCPU(guardResults);

            // Assert (guard fails if ErrorCount > 3)
            Assert.Equal(1, guardResults[0]); // Pass
            Assert.Equal(0, guardResults[1]); // Fail
            Assert.Equal(1, guardResults[2]); // Pass
            Assert.Equal(0, guardResults[3]); // Fail
        }

        [Fact]
        public unsafe void UpdateContextKernel_UpdatesContextCorrectly()
        {
            // Arrange
            var instances = new GPUStateMachineInstance[]
            {
                new GPUStateMachineInstance { InstanceId = 0 }
            };

            var contextData = new byte[] { 1, 2, 3, 4, 5 };
            var contextUpdates = new byte[256];
            Array.Copy(contextData, contextUpdates, contextData.Length);

            // Allocate GPU buffers
            using var instanceBuffer = _accelerator.Allocate1D(instances);
            using var contextBuffer = _accelerator.Allocate1D(contextUpdates);

            // Compile and execute kernel
            var kernel = _accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<GPUStateMachineInstance>, ArrayView<byte>, int>(
                GPUStateMachineKernels.UpdateContextKernel);

            kernel(1, instanceBuffer.View, contextBuffer.View, 256);

            _accelerator.Synchronize();

            // Copy back results
            var result = new GPUStateMachineInstance[1];
            instanceBuffer.CopyToCPU(result);

            // Assert
            fixed (byte* ptr = result[0].ContextData)
            {
                Assert.Equal(1, ptr[0]);
                Assert.Equal(2, ptr[1]);
                Assert.Equal(3, ptr[2]);
                Assert.Equal(4, ptr[3]);
                Assert.Equal(5, ptr[4]);
            }
        }
    }
}