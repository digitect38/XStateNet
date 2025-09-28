using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

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
    public async Task<bool> DeleteControlJobAsync(string jobId)
    {
        if (_controlJobs.TryRemove(jobId, out var job))
        {
            await job.DeleteAsync();
            return true;
        }
        return false;
    }

    // Backward compatibility
    public bool DeleteControlJob(string jobId)
    {
        return DeleteControlJobAsync(jobId).GetAwaiter().GetResult();
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
    public ConcurrentDictionary<string, object> Properties { get; }
    
    public ControlJob(string jobId, List<string> carrierIds, string? recipeId, string? jsonScript)
    {
        JobId = jobId;
        CarrierIds = carrierIds;
        RecipeId = recipeId;
        CreatedTime = DateTime.UtcNow;
        ProcessedSubstrates = new List<string>();
        Properties = new ConcurrentDictionary<string, object>();

        // Create control job state machine
        StateMachine = CreateControlJobStateMachine(jobId, jsonScript);
        StateMachine.Start();
        InitializeAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously initialize the control job
    /// </summary>
    public async Task InitializeAsync()
    {
        await StateMachine.SendAsync("CREATE");
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

        // Update the id in the JSON to be unique for this job (including a GUID for parallel test safety)
        var uniqueId = $"job_{jobId}_{Guid.NewGuid():N}";
        jsonScript = jsonScript.Replace("\"id\": \"E94ControlJobStateMachine\"",
                                      $"\"id\": \"{uniqueId}\"");
        
        // Define action callbacks
        var actionMap = new ActionMap();
        
        // Create state machine from JSON script
        return StateMachineFactory.CreateFromScript(jsonScript, threadSafe: false, true, actionMap);
    }
    
    /// <summary>
    /// Select the control job
    /// </summary>
    public async Task SelectAsync()
    {
        await StateMachine.SendAsync("SELECT");
    }

    // Backward compatibility
    public void Select()
    {
        SelectAsync().GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Deselect the control job
    /// </summary>
    public async Task DeselectAsync()
    {
        await StateMachine.SendAsync("DESELECT");
    }

    // Backward compatibility
    public void Deselect()
    {
        DeselectAsync().GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Start the control job
    /// </summary>
    public async Task StartAsync()
    {
        await StateMachine.SendAsync("START");
        StartedTime = DateTime.UtcNow;
    }

    // Backward compatibility
    public void Start()
    {
        StartAsync().GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Pause the control job
    /// </summary>
    public async Task PauseAsync()
    {
        await StateMachine.SendAsync("PAUSE");
    }

    // Backward compatibility
    public void Pause()
    {
        PauseAsync().GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Resume the control job
    /// </summary>
    public async Task ResumeAsync()
    {
        await StateMachine.SendAsync("RESUME");
    }

    // Backward compatibility
    public void Resume()
    {
        ResumeAsync().GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Stop the control job
    /// </summary>
    public async Task StopAsync()
    {
        await StateMachine.SendAsync("STOP");
    }

    // Backward compatibility
    public void Stop()
    {
        StopAsync().GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Abort the control job
    /// </summary>
    public async Task AbortAsync()
    {
        await StateMachine.SendAsync("ABORT");
    }

    // Backward compatibility
    public void Abort()
    {
        AbortAsync().GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Delete the control job
    /// </summary>
    public async Task DeleteAsync()
    {
        await StateMachine.SendAsync("DELETE");
    }

    // Backward compatibility
    public void Delete()
    {
        DeleteAsync().GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Signal process started
    /// </summary>
    public async Task ProcessStartAsync()
    {
        await StateMachine.SendAsync("PROCESS_START");
    }

    // Backward compatibility
    public void ProcessStart()
    {
        ProcessStartAsync().GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Signal process completed
    /// </summary>
    public async Task ProcessCompleteAsync()
    {
        await StateMachine.SendAsync("PROCESS_COMPLETE");
        CompletedTime = DateTime.UtcNow;
    }

    // Backward compatibility
    public void ProcessComplete()
    {
        ProcessCompleteAsync().GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Signal material arrival
    /// </summary>
    public async Task MaterialInAsync(string carrierId)
    {
        await StateMachine.SendAsync("MATERIAL_IN");
    }

    // Backward compatibility
    public void MaterialIn(string carrierId)
    {
        MaterialInAsync(carrierId).GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Signal material departure
    /// </summary>
    public async Task MaterialOutAsync(string carrierId)
    {
        await StateMachine.SendAsync("MATERIAL_OUT");
    }

    // Backward compatibility
    public void MaterialOut(string carrierId)
    {
        MaterialOutAsync(carrierId).GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Signal material processed
    /// </summary>
    public async Task MaterialProcessedAsync(string substrateid)
    {
        ProcessedSubstrates.Add(substrateid);
        await StateMachine.SendAsync("MATERIAL_PROCESSED");
    }

    // Backward compatibility
    public void MaterialProcessed(string substrateid)
    {
        MaterialProcessedAsync(substrateid).GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Get current state
    /// </summary>
    public string GetCurrentState()
    {
        return StateMachine.GetActiveStateNames();
    }
}