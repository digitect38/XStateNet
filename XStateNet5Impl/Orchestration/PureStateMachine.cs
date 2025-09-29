using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XStateNet.Orchestration
{
    /// <summary>
    /// Pure state machine that has NO send methods
    /// All communication must go through the orchestrator
    /// This completely eliminates the possibility of deadlocks
    /// </summary>
    public interface IPureStateMachine
    {
        string Id { get; }
        string CurrentState { get; }

        /// <summary>
        /// Process an event and return the new state
        /// NO sending to other machines allowed here
        /// </summary>
        Task<string> ProcessEventAsync(string eventName, object? eventData = null);

        /// <summary>
        /// Start the state machine
        /// </summary>
        Task<string> StartAsync();

        /// <summary>
        /// Stop the state machine
        /// </summary>
        void Stop();
    }

    /// <summary>
    /// Adapter to convert existing IStateMachine to IPureStateMachine
    /// This strips out all send capabilities
    /// </summary>
    public class PureStateMachineAdapter : IPureStateMachine
    {
        private readonly IStateMachine _underlying;
        private readonly string _id;

        public string Id => _id;
        public string CurrentState => _underlying.GetActiveStateNames();

        public PureStateMachineAdapter(string id, IStateMachine underlying)
        {
            _id = id ?? throw new ArgumentNullException(nameof(id));
            _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
        }

        public async Task<string> ProcessEventAsync(string eventName, object? eventData = null)
        {
            // IMPORTANT: We use SendAsync here but the machine has NO way to send to others
            // It can only process the event and return the new state
            return await _underlying.SendAsync(eventName, eventData);
        }

        public async Task<string> StartAsync()
        {
            var result = await _underlying.StartAsync();

            // Note: Start doesn't process deferred sends because it doesn't go through orchestrator
            // The orchestrator needs to handle this separately if needed
            return result;
        }

        public void Stop()
        {
            _underlying.Stop();
        }

        // Get the underlying IStateMachine for when we need it
        public IStateMachine GetUnderlying() => _underlying;
    }

    /// <summary>
    /// Context provided to state machine actions
    /// Actions can request sends through this context instead of directly
    /// </summary>
    public class OrchestratedContext
    {
        private readonly EventBusOrchestrator _orchestrator;
        private readonly string _machineId;
        private readonly Queue<DeferredSend> _deferredSends = new();

        public OrchestratedContext(EventBusOrchestrator orchestrator, string machineId)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _machineId = machineId ?? throw new ArgumentNullException(nameof(machineId));
        }

        /// <summary>
        /// Request to send an event to another machine
        /// This will be executed AFTER the current transition completes
        /// </summary>
        public void RequestSend(string toMachineId, string eventName, object? eventData = null)
        {
            _deferredSends.Enqueue(new DeferredSend
            {
                ToMachineId = toMachineId,
                EventName = eventName,
                EventData = eventData
            });
        }

        /// <summary>
        /// Request to send an event to self
        /// This will be executed AFTER the current transition completes
        /// </summary>
        public void RequestSelfSend(string eventName, object? eventData = null)
        {
            RequestSend(_machineId, eventName, eventData);
        }

        /// <summary>
        /// Execute all deferred sends
        /// Called by the orchestrator after the transition completes
        /// </summary>
        internal async Task ExecuteDeferredSends()
        {
            while (_deferredSends.Count > 0)
            {
                var send = _deferredSends.Dequeue();
                await _orchestrator.SendEventFireAndForgetAsync(
                    _machineId,
                    send.ToMachineId,
                    send.EventName,
                    send.EventData);
            }
        }

        private class DeferredSend
        {
            public string ToMachineId { get; set; } = "";
            public string EventName { get; set; } = "";
            public object? EventData { get; set; }
        }
    }

    /// <summary>
    /// Factory for creating pure state machines
    /// </summary>
    public static class PureStateMachineFactory
    {
        /// <summary>
        /// Create a pure state machine from JSON with orchestrated actions
        /// </summary>
        public static IPureStateMachine CreateFromScript(
            string id,
            string json,
            EventBusOrchestrator orchestrator,
            Dictionary<string, Action<OrchestratedContext>>? orchestratedActions = null)
        {
            // Create a persistent context for this machine
            var machineContext = orchestrator.GetOrCreateContext(id);

            // Create action map that uses the persistent context
            var actionMap = new ActionMap();

            if (orchestratedActions != null)
            {
                foreach (var (actionName, action) in orchestratedActions)
                {
                    actionMap[actionName] = new List<NamedAction>
                    {
                        new NamedAction(actionName, async (sm) =>
                        {
                            // Use the persistent context
                            action(machineContext);

                            // Note: We don't execute deferred sends here anymore
                            // The orchestrator will handle it after processing the event
                            await Task.CompletedTask;
                        })
                    };
                }
            }

            // Create the underlying state machine
            var machine = StateMachineFactory.CreateFromScript(json, false, false, actionMap);

            // Register the machine WITH its context
            orchestrator.RegisterMachineWithContext(id, machine, machineContext);

            // Wrap it as a pure state machine
            return new PureStateMachineAdapter(id, machine);
        }

        /// <summary>
        /// Create a simple pure state machine for testing
        /// </summary>
        public static IPureStateMachine CreateSimple(string id, string initialState = "idle")
        {
            var json = $@"{{
                ""id"": ""{id}"",
                ""initial"": ""{initialState}"",
                ""states"": {{
                    ""idle"": {{
                        ""on"": {{
                            ""START"": ""running"",
                            ""PING"": ""ponging""
                        }}
                    }},
                    ""running"": {{
                        ""on"": {{
                            ""STOP"": ""idle"",
                            ""PROCESS"": ""processing""
                        }}
                    }},
                    ""processing"": {{
                        ""on"": {{
                            ""DONE"": ""running"",
                            ""ERROR"": ""error""
                        }}
                    }},
                    ""ponging"": {{
                        ""on"": {{
                            ""PONG_SENT"": ""idle""
                        }}
                    }},
                    ""error"": {{
                        ""on"": {{
                            ""RESET"": ""idle""
                        }}
                    }}
                }}
            }}";

            var machine = StateMachineFactory.CreateFromScript(json, false, false);
            return new PureStateMachineAdapter(id, machine);
        }
    }
}