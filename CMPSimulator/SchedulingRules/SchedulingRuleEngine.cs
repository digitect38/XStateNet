using CMPSimulator.Helpers;
using CMPSimulator.Controllers;
using CMPSimulator.Models;
using LoggerHelper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using XStateNet.Orchestration;

namespace CMPSimulator.SchedulingRules;

/// <summary>
/// Scheduling Rule Engine - Interprets and executes declarative scheduling rules
/// This engine loads JSON-based scheduling rules and executes them dynamically
/// </summary>
public class SchedulingRuleEngine
{
    private readonly SchedulingRulesConfiguration _config;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly RobotScheduler? _robotScheduler; // Phase 1: Robot scheduling delegation

    // Runtime state tracking
    private readonly Dictionary<string, string> _stationStates = new();
    private readonly Dictionary<string, int?> _stationWafers = new();
    private readonly Dictionary<string, string> _robotStates = new();
    private readonly Dictionary<string, int?> _robotWafers = new();
    private readonly Dictionary<string, string> _robotWaitingFor = new();
    private readonly HashSet<string> _robotsWithPendingCommands = new();

    // Performance optimization: Track state signature to skip redundant evaluations
    private string _lastStateSignature = "";

    // Queue management
    private readonly Dictionary<string, List<int>> _queues = new();

    private int _totalWafers;
    private bool _isPaused = false; // Pause scheduler during carrier swap

    // Event for completion notification
    public event EventHandler? AllWafersCompleted;

    public SchedulingRuleEngine(
        SchedulingRulesConfiguration config,
        EventBusOrchestrator orchestrator,
        int totalWafers,
        RobotScheduler? robotScheduler = null)
    {
        _config = config;
        _orchestrator = orchestrator;
        _totalWafers = totalWafers;
        _robotScheduler = robotScheduler;

        InitializeQueues();
        LogConfiguration();
        InitializeLogFile();

        if (_robotScheduler != null)
        {
            Log("[SchedulingRuleEngine] ✓ Using RobotScheduler for robot management (Phase 1)");
        }
        else
        {
            Log("[SchedulingRuleEngine] ⚠ Using legacy direct robot commands (consider upgrading to RobotScheduler)");
        }
    }

    /// <summary>
    /// Load scheduling rules from JSON file
    /// </summary>
    public static SchedulingRuleEngine LoadFromFile(
        string filePath,
        EventBusOrchestrator orchestrator,
        int totalWafers,
        RobotScheduler? robotScheduler = null)
    {
        var json = File.ReadAllText(filePath);
        var config = JsonConvert.DeserializeObject<SchedulingRulesConfiguration>(json)
            ?? throw new InvalidOperationException("Failed to parse scheduling rules JSON");

        return new SchedulingRuleEngine(config, orchestrator, totalWafers, robotScheduler);
    }

    private void InitializeQueues()
    {
        // Initialize LoadPort.Pending queue with wafer IDs
        _queues["LoadPort.Pending"] = Enumerable.Range(1, _totalWafers).ToList();
        _queues["LoadPort.Completed"] = new List<int>();
    }

    private void LogConfiguration()
    {
        Log($"[SchedulingRuleEngine] Loaded: {_config.Id} v{_config.Version}");
        Log($"[SchedulingRuleEngine] Rules: {_config.Rules.Count}, Mode: {_config.Configuration.Mode}");
        Log($"[SchedulingRuleEngine] Parallel Execution: {_config.Configuration.EnableParallelExecution}");
    }

    /// <summary>
    /// Initialize log file with session header using singleton logger
    /// </summary>
    private void InitializeLogFile()
    {
        LoggerHelper.Logger.Instance.InitializeLog(_totalWafers);
    }

    /// <summary>
    /// Helper method to log using LoggerHelper
    /// </summary>
    private void Log(string message,
        string file = "",
        int line = 0)
    {
        LoggerHelper.Logger.Instance.Log(message, file, line);
    }

    /// <summary>
    /// Handle STATION_STATUS event
    /// </summary>
    public void OnStationStatus(string station, string state, int? waferId, OrchestratedContext ctx)
    {
        // Update tracking
        _stationStates[station] = state;
        _stationWafers[station] = waferId;

        // Only log meaningful state changes
        if (state == "empty" || state == "done" || state == "IDLE" || state == "COMPLETE" || state == "occupied")
        {
            Log($"[RuleEngine] 📥 STATION_STATUS: {station} = {state} (wafer: {waferId})");
        }

        // Check if any robot is waiting for this station
        if (state == "empty" || state == "done" || state == "IDLE" || state == "COMPLETE")
        {
            CheckWaitingRobots(ctx, station);
        }

        // Check if any rules can be executed (ALWAYS check, regardless of state)
        // This is critical for rules like P3_BufferToLoadPort which trigger on "occupied" state
        CheckAndExecuteRules(ctx);
    }

    /// <summary>
    /// Handle ROBOT_STATUS event
    /// </summary>
    public void OnRobotStatus(string robot, string state, int? waferId, string? waitingFor, OrchestratedContext ctx)
    {
        // Phase 1: Delegate to RobotScheduler first
        if (_robotScheduler != null)
        {
            _robotScheduler.UpdateRobotStatus(robot, state, waferId, waitingFor);
        }

        // Update tracking
        _robotStates[robot] = state;
        _robotWafers[robot] = waferId;

        // Clear pending command flag when robot state changes to a non-idle state
        // This prevents issuing multiple commands to the same robot before it starts executing
        // Bug fix: If robot reports "idle" immediately after we send a command, don't clear the flag
        // because the robot hasn't actually started the transfer yet (it's transitioning from idle to pickingUp)
        if (state != "idle")
        {
            _robotsWithPendingCommands.Remove(robot);
        }

        // Only log important state transitions
        if (state == "holding" || state == "idle")
        {
            Log($"[RuleEngine] 📥 ROBOT_STATUS: {robot} = {state} (wafer: {waferId})");
        }

        // When robot enters holding state, check priorities
        if (state == "holding")
        {
            CheckAndExecuteRules(ctx);
        }

        // Handle robot waiting for destination
        if (state == "holding" && waitingFor != null)
        {
            _robotWaitingFor[robot] = waitingFor;
            CheckDestinationReady(ctx, robot, waitingFor);
        }
        else if (state == "waitingDestination" && waitingFor != null)
        {
            _robotWaitingFor[robot] = waitingFor;
            CheckDestinationReady(ctx, robot, waitingFor);
        }
        else
        {
            _robotWaitingFor.Remove(robot);
        }

        // Check rules when robot becomes idle
        if (state == "idle")
        {
            CheckAndExecuteRules(ctx);
        }
    }

    /// <summary>
    /// Check if destination station is ready for a waiting robot
    /// </summary>
    private void CheckDestinationReady(OrchestratedContext ctx, string robot, string destination)
    {
        bool destReady = IsDestinationReady(robot, destination);

        if (destReady)
        {
            Log($"[RuleEngine] ✓ Destination {destination} is ready! Sending DESTINATION_READY to {robot}");
            ctx.RequestSend(robot, "DESTINATION_READY", new JObject());
            _robotWaitingFor.Remove(robot);
        }
        else
        {
            var destState = GetStationState(destination);
            Log($"[RuleEngine] ⏸ Destination {destination} not ready (state={destState ?? "N/A"}). Robot {robot} will wait.");
        }
    }

    /// <summary>
    /// Check if any robot is waiting for this station
    /// </summary>
    private void CheckWaitingRobots(OrchestratedContext ctx, string station)
    {
        foreach (var kvp in _robotWaitingFor.ToList())
        {
            if (kvp.Value == station)
            {
                var robot = kvp.Key;
                bool destReady = IsDestinationReady(robot, station);

                if (destReady)
                {
                    Log($"[RuleEngine] ✓ Station {station} is now ready! Sending DESTINATION_READY to {robot}");
                    ctx.RequestSend(robot, "DESTINATION_READY", new JObject());
                    _robotWaitingFor.Remove(robot);
                }
            }
        }
    }

    /// <summary>
    /// Check if destination is ready based on robot-specific wait conditions
    /// </summary>
    private bool IsDestinationReady(string robot, string destination)
    {
        // Check robot-specific wait conditions from configuration
        if (_config.RobotBehaviors.TryGetValue(robot, out var behavior))
        {
            if (behavior.WaitConditions.TryGetValue(destination, out var waitCondition))
            {
                if (waitCondition.AlwaysReady)
                {
                    return true;
                }

                if (waitCondition.ReadyStates != null)
                {
                    var destState = GetStationState(destination);
                    return waitCondition.ReadyStates.Contains(destState ?? "");
                }
            }
        }

        // Fallback: destination is ready if empty or idle
        var state = GetStationState(destination);
        return state == "empty" || state == "idle";
    }

    /// <summary>
    /// Check all rules and execute those that can run
    /// </summary>
    private void CheckAndExecuteRules(OrchestratedContext ctx)
    {
        // Don't execute rules if scheduler is paused (during carrier swap)
        if (_isPaused)
        {
            Log("[RuleEngine] ⏸ CheckAndExecuteRules blocked - scheduler is paused");
            return;
        }

        // PERFORMANCE OPTIMIZATION: Skip evaluation if state hasn't changed
        // Generate state signature from robot/station states and queue counts
        var currentSignature = $"R1:{_robotStates.GetValueOrDefault("R1")}:{_robotWafers.GetValueOrDefault("R1")}|" +
                              $"R2:{_robotStates.GetValueOrDefault("R2")}:{_robotWafers.GetValueOrDefault("R2")}|" +
                              $"R3:{_robotStates.GetValueOrDefault("R3")}:{_robotWafers.GetValueOrDefault("R3")}|" +
                              $"P:{_stationStates.GetValueOrDefault("polisher")}:{_stationWafers.GetValueOrDefault("polisher")}|" +
                              $"C:{_stationStates.GetValueOrDefault("cleaner")}:{_stationWafers.GetValueOrDefault("cleaner")}|" +
                              $"B:{_stationStates.GetValueOrDefault("buffer")}:{_stationWafers.GetValueOrDefault("buffer")}|" +
                              $"Q:{_queues.GetValueOrDefault("LoadPort.Pending")?.Count ?? 0}";

        if (currentSignature == _lastStateSignature)
        {
            // State hasn't changed - skip evaluation to save CPU cycles
            return;
        }

        _lastStateSignature = currentSignature;

        // Sort rules by priority (lower number = higher priority)
        var sortedRules = _config.Rules
            .OrderBy(r => r.Priority)
            .ToList();

        // If parallel execution is enabled, execute all eligible rules
        // If not, execute only the first eligible rule
        bool anyExecuted = false;
        int rulesEvaluated = 0;

        foreach (var rule in sortedRules)
        {
            // Skip automatic rules (handled by state machines)
            if (rule.Action.Type == "automatic")
            {
                continue;
            }

            rulesEvaluated++;

            // Check if rule conditions are met
            bool canExecute = EvaluateCondition(rule.Conditions);

            // Log P4 rule evaluation for debugging wafer i-3 constraint
            if (rule.Id == "P4_LoadPortToHold" && !canExecute)
            {
                var pending = _queues.GetValueOrDefault("LoadPort.Pending")?.Count ?? 0;
                var completed = _queues.GetValueOrDefault("LoadPort.Completed")?.Count ?? 0;
                var nextWafer = pending > 0 ? _queues["LoadPort.Pending"][0] : 0;
                var r1State = GetRobotState("R1");
                Log($"[RuleEngine] ⏸ P4 rule blocked: NextWafer={nextWafer}, R1={r1State}, Pending={pending}, Completed={completed}");
            }

            if (canExecute)
            {
                ExecuteRule(rule, ctx);
                anyExecuted = true;

                // If parallel execution is disabled, stop after first rule
                if (!_config.Configuration.EnableParallelExecution)
                {
                    break;
                }
            }
        }

        // DEBUG: Log when no rules executed
        if (!anyExecuted && rulesEvaluated > 0)
        {
            var pending = _queues.GetValueOrDefault("LoadPort.Pending")?.Count ?? 0;
            var completed = _queues.GetValueOrDefault("LoadPort.Completed")?.Count ?? 0;
            Log($"[RuleEngine] 🔍 No rules executed (evaluated {rulesEvaluated} rules, pending={pending}, completed={completed})");
        }
    }

    /// <summary>
    /// Evaluate a condition tree
    /// </summary>
    private bool EvaluateCondition(Condition condition)
    {
        switch (condition.Type.ToLowerInvariant())
        {
            case "and":
                return condition.Rules?.All(EvaluateCondition) ?? false;

            case "or":
                return condition.Rules?.Any(EvaluateCondition) ?? false;

            case "not":
                return condition.Rules != null && condition.Rules.Count > 0 && !EvaluateCondition(condition.Rules[0]);

            case "stationstate":
                return EvaluateStationState(condition);

            case "robotstate":
                return EvaluateRobotState(condition);

            case "queuecount":
                return EvaluateQueueCount(condition);

            case "queuecontains":
                return EvaluateQueueContains(condition);

            case "comment":
                // Comment conditions always return false (documentation only)
                return false;

            default:
                Log($"[RuleEngine] ⚠ Unknown condition type: {condition.Type}");
                return false;
        }
    }

    private bool EvaluateStationState(Condition condition)
    {
        if (condition.Station == null || condition.Value == null)
            return false;

        var actualState = GetStationState(condition.Station);
        var expectedState = condition.Value.ToString();

        // Check state mapping for aliases (e.g., "done" = ["done", "COMPLETE"])
        if (_config.StateMapping.TryGetValue(condition.Station, out var stationMapping))
        {
            if (stationMapping.TryGetValue(expectedState ?? "", out var aliases))
            {
                return aliases.Contains(actualState ?? "");
            }
        }

        // Direct comparison
        return condition.Operator?.ToLowerInvariant() switch
        {
            "equals" => actualState == expectedState,
            "notequals" => actualState != expectedState,
            _ => actualState == expectedState
        };
    }

    private bool EvaluateRobotState(Condition condition)
    {
        if (condition.Robot == null || condition.Value == null)
            return false;

        var actualState = GetRobotState(condition.Robot);
        var expectedState = condition.Value.ToString();

        return condition.Operator?.ToLowerInvariant() switch
        {
            "equals" => actualState == expectedState,
            "notequals" => actualState != expectedState,
            _ => actualState == expectedState
        };
    }

    private bool EvaluateQueueCount(Condition condition)
    {
        if (condition.Queue == null || condition.Value == null)
            return false;

        if (!_queues.TryGetValue(condition.Queue, out var queue))
            return false;

        var count = queue.Count;
        var expectedValue = Convert.ToInt32(condition.Value);

        return condition.Operator?.ToLowerInvariant() switch
        {
            "greaterthan" => count > expectedValue,
            "lessthan" => count < expectedValue,
            "equals" => count == expectedValue,
            _ => false
        };
    }

    private bool EvaluateQueueContains(Condition condition)
    {
        if (condition.Queue == null || condition.Value == null)
            return false;

        if (!_queues.TryGetValue(condition.Queue, out var queue))
            return false;

        // Resolve value - it can be a reference like @LoadPort.Pending[0]-3
        var value = condition.Value;
        int searchValue;
        int? nextWafer = null; // Track the next wafer for logging

        if (value is string str && str.StartsWith("@"))
        {
            // Handle arithmetic expressions: @LoadPort.Pending[0]-3
            if (str.Contains("-"))
            {
                var parts = str.Split('-');
                var baseValue = ResolveReference(parts[0]);
                var offset = int.Parse(parts[1]);
                nextWafer = Convert.ToInt32(baseValue);
                searchValue = nextWafer.Value - offset;
            }
            else if (str.Contains("+"))
            {
                var parts = str.Split('+');
                var baseValue = ResolveReference(parts[0]);
                var offset = int.Parse(parts[1]);
                nextWafer = Convert.ToInt32(baseValue);
                searchValue = nextWafer.Value + offset;
            }
            else
            {
                searchValue = Convert.ToInt32(ResolveReference(str));
            }
        }
        else
        {
            searchValue = Convert.ToInt32(value);
        }

        // Check if queue contains the value
        bool contains = queue.Contains(searchValue);

        // Log the constraint check
        if (nextWafer.HasValue)
        {
            if (contains)
            {
                Log($"[RuleEngine] ✓ Wafer i-3 constraint satisfied: Wafer {nextWafer} can go (wafer {searchValue} is completed)");
            }
            else
            {
                Log($"[RuleEngine] ⏸ Wafer i-3 constraint BLOCKED: Wafer {nextWafer} must wait (wafer {searchValue} not yet completed)");
            }
        }

        // Support both "contains" and "notContains" operators
        return condition.Operator?.ToLowerInvariant() switch
        {
            "contains" => contains,
            "notcontains" => !contains,
            _ => contains
        };
    }

    /// <summary>
    /// Execute a scheduling rule
    /// </summary>
    private void ExecuteRule(SchedulingRule rule, OrchestratedContext ctx)
    {
        Log($"[RuleEngine] [P{rule.Priority}] Executing rule: {rule.Id} ({rule.Description})");

        // Execute action
        if (rule.Action.Type == "transfer" && rule.Action.Parameters != null)
        {
            var parameters = ResolveParameters(rule.Action.Parameters);
            var robot = parameters.GetValueOrDefault("robot")?.ToString() ?? rule.Robot;
            var waferId = Convert.ToInt32(parameters.GetValueOrDefault("waferId"));

            Log($"[RuleEngine] [P{rule.Priority}] {rule.From}→{rule.To}: Commanding {robot} to transfer wafer {waferId}");

            // Phase 1: Use RobotScheduler if available
            if (_robotScheduler != null)
            {
                // NEW: Delegate to RobotScheduler (null-safe!)
                try
                {
                    var request = new TransferRequest
                    {
                        WaferId = waferId,
                        From = rule.From ?? throw new InvalidOperationException("Rule.From is null"),
                        To = rule.To ?? throw new InvalidOperationException("Rule.To is null"),
                        Priority = rule.Priority,
                        PreferredRobotId = robot
                    };

                    _robotScheduler.RequestTransfer(request);
                    _robotsWithPendingCommands.Add(robot);
                }
                catch (ArgumentException ex)
                {
                    Log($"[RuleEngine] ❌ CRITICAL: Transfer request validation failed: {ex.Message}");
                    Log($"[RuleEngine] ❌ Rule: {rule.Id}, WaferId: {waferId}, From: {rule.From ?? "NULL"}, To: {rule.To ?? "NULL"}");
                }
            }
            else
            {
                // LEGACY: Direct robot command (old behavior)
                _robotsWithPendingCommands.Add(robot);

                ctx.RequestSend(robot, rule.Action.Command ?? "TRANSFER", new JObject
                {
                    ["waferId"] = waferId,
                    ["from"] = rule.From,
                    ["to"] = rule.To
                });

                Log($"[RuleEngine] 📤 TRANSFER command sent: {robot} <- TRANSFER(wafer={waferId}, from={rule.From}, to={rule.To})");
            }
        }

        // Execute effects
        if (rule.Effects != null)
        {
            foreach (var effect in rule.Effects)
            {
                ExecuteEffect(effect, ctx);
            }
        }
    }

    /// <summary>
    /// Resolve parameters (handle references like @station.waferId)
    /// </summary>
    private Dictionary<string, object> ResolveParameters(Dictionary<string, object> parameters)
    {
        var resolved = new Dictionary<string, object>();

        foreach (var kvp in parameters)
        {
            var value = kvp.Value;

            if (value is string str && str.StartsWith("@"))
            {
                // Resolve reference
                resolved[kvp.Key] = ResolveReference(str);
            }
            else
            {
                resolved[kvp.Key] = value;
            }
        }

        return resolved;
    }

    /// <summary>
    /// Resolve references like @station.waferId or @queue[0]
    /// </summary>
    private object ResolveReference(string reference)
    {
        // Remove @ prefix
        var path = reference.Substring(1);

        // Handle queue index access: LoadPort.Pending[0]
        if (path.Contains("[") && path.Contains("]"))
        {
            var queueName = path.Substring(0, path.IndexOf("["));
            var indexStr = path.Substring(path.IndexOf("[") + 1, path.IndexOf("]") - path.IndexOf("[") - 1);
            var index = int.Parse(indexStr);

            if (_queues.TryGetValue(queueName, out var queue) && queue.Count > index)
            {
                var waferId = queue[index];

                // DEBUG: Log for wafers 2 and 10 to trace "skipped" bug
                if (waferId == 2 || waferId == 10)
                {
                    Console.WriteLine($"[DEBUG ResolveReference] {reference} → waferId={waferId}, Queue: {string.Join(", ", queue.Take(5))}");
                }

                return waferId;
            }

            return 0;
        }

        // Handle station.waferId access
        if (path.Contains("."))
        {
            var parts = path.Split('.');
            var station = parts[0];
            var property = parts[1];

            if (property == "waferId" && _stationWafers.TryGetValue(station, out var waferId))
            {
                return waferId ?? 0;
            }
        }

        return 0;
    }

    /// <summary>
    /// Execute an effect (side effect of executing a rule)
    /// </summary>
    private void ExecuteEffect(Effect effect, OrchestratedContext ctx)
    {
        switch (effect.Type.ToLowerInvariant())
        {
            case "queueoperation":
                ExecuteQueueOperation(effect, ctx);
                break;

            case "checkcompletion":
                CheckCompletion(effect);
                break;

            default:
                Log($"[RuleEngine] ⚠ Unknown effect type: {effect.Type}");
                break;
        }
    }

    private void ExecuteQueueOperation(Effect effect, OrchestratedContext? ctx = null)
    {
        if (effect.Queue == null)
            return;

        if (!_queues.TryGetValue(effect.Queue, out var queue))
            return;

        switch (effect.Operation?.ToLowerInvariant())
        {
            case "add":
                if (effect.Value != null)
                {
                    var value = ResolveReference(effect.Value.ToString() ?? "");
                    var waferId = Convert.ToInt32(value);
                    queue.Add(waferId);

                    // DEBUG: Log wafer completion
                    if (effect.Queue == "LoadPort.Completed")
                    {
                        var completedCount = queue.Count;
                        Log($"[RuleEngine] ✅ Wafer {waferId} completed ({completedCount}/{_totalWafers})");
                    }

                    // E87: Notify carrier when wafer completes
                    if (effect.Queue == "LoadPort.Completed" && ctx != null)
                    {
                        // Send WAFER_COMPLETED event to active carrier
                        ctx.RequestSend("scheduler", "CARRIER_WAFER_COMPLETED", new JObject
                        {
                            ["waferId"] = waferId
                        });
                    }
                }
                break;

            case "removefirst":
                if (queue.Count > 0)
                {
                    var removedItem = queue[0];
                    queue.RemoveAt(0);
                    Log($"[RuleEngine] 📤 Queue {effect.Queue} removed wafer {removedItem} (remaining: {queue.Count})");

                    // DEBUG: Log for wafers 2 and 10 to trace "skipped" bug
                    if (removedItem == 2 || removedItem == 10)
                    {
                        Console.WriteLine($"[DEBUG removeFirst] Wafer {removedItem} removed from queue. Remaining queue: {string.Join(", ", queue.Take(5))}");
                    }
                }
                else
                {
                    Log($"[RuleEngine] ⚠ Attempted to removeFirst from empty queue: {effect.Queue}");
                }
                break;

            case "remove":
                if (effect.Value != null)
                {
                    var value = Convert.ToInt32(effect.Value);
                    queue.Remove(value);
                }
                break;
        }
    }

    private void CheckCompletion(Effect effect)
    {
        if (effect.Condition == null)
            return;

        // Parse condition: "LoadPort.Completed.Count >= TotalWafers"
        if (effect.Condition.Contains("LoadPort.Completed.Count >= TotalWafers"))
        {
            var completedCount = _queues.GetValueOrDefault("LoadPort.Completed")?.Count ?? 0;

            Log($"[RuleEngine] 🔍 CheckCompletion: {completedCount}/{_totalWafers} wafers completed");

            if (completedCount >= _totalWafers)
            {
                Log($"[RuleEngine] ✅ All {_totalWafers} wafers completed!");
                AllWafersCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private string? GetStationState(string station)
    {
        return _stationStates.GetValueOrDefault(station);
    }

    private string? GetRobotState(string robot)
    {
        // If robot has a pending command, treat it as not idle
        if (_robotsWithPendingCommands.Contains(robot))
        {
            return "busy";
        }

        return _robotStates.GetValueOrDefault(robot);
    }

    // Public accessors for debugging/monitoring
    public int PendingCount => _queues.GetValueOrDefault("LoadPort.Pending")?.Count ?? 0;
    public int CompletedCount => _queues.GetValueOrDefault("LoadPort.Completed")?.Count ?? 0;
    public IReadOnlyList<int> Completed => _queues.GetValueOrDefault("LoadPort.Completed")?.AsReadOnly() ?? new List<int>().AsReadOnly();

    /// <summary>
    /// Reset the rule engine for a new carrier batch
    /// NOTE: This does NOT change the pause state - use Resume() explicitly after carrier swap
    /// </summary>
    public void Reset(string? carrierId = null)
    {
        // DEBUG: Log state before reset
        var pendingBefore = _queues.GetValueOrDefault("LoadPort.Pending")?.Count ?? 0;
        var completedBefore = _queues.GetValueOrDefault("LoadPort.Completed")?.Count ?? 0;
        var carrierInfo = carrierId != null ? $" for {carrierId}" : "";
        Log($"[RuleEngine] 🔄 Reset starting{carrierInfo} - Before: Pending={pendingBefore}, Completed={completedBefore}, Paused={_isPaused}");

        // Clear all completed wafers and reset to pending
        _queues["LoadPort.Pending"].Clear();
        _queues["LoadPort.Pending"].AddRange(Enumerable.Range(1, _totalWafers));
        _queues["LoadPort.Completed"].Clear();

        // Clear station and robot states
        _stationStates.Clear();
        _stationWafers.Clear();
        _robotStates.Clear();
        _robotWafers.Clear();
        _robotWaitingFor.Clear();
        _robotsWithPendingCommands.Clear();

        // DEBUG: Log state after reset
        var pendingAfter = _queues["LoadPort.Pending"].Count;
        Log($"[RuleEngine] ↻ Reset complete{carrierInfo} - After: Pending={pendingAfter}, Completed=0, Paused={_isPaused} (ready for next carrier)");
    }

    /// <summary>
    /// Pause scheduler (prevents rule execution during carrier swap)
    /// </summary>
    public void Pause()
    {
        _isPaused = true;
        Log("[RuleEngine] ⏸ Scheduler paused");
    }

    /// <summary>
    /// Resume scheduler (allows rule execution to continue)
    /// </summary>
    public void Resume(string? carrierId = null)
    {
        _isPaused = false;
        var carrierInfo = carrierId != null ? $" for {carrierId}" : "";
        Log($"[RuleEngine] ▶ Scheduler resumed{carrierInfo}");

        // DEBUG: Log queue state after resume
        var pending = _queues.GetValueOrDefault("LoadPort.Pending")?.Count ?? 0;
        var completed = _queues.GetValueOrDefault("LoadPort.Completed")?.Count ?? 0;
        Log($"[RuleEngine] 📊 Queue Status: Pending={pending}, Completed={completed}/{_totalWafers}");

        if (pending > 0)
        {
            var pendingWafers = string.Join(", ", _queues["LoadPort.Pending"].Take(5));
            Log($"[RuleEngine] 📊 Next wafers: {pendingWafers}{(pending > 5 ? "..." : "")}");
        }
    }
}
