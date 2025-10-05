using XStateNet.Orchestration;

namespace XStateNet.Semi.Schedulers;

/// <summary>
/// Load Port Station - Entry/Exit point for wafers
/// </summary>
public class LoadPortStation
{
    private readonly string _stationId;
    private readonly string _instanceId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    public string MachineId => $"{_stationId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public LoadPortStation(string stationId, EventBusOrchestrator orchestrator)
    {
        _stationId = stationId;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
        _orchestrator = orchestrator;

        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'idle',
            states: {
                idle: {
                    on: {
                        LOAD_WAFER: {
                            target: 'loading',
                            actions: ['startLoad']
                        },
                        UNLOAD_WAFER: {
                            target: 'unloading',
                            actions: ['startUnload']
                        }
                    }
                },
                loading: {
                    after: {
                        '500': {
                            target: 'ready',
                            actions: ['completeLoad']
                        }
                    }
                },
                ready: {
                    entry: ['notifyReady'],
                    on: {
                        WAFER_PICKED: 'idle',
                        WAFER_PLACED: 'idle'
                    }
                },
                unloading: {
                    after: {
                        '500': {
                            target: 'idle',
                            actions: ['completeUnload']
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["startLoad"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📥 Loading wafer...");
            },

            ["completeLoad"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Wafer ready for pickup");
            },

            ["notifyReady"] = (ctx) =>
            {
                // Notify scheduler that load port is ready
            },

            ["startUnload"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📤 Unloading wafer...");
            },

            ["completeUnload"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Wafer unloaded - ready for next");
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: new Dictionary<string, Func<StateMachine, bool>>(),
            enableGuidIsolation: false
        );
    }

    public async Task<string> StartAsync() => await _machine.StartAsync();
    public string GetCurrentState() => _machine.CurrentState;
}
