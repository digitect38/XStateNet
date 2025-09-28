using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

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
        var resourceName = "SemiStandard.XStates.E30GemStates.json";
        
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
                jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "SemiStandard.XStates", "E30GemStates.json");
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
        _stateMachine = StateMachineFactory.CreateFromScript(jsonScript, threadSafe:false, guidIsolate:true, actionMap);
        _stateMachine.Start();
    }
    
    /// <summary>
    /// Enable communication
    /// </summary>
    public async Task EnableAsync(bool immediate = false)
    {
        await _stateMachine.SendAsync(immediate ? "ENABLE_IMMEDIATE" : "ENABLE");
    }
    
    /// <summary>
    /// Disable communication
    /// </summary>
    public async Task DisableAsync()
    {
        await _stateMachine.SendAsync("DISABLE");
    }
    
    /// <summary>
    /// Select equipment
    /// </summary>
    public async Task SelectAsync()
    {
        await _stateMachine.SendAsync("SELECT");
    }
    
    /// <summary>
    /// Deselect equipment
    /// </summary>
    public async Task DeselectAsync()
    {
        await _stateMachine.SendAsync("DESELECT");
    }
    
    /// <summary>
    /// Go online
    /// </summary>
    public async Task GoOnlineAsync(bool remote = true)
    {
        await _stateMachine.SendAsync(remote ? "ONLINE_REMOTE" : "ONLINE_LOCAL");
    }
    
    /// <summary>
    /// Go offline
    /// </summary>
    public async Task GoOfflineAsync()
    {
        await _stateMachine.SendAsync("OFFLINE");
    }
    
    /// <summary>
    /// Handle S1F13 communication request
    /// </summary>
    public async Task HandleCommunicationRequestAsync()
    {
        await _stateMachine.SendAsync("RECEIVE_S1F13");
        // Send S1F14 response
        await _stateMachine.SendAsync("SEND_S1F14");
    }
    
    /// <summary>
    /// Get current state
    /// </summary>
    public string GetCurrentState()
    {
        return _stateMachine.GetSourceSubStateCollection(null).ToCsvString(_stateMachine, true);
    }
}