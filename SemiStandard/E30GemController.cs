using System;
using System.IO;
using System.Reflection;

namespace XStateNet.Semi;

/// <summary>
/// E30 GEM (Generic Equipment Model) controller implementation using XStateNet
/// </summary>
public class E30GemController
{
    private StateMachine _stateMachine = null!;
    private readonly ISemiCommunication? _communication;
    private static string? _jsonScript;
    
    /// <summary>
    /// Load the E30 GEM state machine JSON script
    /// </summary>
    static E30GemController()
    {
        // Load embedded JSON resource or from file
        var assembly = typeof(E30GemController).Assembly;
        var resourceName = "SemiStandard.E30GemStates.json";
        
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
            var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "E30GemStates.json");
            if (File.Exists(jsonPath))
            {
                _jsonScript = File.ReadAllText(jsonPath);
            }
            else
            {
                jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "SemiStandard", "E30GemStates.json");
                if (File.Exists(jsonPath))
                {
                    _jsonScript = File.ReadAllText(jsonPath);
                }
            }
        }
    }
    
    public E30GemController(string equipmentId, ISemiCommunication? communication = null)
    {
        _communication = communication;
        InitializeStateMachine(equipmentId);
    }
    
    /// <summary>
    /// Initialize the GEM state machine
    /// </summary>
    private void InitializeStateMachine(string equipmentId)
    {
        if (string.IsNullOrEmpty(_jsonScript))
        {
            throw new InvalidOperationException("E30GemStates.json file not found.");
        }
        
        // Update the id in the JSON to be unique
        var jsonScript = _jsonScript.Replace("\"id\": \"E30GemStateMachine\"", 
                                      $"\"id\": \"gem_{equipmentId}\"");
        
        // Create action map
        var actionMap = new ActionMap();
        
        // Create state machine from JSON script
        _stateMachine = StateMachine.CreateFromScript(jsonScript, actionMap);
        _stateMachine.Start();
    }
    
    /// <summary>
    /// Enable communication
    /// </summary>
    public void Enable(bool immediate = false)
    {
        _stateMachine.Send(immediate ? "ENABLE_IMMEDIATE" : "ENABLE");
    }
    
    /// <summary>
    /// Disable communication
    /// </summary>
    public void Disable()
    {
        _stateMachine.Send("DISABLE");
    }
    
    /// <summary>
    /// Select equipment
    /// </summary>
    public void Select()
    {
        _stateMachine.Send("SELECT");
    }
    
    /// <summary>
    /// Deselect equipment
    /// </summary>
    public void Deselect()
    {
        _stateMachine.Send("DESELECT");
    }
    
    /// <summary>
    /// Go online
    /// </summary>
    public void GoOnline(bool remote = true)
    {
        _stateMachine.Send(remote ? "ONLINE_REMOTE" : "ONLINE_LOCAL");
    }
    
    /// <summary>
    /// Go offline
    /// </summary>
    public void GoOffline()
    {
        _stateMachine.Send("OFFLINE");
    }
    
    /// <summary>
    /// Handle S1F13 communication request
    /// </summary>
    public void HandleCommunicationRequest()
    {
        _stateMachine.Send("RECEIVE_S1F13");
        // Send S1F14 response
        _stateMachine.Send("SEND_S1F14");
    }
    
    /// <summary>
    /// Get current state
    /// </summary>
    public string GetCurrentState()
    {
        return _stateMachine.GetSourceSubStateCollection(null).ToCsvString(_stateMachine, true);
    }
}