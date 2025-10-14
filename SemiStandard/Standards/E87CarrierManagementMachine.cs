using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// E87 Carrier Management Machine - SEMI E87 Standard
/// Manages carrier (FOUP/cassette) and load port lifecycle
/// Refactored to use ExtendedPureStateMachineFactory with EventBusOrchestrator
/// </summary>
public class E87CarrierManagementMachine
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ConcurrentDictionary<string, CarrierMachine> _carriers = new();
    private readonly ConcurrentDictionary<string, LoadPortMachine> _loadPorts = new();
    private readonly ConcurrentDictionary<string, CarrierHistory> _carrierHistory = new();

    public string MachineId => $"E87_CARRIER_MGMT_{_equipmentId}";

    public E87CarrierManagementMachine(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Register a load port
    /// </summary>
    public async Task<LoadPortMachine> RegisterLoadPortAsync(string portId, string portName, int capacity = 25)
    {
        if (_loadPorts.ContainsKey(portId))
        {
            return _loadPorts[portId];
        }

        var loadPort = new LoadPortMachine(portId, portName, capacity, _equipmentId, _orchestrator);
        _loadPorts[portId] = loadPort;

        await loadPort.StartAsync();

        return loadPort;
    }

    /// <summary>
    /// Carrier arrives at load port
    /// </summary>
    public async Task<CarrierMachine?> CarrierArrivedAsync(string carrierId, string portId, int slotCount = 25)
    {
        if (!_loadPorts.TryGetValue(portId, out var loadPort))
            return null;

        if (_carriers.ContainsKey(carrierId))
        {
            return _carriers[carrierId];
        }

        var carrier = new CarrierMachine(carrierId, portId, slotCount, _equipmentId, _orchestrator);
        _carriers[carrierId] = carrier;

        await carrier.StartAsync();
        await Task.Delay(50);

        // Notify load port of carrier placement
        loadPort.CurrentCarrierId = carrierId;
        await loadPort.CarrierPlacedAsync();

        AddCarrierHistory(carrierId, "WaitingForHost", $"Carrier arrived at load port {portId}");

        // Notify E84 and E90
        await _orchestrator.SendEventAsync(
            MachineId,
            $"E84_HANDOFF_{portId}",
            "E84_CARRIER_ARRIVED",
            new JObject
            {
                ["carrierId"] = carrierId,
                ["portId"] = portId,
                ["slotCount"] = slotCount
            }
        );

        return carrier;
    }

    /// <summary>
    /// Start carrier processing (host proceeds)
    /// </summary>
    public async Task<bool> StartCarrierProcessingAsync(string carrierId)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            var result = await carrier.HostProceedAsync();
            AddCarrierHistory(carrierId, "Mapping", "Host authorized carrier processing");
            return result.Success;
        }
        return false;
    }

    /// <summary>
    /// Update carrier slot map after mapping
    /// </summary>
    public async Task UpdateSlotMapAsync(string carrierId, Dictionary<int, SlotState> slotMap)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            foreach (var slot in slotMap)
            {
                carrier.SlotMap[slot.Key] = slot.Value;
            }
            carrier.MappingCompleteTime = DateTime.UtcNow;

            // Count substrates
            carrier.SubstrateCount = carrier.SlotMap.Count(s => s.Value == SlotState.Present);

            await carrier.MappingCompleteAsync();

            AddCarrierHistory(carrierId, "MappingVerification", $"Slot map updated: {carrier.SubstrateCount} substrates present");

            // Notify E90 of substrates
            for (int slot = 1; slot <= carrier.SlotCount; slot++)
            {
                if (carrier.SlotMap.TryGetValue(slot, out var state) && state == SlotState.Present)
                {
                    var substrateId = $"{carrierId}_SLOT{slot}";
                    carrier.SubstrateIds[slot] = substrateId;

                    await _orchestrator.SendEventAsync(
                        MachineId,
                        $"E90_TRACKING_{_equipmentId}",
                        "E90_SUBSTRATE_IN_CARRIER",
                        new JObject
                        {
                            ["substrateId"] = substrateId,
                            ["carrierId"] = carrierId,
                            ["slotNumber"] = slot
                        }
                    );
                }
            }
        }
    }

    /// <summary>
    /// Verify slot map
    /// </summary>
    public async Task<bool> VerifySlotMapAsync(string carrierId, bool verified)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            var result = verified
                ? await carrier.VerifyOkAsync()
                : await carrier.VerifyFailAsync();
            return result.Success;
        }
        return false;
    }

    /// <summary>
    /// Start accessing carrier
    /// </summary>
    public async Task<bool> StartAccessAsync(string carrierId)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            var result = await carrier.StartAccessAsync();
            AddCarrierHistory(carrierId, "InAccess", "Started carrier access");

            // Notify E90 that substrates are being accessed
            await _orchestrator.SendEventAsync(
                MachineId,
                $"E90_TRACKING_{_equipmentId}",
                "E87_CARRIER_ACCESS_STARTED",
                new JObject
                {
                    ["carrierId"] = carrierId
                }
            );

            return result.Success;
        }
        return false;
    }

    /// <summary>
    /// Complete carrier access
    /// </summary>
    public async Task<bool> CompleteAccessAsync(string carrierId)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            var result = await carrier.AccessCompleteAsync();
            AddCarrierHistory(carrierId, "Complete", "Carrier access completed");

            // Notify E40 that carrier processing is complete
            await _orchestrator.SendEventAsync(
                MachineId,
                $"E40_PROCESS_JOB",
                "E87_CARRIER_PROCESSING_COMPLETE",
                new JObject
                {
                    ["carrierId"] = carrierId,
                    ["substrateCount"] = carrier.SubstrateCount
                }
            );

            return result.Success;
        }
        return false;
    }

    /// <summary>
    /// Carrier departed from load port
    /// </summary>
    public async Task<bool> CarrierDepartedAsync(string carrierId)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            carrier.DepartedTime = DateTime.UtcNow;
            await carrier.RemoveAsync();

            // Clear load port
            if (_loadPorts.TryGetValue(carrier.LoadPortId, out var loadPort))
            {
                loadPort.CurrentCarrierId = null;
                await loadPort.CarrierRemovedAsync();
            }

            AddCarrierHistory(carrierId, "CarrierOut", "Carrier departed");

            // Notify E84
            await _orchestrator.SendEventAsync(
                MachineId,
                $"E84_HANDOFF_{carrier.LoadPortId}",
                "E84_CARRIER_DEPARTED",
                new JObject
                {
                    ["carrierId"] = carrierId
                }
            );

            // Remove from tracking
            _carriers.TryRemove(carrierId, out _);

            return true;
        }
        return false;
    }

    /// <summary>
    /// Get carrier information
    /// </summary>
    public CarrierMachine? GetCarrier(string carrierId)
    {
        return _carriers.TryGetValue(carrierId, out var carrier) ? carrier : null;
    }

    /// <summary>
    /// Get carrier at load port
    /// </summary>
    public CarrierMachine? GetCarrierAtPort(string portId)
    {
        if (_loadPorts.TryGetValue(portId, out var loadPort) &&
            !string.IsNullOrEmpty(loadPort.CurrentCarrierId))
        {
            return GetCarrier(loadPort.CurrentCarrierId);
        }
        return null;
    }

    /// <summary>
    /// Get all active carriers
    /// </summary>
    public IEnumerable<CarrierMachine> GetActiveCarriers()
    {
        return _carriers.Values;
    }

    /// <summary>
    /// Get load port
    /// </summary>
    public LoadPortMachine? GetLoadPort(string portId)
    {
        return _loadPorts.TryGetValue(portId, out var port) ? port : null;
    }

    /// <summary>
    /// Get all load ports
    /// </summary>
    public IEnumerable<LoadPortMachine> GetLoadPorts()
    {
        return _loadPorts.Values;
    }

    /// <summary>
    /// Get carrier history
    /// </summary>
    public CarrierHistory? GetCarrierHistory(string carrierId)
    {
        return _carrierHistory.TryGetValue(carrierId, out var history) ? history : null;
    }

    /// <summary>
    /// Add carrier history
    /// </summary>
    private void AddCarrierHistory(string carrierId, string state, string description)
    {
        if (!_carrierHistory.ContainsKey(carrierId))
        {
            _carrierHistory[carrierId] = new CarrierHistory { CarrierId = carrierId };
        }

        _carrierHistory[carrierId].Events.Add(new CarrierEvent
        {
            Timestamp = DateTime.UtcNow,
            State = state,
            Description = description
        });
    }
}

/// <summary>
/// Individual carrier (FOUP) state machine using orchestrator
/// </summary>
public class CarrierMachine
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly string _instanceId;

    public string Id { get; }
    public string LoadPortId { get; }
    public int SlotCount { get; }
    public ConcurrentDictionary<int, SlotState> SlotMap { get; }
    public ConcurrentDictionary<int, string> SubstrateIds { get; }
    public int SubstrateCount { get; set; }
    public DateTime ArrivedTime { get; }
    public DateTime? MappingCompleteTime { get; set; }
    public DateTime? DepartedTime { get; set; }
    public ConcurrentDictionary<string, object> Properties { get; }

    public string MachineId => $"E87_CARRIER_{Id}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public event EventHandler<(string oldState, string newState)>? StateTransitioned;

    public CarrierMachine(string id, string loadPortId, int slotCount, string equipmentId, EventBusOrchestrator orchestrator)
    {
        Id = id;
        LoadPortId = loadPortId;
        SlotCount = slotCount;
        SlotMap = new ConcurrentDictionary<int, SlotState>();
        SubstrateIds = new ConcurrentDictionary<int, string>();
        Properties = new ConcurrentDictionary<string, object>();
        ArrivedTime = DateTime.UtcNow;
        _orchestrator = orchestrator;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Initialize slot map
        for (int i = 1; i <= slotCount; i++)
        {
            SlotMap[i] = SlotState.Unknown;
        }

        // Inline XState JSON definition
        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'NotPresent',
            context: {
                carrierId: '',
                loadPortId: '',
                slotCount: 0
            },
            states: {
                NotPresent: {
                    entry: 'logNotPresent',
                    on: {
                        E87_CARRIER_DETECTED: 'WaitingForHost'
                    }
                },
                WaitingForHost: {
                    entry: 'logWaitingForHost',
                    on: {
                        E87_HOST_PROCEED: 'Mapping',
                        E87_HOST_CANCEL: 'CarrierOut'
                    }
                },
                Mapping: {
                    entry: 'startMapping',
                    on: {
                        E87_MAPPING_COMPLETE: 'MappingVerification',
                        E87_MAPPING_ERROR: 'WaitingForHost'
                    }
                },
                MappingVerification: {
                    entry: 'logMappingVerification',
                    on: {
                        E87_VERIFY_OK: 'ReadyToAccess',
                        E87_VERIFY_FAIL: 'Mapping'
                    },
                    after: {
                        '500': {
                            target: 'ReadyToAccess'
                        }
                    }
                },
                ReadyToAccess: {
                    entry: 'logReadyToAccess',
                    on: {
                        E87_START_ACCESS: 'InAccess',
                        E87_HOST_CANCEL: 'Complete'
                    }
                },
                InAccess: {
                    entry: 'logInAccess',
                    on: {
                        E87_ACCESS_COMPLETE: 'Complete',
                        E87_ACCESS_ERROR: 'AccessPaused'
                    }
                },
                AccessPaused: {
                    entry: 'logAccessPaused',
                    on: {
                        E87_RESUME_ACCESS: 'InAccess',
                        E87_ABORT_ACCESS: 'Complete'
                    }
                },
                Complete: {
                    entry: 'logComplete',
                    on: {
                        E87_CARRIER_REMOVED: 'CarrierOut'
                    }
                },
                CarrierOut: {
                    entry: 'logCarrierOut',
                    type: 'final'
                }
            }
        }
        """;

        // Orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logNotPresent"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📦 Carrier not present");
            },

            ["logWaitingForHost"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏳ Waiting for host authorization");

                ctx.RequestSend("HOST_SYSTEM", "E87_CARRIER_AWAITING_APPROVAL", new JObject
                {
                    ["carrierId"] = Id,
                    ["loadPortId"] = LoadPortId,
                    ["slotCount"] = SlotCount
                });
            },

            ["startMapping"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🗺️ Starting slot mapping");

                ctx.RequestSend("MAPPING_SYSTEM", "E87_START_CARRIER_MAPPING", new JObject
                {
                    ["carrierId"] = Id,
                    ["slotCount"] = SlotCount
                });
            },

            ["logMappingVerification"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Mapping verification");
            },

            ["logReadyToAccess"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Ready to access ({SubstrateCount} substrates)");

                ctx.RequestSend("E40_PROCESS_JOB", "E87_CARRIER_READY", new JObject
                {
                    ["carrierId"] = Id,
                    ["substrateCount"] = SubstrateCount
                });
            },

            ["logInAccess"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔧 Accessing carrier substrates");

                ctx.RequestSend("E90_TRACKING", "E87_CARRIER_ACCESS_IN_PROGRESS", new JObject
                {
                    ["carrierId"] = Id
                });
            },

            ["logAccessPaused"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏸️ Access paused (error recovery)");

                ctx.RequestSend("ALARM_SYSTEM", "E87_CARRIER_ACCESS_PAUSED", new JObject
                {
                    ["carrierId"] = Id,
                    ["reason"] = "ACCESS_ERROR"
                });
            },

            ["logComplete"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Carrier access complete");

                ctx.RequestSend("E94_CONTROL_JOB", "E87_CARRIER_COMPLETE", new JObject
                {
                    ["carrierId"] = Id,
                    ["substrateCount"] = SubstrateCount
                });
            },

            ["logCarrierOut"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🚪 Carrier removed from system");
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            enableGuidIsolation: false  // Already has GUID suffix in MachineId
        );

        // Forward state changes to StateTransitioned event
        // Access underlying IStateMachine to subscribe to StateChanged event
        if (_machine is PureStateMachineAdapter adapter)
        {
            var underlying = adapter.GetUnderlying();
            string? previousState = null;
            underlying.StateChanged += (newState) =>
            {
                var oldState = previousState ?? "NotPresent";
                StateTransitioned?.Invoke(this, (oldState, newState));
                previousState = newState;
            };
        }
    }

    public async Task<string> StartAsync()
    {
        var state = await _machine.StartAsync();
        await Task.Delay(50);
        // Automatically detect carrier
        await _orchestrator.SendEventAsync("SYSTEM", MachineId, "E87_CARRIER_DETECTED", null);
        return state;
    }

    public string GetCurrentState()
    {
        return _machine.CurrentState;
    }

    // Public API methods
    public async Task<EventResult> HostProceedAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "E87_HOST_PROCEED", null);
        return result;
    }

    public async Task<EventResult> MappingCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "E87_MAPPING_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> VerifyOkAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "E87_VERIFY_OK", null);
        return result;
    }

    public async Task<EventResult> VerifyFailAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "E87_VERIFY_FAIL", null);
        return result;
    }

    public async Task<EventResult> StartAccessAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "E87_START_ACCESS", null);
        return result;
    }

    public async Task<EventResult> AccessCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "E87_ACCESS_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> RemoveAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "E87_CARRIER_REMOVED", null);
        return result;
    }
}

/// <summary>
/// Individual load port state machine using orchestrator
/// </summary>
public class LoadPortMachine
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly string _instanceId;

    public string Id { get; }
    public string Name { get; }
    public string? CurrentCarrierId { get; set; }
    public int Capacity { get; }
    public bool IsReserved { get; set; }
    public ConcurrentDictionary<string, object> Properties { get; }

    public string MachineId => $"E87_LOADPORT_{Id}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public LoadPortMachine(string id, string name, int capacity, string equipmentId, EventBusOrchestrator orchestrator)
    {
        Id = id;
        Name = name;
        Capacity = capacity;
        Properties = new ConcurrentDictionary<string, object>();
        _orchestrator = orchestrator;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Inline XState JSON definition
        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'Empty',
            context: {
                portId: '',
                portName: '',
                capacity: 0
            },
            states: {
                Empty: {
                    entry: 'logEmpty',
                    on: {
                        E87_CARRIER_PLACED: 'Loading',
                        E87_RESERVE: 'Reserved'
                    }
                },
                Reserved: {
                    entry: 'logReserved',
                    on: {
                        E87_CARRIER_PLACED: 'Loading',
                        E87_UNRESERVE: 'Empty'
                    }
                },
                Loading: {
                    entry: 'logLoading',
                    on: {
                        E87_LOAD_COMPLETE: 'Loaded',
                        E87_LOAD_ERROR: 'Error'
                    },
                    after: {
                        '1000': {
                            target: 'Loaded'
                        }
                    }
                },
                Loaded: {
                    entry: 'logLoaded',
                    on: {
                        E87_START_MAPPING: 'Mapping',
                        E87_CARRIER_REMOVED: 'Unloading'
                    }
                },
                Mapping: {
                    entry: 'logMapping',
                    on: {
                        E87_MAPPING_COMPLETE: 'Ready',
                        E87_MAPPING_ERROR: 'Error'
                    }
                },
                Ready: {
                    entry: 'logReady',
                    on: {
                        E87_START_ACCESS: 'InAccess',
                        E87_CARRIER_REMOVED: 'Unloading'
                    }
                },
                InAccess: {
                    entry: 'logInAccess',
                    on: {
                        E87_ACCESS_COMPLETE: 'ReadyToUnload',
                        E87_ACCESS_ERROR: 'Error'
                    }
                },
                ReadyToUnload: {
                    entry: 'logReadyToUnload',
                    on: {
                        E87_CARRIER_REMOVED: 'Unloading'
                    }
                },
                Unloading: {
                    entry: 'logUnloading',
                    on: {
                        E87_UNLOAD_COMPLETE: 'Empty',
                        E87_UNLOAD_ERROR: 'Error'
                    },
                    after: {
                        '500': {
                            target: 'Empty'
                        }
                    }
                },
                Error: {
                    entry: 'logError',
                    on: {
                        E87_CLEAR_ERROR: 'Empty',
                        E87_RECOVER: 'Loaded'
                    }
                }
            }
        }
        """;

        // Orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logEmpty"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⚪ Load port empty");
            },

            ["logReserved"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔒 Load port reserved");
                IsReserved = true;
            },

            ["logLoading"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏳ Loading carrier...");

                ctx.RequestSend("E84_HANDOFF", "E87_LOAD_PORT_LOADING", new JObject
                {
                    ["portId"] = Id
                });
            },

            ["logLoaded"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Carrier loaded");

                ctx.RequestSend("E84_HANDOFF", "E87_LOAD_PORT_LOADED", new JObject
                {
                    ["portId"] = Id,
                    ["carrierId"] = CurrentCarrierId
                });
            },

            ["logMapping"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🗺️ Mapping carrier slots...");
            },

            ["logReady"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Ready for access");
            },

            ["logInAccess"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔧 Accessing carrier");
            },

            ["logReadyToUnload"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Ready to unload carrier");
            },

            ["logUnloading"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏳ Unloading carrier...");
            },

            ["logError"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ❌ Load port error");

                ctx.RequestSend("ALARM_SYSTEM", "E87_LOAD_PORT_ERROR", new JObject
                {
                    ["portId"] = Id
                });
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            enableGuidIsolation: false  // Already has GUID suffix in MachineId
        );
    }

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public string GetCurrentState()
    {
        return _machine.CurrentState;
    }

    // Public API methods
    public async Task<EventResult> CarrierPlacedAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "E87_CARRIER_PLACED", null);
        return result;
    }

    public async Task<EventResult> CarrierRemovedAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "E87_CARRIER_REMOVED", null);
        return result;
    }

    public async Task<EventResult> ReserveAsync()
    {
        IsReserved = true;
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "E87_RESERVE", null);
        return result;
    }

    public async Task<EventResult> UnreserveAsync()
    {
        IsReserved = false;
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "E87_UNRESERVE", null);
        return result;
    }
}

/// <summary>
/// Slot states
/// </summary>
public enum SlotState
{
    Unknown,
    Empty,
    Present,
    DoublePlaced,
    CrossPlaced
}

/// <summary>
/// Carrier history
/// </summary>
public class CarrierHistory
{
    public string CarrierId { get; set; } = "";
    public List<CarrierEvent> Events { get; set; } = new List<CarrierEvent>();
}

/// <summary>
/// Carrier event
/// </summary>
public class CarrierEvent
{
    public DateTime Timestamp { get; set; }
    public string State { get; set; } = "";
    public string Description { get; set; } = "";
}
