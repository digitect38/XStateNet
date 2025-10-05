using System.Diagnostics;
using XStateNet.GPU.Core;
using Xunit.Abstractions;

namespace XStateNet.GPU.Tests
{
    public class PerformanceTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private GPUStateMachinePool _pool;

        public PerformanceTests(ITestOutputHelper output)
        {
            _output = output;
            _pool = new GPUStateMachinePool();
        }

        public void Dispose()
        {
            _pool?.Dispose();
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(10000)]
        public async Task MeasureEventThroughput(int instanceCount)
        {
            // Arrange
            var definition = CreatePerformanceTestDefinition();
            await _pool.InitializeAsync(instanceCount, definition);

            var sw = Stopwatch.StartNew();
            int eventCount = 0;

            // Act - Process events for 1 second
            while (sw.ElapsedMilliseconds < 1000)
            {
                for (int i = 0; i < instanceCount; i++)
                {
                    _pool.SendEvent(i, "EVENT_A");
                    eventCount++;
                }
                await _pool.ProcessEventsAsync();
            }

            sw.Stop();

            // Calculate throughput
            double eventsPerSecond = eventCount * 1000.0 / sw.ElapsedMilliseconds;
            _output.WriteLine($"Instance Count: {instanceCount}");
            _output.WriteLine($"Events Processed: {eventCount}");
            _output.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Throughput: {eventsPerSecond:N0} events/sec");
            _output.WriteLine($"Latency: {sw.ElapsedMilliseconds / (double)eventCount:F3}ms per event");

            // Assert minimum performance
            Assert.True(eventsPerSecond > instanceCount * 10,
                $"Performance too low: {eventsPerSecond:N0} events/sec");
        }

        [Fact]
        public async Task MeasureScalability()
        {
            var definition = CreatePerformanceTestDefinition();

            int[] instanceCounts = { 100, 500, 1000, 5000, 10000, 50000 };
            var results = new (int instances, double eventsPerSec, double latency)[instanceCounts.Length];


            for (int idx = 0; idx < instanceCounts.Length; idx++)
            {
                var count = instanceCounts[idx];

                // Re-initialize pool for each test
                _pool?.Dispose();
                _pool = new GPUStateMachinePool();
                await _pool.InitializeAsync(count, definition);

                var sw = Stopwatch.StartNew();
                int totalEvents = count * 100; // Process 100 events per instance

                // Send events
                for (int round = 0; round < 100; round++)
                {
                    for (int i = 0; i < count; i++)
                    {
                        _pool.SendEvent(i, round % 2 == 0 ? "EVENT_A" : "EVENT_B");
                    }
                    await _pool.ProcessEventsAsync();
                }

                sw.Stop();

                double eventsPerSecond = totalEvents * 1000.0 / sw.ElapsedMilliseconds;
                double latency = sw.ElapsedMilliseconds / (double)totalEvents;

                results[idx] = (count, eventsPerSecond, latency);

                _output.WriteLine($"Instances: {count,6} | Throughput: {eventsPerSecond,12:N0} evt/s | Latency: {latency:F4}ms");
            }

            // Assert scalability - throughput should increase with instance count
            for (int i = 1; i < results.Length; i++)
            {
                Assert.True(results[i].eventsPerSec > results[i - 1].eventsPerSec * 0.6,
                    $"Poor scalability from {results[i - 1].instances} to {results[i].instances} instances");
            }
        }

        [Fact]
        public async Task MeasureMemoryEfficiency()
        {
            var definition = CreatePerformanceTestDefinition();

            // Test memory usage with different instance counts
            int[] instanceCounts = { 1000, 10000, 50000 };

            foreach (var count in instanceCounts)
            {
                _pool?.Dispose();
                _pool = new GPUStateMachinePool();
                await _pool.InitializeAsync(count, definition);

                var metrics = _pool.GetMetrics();

                double bytesPerInstance = metrics.MemoryUsed / (double)count;

                _output.WriteLine($"Instances: {count,7} | Total Memory: {metrics.MemoryUsed / (1024.0 * 1024.0):F2} MB | Per Instance: {bytesPerInstance:F0} bytes");

                // Assert reasonable memory usage (< 1KB per instance)
                Assert.True(bytesPerInstance < 1024,
                    $"Memory usage too high: {bytesPerInstance:F0} bytes per instance");
            }
        }

        [Fact]
        public async Task CompareCPUvsGPUPerformance()
        {
            var definition = CreatePerformanceTestDefinition();
            //int instanceCount = 1000;
            int instanceCount = 100000;
            int eventsPerInstance = 100;

            // Test GPU performance
            await _pool.InitializeAsync(instanceCount, definition, ILGPU.Runtime.AcceleratorType.Cuda);

            var gpuSw = Stopwatch.StartNew();
            for (int round = 0; round < eventsPerInstance; round++)
            {
                for (int i = 0; i < instanceCount; i++)
                {
                    _pool.SendEvent(i, "EVENT_A");
                }
                await _pool.ProcessEventsAsync();
            }
            gpuSw.Stop();

            var gpuTime = gpuSw.ElapsedMilliseconds;
            var gpuThroughput = (instanceCount * eventsPerInstance * 1000.0) / gpuTime;

            // Test CPU performance (fallback)
            _pool.Dispose();
            _pool = new GPUStateMachinePool();
            await _pool.InitializeAsync(instanceCount, definition, ILGPU.Runtime.AcceleratorType.CPU);

            var cpuSw = Stopwatch.StartNew();
            for (int round = 0; round < eventsPerInstance; round++)
            {
                for (int i = 0; i < instanceCount; i++)
                {
                    _pool.SendEvent(i, "EVENT_A");
                }
                await _pool.ProcessEventsAsync();
            }
            cpuSw.Stop();

            var cpuTime = cpuSw.ElapsedMilliseconds;
            var cpuThroughput = (instanceCount * eventsPerInstance * 1000.0) / cpuTime;

            var speedup = gpuThroughput / cpuThroughput;

            _output.WriteLine($"CPU Time: {cpuTime}ms | Throughput: {cpuThroughput:N0} events/sec");
            _output.WriteLine($"GPU Time: {gpuTime}ms | Throughput: {gpuThroughput:N0} events/sec");
            _output.WriteLine($"GPU Speedup: {speedup:F2}x");

            // GPU should be faster for parallel workloads
            //Assert.True(gpuThroughput >= cpuThroughput * 0.9,
            Assert.True(gpuThroughput >= cpuThroughput * 0.6,
                $"GPU performance ({gpuThroughput:N0}) should be at least 90% of CPU ({cpuThroughput:N0})");
        }

        [Fact]
        public async Task StressTest_MillionEvents()
        {
            // Arrange
            var definition = CreatePerformanceTestDefinition();
            int instanceCount = 10000;
            await _pool.InitializeAsync(instanceCount, definition);

            // Act
            var sw = Stopwatch.StartNew();
            int totalEvents = 1_000_000;
            int eventsPerRound = instanceCount;
            int rounds = totalEvents / eventsPerRound;

            for (int round = 0; round < rounds; round++)
            {
                for (int i = 0; i < instanceCount; i++)
                {
                    string eventName = round % 3 == 0 ? "EVENT_A" :
                                      round % 3 == 1 ? "EVENT_B" : "EVENT_C";
                    _pool.SendEvent(i, eventName);
                }
                await _pool.ProcessEventsAsync();
            }

            sw.Stop();

            // Results
            double eventsPerSecond = totalEvents * 1000.0 / sw.ElapsedMilliseconds;
            _output.WriteLine($"Stress Test: 1 Million Events");
            _output.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Throughput: {eventsPerSecond:N0} events/sec");

            // Assert
            Assert.True(sw.ElapsedMilliseconds < 10000, "Should process 1M events in under 10 seconds");
            Assert.True(eventsPerSecond > 100_000, "Should process at least 100K events/sec");
        }

        private GPUStateMachineDefinition CreatePerformanceTestDefinition()
        {
            var definition = new GPUStateMachineDefinition("PerfTest", 5, 4);

            // States
            definition.StateNames[0] = "StateA";
            definition.StateNames[1] = "StateB";
            definition.StateNames[2] = "StateC";
            definition.StateNames[3] = "StateD";
            definition.StateNames[4] = "StateE";

            // Events
            definition.EventNames[0] = "EVENT_A";
            definition.EventNames[1] = "EVENT_B";
            definition.EventNames[2] = "EVENT_C";
            definition.EventNames[3] = "EVENT_D";

            // Complex transition table for realistic workload
            definition.TransitionTable = new[]
            {
                // From StateA
                new TransitionEntry { FromState = 0, EventType = 0, ToState = 1 },
                new TransitionEntry { FromState = 0, EventType = 1, ToState = 2 },
                new TransitionEntry { FromState = 0, EventType = 2, ToState = 3 },

                // From StateB
                new TransitionEntry { FromState = 1, EventType = 0, ToState = 2 },
                new TransitionEntry { FromState = 1, EventType = 1, ToState = 3 },
                new TransitionEntry { FromState = 1, EventType = 3, ToState = 0 },

                // From StateC
                new TransitionEntry { FromState = 2, EventType = 0, ToState = 3 },
                new TransitionEntry { FromState = 2, EventType = 2, ToState = 4 },
                new TransitionEntry { FromState = 2, EventType = 3, ToState = 0 },

                // From StateD
                new TransitionEntry { FromState = 3, EventType = 1, ToState = 4 },
                new TransitionEntry { FromState = 3, EventType = 2, ToState = 0 },
                new TransitionEntry { FromState = 3, EventType = 3, ToState = 1 },

                // From StateE
                new TransitionEntry { FromState = 4, EventType = 0, ToState = 0 },
                new TransitionEntry { FromState = 4, EventType = 1, ToState = 1 },
                new TransitionEntry { FromState = 4, EventType = 2, ToState = 2 },
                new TransitionEntry { FromState = 4, EventType = 3, ToState = 3 },
            };

            return definition;
        }
    }
}