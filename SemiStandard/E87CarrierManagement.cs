using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XStateNet.Semi;

/// <summary>
/// E87 Carrier Management System implementation
/// </summary>
public class E87CarrierManagement
{
    private readonly ConcurrentDictionary<string, Carrier> _carriers = new();
    private readonly ConcurrentDictionary<string, LoadPort> _loadPorts = new();
    private readonly ConcurrentDictionary<string, CarrierHistory> _carrierHistory = new();
    private readonly object _updateLock = new();
    
    /// <summary>
    /// Register a load port
    /// </summary>
    public void RegisterLoadPort(string portId, string portName, int capacity = 25)
    {
        _loadPorts[portId] = new LoadPort(portId, portName, capacity);
    }
    
    /// <summary>
    /// Carrier arrives at load port
    /// </summary>
    public Carrier? CarrierArrived(string carrierId, string portId, int slotCount = 25)
    {
        if (!_loadPorts.ContainsKey(portId))
            return null;
            
        var carrier = new Carrier(carrierId)
        {
            LoadPortId = portId,
            SlotCount = slotCount,
            State = CarrierState.WaitingForHost,
            ArrivedTime = DateTime.UtcNow
        };
        
        // Initialize slot map
        for (int i = 1; i <= slotCount; i++)
        {
            carrier.SlotMap[i] = SlotState.Unknown;
        }
        
        lock (_updateLock)
        {
            _carriers[carrierId] = carrier;
            _loadPorts[portId].CurrentCarrierId = carrierId;
            _loadPorts[portId].State = LoadPortState.Loaded;
            
            AddCarrierHistory(carrierId, CarrierState.WaitingForHost, 
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
                carrier.State = CarrierState.InAccess;
                
                // Count substrates
                carrier.SubstrateCount = carrier.SlotMap.Count(s => s.Value == SlotState.Present);
                
                AddCarrierHistory(carrierId, CarrierState.InAccess, 
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
    /// Update carrier state
    /// </summary>
    public void UpdateCarrierState(string carrierId, CarrierState newState, string? reason = null)
    {
        if (_carriers.TryGetValue(carrierId, out var carrier))
        {
            lock (_updateLock)
            {
                var oldState = carrier.State;
                carrier.State = newState;
                carrier.StateChangeTime = DateTime.UtcNow;
                
                AddCarrierHistory(carrierId, newState, 
                    reason ?? $"State changed from {oldState} to {newState}");
                    
                // Update load port state if carrier completed
                if (newState == CarrierState.Complete && !string.IsNullOrEmpty(carrier.LoadPortId))
                {
                    if (_loadPorts.TryGetValue(carrier.LoadPortId, out var loadPort))
                    {
                        loadPort.State = LoadPortState.ReadyToUnload;
                    }
                }
            }
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
                carrier.State = CarrierState.Complete;
                
                // Clear load port
                if (!string.IsNullOrEmpty(carrier.LoadPortId))
                {
                    if (_loadPorts.TryGetValue(carrier.LoadPortId, out var loadPort))
                    {
                        loadPort.CurrentCarrierId = null;
                        loadPort.State = LoadPortState.Empty;
                    }
                }
                
                AddCarrierHistory(carrierId, CarrierState.Complete, "Carrier departed");
                
                // Remove from active tracking
                _carriers.TryRemove(carrierId, out _);
            }
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Get carrier information
    /// </summary>
    public Carrier? GetCarrier(string carrierId)
    {
        return _carriers.TryGetValue(carrierId, out var carrier) ? carrier : null;
    }
    
    /// <summary>
    /// Get carrier at load port
    /// </summary>
    public Carrier? GetCarrierAtPort(string portId)
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
    public IEnumerable<Carrier> GetActiveCarriers()
    {
        return _carriers.Values;
    }
    
    /// <summary>
    /// Get load port status
    /// </summary>
    public LoadPort? GetLoadPort(string portId)
    {
        return _loadPorts.TryGetValue(portId, out var port) ? port : null;
    }
    
    /// <summary>
    /// Get all load ports
    /// </summary>
    public IEnumerable<LoadPort> GetLoadPorts()
    {
        return _loadPorts.Values;
    }
    
    /// <summary>
    /// Add carrier history
    /// </summary>
    private void AddCarrierHistory(string carrierId, CarrierState state, string description)
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
/// Carrier (FOUP/Cassette) information
/// </summary>
public class Carrier
{
    public string Id { get; set; }
    public string? LoadPortId { get; set; }
    public CarrierState State { get; set; }
    public int SlotCount { get; set; }
    public Dictionary<int, SlotState> SlotMap { get; set; }
    public Dictionary<int, string> SubstrateIds { get; set; }
    public int SubstrateCount { get; set; }
    public DateTime ArrivedTime { get; set; }
    public DateTime StateChangeTime { get; set; }
    public DateTime? MappingCompleteTime { get; set; }
    public DateTime? DepartedTime { get; set; }
    public Dictionary<string, object> Properties { get; set; }
    
    public Carrier(string id)
    {
        Id = id;
        SlotMap = new Dictionary<int, SlotState>();
        SubstrateIds = new Dictionary<int, string>();
        Properties = new Dictionary<string, object>();
        StateChangeTime = DateTime.UtcNow;
    }
}

/// <summary>
/// Carrier states per E87
/// </summary>
public enum CarrierState
{
    NotPresent,
    WaitingForHost,
    InAccess,
    Complete,
    CarrierOut
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
/// Load port information
/// </summary>
public class LoadPort
{
    public string Id { get; set; }
    public string Name { get; set; }
    public LoadPortState State { get; set; }
    public string? CurrentCarrierId { get; set; }
    public int Capacity { get; set; }
    public bool IsReserved { get; set; }
    public Dictionary<string, object> Properties { get; set; }
    
    public LoadPort(string id, string name, int capacity = 25)
    {
        Id = id;
        Name = name;
        Capacity = capacity;
        State = LoadPortState.Empty;
        Properties = new Dictionary<string, object>();
    }
}

/// <summary>
/// Load port states
/// </summary>
public enum LoadPortState
{
    Empty,
    Loading,
    Loaded,
    Mapping,
    Ready,
    InAccess,
    ReadyToUnload,
    Unloading,
    Error
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
    public CarrierState State { get; set; }
    public string Description { get; set; } = "";
}