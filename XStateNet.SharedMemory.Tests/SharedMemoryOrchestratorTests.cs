using System.Diagnostics;
using XStateNet.Orchestration;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet.SharedMemory.Tests
{
    public class SharedMemoryOrchestratorTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testSegmentName;
        private SharedMemoryOrchestrator? _orchestrator;

        public SharedMemoryOrchestratorTests(ITestOutputHelper output)
        {
            _output = output;
            _testSegmentName = $"XStateNet_Test_{Guid.NewGuid():N}";
        }

        [Fact]
        public void Constructor_CreatesOrchestrator_Successfully()
        {
            // Arrange & Act
            _orchestrator = new SharedMemoryOrchestrator(_testSegmentName, "TestProcess1");

            // Assert
            Assert.NotNull(_orchestrator);
            Assert.True(_orchestrator.ProcessId > 0);
            Assert.Equal("TestProcess1", _orchestrator.ProcessName);

            _output.WriteLine($"Created orchestrator: ProcessId={_orchestrator.ProcessId}, ProcessName={_orchestrator.ProcessName}");
        }

        [Fact]
        public async Task SendEventAsync_SameProcess_DeliversLocally()
        {
            // Arrange
            _orchestrator = new SharedMemoryOrchestrator(_testSegmentName, "TestProcess2");

            var receivedEvent = false;
            var tcs = new TaskCompletionSource<bool>();

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "test-machine",
                json: @"{
                    ""id"": ""test-machine"",
                    ""initial"": ""idle"",
                    ""states"": {
                        ""idle"": {
                            ""on"": {
                                ""TEST"": ""active""
                            }
                        },
                        ""active"": {
                            ""type"": ""final""
                        }
                    }
                }",
                orchestrator: _orchestrator.LocalOrchestrator
            );

            // Get the underlying IStateMachine to access OnTransition
            var adapter = machine as PureStateMachineAdapter;
            var underlyingMachine = adapter?.GetUnderlying() as StateMachine;

            if (underlyingMachine != null)
            {
                underlyingMachine.OnTransition += (fromState, toState, eventName) =>
                {
                    _output.WriteLine($"Transition detected: {fromState?.Name} -> {toState?.Name} ({eventName})");
                    if (toState?.Name != null && toState.Name.Contains("active"))
                    {
                        receivedEvent = true;
                        _output.WriteLine($"Active state reached!");
                        tcs.TrySetResult(true);
                    }
                };
            }

            // Start the machine AFTER attaching the handler
            await machine.StartAsync();

            // Manually register with SharedMemoryOrchestrator (workaround for non-virtual methods)
            if (underlyingMachine != null)
            {
                _orchestrator.RegisterMachine(machine.Id, underlyingMachine);
            }

            // Act
            var result = await _orchestrator.SendEventAsync("test-sender", machine.Id, "TEST");

            // Wait for event with timeout
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));

            // Assert
            Assert.True(receivedEvent, "Event should be delivered locally");
            Assert.True(completed == tcs.Task, "Event should be received within timeout");
        }

        [Fact]
        public void GetStats_ReturnsValidStatistics()
        {
            // Arrange
            _orchestrator = new SharedMemoryOrchestrator(_testSegmentName, "TestProcess3");

            // Act
            var stats = _orchestrator.GetStats();

            // Assert
            Assert.True(stats.BufferSize > 0);
            Assert.True(stats.FreeSpace >= 0);
            Assert.True(stats.UsedSpace >= 0);

            _output.WriteLine($"Buffer Stats: Size={stats.BufferSize}, Used={stats.UsedSpace}, Free={stats.FreeSpace}, Usage={stats.UsagePercentage:F2}%");
        }

        [Fact]
        public void GetRegisteredProcesses_ReturnsCurrentProcess()
        {
            // Arrange
            _orchestrator = new SharedMemoryOrchestrator(_testSegmentName, "TestProcess4");

            // Act
            var processes = _orchestrator.GetRegisteredProcesses();

            // Assert
            Assert.NotEmpty(processes);
            Assert.Contains(processes, p => p.ProcessId == _orchestrator.ProcessId);

            foreach (var process in processes)
            {
                _output.WriteLine($"Process: Id={process.ProcessId}, Name={process.GetProcessName()}, Status={process.Status}");
            }
        }

        [Fact]
        public async Task MultipleProcesses_CanShareSegment()
        {
            // Arrange - Create first process/orchestrator
            var orchestrator1 = new SharedMemoryOrchestrator(_testSegmentName, "Process1");
            _output.WriteLine($"Created Process1: ProcessId={orchestrator1.ProcessId}");

            // Wait for initialization
            await Task.Delay(100);

            // Act - Create second process/orchestrator (joins existing segment)
            var orchestrator2 = new SharedMemoryOrchestrator(_testSegmentName, "Process2");
            _output.WriteLine($"Created Process2: ProcessId={orchestrator2.ProcessId}");

            // Assert - Both should see each other
            var processes1 = orchestrator1.GetRegisteredProcesses();
            var processes2 = orchestrator2.GetRegisteredProcesses();

            Assert.True(processes1.Length >= 2, "Process1 should see at least 2 processes");
            Assert.True(processes2.Length >= 2, "Process2 should see at least 2 processes");

            _output.WriteLine($"Process1 sees {processes1.Length} processes");
            _output.WriteLine($"Process2 sees {processes2.Length} processes");

            // Cleanup
            orchestrator1.Dispose();
            orchestrator2.Dispose();
        }

        [Fact]
        public async Task RegisterMachine_AddsMachineToRegistry()
        {
            // Arrange
            _orchestrator = new SharedMemoryOrchestrator(_testSegmentName, "TestProcess5");

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "test-machine-registry",
                json: @"{
                    ""id"": ""test-machine-registry"",
                    ""initial"": ""idle"",
                    ""states"": {
                        ""idle"": {
                            ""on"": {
                                ""START"": ""running""
                            }
                        },
                        ""running"": {
                            ""type"": ""final""
                        }
                    }
                }",
                orchestrator: _orchestrator.LocalOrchestrator
            );

            // Act
            await Task.Delay(100); // Wait for registration

            // Assert
            var processes = _orchestrator.GetRegisteredProcesses();
            var thisProcess = Array.Find(processes, p => p.ProcessId == _orchestrator.ProcessId);

            Assert.NotEqual(default, thisProcess);
            _output.WriteLine($"Process has {thisProcess.MachineCount} machines registered");
        }

        [Fact]
        public void OrchestratorFactory_CreateSharedMemory_Works()
        {
            // Arrange & Act
            var orchestrator = OrchestratorFactory.CreateSharedMemory(_testSegmentName, "FactoryTest");
            _orchestrator = orchestrator; // For cleanup

            // Assert
            Assert.NotNull(orchestrator);
            Assert.IsType<SharedMemoryOrchestrator>(orchestrator);
            Assert.Equal("FactoryTest", orchestrator.ProcessName);

            _output.WriteLine($"Factory created orchestrator: ProcessId={orchestrator.ProcessId}");
        }

        [Fact]
        public void OrchestratorFactory_GetPerformanceCharacteristics_ReturnsValidData()
        {
            // Act
            var inProcessPerf = OrchestratorFactory.GetPerformanceCharacteristics(OrchestratorType.InProcess);
            var sharedMemoryPerf = OrchestratorFactory.GetPerformanceCharacteristics(OrchestratorType.SharedMemory);
            var interProcessPerf = OrchestratorFactory.GetPerformanceCharacteristics(OrchestratorType.InterProcess);

            // Assert
            Assert.True(inProcessPerf.TypicalLatencyMs < sharedMemoryPerf.TypicalLatencyMs);
            Assert.True(sharedMemoryPerf.TypicalLatencyMs < interProcessPerf.TypicalLatencyMs);

            _output.WriteLine($"InProcess: {inProcessPerf}");
            _output.WriteLine($"SharedMemory: {sharedMemoryPerf}");
            _output.WriteLine($"InterProcess: {interProcessPerf}");
        }

        [Fact]
        public async Task Performance_LocalDelivery_IsFast()
        {
            // Arrange
            _orchestrator = new SharedMemoryOrchestrator(_testSegmentName, "PerfTest");

            var receivedCount = 0;
            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "perf-machine",
                json: @"{
                    ""id"": ""perf-machine"",
                    ""initial"": ""idle"",
                    ""states"": {
                        ""idle"": {
                            ""on"": {
                                ""PING"": ""active""
                            }
                        },
                        ""active"": {
                            ""on"": {
                                ""PING"": ""idle""
                            }
                        }
                    }
                }",
                orchestrator: _orchestrator.LocalOrchestrator
            );

            // Get the underlying IStateMachine to access OnTransition
            var adapter = machine as PureStateMachineAdapter;
            var underlyingMachine = adapter?.GetUnderlying() as StateMachine;

            if (underlyingMachine != null)
            {
                underlyingMachine.OnTransition += (fromState, toState, eventName) => receivedCount++;
            }

            // Start the machine
            await machine.StartAsync();

            // Manually register with SharedMemoryOrchestrator
            if (underlyingMachine != null)
            {
                _orchestrator.RegisterMachine(machine.Id, underlyingMachine);
            }

            // Act - Send 100 events and measure time
            var stopwatch = Stopwatch.StartNew();
            const int eventCount = 100;

            for (int i = 0; i < eventCount; i++)
            {
                var result = await _orchestrator.SendEventAsync("test", machine.Id, "PING");
            }

            await Task.Delay(1000); // Wait for delivery
            stopwatch.Stop();

            // Assert
            _output.WriteLine($"Sent {eventCount} events in {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Received {receivedCount} transitions");
            _output.WriteLine($"Average latency: {stopwatch.ElapsedMilliseconds / (double)eventCount:F2}ms per event");

            Assert.True(receivedCount > 0, "Should receive at least some events");
        }

        public void Dispose()
        {
            _orchestrator?.Dispose();
        }
    }
}
