using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XStateNet.Semi;

/// <summary>
/// E90 Substrate Tracking implementation using XStateNet state machines
/// </summary>
public class E90SubstrateTracking
{
    private readonly ConcurrentDictionary<string, SubstrateStateMachine> _substrates = new();
    private readonly ConcurrentDictionary<string, SubstrateLocation> _locations = new();
    private readonly ConcurrentDictionary<string, List<SubstrateHistory>> _history = new();
    private readonly object _updateLock = new();
    
    /// <summary>
    /// Register a new substrate with its own state machine
    /// </summary>
    public SubstrateStateMachine RegisterSubstrate(string substrateid, string? lotId = null, int? slotNumber = null)
    {
        var substrate = new SubstrateStateMachine(substrateid, lotId, slotNumber);
        _substrates[substrateid] = substrate;
        _history[substrateid] = new List<SubstrateHistory>();
        
        AddHistory(substrateid, "WaitingForHost", null, "Substrate registered");
        
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
            
            // Send location change event to substrate state machine
            if (_substrates.TryGetValue(substrateid, out var substrate))
            {
                // Trigger state machine transition based on location type
                switch (locationType)
                {
                    case SubstrateLocationType.ProcessModule:
                        substrate.StateMachine.Send("PLACED_IN_PROCESS_MODULE");
                        break;
                    case SubstrateLocationType.Carrier:
                        substrate.StateMachine.Send("PLACED_IN_CARRIER");
                        break;
                    case SubstrateLocationType.Aligner:
                        substrate.StateMachine.Send("PLACED_IN_ALIGNER");
                        break;
                }
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
            substrate.RecipeId = recipeId;
            substrate.StateMachine.Send("START_PROCESS");
            AddHistory(substrateid, "InProcess", null, $"Started processing with recipe {recipeId}");
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
            substrate.StateMachine.Send(success ? "PROCESS_COMPLETE" : "PROCESS_ABORT");
            AddHistory(substrateid, success ? "Processed" : "Aborted", null, 
                success ? "Processing completed" : "Processing aborted");
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Remove substrate from tracking
    /// </summary>
    public bool RemoveSubstrate(string substrateid)
    {
        if (_substrates.TryGetValue(substrateid, out var substrate))
        {
            substrate.StateMachine.Send("REMOVE");
            _substrates.TryRemove(substrateid, out _);
            _locations.TryRemove(substrateid, out _);
            AddHistory(substrateid, "Removed", null, "Substrate removed from tracking");
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Get substrate information
    /// </summary>
    public SubstrateStateMachine? GetSubstrate(string substrateid)
    {
        return _substrates.TryGetValue(substrateid, out var substrate) ? substrate : null;
    }
    
    /// <summary>
    /// Get all substrates in a specific state
    /// </summary>
    public IEnumerable<SubstrateStateMachine> GetSubstratesByState(string stateName)
    {
        return _substrates.Values.Where(s => s.GetCurrentState().Contains(stateName));
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
    private void AddHistory(string substrateid, string? state, string? location, string description)
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
/// Substrate with its own XStateNet state machine
/// </summary>
public class SubstrateStateMachine
{
    public string Id { get; }
    public string? LotId { get; set; }
    public int? SlotNumber { get; set; }
    public StateMachine StateMachine { get; }
    public DateTime AcquiredTime { get; }
    public DateTime? ProcessStartTime { get; set; }
    public DateTime? ProcessEndTime { get; set; }
    public TimeSpan? ProcessingTime { get; set; }
    public string? RecipeId { get; set; }
    public Dictionary<string, object> Properties { get; set; }
    
    public SubstrateStateMachine(string id, string? lotId = null, int? slotNumber = null)
    {
        Id = id;
        LotId = lotId;
        SlotNumber = slotNumber;
        Properties = new Dictionary<string, object>();
        AcquiredTime = DateTime.UtcNow;
        
        // Create E90 substrate state machine
        StateMachine = CreateE90StateMachine(id);
        StateMachine.Start();
    }
    
    /// <summary>
    /// Creates the E90-compliant substrate state machine
    /// </summary>
    private StateMachine CreateE90StateMachine(string substrateid)
    {
        var config = new Dictionary<string, object>
        {
            ["id"] = $"substrate_{substrateid}",
            ["initial"] = "WaitingForHost",
            ["states"] = new Dictionary<string, object>
            {
                ["WaitingForHost"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["ACQUIRE"] = "InCarrier",
                        ["PLACED_IN_CARRIER"] = "InCarrier"
                    }
                },
                ["InCarrier"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["SELECT_FOR_PROCESS"] = "NeedsProcessing",
                        ["SKIP"] = "Skipped",
                        ["REJECT"] = "Rejected"
                    }
                },
                ["NeedsProcessing"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["PLACED_IN_PROCESS_MODULE"] = "ReadyToProcess",
                        ["PLACED_IN_ALIGNER"] = "Aligning",
                        ["ABORT"] = "Aborted"
                    }
                },
                ["Aligning"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["ALIGN_COMPLETE"] = "ReadyToProcess",
                        ["ALIGN_FAIL"] = "Rejected"
                    }
                },
                ["ReadyToProcess"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["START_PROCESS"] = "InProcess",
                        ["ABORT"] = "Aborted"
                    }
                },
                ["InProcess"] = new Dictionary<string, object>
                {
                    ["entry"] = new[] { "recordProcessStart" },
                    ["exit"] = new[] { "recordProcessEnd" },
                    ["on"] = new Dictionary<string, object>
                    {
                        ["PROCESS_COMPLETE"] = "Processed",
                        ["PROCESS_ABORT"] = "Aborted",
                        ["PROCESS_STOP"] = "Stopped",
                        ["PROCESS_ERROR"] = "Rejected"
                    }
                },
                ["Processed"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["PLACED_IN_CARRIER"] = "Complete",
                        ["REMOVE"] = "Removed"
                    }
                },
                ["Aborted"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["PLACED_IN_CARRIER"] = "Complete",
                        ["REMOVE"] = "Removed"
                    }
                },
                ["Stopped"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["RESUME"] = "InProcess",
                        ["ABORT"] = "Aborted"
                    }
                },
                ["Rejected"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["PLACED_IN_CARRIER"] = "Complete",
                        ["REMOVE"] = "Removed"
                    }
                },
                ["Lost"] = new Dictionary<string, object>
                {
                    ["type"] = "final"
                },
                ["Skipped"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["PLACED_IN_CARRIER"] = "Complete",
                        ["REMOVE"] = "Removed"
                    }
                },
                ["Complete"] = new Dictionary<string, object>
                {
                    ["on"] = new Dictionary<string, object>
                    {
                        ["REMOVE"] = "Removed"
                    }
                },
                ["Removed"] = new Dictionary<string, object>
                {
                    ["type"] = "final"
                }
            }
        };
        
        // Create action callbacks
        var actionMap = new ActionMap();
        actionMap["recordProcessStart"] = new List<NamedAction>
        {
            new NamedAction("recordProcessStart", (sm) => 
            {
                ProcessStartTime = DateTime.UtcNow;
            })
        };
        
        actionMap["recordProcessEnd"] = new List<NamedAction>
        {
            new NamedAction("recordProcessEnd", (sm) => 
            {
                ProcessEndTime = DateTime.UtcNow;
                if (ProcessStartTime.HasValue)
                {
                    ProcessingTime = ProcessEndTime.Value - ProcessStartTime.Value;
                }
            })
        };
        
        var json = System.Text.Json.JsonSerializer.Serialize(config);
        var stateMachine = new StateMachine();
        stateMachine.machineId = $"substrate_{substrateid}";
        return StateMachine.CreateFromScript(stateMachine, json, actionMap);
    }
    
    /// <summary>
    /// Get current state of the substrate
    /// </summary>
    public string GetCurrentState()
    {
        return StateMachine.GetSourceSubStateCollection(null).ToCsvString(StateMachine, true);
    }
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
    public string? State { get; set; }
    public string? Location { get; set; }
    public string Description { get; set; } = "";
}