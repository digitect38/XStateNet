using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using XStateNet;
using XStateNet.Orchestration;

namespace CMPSimulator.XStateStations;

/// <summary>
/// WTR (Wafer Transfer Robot) Station using XStateNet state machine
/// States: idle → picking → transiting → placing → idle
/// Priority: Cleaner→LoadPort(1) > Polisher→Cleaner(2) > LoadPort→Polisher(3)
/// </summary>
public class XWTRStation
{
    private readonly string _stationId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IStateMachine _machine;
    private int? _currentWaferId;
    private string? _fromStation;
    private string? _toStation;

    // Priority queue for pending transfer requests
    private readonly SortedDictionary<int, Queue<TransferRequest>> _pendingRequests = new();
    private readonly object _queueLock = new object();

    public string MachineId => _stationId;
    public IStateMachine Machine => _machine;
    public event EventHandler<string>? LogMessage;
    public event EventHandler<WaferTransitEventArgs>? WaferInTransit;

    public XWTRStation(string stationId, EventBusOrchestrator orchestrator)
    {
        _stationId = stationId;
        _orchestrator = orchestrator;

        var definition = $$"""
        {
            "id": "{{_stationId}}",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "TRANSFER_REQUEST": {
                            "target": "picking",
                            "actions": ["saveTransferInfo"]
                        },
                        "CHECK_QUEUE": {
                            "target": "picking",
                            "cond": "hasPendingRequests",
                            "actions": ["dequeueNextRequest"]
                        }
                    }
                },
                "picking": {
                    "entry": ["pickupWafer"],
                    "on": {
                        "TRANSFER_REQUEST": {
                            "actions": ["queueTransferRequest"]
                        }
                    },
                    "after": {
                        "300": "transiting"
                    }
                },
                "transiting": {
                    "entry": ["updateVisualPosition"],
                    "on": {
                        "TRANSFER_REQUEST": {
                            "actions": ["queueTransferRequest"]
                        }
                    },
                    "after": {
                        "600": "placing"
                    }
                },
                "placing": {
                    "entry": ["placeWafer"],
                    "on": {
                        "TRANSFER_REQUEST": {
                            "actions": ["queueTransferRequest"]
                        }
                    },
                    "after": {
                        "300": [
                            {
                                "target": "picking",
                                "cond": "hasPendingRequests",
                                "actions": ["notifyDestination", "dequeueNextRequest"]
                            },
                            {
                                "target": "idle",
                                "actions": ["notifyDestination", "clearWafer"]
                            }
                        ]
                    }
                }
            }
        }
        """;

        var actionMap = new ActionMap();

        void AddAction(string name, Action<StateMachine> handler)
        {
            actionMap[name] = new List<NamedAction>
            {
                new NamedAction(name, async (sm) =>
                {
                    handler(sm);
                    await Task.CompletedTask;
                })
            };
        }

        AddAction("saveTransferInfo", (sm) =>
        {
            Log($"🔍 saveTransferInfo action called");

            // Extract payload from ContextMap
            var eventData = sm.ContextMap?["_event"];

            if (eventData == null)
            {
                Log($"⚠️ eventData is NULL!");
                return;
            }

            Log($"🔍 eventData type: {eventData.GetType().FullName}");

            // Check if it's a Dictionary
            if (eventData is Dictionary<string, object> dict)
            {
                if (dict.ContainsKey("waferId") && dict.ContainsKey("from") && dict.ContainsKey("to"))
                {
                    var waferId = Convert.ToInt32(dict["waferId"]);
                    var from = dict["from"]?.ToString();
                    var to = dict["to"]?.ToString();

                    var request = new TransferRequest(waferId, from!, to!);
                    var priority = GetPriority(from!, to!);

                    lock (_queueLock)
                    {
                        // If WTR is idle, process immediately
                        if (_currentWaferId == null)
                        {
                            _currentWaferId = waferId;
                            _fromStation = from;
                            _toStation = to;
                            Log($"🤖 [P{priority}] Immediate transfer: Wafer {waferId} from {from} to {to}");
                        }
                        else
                        {
                            // WTR is busy, queue the request
                            if (!_pendingRequests.ContainsKey(priority))
                            {
                                _pendingRequests[priority] = new Queue<TransferRequest>();
                            }
                            _pendingRequests[priority].Enqueue(request);
                            Log($"🤖 [P{priority}] Queued transfer: Wafer {waferId} from {from} to {to} (queue size: {_pendingRequests[priority].Count})");
                        }
                    }
                }
                else
                {
                    Log($"⚠️ Dictionary missing keys. Has: {string.Join(", ", dict.Keys)}");
                }
            }
            else
            {
                Log($"⚠️ eventData is NOT Dictionary!");
            }
        });

        AddAction("queueTransferRequest", (sm) =>
        {
            // This action is called when WTR is busy
            var eventData = sm.ContextMap?["_event"];

            if (eventData is Dictionary<string, object> dict)
            {
                if (dict.ContainsKey("waferId") && dict.ContainsKey("from") && dict.ContainsKey("to"))
                {
                    var waferId = Convert.ToInt32(dict["waferId"]);
                    var from = dict["from"]?.ToString();
                    var to = dict["to"]?.ToString();

                    var request = new TransferRequest(waferId, from!, to!);
                    var priority = GetPriority(from!, to!);

                    lock (_queueLock)
                    {
                        if (!_pendingRequests.ContainsKey(priority))
                        {
                            _pendingRequests[priority] = new Queue<TransferRequest>();
                        }
                        _pendingRequests[priority].Enqueue(request);
                        Log($"🤖 [P{priority}] Queued transfer: Wafer {waferId} from {from} to {to} (queue size: {_pendingRequests[priority].Count})");
                    }
                }
            }
        });

        AddAction("dequeueNextRequest", (sm) =>
        {
            // Clear previous wafer info first
            _currentWaferId = null;
            _fromStation = null;
            _toStation = null;

            lock (_queueLock)
            {
                // Find highest priority (lowest number) with pending requests
                foreach (var priority in _pendingRequests.Keys.OrderBy(p => p))
                {
                    if (_pendingRequests[priority].Count > 0)
                    {
                        var request = _pendingRequests[priority].Dequeue();
                        _currentWaferId = request.WaferId;
                        _fromStation = request.From;
                        _toStation = request.To;

                        Log($"🤖 [P{priority}] Dequeued: Wafer {_currentWaferId} from {_fromStation} to {_toStation}");

                        // Clean up empty queues
                        if (_pendingRequests[priority].Count == 0)
                        {
                            _pendingRequests.Remove(priority);
                        }
                        return;
                    }
                }
            }
        });

        AddAction("clearWafer", (sm) =>
        {
            _currentWaferId = null;
            _fromStation = null;
            _toStation = null;
        });

        AddAction("pickupWafer", (sm) =>
        {
            if (_currentWaferId.HasValue && _fromStation != null)
            {
                Log($"🤖 Picking up Wafer {_currentWaferId} from {_fromStation}");

                // Notify source station
                var payload = new Dictionary<string, object>
                {
                    ["waferId"] = _currentWaferId.Value
                };
                _ = _orchestrator.SendEventFireAndForgetAsync(
                    MachineId,
                    _fromStation,
                    "WAFER_PICKED_UP",
                    payload
                );
            }
        });

        AddAction("updateVisualPosition", (sm) =>
        {
            if (_currentWaferId.HasValue)
            {
                Log($"🤖 Transiting Wafer {_currentWaferId}");

                // Raise event for UI to update wafer position
                WaferInTransit?.Invoke(this, new WaferTransitEventArgs(
                    _currentWaferId.Value,
                    _stationId
                ));
            }
        });

        AddAction("placeWafer", (sm) =>
        {
            Console.WriteLine($"[{_stationId}] placeWafer: _currentWaferId={_currentWaferId}, _toStation={_toStation}");
            Log($"🤖 Placing Wafer {_currentWaferId} at {_toStation}");
        });

        AddAction("notifyDestination", (sm) =>
        {
            if (_currentWaferId.HasValue && _toStation != null)
            {
                Log($"🤖 Delivered Wafer {_currentWaferId} to {_toStation}");

                // Notify destination station
                var payload = new Dictionary<string, object>
                {
                    ["waferId"] = _currentWaferId.Value
                };
                _ = _orchestrator.SendEventFireAndForgetAsync(
                    MachineId,
                    _toStation,
                    "WAFER_ARRIVED",
                    payload
                );

                // NOTE: Don't clear _currentWaferId here - let dequeueNextRequest do it
                // This ensures guard evaluation happens while we still have "busy" state

                // Debug: Check if there are pending requests
                lock (_queueLock)
                {
                    var hasPending = _pendingRequests.Any(kvp => kvp.Value.Count > 0);
                    Log($"🔍 After delivery - Pending requests: {hasPending}, Queue count: {_pendingRequests.Sum(kvp => kvp.Value.Count)}");
                }
            }
        });

        // Create guard map
        var guardMap = new GuardMap
        {
            ["hasPendingRequests"] = new NamedGuard((sm) =>
            {
                lock (_queueLock)
                {
                    return _pendingRequests.Any(kvp => kvp.Value.Count > 0);
                }
            }, "hasPendingRequests")
        };

        _machine = StateMachineFactory.CreateFromScript(
            jsonScript: definition,
            threadSafe: false,
            guidIsolate: false,
            actionCallbacks: actionMap,
            guardCallbacks: guardMap
        );

        _orchestrator.RegisterMachine(MachineId, _machine);
    }

    /// <summary>
    /// Calculate priority based on transfer direction (backward priority)
    /// Priority 1 (highest): Cleaner → LoadPort (closest to completion)
    /// Priority 2: Polisher → Cleaner
    /// Priority 3 (lowest): LoadPort → Polisher (start of process)
    /// </summary>
    private int GetPriority(string from, string to)
    {
        return (from.ToLower(), to.ToLower()) switch
        {
            ("cleaner", "buffer") => 1,      // Cleaner to Buffer (part of return path)
            ("buffer", "loadport") => 1,     // Buffer to LoadPort (final return)
            ("polisher", "cleaner") => 2,    // Polisher to Cleaner (middle of process)
            ("loadport", "polisher") => 3,   // LoadPort to Polisher (start of process)
            _ => 99                           // Unknown routes get lowest priority
        };
    }

    public async Task<string> StartAsync() => await _machine.StartAsync();
    public string GetCurrentState() => _machine.GetActiveStateNames();

    private void Log(string message)
    {
        LogMessage?.Invoke(this, $"[{MachineId.ToUpper()}] {message}");
    }
}

public class WaferTransitEventArgs : EventArgs
{
    public int WaferId { get; }
    public string RobotId { get; }

    public WaferTransitEventArgs(int waferId, string robotId)
    {
        WaferId = waferId;
        RobotId = robotId;
    }
}

/// <summary>
/// Represents a wafer transfer request with priority
/// </summary>
internal record TransferRequest(int WaferId, string From, string To);
