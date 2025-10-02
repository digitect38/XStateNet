using System;
using System.Threading.Tasks;
using XStateNet.Orchestration;

namespace XStateNet.Distributed
{
    /// <summary>
    /// Orchestrated context for distributed state machines
    /// Provides location-transparent communication across processes/nodes
    /// </summary>
    public class DistributedOrchestratedContext
    {
        private readonly string _sourceMachineId;
        private readonly IMessageBus _messageBus;
        private readonly IPureStateMachine _machine;

        public DistributedOrchestratedContext(
            string sourceMachineId,
            IMessageBus messageBus,
            IPureStateMachine machine)
        {
            _sourceMachineId = sourceMachineId;
            _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
        }

        /// <summary>
        /// Request to send event to another machine (distributed)
        /// </summary>
        public void RequestSend(string targetMachineId, string eventName, object? payload = null)
        {
            // Queue the send request - will be processed after current action completes
            _ = _messageBus.SendEventAsync(_sourceMachineId, targetMachineId, eventName, payload);
        }

        /// <summary>
        /// Request to send event to self (distributed)
        /// </summary>
        public void RequestSelfSend(string eventName, object? payload = null)
        {
            RequestSend(_sourceMachineId, eventName, payload);
        }

        /// <summary>
        /// Broadcast event to all machines (distributed)
        /// </summary>
        public void RequestBroadcast(string eventName, object? payload = null)
        {
            _ = _messageBus.PublishAsync(eventName, payload);
        }

        /// <summary>
        /// Access to underlying machine (read-only operations)
        /// </summary>
        public IPureStateMachine Machine => _machine;

        /// <summary>
        /// Current machine ID
        /// </summary>
        public string MachineId => _sourceMachineId;

        /// <summary>
        /// Get current state name
        /// </summary>
        public string CurrentState => _machine.CurrentState;
    }
}
