using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace XStateNet.Semi;

/// <summary>
/// E94 Control Job Management implementation using XStateNet
/// </summary>
public class E94ControlJobManager
{
    private readonly ConcurrentDictionary<string, ControlJob> _controlJobs = new();
    private static string? _jsonScript;
    
    /// <summary>
    /// Load the E94 Control Job state machine JSON script
    /// </summary>
    static E94ControlJobManager()
    {
        // Load embedded JSON resource or from file
        var assembly = typeof(E94ControlJobManager).Assembly;
        var resourceName = "SemiStandard.XStateScripts.E94ControlJobStates.json";
        
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
            var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "E94ControlJobStates.json");
            if (File.Exists(jsonPath))
            {
                _jsonScript = File.ReadAllText(jsonPath);
            }
            else
            {
                jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "SemiStandard.XStateScripts", "E94ControlJobStates.json");
                if (File.Exists(jsonPath))
                {
                    _jsonScript = File.ReadAllText(jsonPath);
                }
            }
        }
    }
    
    /// <summary>
    /// Create a new control job
    /// </summary>
    public ControlJob CreateControlJob(string jobId, List<string> carrierIds, string? recipeId = null)
    {
        var job = new ControlJob(jobId, carrierIds, recipeId, _jsonScript);
        _controlJobs[jobId] = job;
        return job;
    }
    
    /// <summary>
    /// Get control job by ID
    /// </summary>
    public ControlJob? GetControlJob(string jobId)
    {
        return _controlJobs.TryGetValue(jobId, out var job) ? job : null;
    }
    
    /// <summary>
    /// Delete control job
    /// </summary>
    public bool DeleteControlJob(string jobId)
    {
        if (_controlJobs.TryRemove(jobId, out var job))
        {
            job.Delete();
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Get all control jobs
    /// </summary>
    public IEnumerable<ControlJob> GetAllJobs()
    {
        return _controlJobs.Values;
    }
}

/// <summary>
/// Control Job with its own XStateNet state machine
/// </summary>
public class ControlJob
{
    public string JobId { get; }
    public List<string> CarrierIds { get; }
    public string? RecipeId { get; }
    public StateMachine StateMachine { get; }
    public DateTime CreatedTime { get; }
    public DateTime? StartedTime { get; private set; }
    public DateTime? CompletedTime { get; private set; }
    public List<string> ProcessedSubstrates { get; }
    public Dictionary<string, object> Properties { get; }
    
    public ControlJob(string jobId, List<string> carrierIds, string? recipeId, string? jsonScript)
    {
        JobId = jobId;
        CarrierIds = carrierIds;
        RecipeId = recipeId;
        CreatedTime = DateTime.UtcNow;
        ProcessedSubstrates = new List<string>();
        Properties = new Dictionary<string, object>();
        
        // Create control job state machine
        StateMachine = CreateControlJobStateMachine(jobId, jsonScript);
        StateMachine.Start();
        StateMachine.Send("CREATE");
    }
    
    /// <summary>
    /// Creates the E94 control job state machine from JSON script
    /// </summary>
    private StateMachine CreateControlJobStateMachine(string jobId, string? jsonScript)
    {
        if (string.IsNullOrEmpty(jsonScript))
        {
            throw new InvalidOperationException("E94ControlJobStates.json file not found.");
        }
        
        // Update the id in the JSON to be unique for this job
        jsonScript = jsonScript.Replace("\"id\": \"E94ControlJobStateMachine\"", 
                                      $"\"id\": \"job_{jobId}\"");
        
        // Define action callbacks
        var actionMap = new ActionMap();
        
        // Create state machine from JSON script
        return StateMachine.CreateFromScript(jsonScript, actionMap);
    }
    
    /// <summary>
    /// Select the control job
    /// </summary>
    public void Select()
    {
        StateMachine.Send("SELECT");
    }
    
    /// <summary>
    /// Deselect the control job
    /// </summary>
    public void Deselect()
    {
        StateMachine.Send("DESELECT");
    }
    
    /// <summary>
    /// Start the control job
    /// </summary>
    public void Start()
    {
        StateMachine.Send("START");
        StartedTime = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Pause the control job
    /// </summary>
    public void Pause()
    {
        StateMachine.Send("PAUSE");
    }
    
    /// <summary>
    /// Resume the control job
    /// </summary>
    public void Resume()
    {
        StateMachine.Send("RESUME");
    }
    
    /// <summary>
    /// Stop the control job
    /// </summary>
    public void Stop()
    {
        StateMachine.Send("STOP");
    }
    
    /// <summary>
    /// Abort the control job
    /// </summary>
    public void Abort()
    {
        StateMachine.Send("ABORT");
    }
    
    /// <summary>
    /// Delete the control job
    /// </summary>
    public void Delete()
    {
        StateMachine.Send("DELETE");
    }
    
    /// <summary>
    /// Signal process started
    /// </summary>
    public void ProcessStart()
    {
        StateMachine.Send("PROCESS_START");
    }
    
    /// <summary>
    /// Signal process completed
    /// </summary>
    public void ProcessComplete()
    {
        StateMachine.Send("PROCESS_COMPLETE");
        CompletedTime = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Signal material arrival
    /// </summary>
    public void MaterialIn(string carrierId)
    {
        StateMachine.Send("MATERIAL_IN");
    }
    
    /// <summary>
    /// Signal material departure
    /// </summary>
    public void MaterialOut(string carrierId)
    {
        StateMachine.Send("MATERIAL_OUT");
    }
    
    /// <summary>
    /// Signal material processed
    /// </summary>
    public void MaterialProcessed(string substrateid)
    {
        ProcessedSubstrates.Add(substrateid);
        StateMachine.Send("MATERIAL_PROCESSED");
    }
    
    /// <summary>
    /// Get current state
    /// </summary>
    public string GetCurrentState()
    {
        return StateMachine.GetSourceSubStateCollection(null).ToCsvString(StateMachine, true);
    }
}