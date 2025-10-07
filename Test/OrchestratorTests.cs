using System.Diagnostics;
using XStateNet.Helpers;
using XStateNet.Orchestration;
using Xunit;
using Xunit.Abstractions;

namespace Test
{
    public class OrchestratorTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly EventBusOrchestrator _orchestrator;
        private readonly List<IPureStateMachine> _machines = new();

        public OrchestratorTests(ITestOutputHelper output)
        {
            _output = output;
            var config = new OrchestratorConfig
            {
                PoolSize = 4,
                EnableLogging = true
            };
            _orchestrator = new EventBusOrchestrator(config);
        }

        public void Dispose()
        {
            foreach (var machine in _machines)
            {
                machine.Stop();
            }
            _orchestrator?.Dispose();
        }

        #region Basic Functionality Tests

        [Fact]
        public async Task Orchestrator_BasicSend_Success()
        {
            // Arrange
            var machine = PureStateMachineFactory.CreateSimple("m1");
            _machines.Add(machine);
            _orchestrator.RegisterMachine("m1", (machine as PureStateMachineAdapter)?.GetUnderlying()!);
            await machine.StartAsync();

            // Act
            var result = await _orchestrator.SendEventAsync("external", "m1", "START");

            // Assert
            Assert.True(result.Success);
            Assert.Contains("running", result.NewState);
            _output.WriteLine($"✓ Basic send successful: {result.NewState}");
        }

        [Fact]
        public async Task Orchestrator_SelfSend_Success()
        {
            // Arrange
            var receivedSelfSend = false;
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["processSelfSend"] = (ctx) =>
                {
                    receivedSelfSend = true;
                    ctx.RequestSelfSend("SELF_COMPLETE");
                }
            };

            var json = @"{
                id: 'self',
                initial: 'idle',
                states: {
                    idle: {
                        on: { START: 'processing' }
                    },
                    processing: {
                        entry: ['processSelfSend'],
                        on: { SELF_COMPLETE: 'done' }
                    },
                    done: {}
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "self",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null,
                services: null,
                delays: null,
                activities: null);
            _machines.Add(machine);
            _orchestrator.RegisterMachine("self", (machine as PureStateMachineAdapter)?.GetUnderlying());
            await machine.StartAsync();

            // Act
            var result = await _orchestrator.SendEventAsync("external", "self", "START");

            // Wait for self-send to process
            await DeterministicWait.WaitForConditionAsync(
                condition: () => receivedSelfSend,
                getProgress: () => receivedSelfSend ? 1 : 0,
                timeoutSeconds: 2);

            // Assert
            Assert.True(result.Success);
            Assert.True(receivedSelfSend);
            _output.WriteLine("✓ Self-send handled correctly through orchestrator");
        }

        [Fact]
        public async Task Orchestrator_NonExistentTarget_ReturnsError()
        {
            // Act
            var result = await _orchestrator.SendEventAsync("sender", "nonexistent", "EVENT");

            // Assert
            Assert.False(result.Success);
            Assert.Contains("not registered", result.ErrorMessage);
            _output.WriteLine($"✓ Correctly handled non-existent target: {result.ErrorMessage}");
        }

        #endregion

        #region No Deadlock Tests

        [Fact]
        public async Task Orchestrator_SimultaneousBidirectional_NoDeadlock()
        {
            // Arrange - Create two machines that ping-pong to each other
            var m1Actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["sendPong"] = (ctx) => ctx.RequestSend("m2", "PONG")
            };

            var m2Actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["sendPong"] = (ctx) => ctx.RequestSend("m1", "PONG")
            };

            var machineJson = @"{
                id: 'machine',
                initial: 'idle',
                states: {
                    idle: {
                        on: { PING: 'responding' }
                    },
                    responding: {
                        entry: ['sendPong'],
                        on: { PONG: 'done' }
                    },
                    done: {}
                }
            }";

            var m1 = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "m1",
                json: machineJson,
                orchestrator: _orchestrator,
                orchestratedActions: m1Actions,
                guards: null,
                services: null,
                delays: null,
                activities: null);
            var m2 = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "m2",
                json: machineJson,
                orchestrator: _orchestrator,
                orchestratedActions: m2Actions,
                guards: null,
                services: null,
                delays: null,
                activities: null);

            _machines.Add(m1);
            _machines.Add(m2);

            _orchestrator.RegisterMachine("m1", (m1 as PureStateMachineAdapter)?.GetUnderlying());
            _orchestrator.RegisterMachine("m2", (m2 as PureStateMachineAdapter)?.GetUnderlying());

            await m1.StartAsync();
            await m2.StartAsync();

            // Act - Both machines send PING to each other simultaneously
            var task1 = _orchestrator.SendEventAsync("m1", "m2", "PING");
            var task2 = _orchestrator.SendEventAsync("m2", "m1", "PING");

            var results = await Task.WhenAll(task1, task2);

            // Assert
            Assert.All(results, r => Assert.True(r.Success));
            _output.WriteLine("✓ Bidirectional communication succeeded without deadlock");
            _output.WriteLine("✓ Orchestrator architecture prevents deadlocks by design!");
        }

        [Fact]
        public async Task Orchestrator_CircularChain_NoDeadlock()
        {
            // Arrange - Create a circular chain: A -> B -> C -> A
            var createChainActions = (string next) => new Dictionary<string, Action<OrchestratedContext>>
            {
                ["forward"] = (ctx) => ctx.RequestSend(next, "CONTINUE")
            };

            var chainJson = @"{
                id: 'chain',
                initial: 'ready',
                states: {
                    ready: {
                        on: { START: 'forwarding' }
                    },
                    forwarding: {
                        entry: ['forward'],
                        on: { CONTINUE: 'done' }
                    },
                    done: {}
                }
            }";

            var machineA = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "A",
                json: chainJson,
                orchestrator: _orchestrator,
                orchestratedActions: createChainActions("B"),
                guards: null,
                services: null,
                delays: null,
                activities: null);
            var machineB = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "B",
                json: chainJson,
                orchestrator: _orchestrator,
                orchestratedActions: createChainActions("C"),
                guards: null,
                services: null,
                delays: null,
                activities: null);
            var machineC = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "C",
                json: chainJson,
                orchestrator: _orchestrator,
                orchestratedActions: createChainActions("A"),
                guards: null,
                services: null,
                delays: null,
                activities: null);

            _machines.AddRange(new[] { machineA, machineB, machineC });

            _orchestrator.RegisterMachine("A", (machineA as PureStateMachineAdapter)?.GetUnderlying());
            _orchestrator.RegisterMachine("B", (machineB as PureStateMachineAdapter)?.GetUnderlying());
            _orchestrator.RegisterMachine("C", (machineC as PureStateMachineAdapter)?.GetUnderlying());

            await Task.WhenAll(machineA.StartAsync(), machineB.StartAsync(), machineC.StartAsync());

            // Act - Start the chain from all three points simultaneously
            var tasks = new[]
            {
                _orchestrator.SendEventAsync("external", "A", "START"),
                _orchestrator.SendEventAsync("external", "B", "START"),
                _orchestrator.SendEventAsync("external", "C", "START")
            };

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.All(results, r => Assert.True(r.Success));
            _output.WriteLine("✓ Circular chain processed without deadlock");
        }

        #endregion

        #region Load Distribution Tests

        [Fact]
        public async Task Orchestrator_EventBusPool_DistributesLoad()
        {
            // Arrange - Create 10 machines
            for (int i = 0; i < 10; i++)
            {
                var machine = PureStateMachineFactory.CreateSimple($"machine{i}");
                _machines.Add(machine);
                _orchestrator.RegisterMachine($"machine{i}", (machine as PureStateMachineAdapter)?.GetUnderlying());
                await machine.StartAsync();
            }

            // Act - Send events to all machines
            var tasks = new List<Task<EventResult>>();
            for (int i = 0; i < 10; i++)
            {
                for (int j = 0; j < 10; j++)
                {
                    tasks.Add(_orchestrator.SendEventAsync("external", $"machine{i}", "START"));
                }
            }

            await Task.WhenAll(tasks);

            // Get stats
            var stats = _orchestrator.GetStats();

            // Assert
            Assert.Equal(10, stats.RegisteredMachines);
            Assert.True(stats.EventBusStats.Count > 0);

            // Check that events were distributed across buses
            var busesWithWork = stats.EventBusStats.Count(bus => bus.TotalProcessed > 0);
            Assert.True(busesWithWork > 1, "Events should be distributed across multiple buses");

            _output.WriteLine($"✓ Load distributed across {busesWithWork} event buses");
            foreach (var bus in stats.EventBusStats)
            {
                _output.WriteLine($"   Bus {bus.BusIndex}: {bus.TotalProcessed} events processed");
            }
        }

        [Fact]
        public async Task Orchestrator_HighThroughput_Performance()
        {
            // Arrange
            var machine = PureStateMachineFactory.CreateSimple("perf");
            _machines.Add(machine);
            _orchestrator.RegisterMachine("perf", (machine as PureStateMachineAdapter)?.GetUnderlying());
            await machine.StartAsync();

            const int eventCount = 1000;
            var stopwatch = Stopwatch.StartNew();

            // Act
            var tasks = Enumerable.Range(0, eventCount)
                .Select(i => _orchestrator.SendEventAsync($"sender{i}", "perf", "START"))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            Assert.All(results, r => Assert.True(r.Success));

            var throughput = eventCount * 1000.0 / stopwatch.ElapsedMilliseconds;
            _output.WriteLine($"✓ Processed {eventCount} events in {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"✓ Throughput: {throughput:F2} events/sec");

            Assert.True(throughput > 100, "Should process at least 100 events/sec");
        }

        #endregion

        #region Complex Scenarios

        [Fact]
        public async Task Orchestrator_ComplexWorkflow_Success()
        {
            // Arrange - Create a workflow: order -> payment -> shipping -> complete
            var workflowSteps = 0;
            var workflowActions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["processOrder"] = (ctx) =>
                {
                    _output.WriteLine("   Processing order...");
                    Interlocked.Increment(ref workflowSteps);
                    ctx.RequestSend("payment", "CHARGE");
                },
                ["processPayment"] = (ctx) =>
                {
                    _output.WriteLine("   Processing payment...");
                    Interlocked.Increment(ref workflowSteps);
                    ctx.RequestSend("shipping", "SHIP");
                },
                ["processShipping"] = (ctx) =>
                {
                    _output.WriteLine("   Processing shipping...");
                    Interlocked.Increment(ref workflowSteps);
                    ctx.RequestSend("order", "COMPLETE");
                }
            };

            var orderJson = @"{
                id: 'order',
                initial: 'pending',
                states: {
                    pending: {
                        on: { PROCESS: 'processing' }
                    },
                    processing: {
                        entry: ['processOrder'],
                        on: { COMPLETE: 'completed' }
                    },
                    completed: {}
                }
            }";

            var paymentJson = @"{
                id: 'payment',
                initial: 'ready',
                states: {
                    ready: {
                        on: { CHARGE: 'charging' }
                    },
                    charging: {
                        entry: ['processPayment'],
                        on: { DONE: 'charged' }
                    },
                    charged: {}
                }
            }";

            var shippingJson = @"{
                id: 'shipping',
                initial: 'ready',
                states: {
                    ready: {
                        on: { SHIP: 'shipping' }
                    },
                    shipping: {
                        entry: ['processShipping'],
                        on: { DONE: 'shipped' }
                    },
                    shipped: {}
                }
            }";

            var orderMachine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "order",
                json: orderJson,
                orchestrator: _orchestrator,
                orchestratedActions: workflowActions,
                guards: null,
                services: null,
                delays: null,
                activities: null);
            var paymentMachine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "payment",
                json: paymentJson,
                orchestrator: _orchestrator,
                orchestratedActions: workflowActions,
                guards: null,
                services: null,
                delays: null,
                activities: null);
            var shippingMachine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "shipping",
                json: shippingJson,
                orchestrator: _orchestrator,
                orchestratedActions: workflowActions,
                guards: null,
                services: null,
                delays: null,
                activities: null);

            _machines.AddRange(new[] { orderMachine, paymentMachine, shippingMachine });

            _orchestrator.RegisterMachine("order", (orderMachine as PureStateMachineAdapter)?.GetUnderlying());
            _orchestrator.RegisterMachine("payment", (paymentMachine as PureStateMachineAdapter)?.GetUnderlying());
            _orchestrator.RegisterMachine("shipping", (shippingMachine as PureStateMachineAdapter)?.GetUnderlying());

            await Task.WhenAll(
                orderMachine.StartAsync(),
                paymentMachine.StartAsync(),
                shippingMachine.StartAsync());

            // Act
            var result = await _orchestrator.SendEventAsync("customer", "order", "PROCESS");

            // Wait for all 3 workflow steps to complete
            await DeterministicWait.WaitForCountAsync(
                getCount: () => workflowSteps,
                targetValue: 3,
                timeoutSeconds: 3);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(3, workflowSteps);
            _output.WriteLine("✓ Complex workflow executed successfully");
        }

        [Fact]
        public async Task Orchestrator_MassiveSelfSends_NoStackOverflow()
        {
            // Arrange - Machine that sends to itself many times
            var sendCount = 0;
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["increment"] = (ctx) =>
                {
                    if (sendCount < 100)
                    {
                        sendCount++;
                        if (sendCount < 100)
                        {
                            ctx.RequestSelfSend("INCREMENT");
                        }
                    }
                }
            };

            var json = @"{
                id: 'counter',
                initial: 'counting',
                states: {
                    counting: {
                        entry: ['increment'],
                        on: { INCREMENT: 'counting' }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "counter",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null,
                services: null,
                delays: null,
                activities: null);
            _machines.Add(machine);

            // Machine is already registered by factory, use actual machine ID
            var machineId = machine.Id;
            await machine.StartAsync();

            // Act
            var result = await _orchestrator.SendEventAsync("external", machineId, "INCREMENT");

            // Wait for all 100 self-sends to process
            await DeterministicWait.WaitForCountAsync(
                getCount: () => sendCount,
                targetValue: 100,
                timeoutSeconds: 5);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(100, sendCount);
            _output.WriteLine($"✓ Processed {sendCount} self-sends without stack overflow");
            _output.WriteLine("✓ Orchestrator queue-based approach prevents stack overflow");
        }

        #endregion

        #region Timeout Tests

        [Fact]
        public async Task Orchestrator_Timeout_ReturnsError()
        {
            // Arrange - Slow machine
            var slowActions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["slowAction"] = (ctx) =>
                {
                    // Synchronous blocking delay to test timeout
                    Thread.Sleep(500);
                }
            };

            var json = @"{
                id: 'slow',
                initial: 'idle',
                states: {
                    idle: {
                        on: { SLOW: 'processing' }
                    },
                    processing: {
                        entry: ['slowAction'],
                        on: { DONE: 'idle' }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "slow",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: slowActions,
                guards: null,
                services: null,
                delays: null,
                activities: null);
            _machines.Add(machine);
            _orchestrator.RegisterMachine("slow", (machine as PureStateMachineAdapter)?.GetUnderlying());
            await machine.StartAsync();

            // Act
            var result = await _orchestrator.SendEventAsync("external", "slow", "SLOW", null, 100);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("timed out", result.ErrorMessage);
            _output.WriteLine($"✓ Timeout handled correctly: {result.ErrorMessage}");
        }

        #endregion
    }
}
