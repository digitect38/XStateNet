using System;
using System.IO;
using System.Reflection;

namespace XStateNet.Semi;

/// <summary>
/// E84 Handoff controller for automated material handling using XStateNet
/// </summary>
public class E84HandoffController
{
    private StateMachine _stateMachine = null!;
    private static string? _jsonScript;
    
    // E84 signal states
    public bool LoadRequest { get; private set; }
    public bool UnloadRequest { get; private set; }
    public bool Ready { get; private set; }
    public bool HoAvailable { get; private set; }
    public bool EsInterlock { get; private set; }
    
    /// <summary>
    /// Load the E84 Handoff state machine JSON script
    /// </summary>
    static E84HandoffController()
    {
        // Load embedded JSON resource or from file
        var assembly = typeof(E84HandoffController).Assembly;
        var resourceName = "SemiStandard.E84HandoffStates.json";
        
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
            var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "E84HandoffStates.json");
            if (File.Exists(jsonPath))
            {
                _jsonScript = File.ReadAllText(jsonPath);
            }
            else
            {
                jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "SemiStandard", "E84HandoffStates.json");
                if (File.Exists(jsonPath))
                {
                    _jsonScript = File.ReadAllText(jsonPath);
                }
            }
        }
    }
    
    public E84HandoffController(string portId)
    {
        InitializeStateMachine(portId);
    }
    
    /// <summary>
    /// Initialize the E84 handoff state machine
    /// </summary>
    private void InitializeStateMachine(string portId)
    {
        if (string.IsNullOrEmpty(_jsonScript))
        {
            throw new InvalidOperationException("E84HandoffStates.json file not found.");
        }
        
        // Update the id in the JSON to be unique
        var jsonScript = _jsonScript.Replace("\"id\": \"E84HandoffStateMachine\"", $"\"id\": \"e84_{portId}\"");
        
        // Create action map for E84 signals
        var actionMap = new ActionMap();
        
        actionMap["setLoadRequest"] = new List<NamedAction>
        {
            new NamedAction("setLoadRequest", (sm) => 
            {
                LoadRequest = true;
                Logger.Info($"E84 Load Request ON for port {portId}");
            })
        };
        
        actionMap["clearLoadRequest"] = new List<NamedAction>
        {
            new NamedAction("clearLoadRequest", (sm) => 
            {
                LoadRequest = false;
                Logger.Info($"E84 Load Request OFF for port {portId}");
            })
        };
        
        actionMap["setUnloadRequest"] = new List<NamedAction>
        {
            new NamedAction("setUnloadRequest", (sm) => 
            {
                UnloadRequest = true;
                Logger.Info($"E84 Unload Request ON for port {portId}");
            })
        };
        
        actionMap["clearUnloadRequest"] = new List<NamedAction>
        {
            new NamedAction("clearUnloadRequest", (sm) => 
            {
                UnloadRequest = false;
                Logger.Info($"E84 Unload Request OFF for port {portId}");
            })
        };
        
        actionMap["setReady"] = new List<NamedAction>
        {
            new NamedAction("setReady", (sm) => 
            {
                Ready = true;
                HoAvailable = true;
                Logger.Info($"E84 Ready ON for port {portId}");
            })
        };
        
        actionMap["clearReady"] = new List<NamedAction>
        {
            new NamedAction("clearReady", (sm) => 
            {
                Ready = false;
                HoAvailable = false;
                Logger.Info($"E84 Ready OFF for port {portId}");
            })
        };
        
        actionMap["setAlarm"] = new List<NamedAction>
        {
            new NamedAction("setAlarm", (sm) => 
            {
                EsInterlock = true;
                Logger.Error($"E84 Transfer blocked for port {portId}");
            })
        };
        
        actionMap["clearAlarm"] = new List<NamedAction>
        {
            new NamedAction("clearAlarm", (sm) => 
            {
                EsInterlock = false;
                Logger.Info($"E84 Transfer alarm cleared for port {portId}");
            })
        };
        
        // Create state machine from JSON script
        _stateMachine = StateMachine.CreateFromScript(jsonScript, actionMap);
        _stateMachine.Start();
    }
    
    /// <summary>
    /// Signal carrier stage 0 sensor
    /// </summary>
    public void SetCS0(bool on)
    {
        _stateMachine.Send(on ? "CS_0_ON" : "CS_0_OFF");
    }
    
    /// <summary>
    /// Signal valid carrier
    /// </summary>
    public void SetValid(bool on)
    {
        _stateMachine.Send(on ? "VALID_ON" : "VALID_OFF");
    }
    
    /// <summary>
    /// Signal transfer request from AGV/OHT
    /// </summary>
    public void SetTransferRequest(bool on)
    {
        _stateMachine.Send(on ? "TR_REQ_ON" : "TR_REQ_OFF");
    }
    
    /// <summary>
    /// Signal busy status
    /// </summary>
    public void SetBusy(bool on)
    {
        _stateMachine.Send(on ? "BUSY_ON" : "BUSY_OFF");
    }
    
    /// <summary>
    /// Signal transfer complete
    /// </summary>
    public void SetComplete(bool on)
    {
        _stateMachine.Send(on ? "COMPT_ON" : "COMPT_OFF");
    }
    
    /// <summary>
    /// Reset handoff controller
    /// </summary>
    public void Reset()
    {
        // Clear all signals first
        LoadRequest = false;
        UnloadRequest = false;
        Ready = false;
        HoAvailable = false;
        EsInterlock = false;
        
        // Send CS_0_OFF to return to idle from notReady state
        _stateMachine.Send("CS_0_OFF");
        _stateMachine.Send("RESET");
    }
    
    /// <summary>
    /// Get current state
    /// </summary>
    public string GetCurrentState()
    {
        return _stateMachine.GetSourceSubStateCollection(null).ToCsvString(_stateMachine, true);
    }
}