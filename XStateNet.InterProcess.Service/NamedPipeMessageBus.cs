using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace XStateNet.InterProcess.Service;

/// <summary>
/// Named Pipe-based message bus for InterProcess communication
/// High performance IPC for Windows and Linux
/// </summary>
public class NamedPipeMessageBus : IInterProcessMessageBus
{
    private readonly string _pipeName;
    private readonly ILogger<NamedPipeMessageBus> _logger;
    private readonly ConcurrentDictionary<string, MachineRegistration> _registeredMachines = new();
    private readonly ConcurrentDictionary<string, List<Func<MachineEvent, Task>>> _subscriptions = new();
    private readonly ConcurrentDictionary<string, StreamWriter> _clientWriters = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writerLocks = new();
    private readonly ConcurrentBag<Task> _clientHandlers = new();
    private CancellationTokenSource? _serverCts;
    private Task? _serverTask;
    private DateTime _lastActivityAt = DateTime.UtcNow;
    private int _connectionCount;

    public int ConnectionCount => _connectionCount;

    public NamedPipeMessageBus(string pipeName, ILogger<NamedPipeMessageBus> logger)
    {
        _pipeName = pipeName;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Named Pipe Message Bus on pipe: {PipeName}", _pipeName);

        _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _serverTask = Task.Run(() => RunServerAsync(_serverCts.Token), _serverCts.Token);

        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping Named Pipe Message Bus");

        _serverCts?.Cancel();

        if (_serverTask != null)
        {
            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        await Task.WhenAll(_clientHandlers.ToArray());
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _logger.LogDebug("Waiting for client connection...");

                await server.WaitForConnectionAsync(cancellationToken);

                Interlocked.Increment(ref _connectionCount);
                _lastActivityAt = DateTime.UtcNow;

                _logger.LogInformation("Client connected. Total connections: {Count}", _connectionCount);

                // Handle client in background
                var clientTask = Task.Run(() => HandleClientAsync(server, cancellationToken), cancellationToken);
                _clientHandlers.Add(clientTask);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in server loop");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        StreamReader? reader = null;
        StreamWriter? writer = null;
        var writerLock = new SemaphoreSlim(1, 1); // Lock for this connection

        try
        {
            reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

            while (!cancellationToken.IsCancellationRequested && stream.IsConnected)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break;

                _lastActivityAt = DateTime.UtcNow;

                try
                {
                    var message = JsonSerializer.Deserialize<PipeMessage>(line);
                    if (message != null)
                    {
                        await ProcessMessageAsync(message, writer, writerLock, cancellationToken);
                    }
                    else
                    {
                        _logger.LogWarning("Deserialized message is null");
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid JSON received");
                    await WriteResponseAsync(writer, writerLock, new PipeResponse
                    {
                        Success = false,
                        Error = "Invalid JSON format"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message");
                    await WriteResponseAsync(writer, writerLock, new PipeResponse
                    {
                        Success = false,
                        Error = ex.Message
                    });
                }
            }
        }
        catch (IOException)
        {
            // Client disconnected
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client");
        }
        finally
        {
            try
            {
                // Remove client writers for machines that were using this connection
                var disconnectedMachines = _clientWriters.Where(kvp => kvp.Value == writer).Select(kvp => kvp.Key).ToList();
                foreach (var machineId in disconnectedMachines)
                {
                    _clientWriters.TryRemove(machineId, out _);

                    // Remove the lock reference (but don't dispose - we'll dispose once below)
                    _writerLocks.TryRemove(machineId, out _);

                    _logger.LogInformation("Removed writer for disconnected machine: {MachineId}", machineId);
                }

                // Dispose writer and reader
                writer?.Dispose();
                reader?.Dispose();

                // Dispose the connection lock
                writerLock?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up client connection");
            }

            Interlocked.Decrement(ref _connectionCount);
            _logger.LogInformation("Client disconnected. Remaining connections: {Count}", _connectionCount);
        }
    }

    private async Task WriteResponseAsync(StreamWriter writer, SemaphoreSlim writerLock, PipeResponse response)
    {
        await writerLock.WaitAsync();
        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            await writer.FlushAsync();
        }
        finally
        {
            writerLock.Release();
        }
    }

    private async Task ProcessMessageAsync(PipeMessage message, StreamWriter writer, SemaphoreSlim writerLock, CancellationToken cancellationToken)
    {
        try
        {
            switch (message.Type)
            {
                case MessageType.Register:
                    await HandleRegisterAsync(message, writer, writerLock);
                    break;

                case MessageType.Unregister:
                    await HandleUnregisterAsync(message, writer, writerLock);
                    break;

                case MessageType.SendEvent:
                    await HandleSendEventAsync(message, writer, writerLock);
                    break;

                case MessageType.Subscribe:
                    await HandleSubscribeAsync(message, writer, writerLock);
                    break;

                default:
                    await WriteResponseAsync(writer, writerLock, new PipeResponse
                    {
                        Success = false,
                        Error = $"Unknown message type: {message.Type}"
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message: {Type}", message.Type);
            await WriteResponseAsync(writer, writerLock, new PipeResponse
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    private async Task HandleRegisterAsync(PipeMessage message, StreamWriter writer, SemaphoreSlim writerLock)
    {
        if (message.Payload == null)
        {
            await WriteResponseAsync(writer, writerLock, new PipeResponse
            {
                Success = false,
                Error = "Payload is null"
            });
            return;
        }

        var reg = JsonSerializer.Deserialize<MachineRegistration>(message.Payload.Value.GetRawText());
        if (reg != null)
        {
            _registeredMachines[reg.MachineId] = reg;
            _logger.LogInformation("Machine registered: {MachineId} (PID: {ProcessId})", reg.MachineId, reg.ProcessId);

            await WriteResponseAsync(writer, writerLock, new PipeResponse
            {
                Success = true,
                Data = new { MachineId = reg.MachineId, RegisteredAt = reg.RegisteredAt }
            });
        }
    }

    private async Task HandleUnregisterAsync(PipeMessage message, StreamWriter writer, SemaphoreSlim writerLock)
    {
        var machineId = message.Payload?.GetString();
        if (!string.IsNullOrEmpty(machineId))
        {
            _registeredMachines.TryRemove(machineId, out _);
            _subscriptions.TryRemove(machineId, out _);
            _logger.LogInformation("Machine unregistered: {MachineId}", machineId);

            await WriteResponseAsync(writer, writerLock, new PipeResponse
            {
                Success = true
            });
        }
    }

    private async Task HandleSendEventAsync(PipeMessage message, StreamWriter writer, SemaphoreSlim writerLock)
    {
        if (message.Payload == null)
        {
            await WriteResponseAsync(writer, writerLock, new PipeResponse
            {
                Success = false,
                Error = "Payload is null"
            });
            return;
        }

        var evt = JsonSerializer.Deserialize<MachineEvent>(message.Payload.Value.GetRawText());
        if (evt != null)
        {
            _logger.LogDebug("Routing event: {Source} -> {Target}: {Event}",
                evt.SourceMachineId, evt.TargetMachineId, evt.EventName);

            // Send event to target machine via its pipe
            if (_clientWriters.TryGetValue(evt.TargetMachineId, out var targetWriter) &&
                _writerLocks.TryGetValue(evt.TargetMachineId, out var targetLock))
            {
                try
                {
                    // Send event to target client
                    var eventResponse = new PipeResponse
                    {
                        Success = true,
                        Data = evt
                    };

                    // Lock to prevent concurrent writes to same StreamWriter
                    await targetLock.WaitAsync();
                    try
                    {
                        await targetWriter.WriteLineAsync(JsonSerializer.Serialize(eventResponse));
                        await targetWriter.FlushAsync();
                    }
                    finally
                    {
                        targetLock.Release();
                    }

                    // Also invoke local handlers if any
                    if (_subscriptions.TryGetValue(evt.TargetMachineId, out var handlers))
                    {
                        foreach (var handler in handlers)
                        {
                            try
                            {
                                await handler(evt);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error invoking local handler for {MachineId}", evt.TargetMachineId);
                            }
                        }
                    }

                    // Send success response back to sender
                    await WriteResponseAsync(writer, writerLock, new PipeResponse
                    {
                        Success = true,
                        Data = new { Delivered = true, TargetMachineId = evt.TargetMachineId }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error delivering event to {Target}", evt.TargetMachineId);
                    await WriteResponseAsync(writer, writerLock, new PipeResponse
                    {
                        Success = false,
                        Error = $"Failed to deliver event: {ex.Message}"
                    });
                }
            }
            else
            {
                _logger.LogWarning("No pipe connection for target machine: {MachineId}", evt.TargetMachineId);
                await WriteResponseAsync(writer, writerLock, new PipeResponse
                {
                    Success = false,
                    Error = $"Target machine not connected: {evt.TargetMachineId}"
                });
            }
        }
    }

    private async Task HandleSubscribeAsync(PipeMessage message, StreamWriter writer, SemaphoreSlim writerLock)
    {
        var machineId = message.Payload?.GetString();
        if (!string.IsNullOrEmpty(machineId))
        {
            // Store the writer for this machine so we can send events to it
            _clientWriters[machineId] = writer;

            // Store the SAME lock from the connection (don't create new one!)
            _writerLocks[machineId] = writerLock;

            _logger.LogInformation("Machine subscribed: {MachineId}", machineId);

            await WriteResponseAsync(writer, writerLock, new PipeResponse
            {
                Success = true,
                Data = new { MachineId = machineId, SubscribedAt = DateTime.UtcNow }
            });
        }
    }

    public async Task SendEventAsync(string sourceMachineId, string targetMachineId, string eventName, object? payload = null)
    {
        var evt = new MachineEvent(sourceMachineId, targetMachineId, eventName, payload, DateTime.UtcNow);

        _logger.LogDebug("Sending event: {Source} -> {Target}: {Event}", sourceMachineId, targetMachineId, eventName);

        if (_subscriptions.TryGetValue(targetMachineId, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    await handler(evt);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error invoking handler for {MachineId}", targetMachineId);
                }
            }
        }
        else
        {
            _logger.LogWarning("No subscribers for target machine: {MachineId}", targetMachineId);
        }
    }

    public Task<IDisposable> SubscribeAsync(string machineId, Func<MachineEvent, Task> handler)
    {
        var handlers = _subscriptions.GetOrAdd(machineId, _ => new List<Func<MachineEvent, Task>>());
        handlers.Add(handler);

        _logger.LogInformation("Handler subscribed for machine: {MachineId}", machineId);

        return Task.FromResult<IDisposable>(new Subscription(() =>
        {
            handlers.Remove(handler);
            if (handlers.Count == 0)
            {
                _subscriptions.TryRemove(machineId, out _);
            }
        }));
    }

    public Task RegisterMachineAsync(string machineId, MachineRegistration registration)
    {
        _registeredMachines[machineId] = registration;
        _logger.LogInformation("Machine registered: {MachineId}", machineId);
        return Task.CompletedTask;
    }

    public Task UnregisterMachineAsync(string machineId)
    {
        _registeredMachines.TryRemove(machineId, out _);
        _subscriptions.TryRemove(machineId, out _);
        _logger.LogInformation("Machine unregistered: {MachineId}", machineId);
        return Task.CompletedTask;
    }

    public HealthStatus GetHealthStatus()
    {
        return new HealthStatus(
            IsHealthy: true,
            ConnectionCount: _connectionCount,
            RegisteredMachines: _registeredMachines.Count,
            LastActivityAt: _lastActivityAt
        );
    }

    public void Dispose()
    {
        _serverCts?.Cancel();
        _serverCts?.Dispose();
    }

    private class Subscription : IDisposable
    {
        private readonly Action _dispose;

        public Subscription(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose() => _dispose();
    }
}

// Protocol messages
internal enum MessageType
{
    Register,
    Unregister,
    SendEvent,
    Subscribe
}

internal record PipeMessage
{
    public MessageType Type { get; set; }
    public JsonElement? Payload { get; set; }
}

internal record PipeResponse
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }
}
