namespace XStateNet.Orchestration
{
    /// <summary>
    /// Distributed message bus interface stub
    /// NOTE: Actual implementation is in XStateNet.Distributed assembly to avoid circular dependencies
    /// Use XStateNet.Distributed.DistributedMessageBusAdapter instead
    /// </summary>
    /// <remarks>
    /// This is a placeholder class. For distributed scenarios, reference the XStateNet.Distributed
    /// assembly and use XStateNet.Distributed.DistributedMessageBusAdapter which properly wraps
    /// OptimizedInMemoryEventBus, RabbitMQ, Redis, or other transport mechanisms.
    /// </remarks>
    public class DistributedMessageBus : IMessageBus
    {
        public DistributedMessageBus()
        {
            throw new NotSupportedException(
                "DistributedMessageBus is a stub. Please reference XStateNet.Distributed assembly and use:\n" +
                "- XStateNet.Distributed.DistributedMessageBusAdapter for OptimizedInMemoryEventBus\n" +
                "- Or implement IMessageBus with your own transport");
        }

        public Task SendEventAsync(string sourceMachineId, string targetMachineId, string eventName, object? payload = null)
        {
            throw new NotImplementedException("Use XStateNet.Distributed assembly");
        }

        public Task<IDisposable> SubscribeAsync(string machineId, Func<MachineEvent, Task> handler)
        {
            throw new NotImplementedException("Use XStateNet.Distributed assembly");
        }

        public Task ConnectAsync()
        {
            throw new NotImplementedException("Use XStateNet.Distributed assembly");
        }

        public Task DisconnectAsync()
        {
            throw new NotImplementedException("Use XStateNet.Distributed assembly");
        }

        public Task PublishAsync(string eventName, object? payload = null)
        {
            throw new NotImplementedException("Use XStateNet.Distributed assembly");
        }

        public void Dispose()
        {
            // Stub
        }
    }
}
