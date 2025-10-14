using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Monitoring;
using Newtonsoft.Json.Linq;
using CMPSimulator.SchedulingRules;

namespace CMPSimulator.StateMachines;

/// <summary>
/// Declarative Scheduler State Machine
/// Uses a declarative rule engine to execute scheduling logic from JSON configuration
/// This replaces hardcoded scheduling logic with a configurable, XState-based DSL
/// </summary>
public class DeclarativeSchedulerMachine
{
    private readonly IPureStateMachine _machine;
    private readonly StateMachineMonitor _monitor;
    private readonly Action<string> _logger;
    private readonly EventBusOrchestrator _orchestrator;
    private StateMachine? _underlyingMachine;

    private readonly SchedulingRuleEngine _ruleEngine;

    public string CurrentState => _machine.CurrentState;

    // Expose StateChanged event for Pub/Sub
    public event EventHandler<StateTransitionEventArgs>? StateChanged
    {
        add => _monitor.StateTransitioned += value;
        remove => _monitor.StateTransitioned -= value;
    }

    // Event for completion notification
    public event EventHandler? AllWafersCompleted;

    public DeclarativeSchedulerMachine(
        string rulesFilePath,
        EventBusOrchestrator orchestrator,
        Action<string> logger,
        int totalWafers = 10)
    {
        _logger = logger;
        _orchestrator = orchestrator;

        // Load scheduling rules from JSON file
        _ruleEngine = SchedulingRuleEngine.LoadFromFile(rulesFilePath, orchestrator, logger, totalWafers);

        // Subscribe to rule engine events
        _ruleEngine.AllWafersCompleted += (s, e) =>
        {
            AllWafersCompleted?.Invoke(this, EventArgs.Empty);
        };

        var definition = """
        {
            "id": "scheduler",
            "initial": "running",
            "states": {
                "running": {
                    "entry": ["reportRunning"],
                    "on": {
                        "STATION_STATUS": {
                            "actions": ["onStationStatus"]
                        },
                        "ROBOT_STATUS": {
                            "actions": ["onRobotStatus"]
                        },
                        "CARRIER_STATUS": {
                            "actions": ["onCarrierStatus"]
                        },
                        "LOADPORT_STATUS": {
                            "actions": ["onLoadPortStatus"]
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["reportRunning"] = (ctx) =>
            {
                _logger("[DeclarativeScheduler] Running (Event-driven mode with declarative rules)");
                _logger("[DeclarativeScheduler] Scheduling logic loaded from JSON configuration");
            },

            ["onStationStatus"] = (ctx) =>
            {
                // Extract event data
                if (_underlyingMachine?.ContextMap == null) return;
                var data = _underlyingMachine.ContextMap["_event"] as JObject;
                if (data == null) return;

                var station = data["station"]?.ToString();
                var state = data["state"]?.ToString();
                var wafer = data["wafer"]?.ToObject<int?>();

                if (station == null || state == null) return;

                // Delegate to rule engine
                _ruleEngine.OnStationStatus(station, state, wafer, ctx);
            },

            ["onRobotStatus"] = (ctx) =>
            {
                // Extract event data
                if (_underlyingMachine?.ContextMap == null) return;
                var data = _underlyingMachine.ContextMap["_event"] as JObject;
                if (data == null) return;

                var robot = data["robot"]?.ToString();
                var state = data["state"]?.ToString();
                var wafer = data["wafer"]?.ToObject<int?>();
                var waitingFor = data["waitingFor"]?.ToString();

                if (robot == null || state == null) return;

                // Delegate to rule engine
                _ruleEngine.OnRobotStatus(robot, state, wafer, waitingFor, ctx);
            },

            ["onCarrierStatus"] = (ctx) =>
            {
                // Handle carrier status updates (E87)
                if (_underlyingMachine?.ContextMap == null) return;
                var data = _underlyingMachine.ContextMap["_event"] as JObject;
                if (data == null) return;

                var carrierId = data["carrierId"]?.ToString();
                var state = data["state"]?.ToString();

                if (carrierId != null && state != null)
                {
                    _logger($"[DeclarativeScheduler] 📦 CARRIER_STATUS: {carrierId} = {state}");
                }
            },

            ["onLoadPortStatus"] = (ctx) =>
            {
                // Handle LoadPort status updates (E84)
                if (_underlyingMachine?.ContextMap == null) return;
                var data = _underlyingMachine.ContextMap["_event"] as JObject;
                if (data == null) return;

                var station = data["station"]?.ToString();
                var state = data["state"]?.ToString();

                if (station != null && state != null)
                {
                    _logger($"[DeclarativeScheduler] 🚪 LOADPORT_STATUS: {station} = {state}");
                }
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: "scheduler",
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: null,
            services: null,
            enableGuidIsolation: false
        );

        _underlyingMachine = ((PureStateMachineAdapter)_machine).GetUnderlying() as StateMachine;
        _monitor = new StateMachineMonitor(_underlyingMachine!);
        _monitor.StartMonitoring();
    }

    public async Task<string> StartAsync()
    {
        var result = await _machine.StartAsync();

        var context = _orchestrator.GetOrCreateContext("scheduler");
        await context.ExecuteDeferredSends();

        return result;
    }

    // Public accessors for debugging/monitoring
    public int PendingCount => _ruleEngine.PendingCount;
    public int CompletedCount => _ruleEngine.CompletedCount;
    public IReadOnlyList<int> Completed => _ruleEngine.Completed;
}
