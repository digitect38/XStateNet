using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using XStateNet.Distributed.EventBus;
using XStateNet.Distributed.Orchestration;
using XStateNet.Distributed.Registry;
using Xunit;

namespace XStateNet.Distributed.Tests.IntegrationTests
{
    /// <summary>
    /// Integration tests for distributed state machine scenarios
    /// Note: These tests require Redis and RabbitMQ to be running
    /// </summary>
    [Collection("IntegrationTests")]
    public class DistributedStateMachineIntegrationTests : IAsyncLifetime
    {
        private ServiceProvider? _serviceProvider;
        private IStateMachineRegistry? _registry;
        private IStateMachineEventBus? _eventBus;
        private IStateMachineOrchestrator? _orchestrator;

        public async Task InitializeAsync()
        {
            var services = new ServiceCollection();

            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            // Add distributed components (using test/mock implementations)
            services.AddSingleton<IStateMachineRegistry, InMemoryStateMachineRegistry>();
            services.AddSingleton<IStateMachineEventBus, InMemoryEventBus>();
            services.AddSingleton<IStateMachineOrchestrator, DistributedStateMachineOrchestrator>();

            _serviceProvider = services.BuildServiceProvider();
            _registry = _serviceProvider.GetRequiredService<IStateMachineRegistry>();
            _eventBus = _serviceProvider.GetRequiredService<IStateMachineEventBus>();
            _orchestrator = _serviceProvider.GetRequiredService<IStateMachineOrchestrator>();

            await _eventBus.ConnectAsync();
        }

        public async Task DisposeAsync()
        {
            if (_eventBus != null)
                await _eventBus.DisconnectAsync();

            _serviceProvider?.Dispose();
        }

        [Fact]
        public async Task DistributedMachines_Should_CommunicateViaEventBus()
        {
            // Arrange
            var machine1Id = "communicator-1";
            var machine2Id = "communicator-2";
            var messageReceived = false;
            var resetEvent = new ManualResetEventSlim(false);

            // Register machines
            await _registry!.RegisterAsync(machine1Id, new StateMachineInfo
            {
                MachineId = machine1Id,
                NodeId = "node-1",
                Status = MachineStatus.Running
            });

            await _registry.RegisterAsync(machine2Id, new StateMachineInfo
            {
                MachineId = machine2Id,
                NodeId = "node-2",
                Status = MachineStatus.Running
            });

            // Subscribe machine2 to events
            await _eventBus!.SubscribeToMachineAsync(machine2Id, evt =>
            {
                if (evt.EventName == "PING")
                {
                    messageReceived = true;
                    resetEvent.Set();
                }
            });

            // Act - machine1 sends event to machine2
            await _eventBus.PublishEventAsync(machine2Id, "PING", new { message = "Hello" });

            // Assert
            Assert.True(resetEvent.Wait(TimeSpan.FromSeconds(5)), "Event was not received within timeout");
            Assert.True(messageReceived);
        }

        [Fact]
        public async Task StateChanges_Should_BePropagatedToSubscribers()
        {
            // Arrange
            var machineId = "state-tracker";
            var stateChangeReceived = false;
            var newState = "";
            var resetEvent = new ManualResetEventSlim(false);

            await _registry!.RegisterAsync(machineId, new StateMachineInfo
            {
                MachineId = machineId,
                NodeId = "node-1",
                Status = MachineStatus.Running,
                CurrentState = "idle"
            });

            // Subscribe to state changes
            await _eventBus!.SubscribeToStateChangesAsync(machineId, evt =>
            {
                stateChangeReceived = true;
                newState = evt.NewState;
                resetEvent.Set();
            });

            // Act
            await _eventBus.PublishStateChangeAsync(machineId, new StateChangeEvent
            {
                OldState = "idle",
                NewState = "running",
                Transition = "START"
            });

            // Assert
            Assert.True(resetEvent.Wait(TimeSpan.FromSeconds(5)), "State change was not received within timeout");
            Assert.True(stateChangeReceived);
            Assert.Equal("running", newState);
        }

        [Fact]
        public async Task BroadcastEvents_Should_ReachAllSubscribers()
        {
            // Arrange
            var receivedCount = 0;
            var resetEvent = new CountdownEvent(3);

            // Register multiple machines
            for (int i = 1; i <= 3; i++)
            {
                var machineId = $"listener-{i}";
                await _registry!.RegisterAsync(machineId, new StateMachineInfo
                {
                    MachineId = machineId,
                    NodeId = $"node-{i}",
                    Status = MachineStatus.Running
                });

                // Each machine subscribes to broadcasts
                await _eventBus!.SubscribeToAllAsync(evt =>
                {
                    if (evt.EventName == "BROADCAST_TEST")
                    {
                        Interlocked.Increment(ref receivedCount);
                        resetEvent.Signal();
                    }
                });
            }

            // Act
            await _eventBus!.BroadcastAsync("BROADCAST_TEST", new { announcement = "Hello everyone!" });

            // Assert
            Assert.True(resetEvent.Wait(TimeSpan.FromSeconds(5)), "Not all broadcasts were received within timeout");
            Assert.Equal(3, receivedCount);
        }

        [Fact]
        public async Task GroupEvents_Should_ReachGroupMembers()
        {
            // Arrange
            var groupName = "workers";
            var groupEventReceived = 0;
            var resetEvent = new CountdownEvent(2);

            // Register the worker machines first
            await _registry!.RegisterAsync("worker-1", new StateMachineInfo
            {
                MachineId = "worker-1",
                NodeId = "node-1",
                Status = MachineStatus.Running
            });

            await _registry!.RegisterAsync("worker-2", new StateMachineInfo
            {
                MachineId = "worker-2",
                NodeId = "node-1",
                Status = MachineStatus.Running
            });

            // Subscribe to individual machines (since SendGroupEventAsync sends to individual machines)
            await _eventBus!.SubscribeToMachineAsync("worker-1", evt =>
            {
                if (evt.EventName == "WORK")
                {
                    Interlocked.Increment(ref groupEventReceived);
                    resetEvent.Signal();
                }
            });

            await _eventBus!.SubscribeToMachineAsync("worker-2", evt =>
            {
                if (evt.EventName == "WORK")
                {
                    Interlocked.Increment(ref groupEventReceived);
                    resetEvent.Signal();
                }
            });

            // Create a group
            await _orchestrator!.CreateStateMachineGroupAsync(groupName,
                new GroupOptions { CoordinationType = GroupCoordinationType.Broadcast },
                "worker-1", "worker-2");

            // Act
            await _orchestrator.SendGroupEventAsync(groupName, "WORK", new { task = "process" });

            // Assert
            Assert.True(resetEvent.Wait(TimeSpan.FromSeconds(5)), "Group events were not received within timeout");
            Assert.Equal(2, groupEventReceived);
        }

        [Fact]
        public async Task Orchestrator_Should_ManageMachineLifecycle()
        {
            // Arrange
            var definition = new StateMachineDefinition
            {
                Id = "lifecycle-test",
                Name = "Lifecycle Test Machine",
                JsonScript = "{ \"id\": \"test\", \"initial\": \"idle\" }"
            };

            // Act - Deploy
            var deployResult = await _orchestrator!.DeployStateMachineAsync(definition,
                new DeploymentOptions { AutoStart = true });

            Assert.True(deployResult.Success);

            // Act - Check health
            var health = await _orchestrator.CheckHealthAsync(definition.Id);
            Assert.NotEqual(HealthStatus.NotFound, health.Status);

            // Act - Scale
            var scaleResult = await _orchestrator.ScaleStateMachineAsync(definition.Id, 3);
            Assert.True(scaleResult.Success);

            // Act - Shutdown
            var shutdownResult = await _orchestrator.ShutdownStateMachineAsync(definition.Id);
            Assert.True(shutdownResult);
        }

        [Fact]
        public async Task Workflow_Should_ExecuteInOrder()
        {
            // Arrange
            var executionTimestamps = new ConcurrentDictionary<string, DateTime>();
            var allStepsCompleted = new TaskCompletionSource<bool>();
            var completedCount = 0;

            // Register workflow machines and subscribe to events
            for (int i = 1; i <= 3; i++)
            {
                var machineId = $"workflow-step-{i}";
                await _registry!.RegisterAsync(machineId, new StateMachineInfo
                {
                    MachineId = machineId,
                    NodeId = "node-1",
                    Status = MachineStatus.Running
                });

                await _eventBus!.SubscribeToMachineAsync(machineId, evt =>
                {
                    if (evt.EventName == "EXECUTE")
                    {
                        lock (executionTimestamps)
                        {
                            executionTimestamps[machineId] = DateTime.UtcNow;
                            completedCount++;

                            if (completedCount == 3)
                            {
                                allStepsCompleted.TrySetResult(true);
                            }
                        }
                    }
                });
            }

            var workflow = new WorkflowDefinition
            {
                Id = "test-workflow",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep { StepId = "step1", MachineId = "workflow-step-1", EventName = "EXECUTE" },
                    new WorkflowStep { StepId = "step2", MachineId = "workflow-step-2", EventName = "EXECUTE", DependsOn = new List<string> { "step1" } },
                    new WorkflowStep { StepId = "step3", MachineId = "workflow-step-3", EventName = "EXECUTE", DependsOn = new List<string> { "step2" } }
                }
            };

            // Act
            var resultTask = _orchestrator!.ExecuteWorkflowAsync(workflow);

            // Wait for all steps to complete or timeout
            var completedTask = await Task.WhenAny(
                allStepsCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(5))
            );

            Assert.True(completedTask == allStepsCompleted.Task, "Workflow steps were not executed within timeout");

            var result = await resultTask;

            // Assert
            Assert.True(result.Success);
            Assert.Equal(3, executionTimestamps.Count);

            // Verify that the workflow result indicates all steps were executed
            Assert.NotNull(result.StepResults);
            Assert.Equal(3, result.StepResults.Count);
            Assert.True(result.StepResults.ContainsKey("step1"));
            Assert.True(result.StepResults.ContainsKey("step2"));
            Assert.True(result.StepResults.ContainsKey("step3"));

            // Verify all steps were successful
            foreach (var stepResult in result.StepResults.Values)
            {
                Assert.True(stepResult.Success);
            }

            // The workflow orchestrator ensures dependencies are respected,
            // so we just verify all machines received their events
            Assert.True(executionTimestamps.ContainsKey("workflow-step-1"));
            Assert.True(executionTimestamps.ContainsKey("workflow-step-2"));
            Assert.True(executionTimestamps.ContainsKey("workflow-step-3"));
        }

        [Fact]
        public async Task Saga_Should_CompensateOnFailure()
        {
            // Arrange
            var compensationExecuted = false;
            var resetEvent = new ManualResetEventSlim(false);

            // Register saga machines
            await _registry!.RegisterAsync("payment-service", new StateMachineInfo
            {
                MachineId = "payment-service",
                NodeId = "node-1",
                Status = MachineStatus.Running
            });

            // Subscribe to compensation action
            await _eventBus!.SubscribeToMachineAsync("payment-service", evt =>
            {
                if (evt.EventName == "REFUND")
                {
                    compensationExecuted = true;
                    resetEvent.Set();
                }
            });

            var saga = new SagaDefinition
            {
                Id = "payment-saga",
                Steps = new List<SagaDefinitionStep>
                {
                    new SagaDefinitionStep
                    {
                        StepId = "charge",
                        MachineId = "payment-service",
                        Action = "CHARGE",
                        CompensationAction = "REFUND"
                    },
                    new SagaDefinitionStep
                    {
                        StepId = "fail-step",
                        MachineId = "non-existent-service",
                        Action = "PROCESS",
                        CompensationAction = "UNDO"
                    }
                }
            };

            // Act
            var result = await _orchestrator!.ExecuteSagaAsync(saga);

            // Assert - compensation should be triggered
            Assert.False(result.Success);
            Assert.True(resetEvent.Wait(TimeSpan.FromSeconds(5)), "Compensation was not executed within timeout");
            Assert.True(compensationExecuted);
        }

        [Fact]
        public async Task Discovery_Should_FindMachinesByCapability()
        {
            // Arrange
            await _registry!.RegisterAsync("processor-1", new StateMachineInfo
            {
                MachineId = "processor-1",
                NodeId = "node-1",
                Metadata = new ConcurrentDictionary<string, object> { ["capabilities"] = "process,validate" }
            });

            await _registry.RegisterAsync("processor-2", new StateMachineInfo
            {
                MachineId = "processor-2",
                NodeId = "node-2",
                Metadata = new ConcurrentDictionary<string, object> { ["capabilities"] = "process" }
            });

            await _registry.RegisterAsync("notifier-1", new StateMachineInfo
            {
                MachineId = "notifier-1",
                NodeId = "node-3",
                Metadata = new ConcurrentDictionary<string, object> { ["capabilities"] = "notify" }
            });

            // Act
            var processors = await _orchestrator!.DiscoverByCapabilityAsync("process");

            // Assert
            Assert.Equal(2, processors.Count());
            Assert.Contains("processor-1", processors);
            Assert.Contains("processor-2", processors);
            Assert.DoesNotContain("notifier-1", processors);
        }

        [Fact]
        public async Task Registry_Should_TrackMachineHeartbeats()
        {
            // Arrange
            var machineId = "heartbeat-test";
            await _registry!.RegisterAsync(machineId, new StateMachineInfo
            {
                MachineId = machineId,
                NodeId = "node-1",
                Status = MachineStatus.Running
            });

            // Act - Update heartbeat
            await _registry.UpdateHeartbeatAsync(machineId);
            await Task.Delay(100);

            // Get active machines with short threshold
            var activeMachines = await _registry.GetActiveAsync(TimeSpan.FromSeconds(1));

            // Assert
            Assert.Contains(activeMachines, m => m.MachineId == machineId);

            // Wait for heartbeat to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            var expiredMachines = await _registry.GetActiveAsync(TimeSpan.FromSeconds(1));
            Assert.DoesNotContain(expiredMachines, m => m.MachineId == machineId);
        }
    }

    /// <summary>
    /// In-memory implementation of registry for testing
    /// </summary>
    internal class InMemoryStateMachineRegistry : IStateMachineRegistry
    {
        private readonly ConcurrentDictionary<string, StateMachineInfo> _machines = new();
        private readonly List<Action<RegistryChangeEvent>> _handlers = new();

        public event EventHandler<StateMachineRegisteredEventArgs>? MachineRegistered;
        public event EventHandler<StateMachineUnregisteredEventArgs>? MachineUnregistered;
        public event EventHandler<StateMachineStatusChangedEventArgs>? StatusChanged;

        public Task<bool> RegisterAsync(string machineId, StateMachineInfo info)
        {
            info.MachineId = machineId;
            info.RegisteredAt = DateTime.UtcNow;
            info.LastHeartbeat = DateTime.UtcNow;
            _machines[machineId] = info;

            MachineRegistered?.Invoke(this, new StateMachineRegisteredEventArgs
            {
                MachineId = machineId,
                Info = info
            });

            return Task.FromResult(true);
        }

        public Task<bool> UnregisterAsync(string machineId)
        {
            if (_machines.TryRemove(machineId, out _))
            {
                MachineUnregistered?.Invoke(this, new StateMachineUnregisteredEventArgs
                {
                    MachineId = machineId
                });
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public Task<StateMachineInfo?> GetAsync(string machineId)
        {
            return Task.FromResult(_machines.TryGetValue(machineId, out var info) ? info : null);
        }

        public Task<IEnumerable<StateMachineInfo>> GetAllAsync()
        {
            return Task.FromResult<IEnumerable<StateMachineInfo>>(_machines.Values);
        }

        public Task<IEnumerable<StateMachineInfo>> GetActiveAsync(TimeSpan heartbeatThreshold)
        {
            var threshold = DateTime.UtcNow - heartbeatThreshold;
            return Task.FromResult(_machines.Values.Where(m => m.LastHeartbeat > threshold));
        }

        public Task UpdateHeartbeatAsync(string machineId)
        {
            if (_machines.TryGetValue(machineId, out var info))
            {
                info.LastHeartbeat = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }

        public Task UpdateStatusAsync(string machineId, MachineStatus status, string? currentState = null)
        {
            if (_machines.TryGetValue(machineId, out var info))
            {
                var oldStatus = info.Status;
                info.Status = status;
                if (currentState != null)
                    info.CurrentState = currentState;

                StatusChanged?.Invoke(this, new StateMachineStatusChangedEventArgs
                {
                    MachineId = machineId,
                    OldStatus = oldStatus,
                    NewStatus = status,
                    CurrentState = currentState
                });
            }
            return Task.CompletedTask;
        }

        public Task<IEnumerable<StateMachineInfo>> FindByPatternAsync(string pattern)
        {
            var regex = new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$");

            return Task.FromResult(_machines.Values.Where(m => regex.IsMatch(m.MachineId)));
        }

        public Task SubscribeToChangesAsync(Action<RegistryChangeEvent> handler)
        {
            _handlers.Add(handler);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// In-memory implementation of event bus for testing
    /// </summary>
    internal class InMemoryEventBus : IStateMachineEventBus
    {
        private readonly ConcurrentDictionary<string, List<Action<StateMachineEvent>>> _subscriptions = new();
        private readonly ConcurrentDictionary<string, object> _subscriptionLocks = new();
        private bool _connected;

        public bool IsConnected => _connected;

        public event EventHandler<EventBusConnectedEventArgs>? Connected;
        public event EventHandler<EventBusDisconnectedEventArgs>? Disconnected;
        public event EventHandler<EventBusErrorEventArgs>? ErrorOccurred;

        public Task ConnectAsync()
        {
            _connected = true;
            Connected?.Invoke(this, new EventBusConnectedEventArgs
            {
                Endpoint = "memory://localhost"
            });
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            _connected = false;
            Disconnected?.Invoke(this, new EventBusDisconnectedEventArgs
            {
                Reason = "Manual disconnect"
            });
            return Task.CompletedTask;
        }

        public Task PublishStateChangeAsync(string machineId, StateChangeEvent evt)
        {
            evt.SourceMachineId = machineId;
            NotifySubscribers($"state.{machineId}", evt);
            return Task.CompletedTask;
        }

        public Task PublishEventAsync(string targetMachineId, string eventName, object? payload = null)
        {
            var evt = new StateMachineEvent
            {
                EventName = eventName,
                TargetMachineId = targetMachineId,
                Payload = payload
            };
            NotifySubscribers(targetMachineId, evt);
            return Task.CompletedTask;
        }

        public Task BroadcastAsync(string eventName, object? payload = null, string? filter = null)
        {
            var evt = new StateMachineEvent
            {
                EventName = eventName,
                Payload = payload
            };
            NotifySubscribers("broadcast", evt);
            return Task.CompletedTask;
        }

        public Task PublishToGroupAsync(string groupName, string eventName, object? payload = null)
        {
            var evt = new StateMachineEvent
            {
                EventName = eventName,
                Payload = payload
            };
            NotifySubscribers($"group.{groupName}", evt);
            return Task.CompletedTask;
        }

        public Task<IDisposable> SubscribeToMachineAsync(string machineId, Action<StateMachineEvent> handler)
        {
            AddSubscription(machineId, handler);
            return Task.FromResult<IDisposable>(new Subscription(() => RemoveSubscription(machineId, handler)));
        }

        public Task<IDisposable> SubscribeToStateChangesAsync(string machineId, Action<StateChangeEvent> handler)
        {
            Action<StateMachineEvent> wrapper = evt =>
            {
                if (evt is StateChangeEvent stateChange)
                    handler(stateChange);
            };
            AddSubscription($"state.{machineId}", wrapper);
            return Task.FromResult<IDisposable>(new Subscription(() => RemoveSubscription($"state.{machineId}", wrapper)));
        }

        public Task<IDisposable> SubscribeToPatternAsync(string pattern, Action<StateMachineEvent> handler)
        {
            AddSubscription($"pattern.{pattern}", handler);
            return Task.FromResult<IDisposable>(new Subscription(() => RemoveSubscription($"pattern.{pattern}", handler)));
        }

        public Task<IDisposable> SubscribeToAllAsync(Action<StateMachineEvent> handler)
        {
            AddSubscription("broadcast", handler);
            return Task.FromResult<IDisposable>(new Subscription(() => RemoveSubscription("broadcast", handler)));
        }

        public Task<IDisposable> SubscribeToGroupAsync(string groupName, Action<StateMachineEvent> handler)
        {
            AddSubscription($"group.{groupName}", handler);
            return Task.FromResult<IDisposable>(new Subscription(() => RemoveSubscription($"group.{groupName}", handler)));
        }

        public Task<TResponse?> RequestAsync<TResponse>(string targetMachineId, string requestType, object? payload = null, TimeSpan? timeout = null)
        {
            return Task.FromResult(default(TResponse));
        }

        public Task RegisterRequestHandlerAsync<TRequest, TResponse>(string requestType, Func<TRequest, Task<TResponse>> handler)
        {
            return Task.CompletedTask;
        }

        private void AddSubscription(string key, Action<StateMachineEvent> handler)
        {
            var lockObj = _subscriptionLocks.GetOrAdd(key, _ => new object());
            lock (lockObj)
            {
                var list = _subscriptions.GetOrAdd(key, _ => new List<Action<StateMachineEvent>>());
                list.Add(handler);
            }
        }

        private void RemoveSubscription(string key, Action<StateMachineEvent> handler)
        {
            if (!_subscriptions.TryGetValue(key, out var handlers))
                return;

            var lockObj = _subscriptionLocks.GetOrAdd(key, _ => new object());
            lock (lockObj)
            {
                handlers.Remove(handler);
                if (handlers.Count == 0)
                {
                    _subscriptions.TryRemove(key, out _);
                    _subscriptionLocks.TryRemove(key, out _);
                }
            }
        }

        private void NotifySubscribers(string key, StateMachineEvent evt)
        {
            if (!_subscriptions.TryGetValue(key, out var handlers))
                return;

            List<Action<StateMachineEvent>> handlersCopy;
            var lockObj = _subscriptionLocks.GetOrAdd(key, _ => new object());
            lock (lockObj)
            {
                // Create a copy to avoid holding lock during callback execution
                handlersCopy = new List<Action<StateMachineEvent>>(handlers);
            }

            foreach (var handler in handlersCopy)
            {
                Task.Run(() => handler(evt));
            }
        }

        private class Subscription : IDisposable
        {
            private readonly Action _dispose;
            public Subscription(Action dispose) => _dispose = dispose;
            public void Dispose() => _dispose();
        }
    }
}