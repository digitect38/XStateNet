using System;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Distributed.PubSub;
using XStateNet.Distributed.EventBus;
using Microsoft.Extensions.Logging;

class TestStateChange
{
    static async Task Main()
    {
        Console.WriteLine("Testing StateChanged event...");

        var json = @"{
            'id': 'test',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'GO': 'running'
                    }
                },
                'running': {
                    'on': {
                        'STOP': 'idle'
                    }
                }
            }
        }";

        var machine = StateMachine.CreateFromScript(json, true);

        int stateChangeCount = 0;
        machine.StateChanged += (newState) =>
        {
            stateChangeCount++;
            Console.WriteLine($"State changed to: {newState}");
        };

        Console.WriteLine($"Initial state: {machine.RootState?.CurrentState?.Name}");

        await machine.StartAsync();
        Console.WriteLine($"After Start - State: {machine.RootState?.CurrentState?.Name}, Changes: {stateChangeCount}");

        machine.Send("GO");
        Console.WriteLine($"After GO - State: {machine.RootState?.CurrentState?.Name}, Changes: {stateChangeCount}");

        machine.Send("STOP");
        Console.WriteLine($"After STOP - State: {machine.RootState?.CurrentState?.Name}, Changes: {stateChangeCount}");

        Console.WriteLine($"\nTotal state changes: {stateChangeCount}");

        // Now test with EventNotificationService
        Console.WriteLine("\n\nTesting with EventNotificationService...");
        var eventBus = new InMemoryEventBus();
        var service = new EventNotificationService(machine, eventBus, "test-1");

        int notificationCount = 0;
        await eventBus.ConnectAsync();
        var sub = await eventBus.SubscribeToStateChangesAsync("test-1", evt =>
        {
            notificationCount++;
            Console.WriteLine($"Notification received: {evt.NewState}");
        });

        await service.StartAsync();

        machine.Send("GO");
        await Task.Delay(100);
        machine.Send("STOP");
        await Task.Delay(100);

        Console.WriteLine($"Notifications received: {notificationCount}");
    }
}

// Minimal InMemoryEventBus implementation for testing
public class InMemoryEventBus : IStateMachineEventBus
{
    private readonly Dictionary<string, List<Action<object>>> _handlers = new();

    public bool IsConnected { get; private set; }
    public event EventHandler<EventBusConnectedEventArgs>? Connected;
    public event EventHandler<EventBusDisconnectedEventArgs>? Disconnected;
    public event EventHandler<EventBusErrorEventArgs>? ErrorOccurred;

    public Task ConnectAsync()
    {
        IsConnected = true;
        Connected?.Invoke(this, new EventBusConnectedEventArgs { Endpoint = "memory://test" });
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task PublishStateChangeAsync(string machineId, StateChangeEvent evt)
    {
        var key = $"state.{machineId}";
        if (_handlers.TryGetValue(key, out var handlers))
        {
            foreach (var handler in handlers.ToList())
            {
                handler(evt);
            }
        }
        return Task.CompletedTask;
    }

    public Task<IDisposable> SubscribeToStateChangesAsync(string machineId, Action<StateChangeEvent> handler)
    {
        var key = $"state.{machineId}";
        if (!_handlers.ContainsKey(key))
        {
            _handlers[key] = new List<Action<object>>();
        }

        Action<object> wrapper = obj =>
        {
            if (obj is StateChangeEvent evt)
                handler(evt);
        };

        _handlers[key].Add(wrapper);
        return Task.FromResult<IDisposable>(new Subscription(() =>
        {
            if (_handlers.TryGetValue(key, out var list))
            {
                list.Remove(wrapper);
            }
        }));
    }

    // Other interface methods (not needed for test)
    public Task PublishEventAsync(string targetMachineId, string eventName, object? payload = null) => Task.CompletedTask;
    public Task BroadcastAsync(string eventName, object? payload = null, string? filter = null) => Task.CompletedTask;
    public Task PublishToGroupAsync(string groupName, string eventName, object? payload = null) => Task.CompletedTask;
    public Task<IDisposable> SubscribeToMachineAsync(string machineId, Func<StateMachineEvent, Task> handler) => Task.FromResult<IDisposable>(new Subscription(() => {}));
    public Task<IDisposable> SubscribeToBroadcastAsync(Func<StateMachineEvent, Task> handler, string? filter = null) => Task.FromResult<IDisposable>(new Subscription(() => {}));
    public Task<IDisposable> SubscribeToGroupAsync(string groupName, Func<StateMachineEvent, Task> handler) => Task.FromResult<IDisposable>(new Subscription(() => {}));
    public Task<TResponse?> RequestAsync<TResponse>(string targetMachineId, string eventName, object? payload = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => Task.FromResult<TResponse?>(default);
    public Task<IDisposable> RespondAsync<TRequest, TResponse>(string machineId, string eventName, Func<TRequest, Task<TResponse>> handler) => Task.FromResult<IDisposable>(new Subscription(() => {}));

    private class Subscription : IDisposable
    {
        private readonly Action _dispose;
        public Subscription(Action dispose) => _dispose = dispose;
        public void Dispose() => _dispose();
    }
}