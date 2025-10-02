using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using XStateNet.Distributed.EventBus.Optimized;
using XStateNet.Orchestration;

namespace XStateNet.Distributed
{
    /// <summary>
    /// Distributed message bus adapter implementation for inter-process and inter-node communication
    /// Wraps OptimizedInMemoryEventBus and implements IMessageBus from XStateNet.Orchestration
    /// </summary>
    public class DistributedMessageBusAdapter : IMessageBus
    {
        private readonly OptimizedInMemoryEventBus _eventBus;
        private readonly ConcurrentDictionary<string, IDisposable> _subscriptions;
        private bool _disposed;

        public DistributedMessageBusAdapter(OptimizedInMemoryEventBus eventBus)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _subscriptions = new ConcurrentDictionary<string, IDisposable>();
        }

        public DistributedMessageBusAdapter(int workerCount = 4)
        {
            _eventBus = new OptimizedInMemoryEventBus(logger: null); // workerCount removed from API
            _subscriptions = new ConcurrentDictionary<string, IDisposable>();
        }

        public async Task SendEventAsync(string sourceMachineId, string targetMachineId, string eventName, object? payload = null)
        {
            await _eventBus.PublishEventAsync(targetMachineId, eventName, payload);
        }

        public async Task<IDisposable> SubscribeAsync(string machineId, Func<MachineEvent, Task> handler)
        {
            var subscription = await _eventBus.SubscribeToMachineAsync(machineId, async evt =>
            {
                var machineEvent = new MachineEvent
                {
                    SourceMachineId = evt.SourceMachineId ?? string.Empty,
                    TargetMachineId = machineId,
                    EventName = evt.EventName,
                    Payload = evt.Payload,
                    Timestamp = DateTime.UtcNow
                };
                await handler(machineEvent);
            });

            _subscriptions[machineId] = subscription;
            return subscription;
        }

        public async Task ConnectAsync()
        {
            await _eventBus.ConnectAsync();
        }

        public async Task DisconnectAsync()
        {
            await _eventBus.DisconnectAsync();
        }

        public async Task PublishAsync(string eventName, object? payload = null)
        {
            // Broadcast not directly supported - would need to implement via pub/sub topic
            // For now, throw not implemented
            await Task.CompletedTask;
            throw new NotImplementedException("Broadcast not yet implemented for OptimizedInMemoryEventBus");
        }

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var subscription in _subscriptions.Values)
            {
                subscription?.Dispose();
            }
            _subscriptions.Clear();

            _eventBus?.Dispose();
            _disposed = true;
        }
    }
}
