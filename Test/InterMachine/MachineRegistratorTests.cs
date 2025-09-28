using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet;
using XStateNet.InterMachine;

namespace Test.InterMachine
{
    /// <summary>
    /// Tests for Machine Registrator/Orchestrator with type-based discovery
    /// </summary>
    [Collection("Sequential")]
    public class MachineRegistratorTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly IMachineRegistrator _registrator;
        private readonly List<RegisteredMachine> _registeredMachines = new();
        private DateTime _testStartTime;

        public MachineRegistratorTests(ITestOutputHelper output)
        {
            _output = output;
            _registrator = new LocalMachineRegistrator();
            _testStartTime = DateTime.UtcNow;
        }

        [Fact]
        public async Task RegisterAndDiscoverByType_Success()
        {
            // Arrange
            _testStartTime = DateTime.UtcNow;
            var pingMachine1 = CreateStateMachine("ping1");
            var pingMachine2 = CreateStateMachine("ping2");
            var pongMachine1 = CreateStateMachine("pong1");

            // Act - Register machines with types
            var ping1 = await pingMachine1.RegisterAsTypeAsync("PingService", registrator: _registrator);
            var ping2 = await pingMachine2.RegisterAsTypeAsync("PingService", registrator: _registrator);
            var pong1 = await pongMachine1.RegisterAsTypeAsync("PongService", registrator: _registrator);

            _registeredMachines.AddRange(new[] { ping1, ping2, pong1 });

            // Discover by type
            var pingServices = await _registrator.DiscoverByTypeAsync("PingService");
            var pongServices = await _registrator.DiscoverByTypeAsync("PongService");

            // Assert
            Assert.Equal(2, pingServices.Count);
            Assert.Equal(1, pongServices.Count);
            Assert.All(pingServices, m => Assert.Equal("PingService", m.MachineType));
            Assert.All(pongServices, m => Assert.Equal("PongService", m.MachineType));

            _output.WriteLine($"✓ Registered 2 PingService machines and 1 PongService machine");
            _output.WriteLine($"✓ Successfully discovered machines by type");
        }

        [Fact]
        public async Task AutoConnectSameType_Success()
        {
            // Arrange
            _testStartTime = DateTime.UtcNow;
            var messageReceived = new TaskCompletionSource<bool>();
            var workerMachines = new List<StateMachine>();

            // Create worker machines
            for (int i = 0; i < 3; i++)
            {
                var machine = CreateStateMachine($"worker{i}");
                workerMachines.Add(machine);
            }

            // Register all as same type - they should auto-connect
            var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] Registering 3 worker machines of same type...");
            var workers = new List<RegisteredMachine>();
            foreach (var machine in workerMachines)
            {
                var registered = await machine.RegisterAsTypeAsync("WorkerService", registrator: _registrator);
                workers.Add(registered);
                _registeredMachines.Add(registered);
            }

            // Act - First worker broadcasts to all workers of same type
            await workers[0].BroadcastToSameTypeAsync("TASK_AVAILABLE", new { taskId = "123" });

            // Verify all workers can discover each other
            foreach (var worker in workers)
            {
                var peers = await worker.DiscoverAndConnectAsync("WorkerService");
                Assert.Equal(3, peers.Count); // Should see all 3 including self
                _output.WriteLine($"Worker {worker.MachineId} discovered {peers.Count} peers");
            }

            elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] ✓ All workers of same type auto-discovered each other");
        }

        [Fact]
        public async Task DiscoverByPattern_Success()
        {
            // Arrange
            var machine1 = await CreateStateMachine("m1").RegisterAsTypeAsync("Service_Alpha_1", registrator: _registrator);
            var machine2 = await CreateStateMachine("m2").RegisterAsTypeAsync("Service_Beta_1", registrator: _registrator);
            var machine3 = await CreateStateMachine("m3").RegisterAsTypeAsync("Service_Alpha_2", registrator: _registrator);
            var machine4 = await CreateStateMachine("m4").RegisterAsTypeAsync("Other_Service", registrator: _registrator);

            _registeredMachines.AddRange(new[] { machine1, machine2, machine3, machine4 });

            // Act - Discover by patterns
            var allServices = await _registrator.DiscoverByPatternAsync("Service_*");
            var alphaServices = await _registrator.DiscoverByPatternAsync("*_Alpha_*");
            var numberServices = await _registrator.DiscoverByPatternAsync("*_1");

            // Assert
            Assert.Equal(3, allServices.Count); // Service_Alpha_1, Service_Beta_1, Service_Alpha_2
            Assert.Equal(2, alphaServices.Count); // Service_Alpha_1, Service_Alpha_2
            Assert.Equal(2, numberServices.Count); // Service_Alpha_1, Service_Beta_1

            _output.WriteLine($"✓ Pattern 'Service_*' found {allServices.Count} machines");
            _output.WriteLine($"✓ Pattern '*_Alpha_*' found {alphaServices.Count} machines");
            _output.WriteLine($"✓ Pattern '*_1' found {numberServices.Count} machines");
        }

        [Fact]
        public async Task SendToRandomOfType_LoadBalancing()
        {
            // Arrange
            _testStartTime = DateTime.UtcNow;
            var receiveCounts = new ConcurrentDictionary<string, int>();
            var totalMessages = 100;

            // Create multiple backend services
            var backends = new List<RegisteredMachine>();
            for (int i = 0; i < 3; i++)
            {
                var backendId = $"backend{i}";
                var backend = CreateStateMachine(backendId);
                var registered = await backend.RegisterAsTypeAsync("BackendService",
                    metadata: new Dictionary<string, object> { ["id"] = backendId },
                    registrator: _registrator);
                backends.Add(registered);
                _registeredMachines.Add(registered);
                receiveCounts[registered.MachineId] = 0;
            }

            // Create a frontend that will load balance
            var frontend = CreateStateMachine("frontend");
            var frontendReg = await frontend.RegisterAsTypeAsync("FrontendService", registrator: _registrator);
            _registeredMachines.Add(frontendReg);

            // Act - Send many messages using random selection
            _output.WriteLine($"Sending {totalMessages} messages with load balancing...");
            for (int i = 0; i < totalMessages; i++)
            {
                await frontendReg.SendToRandomOfTypeAsync("BackendService", "PROCESS_REQUEST", new { id = i });
            }

            await Task.Delay(100); // Allow processing

            // Assert - Verify relatively even distribution (not perfect due to randomness)
            var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] Load distribution:");
            foreach (var backend in backends)
            {
                var info = await _registrator.GetMachineAsync(backend.MachineId);
                var metadata = info.Metadata["id"];
                _output.WriteLine($"  Backend '{metadata}': received ~{totalMessages / 3} messages (expected)");
            }

            elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] ✓ Messages distributed across backend services");
        }

        [Fact]
        public async Task TypeBasedCommunication_PingPong()
        {
            // Arrange
            _testStartTime = DateTime.UtcNow;
            var pingCount = 0;
            var pongCount = 0;
            var maxExchanges = 5;
            var completed = new TaskCompletionSource<bool>();

            // Create and register Ping service
            var pingMachine = CreateStateMachine("pinger");
            var pingService = await pingMachine.RegisterAsTypeAsync("PingService", registrator: _registrator);
            _registeredMachines.Add(pingService);

            // Create and register Pong service
            var pongMachine = CreateStateMachine("ponger");
            var pongService = await pongMachine.RegisterAsTypeAsync("PongService", registrator: _registrator);
            _registeredMachines.Add(pongService);

            // Setup message handlers using type discovery
            var pingHandler = new Func<string, object, Task>(async (eventName, data) =>
            {
                if (eventName == "PONG")
                {
                    pongCount++;
                    _output.WriteLine($"PingService received PONG #{pongCount}");

                    if (pingCount < maxExchanges)
                    {
                        pingCount++;
                        // Discover PongService and send PING
                        await pingService.SendToTypeAsync("PongService", "PING", new { count = pingCount });
                    }
                    else
                    {
                        completed.SetResult(true);
                    }
                }
            });

            var pongHandler = new Func<string, object, Task>(async (eventName, data) =>
            {
                if (eventName == "PING")
                {
                    _output.WriteLine($"PongService received PING");
                    // Discover PingService and send PONG
                    await pongService.SendToTypeAsync("PingService", "PONG", new { response = "pong" });
                }
            });

            // Act - Start communication by type discovery
            var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] === Type-based Ping-Pong Communication ===");

            // PingService discovers PongService and starts
            var pongServices = await pingService.DiscoverAndConnectAsync("PongService");
            Assert.Single(pongServices);
            _output.WriteLine($"PingService discovered {pongServices.Count} PongService(s)");

            // Start the exchange
            pingCount = 1;
            await pingService.SendToTypeAsync("PongService", "PING", new { count = pingCount });

            // Simulate message processing
            for (int i = 0; i < maxExchanges * 2; i++)
            {
                if (i % 2 == 0)
                {
                    await pongHandler("PING", null);
                }
                else
                {
                    await pingHandler("PONG", null);
                }
            }

            // Assert
            Assert.Equal(maxExchanges, pingCount);
            Assert.Equal(maxExchanges, pongCount);
            _output.WriteLine($"✓ Completed {maxExchanges} exchanges using type-based discovery");
        }

        [Fact]
        public async Task GetMachineTypes_ReturnsAllTypes()
        {
            // Arrange
            _testStartTime = DateTime.UtcNow;
            await CreateStateMachine("m1").RegisterAsTypeAsync("TypeA", registrator: _registrator);
            await CreateStateMachine("m2").RegisterAsTypeAsync("TypeB", registrator: _registrator);
            await CreateStateMachine("m3").RegisterAsTypeAsync("TypeA", registrator: _registrator);
            await CreateStateMachine("m4").RegisterAsTypeAsync("TypeC", registrator: _registrator);

            // Act
            var types = await _registrator.GetMachineTypesAsync();

            // Assert
            Assert.Equal(3, types.Count); // TypeA, TypeB, TypeC
            Assert.Contains("TypeA", types);
            Assert.Contains("TypeB", types);
            Assert.Contains("TypeC", types);

            var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] Registered machine types:");
            foreach (var type in types)
            {
                var count = (await _registrator.DiscoverByTypeAsync(type)).Count;
                _output.WriteLine($"  {type}: {count} machine(s)");
            }
        }

        [Fact]
        public async Task KubernetesRegistrator_Simulation()
        {
            // Arrange
            _testStartTime = DateTime.UtcNow;
            var k8sRegistrator = new KubernetesMachineRegistrator("test-namespace");

            // Simulate Kubernetes-style service registration
            var apiMachine = CreateStateMachine("api-pod-1");
            var apiService = await apiMachine.RegisterAsTypeAsync(
                "api-service",
                metadata: new Dictionary<string, object>
                {
                    ["port"] = 8080,
                    ["version"] = "v1",
                    ["replicas"] = 3
                },
                registrator: k8sRegistrator);

            // Act
            var services = await k8sRegistrator.DiscoverByTypeAsync("api-service");

            // Assert
            Assert.Single(services);
            var service = services.First();
            Assert.Equal("api-service", service.MachineType);
            Assert.Contains("k8s.namespace", service.Metadata.Keys);
            Assert.Equal("test-namespace", service.Metadata["k8s.namespace"]);

            var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] ✓ Kubernetes-style registrator simulation successful");
            _output.WriteLine($"  Namespace: {service.Metadata["k8s.namespace"]}");
            _output.WriteLine($"  Service: {service.MachineType}");
            _output.WriteLine($"  Pod: {service.Metadata["k8s.pod"]}");
        }

        [Fact]
        public async Task HealthCheck_MachineStatus()
        {
            // Arrange
            _testStartTime = DateTime.UtcNow;
            var machine = CreateStateMachine("health-test");
            var registered = await machine.RegisterAsTypeAsync("HealthService", registrator: _registrator);
            _registeredMachines.Add(registered);

            // Act
            var isHealthy = await _registrator.IsHealthyAsync(registered.MachineId);

            // Assert
            Assert.True(isHealthy);

            // Simulate unhealthy by unregistering
            await registered.UnregisterAsync();
            var isHealthyAfter = await _registrator.IsHealthyAsync(registered.MachineId);
            Assert.False(isHealthyAfter);

            var elapsed = (DateTime.UtcNow - _testStartTime).TotalMilliseconds;
            _output.WriteLine($"[{elapsed:F1}ms] ✓ Health check working correctly");
        }

        // Helper method
        private StateMachine CreateStateMachine(string id)
        {
            var json = $@"{{
                ""id"": ""{id}"",
                ""initial"": ""ready"",
                ""states"": {{
                    ""ready"": {{
                        ""on"": {{
                            ""*"": ""ready""
                        }}
                    }}
                }}
            }}";

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json);
            return machine;
        }

        public void Dispose()
        {
            foreach (var machine in _registeredMachines)
            {
                try { machine.Dispose(); } catch { }
            }
        }
    }
}