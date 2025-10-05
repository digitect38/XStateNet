using XStateNet.Orchestration;

namespace XStateNet.Semi.Schedulers;

/// <summary>
/// Post-Cleaning Station - Cleans wafers after polishing
/// </summary>
public class PostCleaningStation
{
    private readonly string _stationId;
    private readonly string _instanceId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    public string MachineId => $"{_stationId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public PostCleaningStation(string stationId, EventBusOrchestrator orchestrator)
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
                        '400': 'cleaning'
                    }
                },
                cleaning: {
                    entry: ['startCleaning'],
                    after: {
                        '2000': 'drying'
                    }
                },
                drying: {
                    entry: ['startDrying'],
                    after: {
                        '1500': 'unloading'
                    }
                },
                unloading: {
                    entry: ['unloadWafer'],
                    after: {
                        '400': {
                            target: 'ready',
                            actions: ['cleaningComplete']
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
                Console.WriteLine($"[{MachineId}] 📥 Accepting wafer {waferId} for cleaning");
            },

            ["loadWafer"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Loading wafer into cleaning chamber...");
            },

            ["startCleaning"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🧼 Cleaning wafer (removing slurry residue)...");
            },

            ["startDrying"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 💨 Drying wafer...");
            },

            ["unloadWafer"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Unloading clean wafer...");
            },

            ["cleaningComplete"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Cleaning complete - wafer ready for return");
            },

            ["notifyReady"] = (ctx) =>
            {
                // Notify scheduler that cleaning is complete
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
