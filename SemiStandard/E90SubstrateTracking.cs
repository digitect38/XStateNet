using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;

namespace XStateNet.Semi;

/// <summary>
/// E90 Substrate Tracking implementation using XStateNet state machines
/// Uses JSON scripts to define state machines as intended by XStateNet design
/// </summary>
public class E90SubstrateTracking
{
    private readonly ConcurrentDictionary<string, SubstrateStateMachine> _substrates = new();
    private readonly ConcurrentDictionary<string, SubstrateLocation> _locations = new();
    private readonly ConcurrentDictionary<string, List<SubstrateHistory>> _history = new();
    private readonly object _updateLock = new();
    private static string? _jsonScript;
    
    /// <summary>
    /// Load the E90 state machine JSON script
    /// </summary>
    static E90SubstrateTracking()
    {
        // Load embedded JSON resource or from file
        var assembly = typeof(E90SubstrateTracking).Assembly;
        var resourceName = "SemiStandard.XStateScripts.E90SubstrateStates.json";
        
        // First try to load from embedded resource
        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream != null)
            {
                using (var reader = new StreamReader(stream))
                {
                    _jsonScript = reader.ReadToEnd();
                }
            }
        }
        
        // If not embedded, try to load from file
        if (string.IsNullOrEmpty(_jsonScript))
        {
            var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XStateScripts", "E90SubstrateStates.json");
            if (File.Exists(jsonPath))
            {
                _jsonScript = File.ReadAllText(jsonPath);
            }
            else
            {
                // If file doesn't exist in base directory, try the SemiStandard directory
                jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "SemiStandard", "XStateScripts", "E90SubstrateStates.json");
                if (File.Exists(jsonPath))
                {
                    _jsonScript = File.ReadAllText(jsonPath);
                }
            }
        }
    }
    
    /// <summary>
    /// Register a new substrate with its own state machine
    /// </summary>
    public SubstrateStateMachine RegisterSubstrate(string substrateid, string? lotId = null, int? slotNumber = null)
    {
        var substrate = new SubstrateStateMachine(substrateid, lotId, slotNumber, _jsonScript);
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
            // Record location change in history
            if (_locations.TryGetValue(substrateid, out var prevLocation))
            {
                AddHistory(substrateid, null, locationId, 
                    $"Moved from {prevLocation.LocationId} to {locationId}");
            }
            else
            {
                // First location update
                AddHistory(substrateid, null, locationId, 
                    $"Located at {locationId}");
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
/// Substrate with its own XStateNet state machine created from JSON
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
    
    public SubstrateStateMachine(string id, string? lotId = null, int? slotNumber = null, string? jsonScript = null)
    {
        Id = id;
        LotId = lotId;
        SlotNumber = slotNumber;
        Properties = new Dictionary<string, object>();
        AcquiredTime = DateTime.UtcNow;
        
        // Create E90 substrate state machine from JSON script
        StateMachine = CreateE90StateMachine(id, jsonScript);
        StateMachine.Start();
    }
    
    /// <summary>
    /// Creates the E90-compliant substrate state machine from JSON script
    /// </summary>
    private StateMachine CreateE90StateMachine(string substrateid, string? jsonScript)
    {
        // Define action callbacks for the state machine
        var actionMap = new ActionMap();
        
        actionMap["recordProcessStart"] = new List<NamedAction>
        {
            new NamedAction("recordProcessStart", (sm) => 
            {
                ProcessStartTime = DateTime.UtcNow;
                Logger.Info($"Substrate {substrateid} process started at {ProcessStartTime}");
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
                    Logger.Info($"Substrate {substrateid} process ended. Duration: {ProcessingTime}");
                }
            })
        };
        
        // Use the provided JSON script
        if (string.IsNullOrEmpty(jsonScript))
        {
            throw new InvalidOperationException("E90SubstrateStates.json file not found. Please ensure the JSON file is included as an embedded resource or available in the application directory.");
        }
        
        // Update the id in the JSON to be unique for this substrate
        jsonScript = jsonScript.Replace("\"id\": \"E90SubstrateStateMachine\"", 
                                      $"\"id\": \"substrate_{substrateid}\"");
        
        // Create state machine from JSON script using XStateNet's intended API
        return StateMachine.CreateFromScript(jsonScript, actionMap);
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