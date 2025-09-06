using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XStateNet.Semi;

/// <summary>
/// E90 Substrate Tracking implementation
/// </summary>
public class E90SubstrateTracking
{
    private readonly ConcurrentDictionary<string, Substrate> _substrates = new();
    private readonly ConcurrentDictionary<string, SubstrateLocation> _locations = new();
    private readonly ConcurrentDictionary<string, List<SubstrateHistory>> _history = new();
    private readonly object _updateLock = new();
    
    /// <summary>
    /// Register a new substrate
    /// </summary>
    public Substrate RegisterSubstrate(string substrateid, string? lotId = null, int? slotNumber = null)
    {
        var substrate = new Substrate(substrateid)
        {
            LotId = lotId,
            SlotNumber = slotNumber,
            State = SubstrateState.WaitingForHost,
            AcquiredTime = DateTime.UtcNow
        };
        
        _substrates[substrateid] = substrate;
        _history[substrateid] = new List<SubstrateHistory>();
        
        AddHistory(substrateid, SubstrateState.WaitingForHost, null, "Substrate registered");
        
        return substrate;
    }
    
    /// <summary>
    /// Update substrate location
    /// </summary>
    public void UpdateLocation(string substrateid, string locationId, SubstrateLocationType locationType)
    {
        if (!_substrates.ContainsKey(substrateid))
            return;
            
        var location = new SubstrateLocation(locationId, locationType);
        
        lock (_updateLock)
        {
            // Record previous location in history
            if (_locations.TryGetValue(substrateid, out var prevLocation))
            {
                AddHistory(substrateid, null, prevLocation.LocationId, 
                    $"Moved from {prevLocation.LocationId} to {locationId}");
            }
            
            _locations[substrateid] = location;
        }
    }
    
    /// <summary>
    /// Update substrate state
    /// </summary>
    public void UpdateState(string substrateid, SubstrateState newState, string? reason = null)
    {
        if (_substrates.TryGetValue(substrateid, out var substrate))
        {
            lock (_updateLock)
            {
                var oldState = substrate.State;
                substrate.State = newState;
                substrate.StateChangeTime = DateTime.UtcNow;
                
                // Update processing times
                if (oldState == SubstrateState.NeedsProcessing && newState == SubstrateState.InProcess)
                {
                    substrate.ProcessStartTime = DateTime.UtcNow;
                }
                else if (oldState == SubstrateState.InProcess && 
                         (newState == SubstrateState.Processed || newState == SubstrateState.Aborted))
                {
                    substrate.ProcessEndTime = DateTime.UtcNow;
                    if (substrate.ProcessStartTime.HasValue)
                    {
                        substrate.ProcessingTime = substrate.ProcessEndTime.Value - substrate.ProcessStartTime.Value;
                    }
                }
                
                AddHistory(substrateid, newState, null, reason ?? $"State changed from {oldState} to {newState}");
            }
        }
    }
    
    /// <summary>
    /// Start processing a substrate
    /// </summary>
    public bool StartProcessing(string substrateid, string? recipeId = null)
    {
        if (_substrates.TryGetValue(substrateid, out var substrate))
        {
            if (substrate.State != SubstrateState.NeedsProcessing)
                return false;
                
            lock (_updateLock)
            {
                substrate.RecipeId = recipeId;
                UpdateState(substrateid, SubstrateState.InProcess, $"Started processing with recipe {recipeId}");
            }
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Complete processing
    /// </summary>
    public bool CompleteProcessing(string substrateid, bool success = true)
    {
        if (_substrates.TryGetValue(substrateid, out var substrate))
        {
            if (substrate.State != SubstrateState.InProcess)
                return false;
                
            var newState = success ? SubstrateState.Processed : SubstrateState.Aborted;
            UpdateState(substrateid, newState, success ? "Processing completed" : "Processing aborted");
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Remove substrate from tracking
    /// </summary>
    public bool RemoveSubstrate(string substrateid)
    {
        if (_substrates.TryRemove(substrateid, out var substrate))
        {
            _locations.TryRemove(substrateid, out _);
            AddHistory(substrateid, SubstrateState.Removed, null, "Substrate removed from tracking");
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Get substrate information
    /// </summary>
    public Substrate? GetSubstrate(string substrateid)
    {
        return _substrates.TryGetValue(substrateid, out var substrate) ? substrate : null;
    }
    
    /// <summary>
    /// Get all substrates in a specific state
    /// </summary>
    public IEnumerable<Substrate> GetSubstratesByState(SubstrateState state)
    {
        return _substrates.Values.Where(s => s.State == state);
    }
    
    /// <summary>
    /// Get all substrates at a specific location
    /// </summary>
    public IEnumerable<string> GetSubstratesAtLocation(string locationId)
    {
        return _locations
            .Where(kvp => kvp.Value.LocationId == locationId)
            .Select(kvp => kvp.Key);
    }
    
    /// <summary>
    /// Get substrate history
    /// </summary>
    public IReadOnlyList<SubstrateHistory> GetHistory(string substrateid)
    {
        return _history.TryGetValue(substrateid, out var history) 
            ? history.AsReadOnly() 
            : new List<SubstrateHistory>().AsReadOnly();
    }
    
    /// <summary>
    /// Add history entry
    /// </summary>
    private void AddHistory(string substrateid, SubstrateState? state, string? location, string description)
    {
        if (_history.TryGetValue(substrateid, out var history))
        {
            history.Add(new SubstrateHistory
            {
                Timestamp = DateTime.UtcNow,
                State = state,
                Location = location,
                Description = description
            });
        }
    }
}

/// <summary>
/// Substrate information
/// </summary>
public class Substrate
{
    public string Id { get; set; }
    public string? LotId { get; set; }
    public int? SlotNumber { get; set; }
    public SubstrateState State { get; set; }
    public DateTime AcquiredTime { get; set; }
    public DateTime StateChangeTime { get; set; }
    public DateTime? ProcessStartTime { get; set; }
    public DateTime? ProcessEndTime { get; set; }
    public TimeSpan? ProcessingTime { get; set; }
    public string? RecipeId { get; set; }
    public Dictionary<string, object> Properties { get; set; }
    
    public Substrate(string id)
    {
        Id = id;
        Properties = new Dictionary<string, object>();
        StateChangeTime = DateTime.UtcNow;
    }
}

/// <summary>
/// Substrate states per E90
/// </summary>
public enum SubstrateState
{
    WaitingForHost,
    InCarrier,
    NeedsProcessing,
    InProcess,
    Processed,
    Aborted,
    Stopped,
    Rejected,
    Lost,
    Skipped,
    Removed
}

/// <summary>
/// Substrate location information
/// </summary>
public class SubstrateLocation
{
    public string LocationId { get; set; }
    public SubstrateLocationType Type { get; set; }
    public DateTime Timestamp { get; set; }
    
    public SubstrateLocation(string locationId, SubstrateLocationType type)
    {
        LocationId = locationId;
        Type = type;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Substrate location types
/// </summary>
public enum SubstrateLocationType
{
    Carrier,
    ProcessModule,
    TransferModule,
    Aligner,
    Buffer,
    LoadPort,
    Other
}

/// <summary>
/// Substrate history entry
/// </summary>
public class SubstrateHistory
{
    public DateTime Timestamp { get; set; }
    public SubstrateState? State { get; set; }
    public string? Location { get; set; }
    public string Description { get; set; } = "";
}