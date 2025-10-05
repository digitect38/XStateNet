using XStateNet.Orchestration;

namespace XStateNet.Semi.Schedulers;

/// <summary>
/// Multi-Station CMP Master Scheduler
/// Orchestrates wafer flow: Loadport → WTR1 → Polishing → WTR2 → PostCleaning → WTR1 → Loadport
/// </summary>
public class MultiStationCMPMasterScheduler
{
    private readonly string _schedulerId;
    private readonly string _instanceId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;
    private readonly int _maxWip;

    private readonly Dictionary<string, StationInfo> _stations = new();
    private readonly Queue<WaferRequest> _waferQueue = new();
    private int _currentWip = 0;
    private int _totalWafersProcessed = 0;

    public string MachineId => $"MULTISTATION_SCHEDULER_{_schedulerId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public MultiStationCMPMasterScheduler(string schedulerId, EventBusOrchestrator orchestrator, int maxWip = 5)
    {
        _schedulerId = schedulerId;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
        _orchestrator = orchestrator;
        _maxWip = maxWip;

        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'idle',
            context: {
                wipCount: 0,
                maxWip: {{_maxWip}},
                pendingWafers: 0
            },
            states: {
                idle: {
                    entry: ['logIdle'],
                    on: {
                        WAFER_ARRIVED: {
                            target: 'dispatching',
                            actions: ['enqueueWafer']
                        },
                        STATION_READY: {
                            target: 'dispatching'
                        }
                    }
                },
                dispatching: {
                    entry: ['dispatchNextWafer'],
                    after: {
                        '100': 'idle'
                    }
                },
                waiting: {
                    entry: ['logWaiting'],
                    on: {
                        WAFER_ARRIVED: {
                            actions: ['enqueueWafer']
                        },
                        STATION_READY: {
                            target: 'dispatching'
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logIdle"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] 💤 Scheduler idle - WIP: {_currentWip}/{_maxWip}"),

            ["enqueueWafer"] = (ctx) =>
            {
                var waferId = "";
                var lotId = "";
                var recipeId = "DEFAULT";

                var wafer = new WaferRequest
                {
                    WaferId = waferId,
                    LotId = lotId,
                    RecipeId = recipeId,
                    ArrivalTime = DateTime.UtcNow,
                    CurrentStep = ProcessStep.AtLoadPort
                };

                _waferQueue.Enqueue(wafer);
                Console.WriteLine($"[{MachineId}] 📥 Wafer {waferId} queued (Queue: {_waferQueue.Count})");
            },

            ["dispatchNextWafer"] = async (ctx) =>
            {
                if (_currentWip >= _maxWip || _waferQueue.Count == 0)
                {
                    Console.WriteLine($"[{MachineId}] ⏸️ Cannot dispatch (WIP: {_currentWip}/{_maxWip}, Queue: {_waferQueue.Count})");
                    return;
                }

                var wafer = _waferQueue.Dequeue();
                _currentWip++;

                // Start wafer at load port
                var loadPort = GetAvailableStation("LoadPort");
                if (loadPort != null)
                {
                    ctx.RequestSend(loadPort.StationId, "LOAD_WAFER", new
                    {
                        waferId = wafer.WaferId,
                        lotId = wafer.LotId,
                        recipeId = wafer.RecipeId
                    });

                    Console.WriteLine($"[{MachineId}] ✅ Wafer {wafer.WaferId} dispatched to {loadPort.StationId}");
                }
            },

            ["logWaiting"] = (ctx) =>
                Console.WriteLine($"[{MachineId}] ⏳ Waiting - {_waferQueue.Count} wafers queued"),
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

    public async Task RegisterStationAsync(string stationId, string stationType)
    {
        _stations[stationId] = new StationInfo
        {
            StationId = stationId,
            StationType = stationType,
            IsAvailable = true
        };

        Console.WriteLine($"[{MachineId}] 🔧 Registered station: {stationId} ({stationType})");
    }

    private StationInfo? GetAvailableStation(string stationType)
    {
        return _stations.Values
            .Where(s => s.StationType == stationType && s.IsAvailable)
            .FirstOrDefault();
    }

    public async Task<string> StartAsync() => await _machine.StartAsync();
    public string GetCurrentState() => _machine.CurrentState;
    public int GetCurrentWip() => _currentWip;
    public int GetQueueLength() => _waferQueue.Count;
    public int GetTotalWafersProcessed() => _totalWafersProcessed;
    public int GetMaxWip() => _maxWip;
}

public class WaferRequest
{
    public string WaferId { get; set; } = "";
    public string LotId { get; set; } = "";
    public string RecipeId { get; set; } = "";
    public DateTime ArrivalTime { get; set; }
    public ProcessStep CurrentStep { get; set; }
}

public enum ProcessStep
{
    AtLoadPort,
    InWTR1,
    AtPolishing,
    InWTR2,
    AtPostCleaning,
    ReturningToLoadPort,
    Completed
}

public class StationInfo
{
    public string StationId { get; set; } = "";
    public string StationType { get; set; } = "";
    public bool IsAvailable { get; set; }
}
