using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;

namespace XStateNet.Semi;

/// <summary>
/// Integrated SEMI equipment controller using XStateNet
/// </summary>
public class SemiEquipmentController
{
    private StateMachine _stateMachine = null!;
    private readonly SemiVariableCollection _variables;
    private readonly E90SubstrateTracking _substrateTracking;
    private readonly E87CarrierManagement _carrierManagement;
    private ISemiCommunication? _communication;
    private static string? _equipmentJsonScript;
    
    /// <summary>
    /// Load the equipment state machine JSON script
    /// </summary>
    static SemiEquipmentController()
    {
        // Load embedded JSON resource or from file
        var assembly = typeof(SemiEquipmentController).Assembly;
        var resourceName = "SemiStandard.SemiEquipmentStates.json";
        
        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream != null)
            {
                using (var reader = new StreamReader(stream))
                {
                    _equipmentJsonScript = reader.ReadToEnd();
                }
            }
        }
        
        // If not embedded, try to load from file
        if (string.IsNullOrEmpty(_equipmentJsonScript))
        {
            var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SemiEquipmentStates.json");
            if (File.Exists(jsonPath))
            {
                _equipmentJsonScript = File.ReadAllText(jsonPath);
            }
            else
            {
                jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "SemiStandard", "SemiEquipmentStates.json");
                if (File.Exists(jsonPath))
                {
                    _equipmentJsonScript = File.ReadAllText(jsonPath);
                }
            }
        }
    }
    
    /// <summary>
    /// Equipment state machine states
    /// </summary>
    private const string StateOffline = "offline";
    private const string StateLocal = "local";
    private const string StateRemote = "remote";
    private const string StateInit = "init";
    private const string StateIdle = "idle";
    private const string StateSetup = "setup";
    private const string StateReady = "ready";
    private const string StateExecuting = "executing";
    private const string StateCompleting = "completing";
    
    public SemiEquipmentController(string equipmentId)
    {
        _variables = new SemiVariableCollection();
        _substrateTracking = new E90SubstrateTracking();
        _carrierManagement = new E87CarrierManagement();
        
        InitializeStateMachine(equipmentId);
        InitializeSemiVariables();
    }
    
    /// <summary>
    /// Initialize the equipment state machine using JSON script
    /// </summary>
    private void InitializeStateMachine(string equipmentId)
    {
        // Use the loaded JSON script
        if (string.IsNullOrEmpty(_equipmentJsonScript))
        {
            throw new InvalidOperationException("SemiEquipmentStates.json file not found. Please ensure the JSON file is included as an embedded resource or available in the application directory.");
        }
        
        // Update the id in the JSON to be unique for this equipment
        var jsonScript = _equipmentJsonScript.Replace("\"id\": \"SemiEquipmentStateMachine\"", 
                                      $"\"id\": \"{equipmentId}\"");
        
        // Register actions
        var actionMap = new ActionMap();
        actionMap["offlineEntry"] = new List<NamedAction>
        {
            new NamedAction("offlineEntry", (sm) => 
            {
                _variables.UpdateStatusVariable(5, 0); // ControlState = Offline
                ReportEvent(1); // EquipmentOffline
            })
        };
        
        actionMap["offlineExit"] = new List<NamedAction>
        {
            new NamedAction("offlineExit", (sm) => 
            {
                // Action when exiting offline state
            })
        };
        
        actionMap["localEntry"] = new List<NamedAction>
        {
            new NamedAction("localEntry", (sm) => 
            {
                _variables.UpdateStatusVariable(5, 1); // ControlState = Local
                ReportEvent(2); // EquipmentLocal
            })
        };
        
        actionMap["localExit"] = new List<NamedAction>
        {
            new NamedAction("localExit", (sm) => 
            {
                // Action when exiting local state
            })
        };
        
        actionMap["remoteEntry"] = new List<NamedAction>
        {
            new NamedAction("remoteEntry", (sm) => 
            {
                _variables.UpdateStatusVariable(5, 2); // ControlState = Remote
                ReportEvent(3); // EquipmentRemote
            })
        };
        
        actionMap["remoteExit"] = new List<NamedAction>
        {
            new NamedAction("remoteExit", (sm) => 
            {
                // Action when exiting remote state
            })
        };
        
        actionMap["executingEntry"] = new List<NamedAction>
        {
            new NamedAction("executingEntry", (sm) => 
            {
                _variables.UpdateStatusVariable(1, 4); // EquipmentStatus = Executing
                ReportEvent(4); // ProcessStarted
            })
        };
        
        actionMap["executingExit"] = new List<NamedAction>
        {
            new NamedAction("executingExit", (sm) => 
            {
                ReportEvent(5); // ProcessCompleted
            })
        };
        
        // Create state machine from JSON script using XStateNet's intended API
        _stateMachine = StateMachine.CreateFromScript(jsonScript, actionMap);
        _stateMachine.Start();
    }
    
    /// <summary>
    /// Initialize SEMI variables
    /// </summary>
    private void InitializeSemiVariables()
    {
        _variables.InitializeStandardVariables();
        
        // Register load ports
        _carrierManagement.RegisterLoadPort("LP1", "Load Port 1");
        _carrierManagement.RegisterLoadPort("LP2", "Load Port 2");
    }
    
    /// <summary>
    /// Set communication interface
    /// </summary>
    public void SetCommunication(ISemiCommunication communication)
    {
        _communication = communication;
        RegisterMessageHandlers();
    }
    
    /// <summary>
    /// Register SECS message handlers
    /// </summary>
    private void RegisterMessageHandlers()
    {
        if (_communication == null) return;
        
        // S1F13 - Establish communications request
        _communication.RegisterMessageHandler(1, 13, async (msg) =>
        {
            return new SecsMessage(1, 14) // S1F14 - Establish communications acknowledge
            {
                Data = new { COMMACK = 0, MDLN = "XStateNet", SOFTREV = "1.0.0" }
            };
        });
        
        // S2F41 - Host command send
        _communication.RegisterMessageHandler(2, 41, async (msg) =>
        {
            var command = msg.Data as dynamic;
            var result = await ProcessHostCommand(command?.RCMD);
            
            return new SecsMessage(2, 42) // S2F42 - Host command acknowledge
            {
                Data = new { HCACK = result ? 0 : 1 }
            };
        });
    }
    
    /// <summary>
    /// Process host command
    /// </summary>
    private async Task<bool> ProcessHostCommand(string? command)
    {
        if (string.IsNullOrEmpty(command)) return false;
        
        switch (command.ToUpper())
        {
            case "START":
                _stateMachine.Send("start");
                return true;
                
            case "STOP":
            case "ABORT":
                _stateMachine.Send("abort");
                return true;
                
            case "REMOTE":
                _stateMachine.Send("goRemote");
                return true;
                
            case "LOCAL":
                _stateMachine.Send("goLocal");
                return true;
                
            default:
                return false;
        }
    }
    
    /// <summary>
    /// Report collection event
    /// </summary>
    private void ReportEvent(int ceid)
    {
        _communication?.ReportEvent(ceid);
    }
    
    /// <summary>
    /// Process carrier arrival
    /// </summary>
    public async Task<bool> ProcessCarrierArrival(string carrierId, string portId)
    {
        var carrier = _carrierManagement.CarrierArrived(carrierId, portId);
        if (carrier == null) return false;
        
        // Report carrier arrived event
        if (_communication != null)
        {
            await _communication.ReportEvent(100, new Dictionary<int, object> 
            { 
                { 1000, carrierId }, 
                { 1001, portId } 
            });
        }
        
        // Simulate slot mapping
        var slotMap = new Dictionary<int, SlotState>();
        for (int i = 1; i <= 25; i++)
        {
            slotMap[i] = i <= 20 ? SlotState.Present : SlotState.Empty;
        }
        _carrierManagement.UpdateSlotMap(carrierId, slotMap);
        
        // Register substrates for present slots
        for (int i = 1; i <= 20; i++)
        {
            var substrateid = $"{carrierId}_{i:D2}";
            _substrateTracking.RegisterSubstrate(substrateid, carrierId, i);
            _carrierManagement.AssociateSubstrate(carrierId, i, substrateid);
            _substrateTracking.UpdateLocation(substrateid, portId, SubstrateLocationType.Carrier);
        }
        
        return true;
    }
    
    /// <summary>
    /// Process substrate for production
    /// </summary>
    public async Task<bool> ProcessSubstrate(string substrateid, string recipeId)
    {
        // Get substrate
        var substrate = _substrateTracking.GetSubstrate(substrateid);
        if (substrate == null) return false;
        
        // Send event to move to NeedsProcessing state
        substrate.StateMachine.Send("SELECT_FOR_PROCESS");
        
        // Move to process module
        _substrateTracking.UpdateLocation(substrateid, "PM1", SubstrateLocationType.ProcessModule);
        
        // Start processing
        if (!_substrateTracking.StartProcessing(substrateid, recipeId))
            return false;
        
        // Report process started
        if (_communication != null)
        {
            await _communication.ReportEvent(200, new Dictionary<int, object>
            {
                { 2000, substrateid },
                { 2001, recipeId }
            });
        }
        
        // Simulate processing
        await Task.Delay(5000);
        
        // Complete processing
        _substrateTracking.CompleteProcessing(substrateid, true);
        
        // Report process completed
        if (_communication != null)
        {
            await _communication.ReportEvent(201, new Dictionary<int, object>
            {
                { 2000, substrateid }
            });
        }
        
        return true;
    }
    
    /// <summary>
    /// Get current equipment state
    /// </summary>
    public string GetCurrentState()
    {
        return _stateMachine.GetSourceSubStateCollection(null).ToCsvString(_stateMachine, true);
    }
    
    /// <summary>
    /// Send event to state machine
    /// </summary>
    public void SendEvent(string eventName)
    {
        _stateMachine.Send(eventName);
    }
    
    /// <summary>
    /// Get variable collection
    /// </summary>
    public SemiVariableCollection Variables => _variables;
    
    /// <summary>
    /// Get substrate tracking
    /// </summary>
    public E90SubstrateTracking SubstrateTracking => _substrateTracking;
    
    /// <summary>
    /// Get carrier management
    /// </summary>
    public E87CarrierManagement CarrierManagement => _carrierManagement;
}