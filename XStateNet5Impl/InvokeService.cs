using System.Collections.Concurrent;

namespace XStateNet;

/// <summary>
/// Represents an invoked service instance
/// </summary>
public class InvokedService : IDisposable
{
    public string Id { get; set; }
    public string ServiceName { get; set; }
    public Task ServiceTask { get; set; } = null!;
    public CancellationTokenSource CancellationToken { get; set; }
    public CompoundState InvokingState { get; set; }
    public object? Result { get; set; }
    public Exception? Error { get; set; }
    public bool IsCompleted => ServiceTask?.IsCompleted ?? false;
    public bool IsFaulted => ServiceTask?.IsFaulted ?? false;
    public bool IsCanceled => ServiceTask?.IsCanceled ?? false;
    private bool _disposed;

    public InvokedService(string id, string serviceName, CompoundState invokingState)
    {
        Id = id;
        ServiceName = serviceName;
        InvokingState = invokingState;
        CancellationToken = new CancellationTokenSource();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CancellationToken?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Manages invoked services lifecycle
/// </summary>
public class ServiceInvoker : StateObject
{
    private readonly ConcurrentDictionary<string, InvokedService> _activeServices = new();


    private readonly ConcurrentDictionary<string, TaskCompletionSource<object>> _serviceCompletions = new();

    public ServiceInvoker(string? machineId) : base(machineId) { }

    /// <summary>
    /// Invokes a service when entering a state
    /// </summary>
    public async Task InvokeService(CompoundState state, string serviceName, NamedService? service)
    {
        if (service == null || StateMachine == null) return;

        var serviceId = $"{state.Name}_{serviceName}_{Guid.NewGuid():N}";
        var invokedService = new InvokedService(serviceId, serviceName, state);

        if (!_activeServices.TryAdd(serviceId, invokedService))
        {
            Logger.Warning($"Service {serviceId} already exists");
            return;
        }

        Logger.Info($"Invoking service '{serviceName}' for state '{state.Name}'");

        try
        {
            // Create a task completion source for this service
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            _serviceCompletions[serviceId] = tcs;

            // Start the service
            invokedService.ServiceTask = Task.Run(async () =>
            {
                try
                {
                    var result = await service.ServiceFunc(StateMachine, invokedService.CancellationToken.Token);
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    throw;
                }
            }, invokedService.CancellationToken.Token);

            // Handle service completion properly
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleServiceCompletion(invokedService);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Unhandled exception in service completion for '{invokedService.ServiceName}': {ex}");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to invoke service '{serviceName}': {ex.Message}");
            invokedService.Error = ex;
            await HandleServiceError(invokedService);
        }
    }

    /// <summary>
    /// Handles service completion (success or failure)
    /// </summary>
    private async Task HandleServiceCompletion(InvokedService service)
    {
        try
        {
            await service.ServiceTask;

            // Service completed successfully
            Logger.Info($"Service '{service.ServiceName}' completed successfully");

            // Get result from task
            object? result = null;
            if (_serviceCompletions.TryGetValue(service.Id, out var tcs))
            {
                if (tcs.Task.IsCompletedSuccessfully)
                {
                    result = tcs.Task.Result;
                    Logger.Info($">>> Service result: {result}");
                }
            }

            // Store result in context
            if (StateMachine?.ContextMap != null && result != null)
            {
                StateMachine.ContextMap["_serviceResult"] = result;
            }

            // Send onDone event
            if (StateMachine != null)
            {
                var doneEvent = $"done.invoke.{service.InvokingState.Name}.{service.ServiceName}";
                var eventData = new { data = result };
#if false
                // Queue events for processing without blocking
                // This avoids lock conflicts while ensuring events are handled
                await Task.Run(() =>
                {
                    try
                    {
                        StateMachine.Send(doneEvent, eventData);
                        StateMachine.Send("onDone", eventData);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to send completion event for service '{service.ServiceName}': {ex.Message}");
                    }
                }).ConfigureAwait(false);
#else
                try
                {
                    await StateMachine.SendAsync(doneEvent, eventData);
                    await StateMachine.SendAsync("onDone", eventData);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to send completion event for service '{service.ServiceName}': {ex.Message}");
                }
#endif                
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info($"Service '{service.ServiceName}' was cancelled");
        }
        catch (Exception ex)
        {
            service.Error = ex;
            await HandleServiceError(service);
        }
        finally
        {
            // Clean up
            if (_activeServices.TryRemove(service.Id, out var removedService))
            {
                removedService?.Dispose();
            }
            _serviceCompletions.TryRemove(service.Id, out _);
        }
    }



    /// <summary>
    /// Handles service errors
    /// </summary>
    private async Task HandleServiceError(InvokedService service)
    {
        Logger.Error($"Service '{service.ServiceName}' failed: {service.Error?.Message}");

        // Store error information in context
        if (StateMachine?.ContextMap != null && service.Error != null)
        {
            StateMachine.ContextMap["_error"] = service.Error;
            StateMachine.ContextMap["_lastError"] = service.Error;  // For backward compatibility
            StateMachine.ContextMap["_errorType"] = service.Error.GetType().Name;
            StateMachine.ContextMap["_errorMessage"] = service.Error.Message;

            var errorEvent = $"error.invoke.{service.InvokingState.Name}.{service.ServiceName}";
#if false
            // Queue error events for processing without blocking
            await Task.Run(() =>
            {
                try
                {
                    StateMachine.Send(errorEvent);
                    StateMachine.Send("onError");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to send error event for service '{service.ServiceName}': {ex.Message}");
                }
            }).ConfigureAwait(false);
        
#else
            try
            {
                await StateMachine.SendAsync(errorEvent);
                await StateMachine.SendAsync("onError");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send error event for service '{service.ServiceName}': {ex.Message}");
            }
#endif
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Cancels a service when exiting a state
    /// </summary>
    public void CancelService(CompoundState state)
    {
        var servicesToCancel = _activeServices.Values
            .Where(s => s.InvokingState == state)
            .ToList();

        foreach (var service in servicesToCancel)
        {
            Logger.Info($"Cancelling service '{service.ServiceName}' for state '{state.Name}'");
            service.CancellationToken.Cancel();
            _activeServices.TryRemove(service.Id, out _);
        }
    }

    /// <summary>
    /// Cancels all active services
    /// </summary>
    public void CancelAllServices()
    {
        foreach (var service in _activeServices.Values)
        {
            service.CancellationToken.Cancel();
        }
        _activeServices.Clear();
        _serviceCompletions.Clear();
    }

    /// <summary>
    /// Gets all active services
    /// </summary>
    public IEnumerable<InvokedService> GetActiveServices()
    {
        return _activeServices.Values;
    }

    /// <summary>
    /// Checks if a state has active services
    /// </summary>
    public bool HasActiveServices(CompoundState state)
    {
        return _activeServices.Values.Any(s => s.InvokingState == state);
    }
}