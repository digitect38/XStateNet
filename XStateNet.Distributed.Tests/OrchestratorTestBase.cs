using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Orchestration;

namespace XStateNet.Tests
{
    /// <summary>
    /// Base class for tests using the EventBusOrchestrator pattern
    /// </summary>
    public class OrchestratorTestBase : IDisposable
    {
        protected readonly EventBusOrchestrator _orchestrator;
        protected readonly List<IPureStateMachine> _machines;

        public OrchestratorTestBase()
        {
            var config = new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4,
                EnableMetrics = false
            };
            _orchestrator = new EventBusOrchestrator(config);
            _machines = new List<IPureStateMachine>();
        }

        protected IPureStateMachine CreateMachine(
            string id,
            string json,
            Dictionary<string, Action<OrchestratedContext>>? orchestratedActions = null)
        {
            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: id,
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: orchestratedActions,
                guards: null,
                services: null,
                delays: null,
                activities: null
            );

            _machines.Add(machine);
            return machine;
        }

        protected async Task WaitForStateAsync(IPureStateMachine machine, string expectedState, int timeoutMs = 5000)
        {
            var startTime = DateTime.UtcNow;
            while (machine.CurrentState != expectedState)
            {
                if ((DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMs)
                {
                    throw new TimeoutException($"Timeout waiting for state '{expectedState}'. Current state: '{machine.CurrentState}'");
                }
                await Task.Delay(10);
            }
        }

        public virtual void Dispose()
        {
            foreach (var machine in _machines)
            {
                machine.Stop();
            }
            _orchestrator?.Dispose();
        }
    }
}
