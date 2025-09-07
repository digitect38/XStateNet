using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XStateNet.Distributed.Registry;
using XStateNet.Distributed.EventBus;

namespace XStateNet.Distributed.Orchestration
{
    /// <summary>
    /// Distributed state machine orchestrator implementation
    /// </summary>
    public class DistributedStateMachineOrchestrator : IStateMachineOrchestrator
    {
        private readonly IStateMachineRegistry _registry;
        private readonly IStateMachineEventBus _eventBus;
        private readonly ILogger<DistributedStateMachineOrchestrator>? _logger;
        private readonly ConcurrentDictionary<string, StateMachineDefinition> _definitions = new();
        private readonly ConcurrentDictionary<string, WorkflowExecution> _activeWorkflows = new();
        private readonly ConcurrentDictionary<string, SagaExecution> _activeSagas = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _groups = new();
        private readonly ConcurrentDictionary<string, GroupOptions> _groupOptions = new();
        private readonly ConcurrentDictionary<string, Queue<string>> _roundRobinQueues = new();
        private readonly ConcurrentDictionary<string, Alert> _activeAlerts = new();
        private readonly Timer _healthCheckTimer;
        private readonly TimeSpan _healthCheckInterval = TimeSpan.FromSeconds(30);
        
        public DistributedStateMachineOrchestrator(
            IStateMachineRegistry registry,
            IStateMachineEventBus eventBus,
            ILogger<DistributedStateMachineOrchestrator>? logger = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger;
            
            _healthCheckTimer = new Timer(async _ => await PerformHealthChecksAsync(), null, 
                _healthCheckInterval, _healthCheckInterval);
        }
        
        public async Task<DeploymentResult> DeployStateMachineAsync(
            StateMachineDefinition definition, 
            DeploymentOptions? options = null)
        {
            try
            {
                options ??= new DeploymentOptions();
                
                var machineId = definition.Id;
                var nodeId = options.TargetNodeId ?? Environment.MachineName;
                
                // Store definition
                _definitions[machineId] = definition;
                
                // Register in distributed registry
                var info = new StateMachineInfo
                {
                    MachineId = machineId,
                    NodeId = nodeId,
                    Endpoint = $"node://{nodeId}/{machineId}",
                    Version = "1.0.0",
                    Metadata = definition.Configuration.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    Status = options.AutoStart ? MachineStatus.Starting : MachineStatus.Stopped,
                    Tags = definition.Labels
                };
                
                var registered = await _registry.RegisterAsync(machineId, info);
                
                if (!registered)
                {
                    return new DeploymentResult
                    {
                        Success = false,
                        MachineId = machineId,
                        ErrorMessage = "Failed to register machine in distributed registry"
                    };
                }
                
                // Deploy instances
                var deployedInstances = new List<string>();
                for (int i = 0; i < options.InitialInstances; i++)
                {
                    var instanceId = $"{machineId}_{i}";
                    
                    // Send deployment command via event bus
                    await _eventBus.PublishEventAsync(nodeId, "Deploy", new
                    {
                        InstanceId = instanceId,
                        Definition = definition,
                        Options = options
                    });
                    
                    deployedInstances.Add(instanceId);
                }
                
                // Start if requested
                if (options.AutoStart)
                {
                    await _registry.UpdateStatusAsync(machineId, MachineStatus.Running);
                    
                    foreach (var instanceId in deployedInstances)
                    {
                        await _eventBus.PublishEventAsync(instanceId, "Start");
                    }
                }
                
                // Set up health check if enabled
                if (options.EnableMonitoring && options.HealthCheck != null)
                {
                    _ = Task.Run(async () => await MonitorHealthAsync(machineId, options.HealthCheck));
                }
                
                return new DeploymentResult
                {
                    Success = true,
                    MachineId = machineId,
                    NodeId = nodeId,
                    Endpoint = info.Endpoint,
                    DeploymentTime = TimeSpan.FromMilliseconds(100) // Placeholder
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to deploy state machine {MachineId}", definition.Id);
                
                return new DeploymentResult
                {
                    Success = false,
                    MachineId = definition.Id,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        public async Task<ScaleResult> ScaleStateMachineAsync(string machineId, int targetInstances)
        {
            try
            {
                var info = await _registry.GetAsync(machineId);
                if (info == null)
                {
                    return new ScaleResult
                    {
                        Success = false
                    };
                }
                
                // For simplicity, assume current instances = 1
                var currentInstances = 1;
                var result = new ScaleResult
                {
                    PreviousInstances = currentInstances,
                    CurrentInstances = targetInstances
                };
                
                if (targetInstances > currentInstances)
                {
                    // Scale up
                    for (int i = currentInstances; i < targetInstances; i++)
                    {
                        var instanceId = $"{machineId}_{i}";
                        
                        await _eventBus.PublishEventAsync(info.NodeId, "Deploy", new
                        {
                            InstanceId = instanceId,
                            Definition = _definitions.GetValueOrDefault(machineId)
                        });
                        
                        result.NewInstanceIds.Add(instanceId);
                    }
                }
                else if (targetInstances < currentInstances)
                {
                    // Scale down
                    for (int i = targetInstances; i < currentInstances; i++)
                    {
                        var instanceId = $"{machineId}_{i}";
                        
                        await _eventBus.PublishEventAsync(instanceId, "Shutdown");
                        
                        result.RemovedInstanceIds.Add(instanceId);
                    }
                }
                
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to scale state machine {MachineId}", machineId);
                return new ScaleResult { Success = false };
            }
        }
        
        public async Task<MigrationResult> MigrateStateMachineAsync(string machineId, string targetNodeId)
        {
            try
            {
                var info = await _registry.GetAsync(machineId);
                if (info == null)
                {
                    return new MigrationResult { Success = false };
                }
                
                var sourceNodeId = info.NodeId;
                
                // Request state snapshot
                var snapshot = await _eventBus.RequestAsync<StateSnapshot>(
                    machineId, "GetSnapshot", timeout: TimeSpan.FromSeconds(10));
                
                // Deploy on target node
                await _eventBus.PublishEventAsync(targetNodeId, "Deploy", new
                {
                    MachineId = machineId,
                    Definition = _definitions.GetValueOrDefault(machineId),
                    Snapshot = snapshot
                });
                
                // Update registry
                info.NodeId = targetNodeId;
                info.Endpoint = $"node://{targetNodeId}/{machineId}";
                await _registry.RegisterAsync(machineId, info);
                
                // Shutdown on source node
                await _eventBus.PublishEventAsync(sourceNodeId, "Shutdown", new { MachineId = machineId });
                
                return new MigrationResult
                {
                    Success = true,
                    SourceNodeId = sourceNodeId,
                    TargetNodeId = targetNodeId,
                    MigrationTime = TimeSpan.FromSeconds(1),
                    StatePreserved = snapshot != null
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to migrate state machine {MachineId}", machineId);
                return new MigrationResult { Success = false };
            }
        }
        
        public async Task<bool> ShutdownStateMachineAsync(string machineId, bool graceful = true, TimeSpan? timeout = null)
        {
            try
            {
                var actualTimeout = timeout ?? TimeSpan.FromSeconds(30);
                
                if (graceful)
                {
                    // Send graceful shutdown signal
                    await _eventBus.PublishEventAsync(machineId, "GracefulShutdown", new
                    {
                        Timeout = actualTimeout
                    });
                    
                    // Wait for confirmation
                    var confirmed = await _eventBus.RequestAsync<bool>(
                        machineId, "ShutdownConfirmation", timeout: actualTimeout);
                    
                    if (!confirmed)
                    {
                        _logger?.LogWarning("Graceful shutdown not confirmed for {MachineId}, forcing shutdown", machineId);
                    }
                }
                else
                {
                    // Force shutdown
                    await _eventBus.PublishEventAsync(machineId, "ForceShutdown");
                }
                
                // Update registry
                await _registry.UpdateStatusAsync(machineId, MachineStatus.Stopped);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to shutdown state machine {MachineId}", machineId);
                return false;
            }
        }
        
        public async Task<bool> RestartStateMachineAsync(string machineId)
        {
            try
            {
                // Shutdown first
                await ShutdownStateMachineAsync(machineId, graceful: true);
                
                // Wait a bit
                await Task.Delay(1000);
                
                // Start again
                await _eventBus.PublishEventAsync(machineId, "Start");
                await _registry.UpdateStatusAsync(machineId, MachineStatus.Running);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to restart state machine {MachineId}", machineId);
                return false;
            }
        }
        
        public async Task<HealthCheckResult> CheckHealthAsync(string machineId)
        {
            try
            {
                var info = await _registry.GetAsync(machineId);
                if (info == null)
                {
                    return new HealthCheckResult
                    {
                        MachineId = machineId,
                        Status = HealthStatus.NotFound,
                        Message = "Machine not found in registry"
                    };
                }
                
                // Check heartbeat
                var heartbeatAge = DateTime.UtcNow - info.LastHeartbeat;
                var items = new List<HealthCheckItem>();
                
                items.Add(new HealthCheckItem
                {
                    Name = "Heartbeat",
                    IsHealthy = heartbeatAge < TimeSpan.FromMinutes(1),
                    Message = $"Last heartbeat: {heartbeatAge.TotalSeconds:F1}s ago"
                });
                
                // Request health status from machine
                try
                {
                    var healthData = await _eventBus.RequestAsync<Dictionary<string, object>>(
                        machineId, "HealthCheck", timeout: TimeSpan.FromSeconds(5));
                    
                    if (healthData != null)
                    {
                        items.Add(new HealthCheckItem
                        {
                            Name = "Machine Response",
                            IsHealthy = true,
                            Data = healthData
                        });
                    }
                }
                catch
                {
                    items.Add(new HealthCheckItem
                    {
                        Name = "Machine Response",
                        IsHealthy = false,
                        Message = "Machine not responding to health check"
                    });
                }
                
                // Determine overall status
                var status = HealthStatus.Healthy;
                if (items.Any(i => !i.IsHealthy))
                {
                    status = items.Count(i => !i.IsHealthy) > 1 ? HealthStatus.Unhealthy : HealthStatus.Degraded;
                }
                
                return new HealthCheckResult
                {
                    MachineId = machineId,
                    Status = status,
                    CheckedAt = DateTime.UtcNow,
                    Items = items
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to check health for {MachineId}", machineId);
                
                return new HealthCheckResult
                {
                    MachineId = machineId,
                    Status = HealthStatus.Unknown,
                    Message = ex.Message
                };
            }
        }
        
        public async Task<MetricsSnapshot> GetMetricsAsync(string machineId, TimeSpan? window = null)
        {
            try
            {
                var actualWindow = window ?? TimeSpan.FromMinutes(5);
                
                var metrics = await _eventBus.RequestAsync<MetricsSnapshot>(
                    machineId, "GetMetrics", new { Window = actualWindow }, 
                    timeout: TimeSpan.FromSeconds(10));
                
                return metrics ?? new MetricsSnapshot
                {
                    MachineId = machineId,
                    Window = actualWindow
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to get metrics for {MachineId}", machineId);
                
                return new MetricsSnapshot
                {
                    MachineId = machineId,
                    Window = window ?? TimeSpan.FromMinutes(5)
                };
            }
        }
        
        public async Task<IEnumerable<Alert>> GetActiveAlertsAsync(string? machineId = null)
        {
            if (machineId != null)
            {
                return _activeAlerts.Values.Where(a => a.MachineId == machineId);
            }
            
            return await Task.FromResult(_activeAlerts.Values);
        }
        
        public async Task<SystemOverview> GetSystemOverviewAsync()
        {
            try
            {
                var allMachines = await _registry.GetAllAsync();
                var machinesList = allMachines.ToList();
                
                var overview = new SystemOverview
                {
                    TotalMachines = machinesList.Count,
                    ActiveMachines = machinesList.Count(m => m.Status == MachineStatus.Running),
                    HealthyMachines = 0,
                    DegradedMachines = 0,
                    UnhealthyMachines = 0,
                    Timestamp = DateTime.UtcNow
                };
                
                // Check health of each machine
                foreach (var machine in machinesList.Where(m => m.Status == MachineStatus.Running))
                {
                    var health = await CheckHealthAsync(machine.MachineId);
                    switch (health.Status)
                    {
                        case HealthStatus.Healthy:
                            overview.HealthyMachines++;
                            break;
                        case HealthStatus.Degraded:
                            overview.DegradedMachines++;
                            break;
                        case HealthStatus.Unhealthy:
                            overview.UnhealthyMachines++;
                            break;
                    }
                }
                
                // Group by node
                overview.MachinesByNode = machinesList
                    .GroupBy(m => m.NodeId)
                    .ToDictionary(g => g.Key, g => g.Count());
                
                // Recent alerts
                overview.RecentAlerts = _activeAlerts.Values
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(10)
                    .ToList();
                
                return overview;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to get system overview");
                throw;
            }
        }
        
        public async Task<WorkflowResult> ExecuteWorkflowAsync(
            WorkflowDefinition workflow, 
            CancellationToken cancellationToken = default)
        {
            var execution = new WorkflowExecution
            {
                WorkflowId = workflow.Id,
                Definition = workflow,
                StartTime = DateTime.UtcNow,
                StepResults = new Dictionary<string, StepResult>()
            };
            
            _activeWorkflows[workflow.Id] = execution;
            
            try
            {
                // Sort steps by dependencies
                var sortedSteps = TopologicalSort(workflow.Steps);
                
                foreach (var step in sortedSteps)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    
                    // Wait for dependencies
                    foreach (var dep in step.DependsOn)
                    {
                        while (!execution.StepResults.ContainsKey(dep) || 
                               !execution.StepResults[dep].Success)
                        {
                            await Task.Delay(100, cancellationToken);
                        }
                    }
                    
                    // Execute step
                    var stepResult = await ExecuteWorkflowStepAsync(step, workflow.Context);
                    execution.StepResults[step.StepId] = stepResult;
                    
                    if (!stepResult.Success && workflow.Compensation != null)
                    {
                        // Handle compensation
                        await HandleWorkflowCompensationAsync(workflow.Compensation, execution);
                        break;
                    }
                }
                
                return new WorkflowResult
                {
                    Success = execution.StepResults.Values.All(r => r.Success),
                    WorkflowId = workflow.Id,
                    ExecutionTime = DateTime.UtcNow - execution.StartTime,
                    StepResults = execution.StepResults
                };
            }
            finally
            {
                _activeWorkflows.TryRemove(workflow.Id, out _);
            }
        }
        
        public async Task<string> CreateStateMachineGroupAsync(
            string groupName, 
            GroupOptions options, 
            params string[] machineIds)
        {
            _groups[groupName] = new HashSet<string>(machineIds);
            _groupOptions[groupName] = options;
            
            if (options.CoordinationType == GroupCoordinationType.RoundRobin)
            {
                _roundRobinQueues[groupName] = new Queue<string>(machineIds);
            }
            
            // Notify machines they're part of a group
            foreach (var machineId in machineIds)
            {
                await _eventBus.PublishEventAsync(machineId, "JoinGroup", new { GroupName = groupName });
            }
            
            _logger?.LogInformation("Created state machine group {GroupName} with {Count} machines", 
                groupName, machineIds.Length);
            
            return groupName;
        }
        
        public async Task SendGroupEventAsync(string groupName, string eventName, object? payload = null)
        {
            if (!_groups.TryGetValue(groupName, out var machines) || 
                !_groupOptions.TryGetValue(groupName, out var options))
            {
                throw new InvalidOperationException($"Group {groupName} not found");
            }
            
            switch (options.CoordinationType)
            {
                case GroupCoordinationType.Broadcast:
                    foreach (var machineId in machines)
                    {
                        await _eventBus.PublishEventAsync(machineId, eventName, payload);
                    }
                    break;
                    
                case GroupCoordinationType.RoundRobin:
                    if (_roundRobinQueues.TryGetValue(groupName, out var queue))
                    {
                        var machineId = queue.Dequeue();
                        await _eventBus.PublishEventAsync(machineId, eventName, payload);
                        queue.Enqueue(machineId);
                    }
                    break;
                    
                case GroupCoordinationType.Random:
                    var random = new Random();
                    var randomMachine = machines.ElementAt(random.Next(machines.Count));
                    await _eventBus.PublishEventAsync(randomMachine, eventName, payload);
                    break;
                    
                case GroupCoordinationType.LeastLoaded:
                    var leastLoaded = await FindLeastLoadedMachineAsync(machines);
                    if (leastLoaded != null)
                    {
                        await _eventBus.PublishEventAsync(leastLoaded, eventName, payload);
                    }
                    break;
                    
                case GroupCoordinationType.Primary:
                    var primary = machines.FirstOrDefault();
                    if (primary != null)
                    {
                        await _eventBus.PublishEventAsync(primary, eventName, payload);
                    }
                    break;
            }
        }
        
        public async Task<SagaResult> ExecuteSagaAsync(
            SagaDefinition saga, 
            CancellationToken cancellationToken = default)
        {
            var execution = new SagaExecution
            {
                SagaId = saga.Id,
                Definition = saga,
                StartTime = DateTime.UtcNow,
                CompletedSteps = new List<string>(),
                CompensatedSteps = new List<string>()
            };
            
            _activeSagas[saga.Id] = execution;
            
            try
            {
                foreach (var step in saga.Steps)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    
                    try
                    {
                        // Verify machine exists before executing step
                        var machineInfo = await _registry.GetAsync(step.MachineId);
                        if (machineInfo == null)
                        {
                            throw new InvalidOperationException($"Machine {step.MachineId} not found in registry");
                        }
                        
                        // Execute step
                        await _eventBus.PublishEventAsync(step.MachineId, step.Action, step.Payload);
                        execution.CompletedSteps.Add(step.StepId);
                        
                        _logger?.LogDebug("Saga {SagaId}: Completed step {StepId}", saga.Id, step.StepId);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Saga {SagaId}: Failed at step {StepId}", saga.Id, step.StepId);
                        
                        // Compensate completed steps in reverse order
                        var stepsToCompensate = saga.Steps
                            .Where(s => execution.CompletedSteps.Contains(s.StepId))
                            .Reverse();
                        
                        foreach (var compensateStep in stepsToCompensate)
                        {
                            try
                            {
                                await _eventBus.PublishEventAsync(
                                    compensateStep.MachineId, 
                                    compensateStep.CompensationAction, 
                                    compensateStep.Payload);
                                
                                execution.CompensatedSteps.Add(compensateStep.StepId);
                                
                                _logger?.LogDebug("Saga {SagaId}: Compensated step {StepId}", 
                                    saga.Id, compensateStep.StepId);
                            }
                            catch (Exception compEx)
                            {
                                _logger?.LogError(compEx, "Saga {SagaId}: Failed to compensate step {StepId}", 
                                    saga.Id, compensateStep.StepId);
                            }
                        }
                        
                        return new SagaResult
                        {
                            Success = false,
                            SagaId = saga.Id,
                            CompletedSteps = execution.CompletedSteps,
                            CompensatedSteps = execution.CompensatedSteps,
                            FailedStep = step.StepId,
                            ErrorMessage = ex.Message
                        };
                    }
                }
                
                return new SagaResult
                {
                    Success = true,
                    SagaId = saga.Id,
                    CompletedSteps = execution.CompletedSteps,
                    CompensatedSteps = execution.CompensatedSteps
                };
            }
            finally
            {
                _activeSagas.TryRemove(saga.Id, out _);
            }
        }
        
        public async Task<IEnumerable<string>> DiscoverByCapabilityAsync(string capability)
        {
            var allMachines = await _registry.GetAllAsync();
            
            return allMachines
                .Where(m => m.Metadata.ContainsKey("capabilities") && 
                           m.Metadata["capabilities"].ToString()!.Contains(capability))
                .Select(m => m.MachineId);
        }
        
        public async Task<RoutingResult> RouteEventAsync(RoutingRequest request)
        {
            try
            {
                var targetMachines = new List<string>();
                
                switch (request.Strategy)
                {
                    case RoutingStrategy.Direct:
                        if (request.Requirements.TryGetValue("machineId", out var machineId))
                        {
                            targetMachines.Add(machineId);
                        }
                        break;
                        
                    case RoutingStrategy.Capability:
                        if (request.Requirements.TryGetValue("capability", out var capability))
                        {
                            var capable = await DiscoverByCapabilityAsync(capability);
                            targetMachines.AddRange(capable);
                        }
                        break;
                        
                    case RoutingStrategy.Broadcast:
                        var allMachines = await _registry.GetAllAsync();
                        targetMachines.AddRange(allMachines.Select(m => m.MachineId));
                        break;
                        
                    case RoutingStrategy.LoadBalanced:
                        var activeMachines = await _registry.GetActiveAsync(TimeSpan.FromMinutes(1));
                        var leastLoaded = await FindLeastLoadedMachineAsync(
                            activeMachines.Select(m => m.MachineId).ToHashSet());
                        if (leastLoaded != null)
                        {
                            targetMachines.Add(leastLoaded);
                        }
                        break;
                        
                    case RoutingStrategy.ContentBased:
                        // Route based on payload content
                        // This would require more complex routing rules
                        break;
                }
                
                // Send events
                foreach (var target in targetMachines)
                {
                    await _eventBus.PublishEventAsync(target, request.EventName, request.Payload);
                }
                
                return new RoutingResult
                {
                    Success = targetMachines.Any(),
                    TargetMachineIds = targetMachines
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to route event {EventName}", request.EventName);
                
                return new RoutingResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        public async Task<bool> UpdateConfigurationAsync(string machineId, Dictionary<string, object> configuration)
        {
            try
            {
                await _eventBus.PublishEventAsync(machineId, "UpdateConfiguration", configuration);
                
                if (_definitions.TryGetValue(machineId, out var definition))
                {
                    definition.Configuration = configuration;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to update configuration for {MachineId}", machineId);
                return false;
            }
        }
        
        public async Task<Dictionary<string, object>> GetConfigurationAsync(string machineId)
        {
            try
            {
                var config = await _eventBus.RequestAsync<Dictionary<string, object>>(
                    machineId, "GetConfiguration", timeout: TimeSpan.FromSeconds(5));
                
                return config ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to get configuration for {MachineId}", machineId);
                
                if (_definitions.TryGetValue(machineId, out var definition))
                {
                    return definition.Configuration;
                }
                
                return new Dictionary<string, object>();
            }
        }
        
        private async Task<StepResult> ExecuteWorkflowStepAsync(WorkflowStep step, Dictionary<string, object> context)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                await _eventBus.PublishEventAsync(step.MachineId, step.EventName, step.Payload);
                
                if (!string.IsNullOrEmpty(step.WaitForState))
                {
                    // Wait for machine to reach desired state
                    var timeout = step.Timeout ?? TimeSpan.FromMinutes(5);
                    var endTime = DateTime.UtcNow + timeout;
                    
                    while (DateTime.UtcNow < endTime)
                    {
                        var info = await _registry.GetAsync(step.MachineId);
                        if (info?.CurrentState == step.WaitForState)
                        {
                            break;
                        }
                        
                        await Task.Delay(500);
                    }
                }
                
                return new StepResult
                {
                    StepId = step.StepId,
                    Success = true,
                    ExecutionTime = DateTime.UtcNow - startTime
                };
            }
            catch (Exception ex)
            {
                return new StepResult
                {
                    StepId = step.StepId,
                    Success = false,
                    ErrorMessage = ex.Message,
                    ExecutionTime = DateTime.UtcNow - startTime
                };
            }
        }
        
        private async Task HandleWorkflowCompensationAsync(
            WorkflowCompensation compensation, 
            WorkflowExecution execution)
        {
            switch (compensation.Strategy)
            {
                case CompensationStrategy.Saga:
                    foreach (var step in compensation.Steps.AsEnumerable().Reverse())
                    {
                        await _eventBus.PublishEventAsync(step.MachineId, step.Action, step.Payload);
                    }
                    break;
                    
                case CompensationStrategy.Retry:
                    // Retry failed steps
                    break;
                    
                case CompensationStrategy.Ignore:
                    // Do nothing
                    break;
                    
                case CompensationStrategy.Manual:
                    // Alert for manual intervention
                    CreateAlert(AlertSeverity.Warning, "Manual compensation required", 
                        $"Workflow {execution.WorkflowId} requires manual compensation");
                    break;
            }
        }
        
        private async Task<string?> FindLeastLoadedMachineAsync(HashSet<string> machines)
        {
            string? leastLoaded = null;
            double minLoad = double.MaxValue;
            
            foreach (var machineId in machines)
            {
                try
                {
                    var metrics = await GetMetricsAsync(machineId, TimeSpan.FromMinutes(1));
                    var load = metrics.EventsPerSecond;
                    
                    if (load < minLoad)
                    {
                        minLoad = load;
                        leastLoaded = machineId;
                    }
                }
                catch
                {
                    // Skip machines that don't respond
                }
            }
            
            return leastLoaded;
        }
        
        private List<WorkflowStep> TopologicalSort(List<WorkflowStep> steps)
        {
            var sorted = new List<WorkflowStep>();
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();
            
            void Visit(WorkflowStep step)
            {
                if (visited.Contains(step.StepId))
                    return;
                    
                if (visiting.Contains(step.StepId))
                    throw new InvalidOperationException($"Circular dependency detected at step {step.StepId}");
                
                visiting.Add(step.StepId);
                
                foreach (var depId in step.DependsOn)
                {
                    var dep = steps.FirstOrDefault(s => s.StepId == depId);
                    if (dep != null)
                        Visit(dep);
                }
                
                visiting.Remove(step.StepId);
                visited.Add(step.StepId);
                sorted.Add(step);
            }
            
            foreach (var step in steps)
            {
                Visit(step);
            }
            
            return sorted;
        }
        
        private async Task MonitorHealthAsync(string machineId, HealthCheckOptions options)
        {
            while (true)
            {
                await Task.Delay(options.Interval);
                
                var health = await CheckHealthAsync(machineId);
                
                if (health.Status == HealthStatus.Unhealthy)
                {
                    CreateAlert(AlertSeverity.Error, $"Machine {machineId} unhealthy", 
                        $"Health check failed for machine {machineId}");
                }
            }
        }
        
        private void CreateAlert(AlertSeverity severity, string title, string description)
        {
            var alert = new Alert
            {
                Severity = severity,
                Title = title,
                Description = description,
                CreatedAt = DateTime.UtcNow
            };
            
            _activeAlerts[alert.Id] = alert;
            
            _logger?.LogWarning("Alert created: {Title} - {Description}", title, description);
        }
        
        private async Task PerformHealthChecksAsync()
        {
            try
            {
                var activeMachines = await _registry.GetActiveAsync(TimeSpan.FromMinutes(2));
                
                foreach (var machine in activeMachines)
                {
                    _ = Task.Run(async () =>
                    {
                        var health = await CheckHealthAsync(machine.MachineId);
                        
                        if (health.Status == HealthStatus.Unhealthy)
                        {
                            await _registry.UpdateStatusAsync(machine.MachineId, MachineStatus.Unhealthy);
                        }
                        else if (health.Status == HealthStatus.Degraded)
                        {
                            await _registry.UpdateStatusAsync(machine.MachineId, MachineStatus.Degraded);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error performing health checks");
            }
        }
        
        private class WorkflowExecution
        {
            public string WorkflowId { get; set; } = string.Empty;
            public WorkflowDefinition Definition { get; set; } = new();
            public DateTime StartTime { get; set; }
            public Dictionary<string, StepResult> StepResults { get; set; } = new();
        }
        
        private class SagaExecution
        {
            public string SagaId { get; set; } = string.Empty;
            public SagaDefinition Definition { get; set; } = new();
            public DateTime StartTime { get; set; }
            public List<string> CompletedSteps { get; set; } = new();
            public List<string> CompensatedSteps { get; set; } = new();
        }
        
        // Made public for testing purposes
        public class StateSnapshot
        {
            public string CurrentState { get; set; } = string.Empty;
            public Dictionary<string, object> Context { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        }
    }
}