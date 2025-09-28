using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

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
        var resourceName = "SemiStandard.XStateScripts.E84HandoffStates.json";
        
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
                jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "XStateScripts.SemiStandard", "E84HandoffStates.json");
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
        
        // Create state machine using the new builder pattern with automatic isolation
        _stateMachine = new StateMachineBuilder()
            .WithJsonScript(_jsonScript)
            .WithBaseId("E84HandoffStateMachine")
            .WithIsolation(StateMachineBuilder.IsolationMode.Guid)
            .WithActionMap(actionMap)
            .WithContext("portId", portId)
            .WithAutoStart(true)
            .Build($"E84Handoff_{portId}");
    }
    
    /// <summary>
    /// Signal carrier stage 0 sensor
    /// </summary>
    public async Task SetCS0Async(bool on)
    {
        await _stateMachine.SendAsync(on ? "CS_0_ON" : "CS_0_OFF");
    }

    public void SetCS0(bool on)
    {
        SetCS0Async(on).GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Signal valid carrier
    /// </summary>
    public async Task SetValidAsync(bool on)
    {
        await _stateMachine.SendAsync(on ? "VALID_ON" : "VALID_OFF");
    }

    public void SetValid(bool on)
    {
        SetValidAsync(on).GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Signal transfer request from AGV/OHT
    /// </summary>
    public async Task SetTransferRequestAsync(bool on)
    {
        await _stateMachine.SendAsync(on ? "TR_REQ_ON" : "TR_REQ_OFF");
    }

    public void SetTransferRequest(bool on)
    {
        SetTransferRequestAsync(on).GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Signal busy status
    /// </summary>
    public async Task SetBusyAsync(bool on)
    {
        await _stateMachine.SendAsync(on ? "BUSY_ON" : "BUSY_OFF");
    }

    public void SetBusy(bool on)
    {
        SetBusyAsync(on).GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Signal transfer complete
    /// </summary>
    public async Task SetCompleteAsync(bool on)
    {
        await _stateMachine.SendAsync(on ? "COMPT_ON" : "COMPT_OFF");
    }

    public void SetComplete(bool on)
    {
        SetCompleteAsync(on).GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Reset handoff controller
    /// </summary>
    public async Task ResetAsync()
    {
        // Clear all signals first
        LoadRequest = false;
        UnloadRequest = false;
        Ready = false;
        HoAvailable = false;
        EsInterlock = false;

        // Send CS_0_OFF to return to idle from notReady state
        await _stateMachine.SendAsync("CS_0_OFF");
        await _stateMachine.SendAsync("RESET");
    }

    public void Reset()
    {
        ResetAsync().GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Get current state
    /// </summary>
    public string GetCurrentState()
    {
        return _stateMachine.GetSourceSubStateCollection(null).ToCsvString(_stateMachine, true);
    }
}