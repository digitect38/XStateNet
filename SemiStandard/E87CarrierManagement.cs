using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;

namespace XStateNet.Semi;

/// <summary>
/// E87 Carrier Management System implementation using XStateNet state machines
/// Uses JSON scripts to define state machines as intended by XStateNet design
/// </summary>
public class E87CarrierManagement
{
    private readonly ConcurrentDictionary<string, CarrierStateMachine> _carriers = new();
    private readonly ConcurrentDictionary<string, LoadPortStateMachine> _loadPorts = new();
    private readonly ConcurrentDictionary<string, CarrierHistory> _carrierHistory = new();
    private readonly object _updateLock = new();
    private static string? _carrierJsonScript;
    private static string? _loadPortJsonScript;
    
    /// <summary>
    /// Load the E87 state machine JSON scripts
    /// </summary>
    static E87CarrierManagement()
    {
        // Load embedded JSON resources or from files
        var assembly = typeof(E87CarrierManagement).Assembly;
        
        // Load carrier state machine JSON
        var carrierResourceName = "SemiStandard.XStateScripts.E87CarrierStates.json";
        using (var stream = assembly.GetManifestResourceStream(carrierResourceName))
        {
            if (stream != null)
            {
                using (var reader = new StreamReader(stream))
                {
                    _carrierJsonScript = reader.ReadToEnd();
                }
            }
        }
        
        // If not embedded, try to load from file
        if (string.IsNullOrEmpty(_carrierJsonScript))
        {
            var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "E87CarrierStates.json");
            if (File.Exists(jsonPath))
            {
                _carrierJsonScript = File.ReadAllText(jsonPath);
            }
            else
            {
                jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "SemiStandard.XStateScripts", "E87CarrierStates.json");
                if (File.Exists(jsonPath))
                {
                    _carrierJsonScript = File.ReadAllText(jsonPath);
                }
            }
        }
        
        // Load load port state machine JSON
        var loadPortResourceName = "SemiStandard.XStateScripts.E87LoadPortStates.json";
        using (var stream = assembly.GetManifestResourceStream(loadPortResourceName))
        {
            if (stream != null)
            {
                using (var reader = new StreamReader(stream))
                {
                    _loadPortJsonScript = reader.ReadToEnd();
                }
            }
        }
        
        // If not embedded, try to load from file
        if (string.IsNullOrEmpty(_loadPortJsonScript))
        {
            var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "E87LoadPortStates.json");
            if (File.Exists(jsonPath))
            {
                _loadPortJsonScript = File.ReadAllText(jsonPath);
            }
            else
            {
                jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "SemiStandard.XStateScripts", "E87LoadPortStates.json");
                if (File.Exists(jsonPath))
                {
                    _loadPortJsonScript = File.ReadAllText(jsonPath);
                }
            }
        }
    }
    
    /// <summary>
    /// Register a load port with its own state machine
    /// </summary>
    public void RegisterLoadPort(string portId, string portName, int capacity = 25)
    {
        _loadPorts[portId] = new LoadPortStateMachine(portId, portName, capacity, _loadPortJsonScript);
    }
    
    /// <summary>
    /// Carrier arrives at load port
    /// </summary>
    public CarrierStateMachine? CarrierArrived(string carrierId, string portId, int slotCount = 25)
    {
        if (!_loadPorts.TryGetValue(portId, out var loadPort))
            return null;
            
        var carrier = new CarrierStateMachine(carrierId, portId, slotCount, _carrierJsonScript);
        
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
    /// Start carrier processing
    /// </summary>
    public bool StartCarrierProcessing(string carrierId)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            carrier.StateMachine.Send("HOST_PROCEED");
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Complete carrier processing
    /// </summary>
    public bool CompleteCarrierProcessing(string carrierId)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            carrier.StateMachine.Send("ACCESS_COMPLETE");
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Remove carrier from system
    /// </summary>
    public bool RemoveCarrier(string carrierId)
    {
        if (_carriers.TryRemove(carrierId, out var carrier))
        {
            carrier.StateMachine.Send("CARRIER_REMOVED");
            carrier.DepartedTime = DateTime.UtcNow;
            
            // Clear from load port
            if (_loadPorts.TryGetValue(carrier.LoadPortId, out var loadPort))
            {
                loadPort.CurrentCarrierId = null;
            }
            
            AddCarrierHistory(carrierId, "Removed", "Carrier removed from system");
            return true;
        }
        return false;
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
    
    public CarrierStateMachine(string id, string loadPortId, int slotCount = 25, string? jsonScript = null)
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
        
        // Create E87 carrier state machine from JSON script
        StateMachine = CreateE87StateMachine(id, jsonScript);
        StateMachine.Start();
    }
    
    /// <summary>
    /// Creates the E87-compliant carrier state machine from JSON script
    /// </summary>
    private StateMachine CreateE87StateMachine(string carrierId, string? jsonScript)
    {
        // Define action callbacks for the state machine
        var actionMap = new ActionMap();
        
        actionMap["startMapping"] = new List<NamedAction>
        {
            new NamedAction("startMapping", (sm) => 
            {
                MappingCompleteTime = DateTime.UtcNow;
                Logger.Info($"Starting slot mapping for carrier {carrierId}");
            })
        };
        
        // Use the provided JSON script
        if (string.IsNullOrEmpty(jsonScript))
        {
            throw new InvalidOperationException("E87CarrierStates.json file not found. Please ensure the JSON file is included as an embedded resource or available in the application directory.");
        }
        
        // Update the id in the JSON to be unique for this carrier
        jsonScript = jsonScript.Replace("\"id\": \"E87CarrierStateMachine\"", 
                                      $"\"id\": \"carrier_{carrierId}\"");
        
        // Create state machine from JSON script using XStateNet's intended API
        return StateMachine.CreateFromScript(jsonScript, actionMap);
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
    
    public LoadPortStateMachine(string id, string name, int capacity = 25, string? jsonScript = null)
    {
        Id = id;
        Name = name;
        Capacity = capacity;
        Properties = new Dictionary<string, object>();
        
        // Create load port state machine from JSON script
        StateMachine = CreateLoadPortStateMachine(id, jsonScript);
        StateMachine.Start();
    }
    
    /// <summary>
    /// Creates the load port state machine from JSON script
    /// </summary>
    private StateMachine CreateLoadPortStateMachine(string portId, string? jsonScript)
    {
        // Use the provided JSON script
        if (string.IsNullOrEmpty(jsonScript))
        {
            throw new InvalidOperationException("E87LoadPortStates.json file not found. Please ensure the JSON file is included as an embedded resource or available in the application directory.");
        }
        
        // Update the id in the JSON to be unique for this load port
        jsonScript = jsonScript.Replace("\"id\": \"E87LoadPortStateMachine\"", 
                                      $"\"id\": \"loadport_{portId}\"");
        
        // Create state machine from JSON script using XStateNet's intended API
        return StateMachine.CreateFromScript(jsonScript);
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