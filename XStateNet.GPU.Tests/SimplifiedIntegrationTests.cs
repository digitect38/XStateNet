using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using XStateNet.GPU;
using XStateNet.GPU.Core;

namespace XStateNet.GPU.Tests
{
    /// <summary>
    /// Simplified integration tests that verify GPU functionality
    /// without depending on the full XStateNet API
    /// </summary>
    public class SimplifiedIntegrationTests : IDisposable
    {
        private GPUStateMachinePool _pool;

        public SimplifiedIntegrationTests()
        {
            _pool = new GPUStateMachinePool();
        }

        public void Dispose()
        {
            _pool?.Dispose();
        }

        [Fact]
        public async Task GPUPool_ProcessesTrafficLightStateMachine()
        {
            // Arrange - Traffic light state machine
            var definition = new GPUStateMachineDefinition("TrafficLight", 3, 1);

            // States
            definition.StateNames[0] = "red";
            definition.StateNames[1] = "yellow";
            definition.StateNames[2] = "green";

            // Events
            definition.EventNames[0] = "TIMER";

            // Transitions (cyclic: red -> yellow -> green -> red)
            definition.TransitionTable = new[]
            {
                new TransitionEntry { FromState = 0, EventType = 0, ToState = 1 }, // red -> yellow
                new TransitionEntry { FromState = 1, EventType = 0, ToState = 2 }, // yellow -> green
                new TransitionEntry { FromState = 2, EventType = 0, ToState = 0 }, // green -> red
            };

            // Act
            await _pool.InitializeAsync(100, definition);

            // All start in red (state 0)
            Assert.Equal("red", _pool.GetState(0));

            // Send TIMER to all
            for (int i = 0; i < 100; i++)
            {
                _pool.SendEvent(i, "TIMER");
            }
            await _pool.ProcessEventsAsync();

            // All should be yellow
            Assert.Equal("yellow", _pool.GetState(0));

            // Send TIMER again
            for (int i = 0; i < 100; i++)
            {
                _pool.SendEvent(i, "TIMER");
            }
            await _pool.ProcessEventsAsync();

            // All should be green
            Assert.Equal("green", _pool.GetState(0));

            // Send TIMER once more to complete cycle
            for (int i = 0; i < 100; i++)
            {
                _pool.SendEvent(i, "TIMER");
            }
            await _pool.ProcessEventsAsync();

            // Back to red
            Assert.Equal("red", _pool.GetState(0));
        }

        [Fact]
        public async Task GPUPool_HandlesE40ProcessJobWorkflow()
        {
            // Arrange - Simplified E40 Process Job state machine
            var definition = new GPUStateMachineDefinition("E40ProcessJob", 7, 6);

            // States
            definition.StateNames[0] = "NoState";
            definition.StateNames[1] = "Queued";
            definition.StateNames[2] = "SettingUp";
            definition.StateNames[3] = "Processing";
            definition.StateNames[4] = "ProcessingComplete";
            definition.StateNames[5] = "Aborting";
            definition.StateNames[6] = "ProcessingError";

            // Events
            definition.EventNames[0] = "CREATE";
            definition.EventNames[1] = "SETUP";
            definition.EventNames[2] = "START";
            definition.EventNames[3] = "COMPLETE";
            definition.EventNames[4] = "ABORT";
            definition.EventNames[5] = "ERROR";

            // Transitions
            definition.TransitionTable = new[]
            {
                // From NoState
                new TransitionEntry { FromState = 0, EventType = 0, ToState = 1 }, // CREATE -> Queued

                // From Queued
                new TransitionEntry { FromState = 1, EventType = 1, ToState = 2 }, // SETUP -> SettingUp
                new TransitionEntry { FromState = 1, EventType = 4, ToState = 5 }, // ABORT -> Aborting

                // From SettingUp
                new TransitionEntry { FromState = 2, EventType = 2, ToState = 3 }, // START -> Processing
                new TransitionEntry { FromState = 2, EventType = 4, ToState = 5 }, // ABORT -> Aborting

                // From Processing
                new TransitionEntry { FromState = 3, EventType = 3, ToState = 4 }, // COMPLETE -> ProcessingComplete
                new TransitionEntry { FromState = 3, EventType = 5, ToState = 6 }, // ERROR -> ProcessingError
                new TransitionEntry { FromState = 3, EventType = 4, ToState = 5 }, // ABORT -> Aborting
            };

            // Act & Assert
            await _pool.InitializeAsync(1000, definition);

            // Create jobs
            var instanceIds = Enumerable.Range(0, 1000).ToArray();
            var eventNames = Enumerable.Repeat("CREATE", 1000).ToArray();
            await _pool.SendEventBatchAsync(instanceIds, eventNames);

            var distribution = await _pool.GetStateDistributionAsync();
            Assert.Equal(1000, distribution["Queued"]);

            // Setup jobs
            eventNames = Enumerable.Repeat("SETUP", 1000).ToArray();
            await _pool.SendEventBatchAsync(instanceIds, eventNames);

            distribution = await _pool.GetStateDistributionAsync();
            Assert.Equal(1000, distribution["SettingUp"]);

            // Start processing
            eventNames = Enumerable.Repeat("START", 1000).ToArray();
            await _pool.SendEventBatchAsync(instanceIds, eventNames);

            distribution = await _pool.GetStateDistributionAsync();
            Assert.Equal(1000, distribution["Processing"]);

            // Mixed outcomes: 50% complete, 30% error, 20% abort
            for (int i = 0; i < 500; i++)
            {
                _pool.SendEvent(i, "COMPLETE");
            }
            for (int i = 500; i < 800; i++)
            {
                _pool.SendEvent(i, "ERROR");
            }
            for (int i = 800; i < 1000; i++)
            {
                _pool.SendEvent(i, "ABORT");
            }
            await _pool.ProcessEventsAsync();

            distribution = await _pool.GetStateDistributionAsync();
            Assert.Equal(500, distribution["ProcessingComplete"]);
            Assert.Equal(300, distribution["ProcessingError"]);
            Assert.Equal(200, distribution["Aborting"]);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(10000)]
        public async Task GPUPool_ScalesWithDifferentInstanceCounts(int instanceCount)
        {
            // Arrange
            var definition = CreateSimpleTestDefinition();

            // Act
            await _pool.InitializeAsync(instanceCount, definition);

            // Send events to all instances
            var instanceIds = Enumerable.Range(0, instanceCount).ToArray();
            var eventNames = Enumerable.Repeat("EVENT_A", instanceCount).ToArray();
            await _pool.SendEventBatchAsync(instanceIds, eventNames);

            // Assert
            var distribution = await _pool.GetStateDistributionAsync();
            Assert.Equal(instanceCount, distribution["StateB"]);

            // Performance should scale
            var metrics = _pool.GetMetrics();
            Assert.Equal(instanceCount, metrics.InstanceCount);
        }

        [Fact]
        public async Task GPUPool_MeasuresPerformance()
        {
            // Arrange
            var definition = CreateSimpleTestDefinition();
            await _pool.InitializeAsync(5000, definition);

            // Act - Send many events
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int round = 0; round < 10; round++)
            {
                for (int i = 0; i < 5000; i++)
                {
                    _pool.SendEvent(i, round % 2 == 0 ? "EVENT_A" : "EVENT_B");
                }
                await _pool.ProcessEventsAsync();
            }
            sw.Stop();

            // Assert
            var totalEvents = 5000 * 10;
            var eventsPerSecond = totalEvents * 1000.0 / sw.ElapsedMilliseconds;

            // Should process at least 10,000 events per second
            Assert.True(eventsPerSecond > 10_000,
                $"Performance too low: {eventsPerSecond:N0} events/sec");

            var metrics = _pool.GetMetrics();
            Assert.NotNull(metrics.AcceleratorType);
            Assert.True(metrics.MemoryUsed > 0);
        }

        private GPUStateMachineDefinition CreateSimpleTestDefinition()
        {
            var definition = new GPUStateMachineDefinition("TestMachine", 3, 2);

            // States
            definition.StateNames[0] = "StateA";
            definition.StateNames[1] = "StateB";
            definition.StateNames[2] = "StateC";

            // Events
            definition.EventNames[0] = "EVENT_A";
            definition.EventNames[1] = "EVENT_B";

            // Transitions
            definition.TransitionTable = new[]
            {
                new TransitionEntry { FromState = 0, EventType = 0, ToState = 1 }, // A + EVENT_A -> B
                new TransitionEntry { FromState = 0, EventType = 1, ToState = 2 }, // A + EVENT_B -> C
                new TransitionEntry { FromState = 1, EventType = 1, ToState = 2 }, // B + EVENT_B -> C
                new TransitionEntry { FromState = 2, EventType = 0, ToState = 0 }, // C + EVENT_A -> A
            };

            return definition;
        }
    }
}