namespace XStateNet.InterProcess.Service;

/// <summary>
/// Message bus interface for InterProcess communication
/// </summary>
public interface IInterProcessMessageBus : IDisposable
{
    /// <summary>
    /// Start the message bus server
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the message bus server
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send event to a specific machine
    /// </summary>
    Task SendEventAsync(string sourceMachineId, string targetMachineId, string eventName, object? payload = null);

    /// <summary>
    /// Subscribe to events for a specific machine
    /// </summary>
    Task<IDisposable> SubscribeAsync(string machineId, Func<MachineEvent, Task> handler);

    /// <summary>
    /// Register a machine with the bus
    /// </summary>
    Task RegisterMachineAsync(string machineId, MachineRegistration registration);

    /// <summary>
    /// Unregister a machine from the bus
    /// </summary>
    Task UnregisterMachineAsync(string machineId);

    /// <summary>
    /// Get current connection count
    /// </summary>
    int ConnectionCount { get; }

    /// <summary>
    /// Get health status
    /// </summary>
    HealthStatus GetHealthStatus();
}

public record MachineEvent(string SourceMachineId, string TargetMachineId, string EventName, object? Payload, DateTime Timestamp);

public record MachineRegistration(string MachineId, string ProcessName, int ProcessId, DateTime RegisteredAt);

public record HealthStatus(bool IsHealthy, int ConnectionCount, int RegisteredMachines, DateTime LastActivityAt, string? ErrorMessage = null);
