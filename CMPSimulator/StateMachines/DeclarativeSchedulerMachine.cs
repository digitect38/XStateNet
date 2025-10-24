using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Monitoring;
using Newtonsoft.Json.Linq;
using CMPSimulator.SchedulingRules;
using CMPSimulator.Controllers;
using LoggerHelper;

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
    private readonly EventBusOrchestrator _orchestrator;
    private StateMachine? _underlyingMachine;

    private readonly SchedulingRuleEngine _ruleEngine;

    public string CurrentState => _machine?.CurrentState ?? "initializing";

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
        int totalWafers = 10,
        RobotScheduler? robotScheduler = null)
    {
        _orchestrator = orchestrator;

        // Load scheduling rules from JSON file with RobotScheduler (Phase 1)
        _ruleEngine = SchedulingRuleEngine.LoadFromFile(rulesFilePath, orchestrator, totalWafers, robotScheduler);

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
                        },
                        "CARRIER_WAFER_COMPLETED": {
                            "actions": ["onCarrierWaferCompleted"]
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
                LoggerHelper.Logger.Instance.Log("[DeclarativeScheduler] Running (Event-driven mode with declarative rules)");
                LoggerHelper.Logger.Instance.Log("[DeclarativeScheduler] Scheduling logic loaded from JSON configuration");
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
                    LoggerHelper.Logger.Instance.Log($"[DeclarativeScheduler] 📦 CARRIER_STATUS: {carrierId} = {state}");
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
                    LoggerHelper.Logger.Instance.Log($"[DeclarativeScheduler] 🚪 LOADPORT_STATUS: {station} = {state}");
                }
            },

            ["onCarrierWaferCompleted"] = (ctx) =>
            {
                // Forward wafer completion to active carrier (E87)
                if (_underlyingMachine?.ContextMap == null) return;
                var data = _underlyingMachine.ContextMap["_event"] as JObject;
                if (data == null) return;

                var waferId = data["waferId"]?.ToObject<int>() ?? 0;
                if (waferId > 0)
                {
                    LoggerHelper.Logger.Instance.Log($"[DeclarativeScheduler] ✅ Wafer {waferId} completed - notifying carrier");

                    // Forward to all active carriers (they'll filter based on their wafer list)
                    // In E87, carriers track their own wafer completion
                    ctx.RequestSend("*", "WAFER_COMPLETED", new JObject
                    {
                        ["waferId"] = waferId
                    });
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

        // NOTE: ExecuteDeferredSends is now automatically handled by StateChanged event
        // Do NOT call it manually here or messages will be sent twice!

        return result;
    }

    // Public accessors for debugging/monitoring
    public int PendingCount => _ruleEngine.PendingCount;
    public int CompletedCount => _ruleEngine.CompletedCount;
    public IReadOnlyList<int> Completed => _ruleEngine.Completed;

    /// <summary>
    /// Reset the scheduler for a new carrier batch
    /// </summary>
    public void Reset(string? carrierId = null)
    {
        _ruleEngine.Reset(carrierId);
    }

    /// <summary>
    /// Pause scheduler (prevents rule execution during carrier swap)
    /// </summary>
    public void Pause()
    {
        _ruleEngine.Pause();
    }

    /// <summary>
    /// Resume scheduler (allows rule execution to continue)
    /// </summary>
    public void Resume(string? carrierId = null)
    {
        _ruleEngine.Resume(carrierId);
    }
}
