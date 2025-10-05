using XStateNet.Orchestration;

namespace XStateNet.Semi.Schedulers;

/// <summary>
/// Wafer Transfer Robot (WTR) - Moves wafers between stations
/// </summary>
public class WaferTransferRobot
{
    private readonly string _robotId;
    private readonly string _instanceId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;

    public string MachineId => $"{_robotId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public WaferTransferRobot(string robotId, EventBusOrchestrator orchestrator)
    {
        _robotId = robotId;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
        _orchestrator = orchestrator;

        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'idle',
            states: {
                idle: {
                    on: {
                        TRANSFER_REQUEST: {
                            target: 'moving',
                            actions: ['startTransfer']
                        }
                    }
                },
                moving: {
                    entry: ['logMoving'],
                    after: {
                        '800': {
                            target: 'picking',
                            actions: ['arriveAtSource']
                        }
                    }
                },
                picking: {
                    entry: ['pickWafer'],
                    after: {
                        '300': {
                            target: 'carrying',
                            actions: ['waferPicked']
                        }
                    }
                },
                carrying: {
                    entry: ['moveToDestination'],
                    after: {
                        '800': {
                            target: 'placing',
                            actions: ['arriveAtDestination']
                        }
                    }
                },
                placing: {
                    entry: ['placeWafer'],
                    after: {
                        '300': {
                            target: 'idle',
                            actions: ['transferComplete']
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["startTransfer"] = (ctx) =>
            {
                var waferId = "";
                var from = "";
                var to = "";
                Console.WriteLine($"[{MachineId}] 🤖 Transfer request: {waferId} from {from} to {to}");
            },

            ["logMoving"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🚶 Moving to source station...");
            },

            ["arriveAtSource"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📍 Arrived at source station");
            },

            ["pickWafer"] = (ctx) =>
            {
                var waferId = "";
                Console.WriteLine($"[{MachineId}] 🤲 Picking wafer {waferId}...");
            },

            ["waferPicked"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Wafer secured");
            },

            ["moveToDestination"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🚶 Moving to destination station...");
            },

            ["arriveAtDestination"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📍 Arrived at destination");
            },

            ["placeWafer"] = (ctx) =>
            {
                var waferId = "";
                Console.WriteLine($"[{MachineId}] 🤲 Placing wafer {waferId}...");
            },

            ["transferComplete"] = (ctx) =>
            {
                var waferId = "";
                Console.WriteLine($"[{MachineId}] ✅ Transfer complete for {waferId}");
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
