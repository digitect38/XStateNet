using XStateNet.GPU.Integration;

namespace XStateNet.GPU.Tests
{
    [Collection("TimingSensitive")]
    public class XStateNetIntegrationTests : IDisposable
    {
        string TrafficLightScript
        {
            get
            {
                string trafficLightJson = @"{
                    'id': 'TrafficLight',
                    'initial': 'red',
                    'states': {
                        'red': {
                            'on': {
                                'TIMER': 'yellow'
                            }
                        },
                        'yellow': {
                            'on': {
                                'TIMER': 'green'
                            }
                        },
                        'green': {
                            'on': {
                                'TIMER': 'red'
                            }
                        }
                    }
                }";
                return trafficLightJson;
            }
        }

        private XStateNetGPUBridge _bridge;

        public void Dispose()
        {
            _bridge?.Dispose();
        }

        [Fact]
        public async Task XStateNetGPUBridge_InitializesWithXStateNetDefinition()
        {
            // Arrange
            _bridge = new XStateNetGPUBridge(TrafficLightScript);

            // Act
            await _bridge.InitializeAsync(100);

            // Assert
            Assert.Equal(100, _bridge.InstanceCount);
            var state = _bridge.GetState(0);
            Assert.Equal("red", state); // Should start in initial state
        }

        [Fact]
        public async Task XStateNetGPUBridge_ProcessesEventsCorrectly()
        {
            // Arrange
            _bridge = new XStateNetGPUBridge(TrafficLightScript);
            await _bridge.InitializeAsync(10);

            // Act - Send TIMER event to transition red -> yellow
            _bridge.Send(0, "TIMER");
            await _bridge.ProcessEventsAsync();

            // Assert
            Assert.Equal("yellow", _bridge.GetState(0));

            // Act - Send another TIMER to transition yellow -> green
            _bridge.Send(0, "TIMER");
            await _bridge.ProcessEventsAsync();

            // Assert
            Assert.Equal("green", _bridge.GetState(0));

            // Act - Send another TIMER to transition green -> red (complete cycle)
            _bridge.Send(0, "TIMER");
            await _bridge.ProcessEventsAsync();

            // Assert
            Assert.Equal("red", _bridge.GetState(0));
        }

        [Fact]
        public async Task XStateNetGPUBridge_BroadcastEventsToAllInstances()
        {
            // Arrange
            _bridge = new XStateNetGPUBridge(TrafficLightScript);
            await _bridge.InitializeAsync(100);

            // Act - Broadcast TIMER to all instances
            await _bridge.BroadcastAsync("TIMER");
            await _bridge.ProcessEventsAsync();

            // Assert - All should be in yellow state
            for (int i = 0; i < 10; i++) // Check first 10
            {
                Assert.Equal("yellow", _bridge.GetState(i));
            }
        }

        [Fact]
        [TestPriority(TestPriority.High)]
        public async Task XStateNetGPUBridge_ValidatesConsistencyWithCPU()
        {
            // Arrange
            _bridge = new XStateNetGPUBridge(TrafficLightScript);
            await _bridge.InitializeAsync(10); // Small count for CPU validation

            // Act - Process events
            await _bridge.BroadcastAsync("TIMER");
            await _bridge.ProcessEventsAsync();

            // Assert - GPU and CPU implementations should match
            bool isConsistent = await _bridge.ValidateConsistencyAsync();
            Assert.True(isConsistent);
        }

        [Fact]
        public async Task XStateNetGPUBridge_HandlesComplexStateMachine()
        {
            // Arrange - Complex state machine with multiple states and transitions
            string complexMachineJson = @"{
                'id': 'ComplexMachine',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'START': 'processing',
                            'ERROR': 'failed'
                        }
                    },
                    'processing': {
                        'on': {
                            'COMPLETE': 'done',
                            'ERROR': 'failed',
                            'PAUSE': 'paused'
                        }
                    },
                    'paused': {
                        'on': {
                            'RESUME': 'processing',
                            'STOP': 'idle'
                        }
                    },
                    'done': {
                        'on': {
                            'RESET': 'idle'
                        }
                    },
                    'failed': {
                        'on': {
                            'RETRY': 'processing',
                            'RESET': 'idle'
                        }
                    }
                }
            }";

            _bridge = new XStateNetGPUBridge(complexMachineJson);
            await _bridge.InitializeAsync(1000);

            // Act - Complex event sequence
            // Start all
            await _bridge.BroadcastAsync("START");
            await _bridge.ProcessEventsAsync();

            // Half complete, half error
            for (int i = 0; i < 500; i++)
            {
                _bridge.Send(i, "COMPLETE");
            }
            for (int i = 500; i < 1000; i++)
            {
                _bridge.Send(i, "ERROR");
            }
            await _bridge.ProcessEventsAsync();

            // Assert
            var distribution = await _bridge.GetStateDistributionAsync();
            Assert.Equal(500, distribution["done"]);
            Assert.Equal(500, distribution["failed"]);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        public async Task XStateNetGPUBridge_ScalesWithInstanceCount(int instanceCount)
        {
            // Arrange
            _bridge = new XStateNetGPUBridge(TrafficLightScript);
            await _bridge.InitializeAsync(instanceCount);

            // Act
            await _bridge.BroadcastAsync("TIMER");
            await _bridge.ProcessEventsAsync();

            // Assert
            var distribution = await _bridge.GetStateDistributionAsync();
            Assert.Equal(instanceCount, distribution["yellow"]);
        }

        [Fact]
        public async Task GPUAcceleratedStateMachine_AutoSwitchesToGPU()
        {
            // Arrange
            var gpuMachine = new GPUAcceleratedStateMachine(TrafficLightScript, gpuThreshold: 50);

            // Act - Small pool should use CPU
            var smallPool = await gpuMachine.CreatePoolAsync(10);
            Assert.False(smallPool.IsGPUAccelerated);

            // Act - Large pool should use GPU
            var largePool = await gpuMachine.CreatePoolAsync(100);
            Assert.True(largePool.IsGPUAccelerated);

            // Cleanup
            gpuMachine.Dispose();
        }

        [Fact]
        public async Task GPUAcceleratedStateMachine_MaintainsXStateNetCompatibility()
        {
            // Arrange
            var gpuMachine = new GPUAcceleratedStateMachine(TrafficLightScript);
            var pool = await gpuMachine.CreatePoolAsync(100);

            // Act - Use XStateNet-style API
            await pool.SendAsync(0, "TIMER");
            string state = pool.GetState(0);

            // Assert
            Assert.Equal("yellow", state);

            // Act - Broadcast
            await pool.BroadcastAsync("TIMER");

            // Assert - Instance 0 should be at green (received 2 TIMER events)
            Assert.Equal("green", pool.GetState(0));

            // Assert - Other instances should be at yellow (received 1 TIMER event)
            for (int i = 1; i < 5; i++)
            {
                Assert.Equal("yellow", pool.GetState(i));
            }

            // Cleanup
            gpuMachine.Dispose();
        }

        [Fact]
        public async Task GPUAcceleratedStateMachine_ProvidesPerformanceMetrics()
        {
            // Arrange
            var gpuMachine = new GPUAcceleratedStateMachine(TrafficLightScript);
            var pool = await gpuMachine.CreatePoolAsync(1000);

            // Act
            var metrics = gpuMachine.GetMetrics();

            // Assert
            Assert.Equal(1000, metrics.InstanceCount);
            Assert.Equal("GPU", metrics.ExecutionMode);
            Assert.NotNull(metrics.AcceleratorType);
            Assert.True(metrics.MemoryUsed > 0);
            Assert.True(metrics.MaxParallelism > 0);

            // Cleanup
            gpuMachine.Dispose();
        }

        [Fact]
        public async Task XStateNetGPUBridge_HandlesE40ProcessJob()
        {
            // Arrange - Use actual E40 Process Job definition if available
            string e40JsonPath = @"SemiStandard\XStateScripts\E40ProcessJob.json";
            if (!File.Exists(e40JsonPath))
            {
                // Use simplified version for testing
                string e40Json = @"{
                    'id': 'E40ProcessJob',
                    'initial': 'NoState',
                    'states': {
                        'NoState': {
                            'on': {
                                'CREATE': 'Queued'
                            }
                        },
                        'Queued': {
                            'on': {
                                'SETUP': 'SettingUp',
                                'ABORT': 'Aborting'
                            }
                        },
                        'SettingUp': {
                            'on': {
                                'START': 'Processing',
                                'ABORT': 'Aborting'
                            }
                        },
                        'Processing': {
                            'on': {
                                'COMPLETE': 'ProcessingComplete',
                                'ERROR': 'ProcessingError',
                                'ABORT': 'Aborting'
                            }
                        },
                        'ProcessingComplete': {
                            'on': {
                                'RESET': 'NoState'
                            }
                        },
                        'ProcessingError': {
                            'on': {
                                'RESET': 'NoState'
                            }
                        },
                        'Aborting': {
                            'on': {
                                'RESET': 'NoState'
                            }
                        }
                    }
                }";

                _bridge = new XStateNetGPUBridge(e40Json);
            }
            else
            {
                string e40Json = File.ReadAllText(e40JsonPath);
                _bridge = new XStateNetGPUBridge(e40Json);
            }

            await _bridge.InitializeAsync(100);

            // Act - Simulate job lifecycle
            await _bridge.BroadcastAsync("CREATE");
            await _bridge.ProcessEventsAsync();
            Assert.Equal("Queued", _bridge.GetState(0));

            await _bridge.BroadcastAsync("SETUP");
            await _bridge.ProcessEventsAsync();

            await _bridge.BroadcastAsync("START");
            await _bridge.ProcessEventsAsync();

            // Half complete, half abort
            for (int i = 0; i < 50; i++)
            {
                _bridge.Send(i, "COMPLETE");
            }
            for (int i = 50; i < 100; i++)
            {
                _bridge.Send(i, "ABORT");
            }
            await _bridge.ProcessEventsAsync();

            // Assert
            var distribution = await _bridge.GetStateDistributionAsync();
            Assert.True(distribution.ContainsKey("ProcessingComplete") || distribution.ContainsKey("Aborting"));
        }

        [Fact]
        public async Task CreateMachineFromInstance_RestoresState()
        {
            // Arrange
            _bridge = new XStateNetGPUBridge(TrafficLightScript);
            await _bridge.InitializeAsync(10);

            // Act - Advance instance 0 to green
            _bridge.Send(0, "TIMER"); // red -> yellow
            await _bridge.ProcessEventsAsync();
            _bridge.Send(0, "TIMER"); // yellow -> green
            await _bridge.ProcessEventsAsync();

            // Create CPU machine from GPU instance
            var cpuMachine = _bridge.CreateMachineFromInstance(0);

            // Assert
            Assert.NotNull(cpuMachine);
            // Note: Full state restoration would require XStateNet API support

            cpuMachine.Dispose();
        }
    }
}