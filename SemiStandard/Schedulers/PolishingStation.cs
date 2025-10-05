using XStateNet.Orchestration;

namespace XStateNet.Semi.Schedulers;

/// <summary>
/// Polishing Station - Performs CMP polishing
/// </summary>
public class PolishingStation
{
    private readonly string _stationId;
    private readonly string _instanceId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    public string MachineId => $"{_stationId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public PolishingStation(string stationId, EventBusOrchestrator orchestrator)
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
                        WAFER_PLACED: {
                            target: 'loading',
                            actions: ['acceptWafer']
                        }
                    }
                },
                loading: {
                    entry: ['loadWafer'],
                    after: {
                        '600': 'polishing'
                    }
                },
                polishing: {
                    entry: ['startPolishing'],
                    after: {
                        '3500': 'unloading'
                    }
                },
                unloading: {
                    entry: ['unloadWafer'],
                    after: {
                        '600': {
                            target: 'ready',
                            actions: ['polishingComplete']
                        }
                    }
                },
                ready: {
                    entry: ['notifyReady'],
                    on: {
                        WAFER_PICKED: 'idle'
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["acceptWafer"] = (ctx) =>
            {
                var waferId = "";
                Console.WriteLine($"[{MachineId}] 📥 Accepting wafer {waferId}");
            },

            ["loadWafer"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Loading wafer onto polish head...");
            },

            ["startPolishing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 💎 Polishing in progress...");
            },

            ["unloadWafer"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Unloading polished wafer...");
            },

            ["polishingComplete"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Polishing complete - wafer ready for pickup");
            },

            ["notifyReady"] = (ctx) =>
            {
                // Notify scheduler that polishing is complete
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
