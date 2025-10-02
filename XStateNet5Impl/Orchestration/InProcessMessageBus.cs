using System;
using System.Threading.Tasks;

namespace XStateNet.Orchestration
{
    /// <summary>
    /// In-process message bus adapter wrapping EventBusOrchestrator
    /// Provides location transparency for local state machines
    /// </summary>
    public class InProcessMessageBus : IMessageBus
    {
        private readonly EventBusOrchestrator _orchestrator;
        private bool _disposed;

        public InProcessMessageBus(EventBusOrchestrator orchestrator)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        }

        public InProcessMessageBus(OrchestratorConfig? config = null)
        {
            _orchestrator = new EventBusOrchestrator(config ?? new OrchestratorConfig());
        }

        public Task SendEventAsync(string sourceMachineId, string targetMachineId, string eventName, object? payload = null)
        {
            return _orchestrator.SendEventAsync(sourceMachineId, targetMachineId, eventName, payload);
        }

        public Task<IDisposable> SubscribeAsync(string machineId, Func<MachineEvent, Task> handler)
        {
            // EventBusOrchestrator doesn't have explicit subscribe - machines are registered automatically
            // This is a no-op for in-process since orchestrator handles routing
            return Task.FromResult<IDisposable>(new DummySubscription());
        }

        public Task ConnectAsync()
        {
            // No connection needed for in-process
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            return Task.CompletedTask;
        }

        public Task PublishAsync(string eventName, object? payload = null)
        {
            // Broadcast to all registered machines
            // Note: EventBusOrchestrator doesn't have broadcast, so we'd need to enhance it
            throw new NotSupportedException("Broadcast is not supported in InProcessMessageBus. Use SendEventAsync to specific machines.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _orchestrator?.Dispose();
            _disposed = true;
        }

        private class DummySubscription : IDisposable
        {
            public void Dispose() { }
        }
    }
}
