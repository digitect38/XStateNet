using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XStateNet.Semi;

/// <summary>
/// E87 Carrier Management System implementation using XStateNet state machines
/// </summary>
public class E87CarrierManagement
{
    private readonly ConcurrentDictionary<string, CarrierStateMachine> _carriers = new();
    private readonly ConcurrentDictionary<string, LoadPortStateMachine> _loadPorts = new();
    private readonly ConcurrentDictionary<string, CarrierHistory> _carrierHistory = new();
    private readonly object _updateLock = new();
    
    /// <summary>
    /// Register a load port with its own state machine
    /// </summary>
    public void RegisterLoadPort(string portId, string portName, int capacity = 25)
    {
        _loadPorts[portId] = new LoadPortStateMachine(portId, portName, capacity);
    }
    
    /// <summary>
    /// Carrier arrives at load port
    /// </summary>
    public CarrierStateMachine? CarrierArrived(string carrierId, string portId, int slotCount = 25)
    {
        if (!_loadPorts.TryGetValue(portId, out var loadPort))
            return null;
            
        var carrier = new CarrierStateMachine(carrierId, portId, slotCount);
        
        lock (_updateLock)
        {
            _carriers[carrierId] = carrier;
            loadPort.CurrentCarrierId = carrierId;
            loadPort.StateMachine.Send("CARRIER_PLACED");
            
            AddCarrierHistory(carrierId, "WaitingForHost", 
                $"Carrier arrived at load port {portId}");
        }
        
        return carrier;
    }
    
    /// <summary>
    /// Update carrier slot map after mapping
    /// </summary>
    public void UpdateSlotMap(string carrierId, Dictionary<int, SlotState> slotMap)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            lock (_updateLock)
            {
                foreach (var slot in slotMap)
                {
                    carrier.SlotMap[slot.Key] = slot.Value;
                }
                carrier.MappingCompleteTime = DateTime.UtcNow;
                
                // Count substrates
                carrier.SubstrateCount = carrier.SlotMap.Count(s => s.Value == SlotState.Present);
                
                // Send mapping complete event to carrier state machine
                carrier.StateMachine.Send("MAPPING_COMPLETE");
                
                AddCarrierHistory(carrierId, "InAccess", 
                    $"Slot map updated: {carrier.SubstrateCount} substrates present");
            }
        }
    }
    
    /// <summary>
    /// Associate substrate with carrier slot
    /// </summary>
    public void AssociateSubstrate(string carrierId, int slotNumber, string substrateid)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            lock (_updateLock)
            {
                carrier.SubstrateIds[slotNumber] = substrateid;
            }
        }
    }
    
    /// <summary>
    /// Start accessing carrier
    /// </summary>
    public void StartAccess(string carrierId)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            carrier.StateMachine.Send("START_ACCESS");
            AddCarrierHistory(carrierId, "InAccess", "Started carrier access");
        }
    }
    
    /// <summary>
    /// Complete carrier access
    /// </summary>
    public void CompleteAccess(string carrierId)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            carrier.StateMachine.Send("ACCESS_COMPLETE");
            AddCarrierHistory(carrierId, "Complete", "Carrier access completed");
        }
    }
    
    /// <summary>
    /// Carrier departed from load port
    /// </summary>
    public bool CarrierDeparted(string carrierId)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            lock (_updateLock)
            {
                carrier.DepartedTime = DateTime.UtcNow;
                carrier.StateMachine.Send("CARRIER_REMOVED");
                
                // Clear load port
                if (!string.IsNullOrEmpty(carrier.LoadPortId))
                {
                    if (_loadPorts.TryGetValue(carrier.LoadPortId, out var loadPort))
                    {
                        loadPort.CurrentCarrierId = null;
                        loadPort.StateMachine.Send("CARRIER_REMOVED");
                    }
                }
                
                AddCarrierHistory(carrierId, "CarrierOut", "Carrier departed");
                
                // Remove from active tracking after a delay
                // In real implementation, you might want to keep history
                _carriers.TryRemove(carrierId, out _);
            }
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Get carrier information
    /// </summary>
    public CarrierStateMachine? GetCarrier(string carrierId)
    {
        return _carriers.TryGetValue(carrierId, out var carrier) ? carrier : null;
    }
    
    /// <summary>
    /// Get carrier at load port
    /// </summary>
    public CarrierStateMachine? GetCarrierAtPort(string portId)
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
    public IEnumerable<CarrierStateMachine> GetActiveCarriers()
    {
        return _carriers.Values;
    }
    
    /// <summary>
    /// Get load port
    /// </summary>
    public LoadPortStateMachine? GetLoadPort(string portId)
    {
        return _loadPorts.TryGetValue(portId, out var port) ? port : null;
    }
    
    /// <summary>
    /// Get all load ports
    /// </summary>
    public IEnumerable<LoadPortStateMachine> GetLoadPorts()
    {
        return _loadPorts.Values;
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
/// Carrier (FOUP/Cassette) with its own XStateNet state machine
/// </summary>
public class CarrierStateMachine
{
    public string Id { get; }
    public string LoadPortId { get; }
    public StateMachine StateMachine { get; }
    public int SlotCount { get; }
    public Dictionary<int, SlotState> SlotMap { get; }
    public Dictionary<int, string> SubstrateIds { get; }
    public int SubstrateCount { get; set; }
    public DateTime ArrivedTime { get; }
    public DateTime? MappingCompleteTime { get; set; }
    public DateTime? DepartedTime { get; set; }
    public Dictionary<string, object> Properties { get; }
    
    public CarrierStateMachine(string id, string loadPortId, int slotCount = 25)
    {
        Id = id;
        LoadPortId = loadPortId;
        SlotCount = slotCount;
        SlotMap = new Dictionary<int, SlotState>();
        SubstrateIds = new Dictionary<int, string>();
        Properties = new Dictionary<string, object>();
        ArrivedTime = DateTime.UtcNow;
        
        // Initialize slot map
        for (int i = 1; i <= slotCount; i++)
        {
            SlotMap[i] = SlotState.Unknown;
        }
        
        // Create E87 carrier state machine
        StateMachine = CreateE87StateMachine(id);
        StateMachine.Start();
    }
    
    /// <summary>
    /// Creates the E87-compliant carrier state machine
    /// </summary>
    private StateMachine CreateE87StateMachine(string carrierId)
    {
        var config = new Dictionary<string, object>
        {
            ["id"] = $"carrier_{carrierId}",
            ["initial"] = "NotPresent",
            ["states"] = new Dictionary<string, object>
            {
                ["NotPresent"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["CARRIER_DETECTED"] = "WaitingForHost"
                    }
                },
                ["WaitingForHost"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["HOST_PROCEED"] = "Mapping",
                        ["HOST_CANCEL"] = "CarrierOut"
                    }
                },
                ["Mapping"] = new Dictionary<string, object>
                {
                    ["entry"] = new[] { "startMapping" },
                    ["on"] = new Dictionary<string, object>
                    {
                        ["MAPPING_COMPLETE"] = "MappingVerification",
                        ["MAPPING_ERROR"] = "WaitingForHost"
                    }
                },
                ["MappingVerification"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["VERIFY_OK"] = "ReadyToAccess",
                        ["VERIFY_FAIL"] = "Mapping"
                    },
                    ["after"] = new Dictionary<string, object>
                    {
                        ["500"] = new Dictionary<string, object>
                        {
                            ["target"] = "ReadyToAccess"
                        }
                    }
                },
                ["ReadyToAccess"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["START_ACCESS"] = "InAccess",
                        ["HOST_CANCEL"] = "Complete"
                    }
                },
                ["InAccess"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["ACCESS_COMPLETE"] = "Complete",
                        ["ACCESS_ERROR"] = "AccessPaused"
                    }
                },
                ["AccessPaused"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["RESUME_ACCESS"] = "InAccess",
                        ["ABORT_ACCESS"] = "Complete"
                    }
                },
                ["Complete"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["CARRIER_REMOVED"] = "CarrierOut"
                    }
                },
                ["CarrierOut"] = new Dictionary<string, object>
                {
                    ["type"] = "final"
                }
            }
        };
        
        // Create action callbacks
        var actionMap = new ActionMap();
        actionMap["startMapping"] = new List<NamedAction>
        {
            new NamedAction("startMapping", (sm) => 
            {
                // In real implementation, this would trigger the mapping hardware
                Logger.Info($"Starting slot mapping for carrier {carrierId}");
            })
        };
        
        var json = System.Text.Json.JsonSerializer.Serialize(config);
        var stateMachine = new StateMachine();
        stateMachine.machineId = $"carrier_{carrierId}";
        return StateMachine.CreateFromScript(stateMachine, json, actionMap);
    }
    
    /// <summary>
    /// Get current state of the carrier
    /// </summary>
    public string GetCurrentState()
    {
        return StateMachine.GetSourceSubStateCollection(null).ToCsvString(StateMachine, true);
    }
}

/// <summary>
/// Load port with its own XStateNet state machine
/// </summary>
public class LoadPortStateMachine
{
    public string Id { get; }
    public string Name { get; }
    public StateMachine StateMachine { get; }
    public string? CurrentCarrierId { get; set; }
    public int Capacity { get; }
    public bool IsReserved { get; set; }
    public Dictionary<string, object> Properties { get; }
    
    public LoadPortStateMachine(string id, string name, int capacity = 25)
    {
        Id = id;
        Name = name;
        Capacity = capacity;
        Properties = new Dictionary<string, object>();
        
        // Create load port state machine
        StateMachine = CreateLoadPortStateMachine(id);
        StateMachine.Start();
    }
    
    /// <summary>
    /// Creates the load port state machine
    /// </summary>
    private StateMachine CreateLoadPortStateMachine(string portId)
    {
        var config = new Dictionary<string, object>
        {
            ["id"] = $"loadport_{portId}",
            ["initial"] = "Empty",
            ["states"] = new Dictionary<string, object>
            {
                ["Empty"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["CARRIER_PLACED"] = "Loading",
                        ["RESERVE"] = "Reserved"
                    }
                },
                ["Reserved"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["CARRIER_PLACED"] = "Loading",
                        ["UNRESERVE"] = "Empty"
                    }
                },
                ["Loading"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["LOAD_COMPLETE"] = "Loaded",
                        ["LOAD_ERROR"] = "Error"
                    },
                    ["after"] = new Dictionary<string, object>
                    {
                        ["1000"] = new Dictionary<string, object>
                        {
                            ["target"] = "Loaded"
                        }
                    }
                },
                ["Loaded"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["START_MAPPING"] = "Mapping",
                        ["CARRIER_REMOVED"] = "Unloading"
                    }
                },
                ["Mapping"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["MAPPING_COMPLETE"] = "Ready",
                        ["MAPPING_ERROR"] = "Error"
                    }
                },
                ["Ready"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["START_ACCESS"] = "InAccess",
                        ["CARRIER_REMOVED"] = "Unloading"
                    }
                },
                ["InAccess"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["ACCESS_COMPLETE"] = "ReadyToUnload",
                        ["ACCESS_ERROR"] = "Error"
                    }
                },
                ["ReadyToUnload"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["CARRIER_REMOVED"] = "Unloading"
                    }
                },
                ["Unloading"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["UNLOAD_COMPLETE"] = "Empty",
                        ["UNLOAD_ERROR"] = "Error"
                    },
                    ["after"] = new Dictionary<string, object>
                    {
                        ["500"] = new Dictionary<string, object>
                        {
                            ["target"] = "Empty"
                        }
                    }
                },
                ["Error"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["CLEAR_ERROR"] = "Empty",
                        ["RECOVER"] = "Loaded"
                    }
                }
            }
        };
        
        var json = System.Text.Json.JsonSerializer.Serialize(config);
        var stateMachine = new StateMachine();
        stateMachine.machineId = $"loadport_{portId}";
        return StateMachine.CreateFromScript(stateMachine, json);
    }
    
    /// <summary>
    /// Get current state of the load port
    /// </summary>
    public string GetCurrentState()
    {
        return StateMachine.GetSourceSubStateCollection(null).ToCsvString(StateMachine, true);
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