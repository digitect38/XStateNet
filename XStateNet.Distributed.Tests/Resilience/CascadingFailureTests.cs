using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XStateNet.Orchestration;
using Xunit;
using Xunit.Abstractions;
using CircuitBreakerOpenException = XStateNet.Orchestration.CircuitBreakerOpenException;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Tests for cascading failures across multiple layers and services
    /// </summary>
    [Collection("TimingSensitive")]
    public class CascadingFailureTests : ResilienceTestBase
    {
        
        private readonly ILoggerFactory _loggerFactory;
        private EventBusOrchestrator? _orchestrator;
        private readonly List<OrchestratedCircuitBreaker> _circuitBreakers = new();

        public CascadingFailureTests(ITestOutputHelper output) : base(output)
        {

            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddDebug().SetMinimumLevel(LogLevel.Warning);
            });
        }

        [Fact]
        public async Task CascadingFailure_ThreeLayerService_PropagatesUpward()
        {
            // Arrange - Create 3-tier architecture: Frontend -> Backend -> Database
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var dbCircuitBreaker = new OrchestratedCircuitBreaker(
                "db-cb", _orchestrator, failureThreshold: 3, openDuration: TimeSpan.FromMilliseconds(500));
            var backendCircuitBreaker = new OrchestratedCircuitBreaker(
                "backend-cb", _orchestrator, failureThreshold: 5, openDuration: TimeSpan.FromMilliseconds(500));
            var frontendCircuitBreaker = new OrchestratedCircuitBreaker(
                "frontend-cb", _orchestrator, failureThreshold: 5, openDuration: TimeSpan.FromMilliseconds(500));

            _circuitBreakers.Add(dbCircuitBreaker);
            _circuitBreakers.Add(backendCircuitBreaker);
            _circuitBreakers.Add(frontendCircuitBreaker);

            await Task.WhenAll(
                dbCircuitBreaker.StartAsync(),
                backendCircuitBreaker.StartAsync(),
                frontendCircuitBreaker.StartAsync()
            );

            var dbFailureCount = 0;
            var backendFailureCount = 0;
            var frontendFailureCount = 0;
            var requestCount = 0;

            var dbOpenTime = DateTime.MaxValue;
            var backendOpenTime = DateTime.MaxValue;

            dbCircuitBreaker.StateTransitioned += (s, e) =>
            {
                if (e.newState.Contains("open") && !e.newState.Contains("halfOpen"))
                    dbOpenTime = DateTime.UtcNow;
            };

            backendCircuitBreaker.StateTransitioned += (s, e) =>
            {
                if (e.newState.Contains("open") && !e.newState.Contains("halfOpen"))
                    backendOpenTime = DateTime.UtcNow;
            };

            // Act - Simulate database failure
            for (int i = 0; i < 50; i++)
            {
                Interlocked.Increment(ref requestCount);

                try
                {
                    // Frontend calls Backend
                    await frontendCircuitBreaker.ExecuteAsync<bool>(async ct =>
                    {
                        // Backend calls Database
                        return await backendCircuitBreaker.ExecuteAsync<bool>(async ct2 =>
                        {
                            return await dbCircuitBreaker.ExecuteAsync<bool>(async ct3 =>
                            {
                                // Simulate database failure
                                Interlocked.Increment(ref dbFailureCount);
                                throw new InvalidOperationException("Database connection failed");
                            }, ct2);
                        }, ct);
                    }, CancellationToken.None);
                }
                catch (CircuitBreakerOpenException)
                {
                    // Circuit is open somewhere in the chain
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref backendFailureCount);
                }
                catch (AggregateException ex) when (ex.InnerException is InvalidOperationException)
                {
                    Interlocked.Increment(ref frontendFailureCount);
                }

                await Task.Yield();
            }

            await WaitForConditionAsync(
                condition: () => dbCircuitBreaker.CurrentState.Contains("open", StringComparison.OrdinalIgnoreCase),
                getProgress: () => dbFailureCount,
                timeoutSeconds: 2,
                noProgressTimeoutMs: 200);

            // Assert
            _output.WriteLine($"DB Circuit: {dbCircuitBreaker.CurrentState}");
            _output.WriteLine($"Backend Circuit: {backendCircuitBreaker.CurrentState}");
            _output.WriteLine($"Frontend Circuit: {frontendCircuitBreaker.CurrentState}");
            _output.WriteLine($"DB Failures: {dbFailureCount}");
            _output.WriteLine($"Backend Failures: {backendFailureCount}");
            _output.WriteLine($"Frontend Failures: {frontendFailureCount}");

            Assert.Contains("open", dbCircuitBreaker.CurrentState, StringComparison.OrdinalIgnoreCase);
            Assert.True(dbFailureCount < 15, "DB circuit should open quickly, preventing excessive failures");

            if (dbOpenTime < backendOpenTime)
            {
                var cascadeDelay = (backendOpenTime - dbOpenTime).TotalMilliseconds;
                _output.WriteLine($"Cascade delay: {cascadeDelay}ms");
            }
        }

        [Fact]
        public async Task CascadingFailure_BulkheadPattern_IsolatesFailures()
        {
            // Arrange - Create multiple isolated services with bulkheads
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 8
            });

            var serviceASuccessCount = 0;
            var serviceBSuccessCount = 0;
            var serviceCSuccessCount = 0;
            var serviceBFailureCount = 0;

            // Service A and C are healthy, Service B fails
            var actions = new Dictionary<string, (string serviceId, Action<OrchestratedContext> action)>
            {
                ["serviceA"] = ("service-a", (ctx) => Interlocked.Increment(ref serviceASuccessCount)),
                ["serviceB"] = ("service-b", (ctx) =>
                {
                    Interlocked.Increment(ref serviceBFailureCount);
                    throw new InvalidOperationException("Service B is down");
                }),
                ["serviceC"] = ("service-c", (ctx) => Interlocked.Increment(ref serviceCSuccessCount))
            };

            foreach (var (actionName, (serviceId, action)) in actions)
            {
                var actionDict = new Dictionary<string, Action<OrchestratedContext>>
                {
                    ["execute"] = action
                };

                var json = $@"{{
                    id: '{serviceId}',
                    initial: 'ready',
                    states: {{
                        ready: {{
                            on: {{ EXECUTE: {{ target: 'processing' }} }}
                        }},
                        processing: {{
                            entry: ['execute'],
                            always: [{{ target: 'ready' }}]
                        }}
                    }}
                }}";

                var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                    id: serviceId,
                    json: json,
                    orchestrator: _orchestrator,
                    orchestratedActions: actionDict,
                    guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                await _orchestrator.StartMachineAsync(serviceId);
            }

            // Act - Send requests to all services in parallel
            var tasks = new List<Task>();
            for (int i = 0; i < 100; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await _orchestrator.SendEventFireAndForgetAsync("test", "service-a", "EXECUTE");
                    }
                    catch { }
                }));

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await _orchestrator.SendEventFireAndForgetAsync("test", "service-b", "EXECUTE");
                    }
                    catch { }
                }));

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await _orchestrator.SendEventFireAndForgetAsync("test", "service-c", "EXECUTE");
                    }
                    catch { }
                }));
            }

            await Task.WhenAll(tasks);
            await WaitForConditionAsync(
                condition: () => serviceASuccessCount + serviceCSuccessCount >= 160,
                getProgress: () => serviceASuccessCount + serviceBFailureCount + serviceCSuccessCount,
                timeoutSeconds: 3,
                noProgressTimeoutMs: 500);

            // Assert - Service A and C should continue working despite Service B failures
            _output.WriteLine($"Service A Success: {serviceASuccessCount}");
            _output.WriteLine($"Service B Failures: {serviceBFailureCount}");
            _output.WriteLine($"Service C Success: {serviceCSuccessCount}");

            Assert.True(serviceASuccessCount > 80, "Service A should remain healthy");
            Assert.True(serviceCSuccessCount > 80, "Service C should remain healthy");
            Assert.True(serviceBFailureCount > 0, "Service B should fail");
            Assert.True(serviceASuccessCount + serviceCSuccessCount > serviceBFailureCount,
                "Healthy services should outnumber failed service");
        }

        [Fact]
        public async Task CascadingFailure_MultipleCircuitBreakers_PreventOverload()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4
            });

            // Create a chain of 5 services, each with its own circuit breaker
            var serviceCount = 5;
            var circuitBreakers = new List<OrchestratedCircuitBreaker>();
            var failureCounts = new int[serviceCount];
            var rejectionCounts = new int[serviceCount];

            for (int i = 0; i < serviceCount; i++)
            {
                var cb = new OrchestratedCircuitBreaker(
                    $"cb-{i}",
                    _orchestrator,
                    failureThreshold: 3,
                    openDuration: TimeSpan.FromMilliseconds(300));
                await cb.StartAsync();
                circuitBreakers.Add(cb);
                _circuitBreakers.Add(cb);
            }

            // Act - Call chain that fails at the last service
            var requestsProcessed = 0;
            for (int req = 0; req < 100; req++)
            {
                try
                {
                    // Call through the chain
                    await circuitBreakers[0].ExecuteAsync<bool>(async ct0 =>
                    {
                        return await circuitBreakers[1].ExecuteAsync<bool>(async ct1 =>
                        {
                            return await circuitBreakers[2].ExecuteAsync<bool>(async ct2 =>
                            {
                                return await circuitBreakers[3].ExecuteAsync<bool>(async ct3 =>
                                {
                                    return await circuitBreakers[4].ExecuteAsync<bool>(async ct4 =>
                                    {
                                        // Last service always fails
                                        Interlocked.Increment(ref failureCounts[4]);
                                        throw new InvalidOperationException("Service 5 failure");
                                    }, ct3);
                                }, ct2);
                            }, ct1);
                        }, ct0);
                    }, CancellationToken.None);

                    requestsProcessed++;
                }
                catch (CircuitBreakerOpenException ex)
                {
                    // Find which circuit breaker rejected
                    for (int i = 0; i < serviceCount; i++)
                    {
                        if (circuitBreakers[i].CurrentState.Contains("open", StringComparison.OrdinalIgnoreCase))
                        {
                            Interlocked.Increment(ref rejectionCounts[i]);
                            break;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Failure before circuits opened
                }
                catch (AggregateException)
                {
                    // Nested exceptions
                }

                await Task.Yield();
            }

            await WaitForConditionAsync(
                condition: () => circuitBreakers[4].CurrentState.Contains("open", StringComparison.OrdinalIgnoreCase),
                getProgress: () => failureCounts.Sum() + rejectionCounts.Sum(),
                timeoutSeconds: 2,
                noProgressTimeoutMs: 200);

            // Assert
            _output.WriteLine("=== Circuit Breaker Chain Status ===");
            for (int i = 0; i < serviceCount; i++)
            {
                _output.WriteLine($"CB-{i}: {circuitBreakers[i].CurrentState}, " +
                    $"Failures: {failureCounts[i]}, Rejections: {rejectionCounts[i]}");
            }

            // Last circuit breaker should open
            Assert.Contains("open", circuitBreakers[4].CurrentState, StringComparison.OrdinalIgnoreCase);

            // Earlier circuit breakers should also open (cascading)
            var openCount = circuitBreakers.Count(cb => cb.CurrentState.Contains("open", StringComparison.OrdinalIgnoreCase));
            Assert.True(openCount >= 2, "Multiple circuit breakers should open due to cascading failures");

            // Total failures should be limited by circuit breakers
            var totalFailures = failureCounts.Sum();
            Assert.True(totalFailures < 30, $"Circuit breakers should limit failures, got {totalFailures}");
        }

        [Fact]
        public async Task CascadingFailure_DownstreamTimeout_TriggersUpstreamFailure()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var downstreamSlowCount = 0;
            var upstreamTimeoutCount = 0;
            var successCount = 0;

            var downstreamActions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["slowOperation"] = (ctx) =>
                {
                    Interlocked.Increment(ref downstreamSlowCount);
                    // Simulate slow operation
                    Thread.Sleep(200); // Intentionally slow
                }
            };

            var upstreamActions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["callDownstream"] = (ctx) =>
                {
                    ctx.RequestSend("downstream", "PROCESS");
                }
            };

            var downstreamJson = @"{
                id: 'downstream',
                initial: 'ready',
                states: {
                    ready: {
                        on: { PROCESS: { target: 'processing' } }
                    },
                    processing: {
                        entry: ['slowOperation'],
                        always: [{ target: 'ready' }]
                    }
                }
            }";

            var upstreamJson = @"{
                id: 'upstream',
                initial: 'ready',
                states: {
                    ready: {
                        on: { CALL: { target: 'calling' } }
                    },
                    calling: {
                        entry: ['callDownstream'],
                        always: [{ target: 'ready' }]
                    }
                }
            }";

            var downstream = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "downstream", json: downstreamJson, orchestrator: _orchestrator,
                orchestratedActions: downstreamActions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);

            var upstream = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "upstream", json: upstreamJson, orchestrator: _orchestrator,
                orchestratedActions: upstreamActions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);

            await Task.WhenAll(
                _orchestrator.StartMachineAsync("downstream"),
                _orchestrator.StartMachineAsync("upstream")
            );

            // Act - Call upstream with timeout
            for (int i = 0; i < 50; i++)
            {
                try
                {
                    using var cts = new CancellationTokenSource(100); // 100ms timeout
                    var task = _orchestrator.SendEventAsync("test", "upstream", "CALL", null, 100);
                    await task.WaitAsync(cts.Token);
                    Interlocked.Increment(ref successCount);
                }
                catch (TimeoutException)
                {
                    Interlocked.Increment(ref upstreamTimeoutCount);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref upstreamTimeoutCount);
                }

                await Task.Yield();
            }

            await WaitForConditionAsync(
                condition: () => upstreamTimeoutCount + successCount >= 40,
                getProgress: () => upstreamTimeoutCount + successCount,
                timeoutSeconds: 3,
                noProgressTimeoutMs: 500);

            // Assert
            _output.WriteLine($"Downstream Slow Operations: {downstreamSlowCount}");
            _output.WriteLine($"Upstream Timeouts: {upstreamTimeoutCount}");
            _output.WriteLine($"Successes: {successCount}");

            Assert.True(upstreamTimeoutCount > 20, "Slow downstream should cause upstream timeouts");
            Assert.True(downstreamSlowCount > 0, "Downstream should have processed some requests");
        }

        [Fact]
        public async Task CascadingFailure_FanOut_PartialFailureHandling()
        {
            // Arrange - One service fans out to multiple downstream services
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 8
            });

            var downstreamCount = 10;
            var successCounts = new int[downstreamCount];
            var failureCounts = new int[downstreamCount];

            // Create multiple downstream services (some healthy, some failing)
            for (int i = 0; i < downstreamCount; i++)
            {
                var serviceIndex = i;
                var shouldFail = i % 3 == 0; // Every 3rd service fails

                var actions = new Dictionary<string, Action<OrchestratedContext>>
                {
                    ["process"] = (ctx) =>
                    {
                        if (shouldFail)
                        {
                            Interlocked.Increment(ref failureCounts[serviceIndex]);
                            throw new InvalidOperationException($"Service {serviceIndex} is unhealthy");
                        }
                        Interlocked.Increment(ref successCounts[serviceIndex]);
                    }
                };

                var json = $@"{{
                    id: 'downstream-{i}',
                    initial: 'ready',
                    states: {{
                        ready: {{
                            on: {{ PROCESS: {{ target: 'processing' }} }}
                        }},
                        processing: {{
                            entry: ['process'],
                            always: [{{ target: 'ready' }}]
                        }}
                    }}
                }}";

                var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                    id: $"downstream-{i}", json: json, orchestrator: _orchestrator,
                    orchestratedActions: actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                await _orchestrator.StartMachineAsync($"downstream-{i}");
            }

            // Act - Fan out requests to all downstream services
            var fanOutCount = 100;
            for (int req = 0; req < fanOutCount; req++)
            {
                var tasks = new List<Task>();
                for (int i = 0; i < downstreamCount; i++)
                {
                    var serviceId = $"downstream-{i}";
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await _orchestrator.SendEventFireAndForgetAsync("test", serviceId, "PROCESS");
                        }
                        catch
                        {
                            // Expected failures from unhealthy services
                        }
                    }));
                }
                await Task.WhenAll(tasks);
            }

            await WaitUntilQuiescentAsync(
                getProgress: () => successCounts.Sum() + failureCounts.Sum(),
                noProgressTimeoutMs: 1000,
                maxWaitSeconds: 5);

            // Assert
            var totalSuccess = successCounts.Sum();
            var totalFailures = failureCounts.Sum();
            var healthyServices = successCounts.Count(c => c > 80);

            _output.WriteLine($"Total Success: {totalSuccess}");
            _output.WriteLine($"Total Failures: {totalFailures}");
            _output.WriteLine($"Healthy Services: {healthyServices}/{downstreamCount}");

            for (int i = 0; i < downstreamCount; i++)
            {
                _output.WriteLine($"  Service {i}: Success={successCounts[i]}, Failures={failureCounts[i]}");
            }

            Assert.True(healthyServices >= 6, "Majority of services should remain healthy");
            Assert.True(totalSuccess > totalFailures * 2, "Successes should outnumber failures");
        }

        public override void Dispose()
        {
            foreach (var cb in _circuitBreakers)
            {
                cb?.Dispose();
            }
            _orchestrator?.Dispose();
            _loggerFactory?.Dispose();
        }
    }
}
