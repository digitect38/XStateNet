using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace XStateNet.InterProcess.TestClient;

/// <summary>
/// Client for connecting to XStateNet InterProcess Service
/// </summary>
public class InterProcessClient : IDisposable
{
    private readonly string _pipeName;
    private readonly string _machineId;
    private NamedPipeClientStream? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Task? _receiveTask;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentQueue<MachineEvent> _receivedEvents = new();
    private readonly ConcurrentDictionary<string, List<Action<MachineEvent>>> _eventHandlers = new();

    public string MachineId => _machineId;
    public bool IsConnected => _client?.IsConnected ?? false;

    public InterProcessClient(string machineId, string pipeName = "XStateNet.MessageBus")
    {
        _machineId = machineId;
        _pipeName = pipeName;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                await _client.ConnectAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new Exception($"Failed to connect to pipe '{_pipeName}' within 5 seconds. Is the InterProcess Service running?");
            }
            catch (TimeoutException)
            {
                throw new Exception($"Failed to connect to pipe '{_pipeName}'. Is the InterProcess Service running?");
            }

            _writer = new StreamWriter(_client, Encoding.UTF8);
            _reader = new StreamReader(_client, Encoding.UTF8);

            Console.WriteLine($"[{_machineId}] ✓ Connected to InterProcess Service");

            // Register this machine BEFORE starting receive loop
            await RegisterAsync();

            // Start the background receive loop
            _cts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{_machineId}] Failed to connect: {ex.Message}");
            throw;
        }
    }

    private async Task RegisterAsync()
    {
        var message = new PipeMessage
        {
            Type = MessageType.Register,
            Payload = JsonSerializer.SerializeToElement(new
            {
                MachineId = _machineId,
                ProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName,
                ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                RegisteredAt = DateTime.UtcNow
            })
        };

        await SendMessageAsync(message);
        await Task.Delay(100);

        // Subscribe to events
        var subscribeMessage = new PipeMessage
        {
            Type = MessageType.Subscribe,
            Payload = JsonSerializer.SerializeToElement(_machineId)
        };

        await SendMessageAsync(subscribeMessage);
        await Task.Delay(100);
    }

    public async Task SendEventAsync(string targetMachineId, string eventName, object? payload = null)
    {
        if (_writer == null || !IsConnected)
        {
            throw new InvalidOperationException("Not connected to service");
        }

        var message = new PipeMessage
        {
            Type = MessageType.SendEvent,
            Payload = JsonSerializer.SerializeToElement(new
            {
                SourceMachineId = _machineId,
                TargetMachineId = targetMachineId,
                EventName = eventName,
                Payload = payload,
                Timestamp = DateTime.UtcNow
            })
        };

        await SendMessageAsync(message);
    }

    public void OnEvent(string eventName, Action<MachineEvent> handler)
    {
        var handlers = _eventHandlers.GetOrAdd(eventName, _ => new List<Action<MachineEvent>>());
        handlers.Add(handler);
    }

    public async Task<MachineEvent?> WaitForEventAsync(string eventName, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (DateTime.UtcNow < deadline)
        {
            // Check received events
            while (_receivedEvents.TryDequeue(out var evt))
            {
                if (evt.EventName == eventName)
                {
                    return evt;
                }
            }

            await Task.Delay(50);
        }

        return null;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader != null)
            {
                var line = await _reader.ReadLineAsync(cancellationToken);
                if (line == null) break;

                try
                {
                    var response = JsonSerializer.Deserialize<PipeResponse>(line);
                    if (response != null && response.Data != null)
                    {
                        // Check if it's an event delivery
                        var dataJson = JsonSerializer.Serialize(response.Data);

                        // Try to parse as MachineEvent
                        try
                        {
                            var evt = JsonSerializer.Deserialize<MachineEvent>(dataJson);
                            if (evt != null && !string.IsNullOrEmpty(evt.EventName))
                            {
                                HandleReceivedEvent(evt);
                            }
                        }
                        catch
                        {
                            // Not an event, ignore
                        }
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"[{_machineId}] Failed to parse message: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{_machineId}] Error processing message: {ex.Message}");
                }
            }
        }
        catch (IOException)
        {
            // Connection closed
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{_machineId}] Error in receive loop: {ex.Message}");
        }
    }

    private void HandleReceivedEvent(MachineEvent evt)
    {
        Console.WriteLine($"[{_machineId}] ✓ Received: {evt.EventName} from {evt.SourceMachineId}");

        _receivedEvents.Enqueue(evt);

        // Invoke handlers
        if (_eventHandlers.TryGetValue(evt.EventName, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    handler(evt);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{_machineId}] Error in event handler: {ex.Message}");
                }
            }
        }
    }

    private async Task SendMessageAsync(PipeMessage message)
    {
        if (_writer == null)
            throw new InvalidOperationException("Writer not initialized");

        var json = JsonSerializer.Serialize(message);
        await _writer.WriteLineAsync(json);
        await _writer.FlushAsync();
    }

    private async Task<PipeResponse?> ReadResponseAsync(CancellationToken cancellationToken = default)
    {
        if (_reader == null)
            throw new InvalidOperationException("Reader not initialized");

        var line = await _reader.ReadLineAsync(cancellationToken);
        if (line == null) return null;

        return JsonSerializer.Deserialize<PipeResponse>(line);
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _receiveTask?.Wait(1000);
        }
        catch { }

        try
        {
            _reader?.Dispose();
            _writer?.Dispose();
            _client?.Dispose();
            _cts?.Dispose();
        }
        catch { }

        Console.WriteLine($"[{_machineId}] Disconnected");
    }
}

// Protocol types
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

public record MachineEvent(
    string SourceMachineId,
    string TargetMachineId,
    string EventName,
    object? Payload,
    DateTime Timestamp);
