using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XStateNet.Semi;

/// <summary>
/// Manages SEMI standard variables (SVID, CEID, ALID, ECID)
/// </summary>
public class SemiVariableCollection
{
    private readonly ConcurrentDictionary<int, StatusVariable> _statusVariables = new();
    private readonly ConcurrentDictionary<int, CollectionEvent> _collectionEvents = new();
    private readonly ConcurrentDictionary<int, AlarmDefinition> _alarms = new();
    private readonly ConcurrentDictionary<int, EquipmentConstant> _equipmentConstants = new();
    private readonly object _updateLock = new();
    
    /// <summary>
    /// Register a status variable
    /// </summary>
    public void RegisterStatusVariable(int svid, string name, Type dataType, object? initialValue = null)
    {
        var sv = new StatusVariable(svid, name, dataType)
        {
            Value = initialValue
        };
        _statusVariables[svid] = sv;
    }
    
    /// <summary>
    /// Update status variable value
    /// </summary>
    public void UpdateStatusVariable(int svid, object value)
    {
        if (_statusVariables.TryGetValue(svid, out var sv))
        {
            lock (_updateLock)
            {
                sv.Value = value;
                sv.LastUpdate = DateTime.UtcNow;
            }
        }
    }
    
    /// <summary>
    /// Get status variable value
    /// </summary>
    public object? GetStatusVariable(int svid)
    {
        return _statusVariables.TryGetValue(svid, out var sv) ? sv.Value : null;
    }
    
    /// <summary>
    /// Register collection event
    /// </summary>
    public void RegisterCollectionEvent(int ceid, string name, params int[] linkedReports)
    {
        var ce = new CollectionEvent(ceid, name);
        ce.LinkedReports.AddRange(linkedReports);
        _collectionEvents[ceid] = ce;
    }
    
    /// <summary>
    /// Register alarm
    /// </summary>
    public void RegisterAlarm(int alid, string text, AlarmSeverity severity = AlarmSeverity.Warning)
    {
        _alarms[alid] = new AlarmDefinition(alid, text, severity);
    }
    
    /// <summary>
    /// Set or clear an alarm
    /// </summary>
    public bool SetAlarm(int alid, bool set = true)
    {
        if (_alarms.TryGetValue(alid, out var alarm))
        {
            lock (_updateLock)
            {
                alarm.Set = set;
                if (set)
                {
                    alarm.SetTime = DateTime.UtcNow;
                }
                else
                {
                    alarm.ClearTime = DateTime.UtcNow;
                }
            }
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Get all active alarms
    /// </summary>
    public IEnumerable<AlarmDefinition> GetActiveAlarms()
    {
        return _alarms.Values.Where(a => a.Set);
    }
    
    /// <summary>
    /// Register equipment constant
    /// </summary>
    public void RegisterEquipmentConstant(int ecid, string name, Type dataType, object defaultValue, 
        object? minValue = null, object? maxValue = null)
    {
        var ec = new EquipmentConstant(ecid, name, dataType)
        {
            Value = defaultValue,
            DefaultValue = defaultValue,
            MinValue = minValue,
            MaxValue = maxValue
        };
        _equipmentConstants[ecid] = ec;
    }
    
    /// <summary>
    /// Update equipment constant
    /// </summary>
    public bool UpdateEquipmentConstant(int ecid, object value)
    {
        if (_equipmentConstants.TryGetValue(ecid, out var ec))
        {
            // Validate against min/max if specified
            if (ec.MinValue != null && Comparer<object>.Default.Compare(value, ec.MinValue) < 0)
                return false;
            if (ec.MaxValue != null && Comparer<object>.Default.Compare(value, ec.MaxValue) > 0)
                return false;
                
            lock (_updateLock)
            {
                ec.Value = value;
                ec.LastUpdate = DateTime.UtcNow;
            }
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Initialize standard SEMI variables
    /// </summary>
    public void InitializeStandardVariables()
    {
        // Standard SVIDs
        RegisterStatusVariable(1, "EquipmentStatus", typeof(int), 0); // 0=Init, 1=Idle, 2=Setup, 3=Ready, 4=Executing
        RegisterStatusVariable(2, "PreviousEquipmentStatus", typeof(int), 0);
        RegisterStatusVariable(3, "OperatorCommand", typeof(int), 0); // 1=Start, 2=Stop, 3=Pause, 4=Resume, 5=Abort
        RegisterStatusVariable(4, "ClockValue", typeof(DateTime), DateTime.UtcNow);
        RegisterStatusVariable(5, "ControlState", typeof(int), 0); // 0=Offline, 1=Local, 2=Remote
        RegisterStatusVariable(6, "PreviousControlState", typeof(int), 0);
        RegisterStatusVariable(7, "PPExecName", typeof(string), "");
        RegisterStatusVariable(8, "MaterialStatus", typeof(int), 0); // 0=None, 1=WaitingForMaterial, 2=InProcess, 3=Processed
        
        // Standard CEIDs
        RegisterCollectionEvent(1, "EquipmentOffline");
        RegisterCollectionEvent(2, "EquipmentLocal");
        RegisterCollectionEvent(3, "EquipmentRemote");
        RegisterCollectionEvent(4, "ProcessStarted");
        RegisterCollectionEvent(5, "ProcessCompleted");
        RegisterCollectionEvent(6, "ProcessAborted");
        RegisterCollectionEvent(7, "MaterialReceived");
        RegisterCollectionEvent(8, "MaterialRemoved");
        RegisterCollectionEvent(9, "MaterialProcessed");
        RegisterCollectionEvent(10, "MaterialLost");
        
        // Standard ALIDs
        RegisterAlarm(1000, "Emergency Stop Activated", AlarmSeverity.Critical);
        RegisterAlarm(1001, "Safety Door Open", AlarmSeverity.Error);
        RegisterAlarm(2000, "Communication Timeout", AlarmSeverity.Warning);
        RegisterAlarm(2001, "Process Recipe Not Found", AlarmSeverity.Error);
        RegisterAlarm(3000, "Temperature Out of Range", AlarmSeverity.Warning);
        RegisterAlarm(3001, "Pressure Out of Range", AlarmSeverity.Warning);
        
        // Standard ECIDs
        RegisterEquipmentConstant(1, "CommunicationTimeout", typeof(int), 120, 10, 600); // seconds
        RegisterEquipmentConstant(2, "DefaultRecipeId", typeof(string), "DEFAULT");
        RegisterEquipmentConstant(3, "MaxConcurrentJobs", typeof(int), 1, 1, 10);
        RegisterEquipmentConstant(4, "EnableTraceData", typeof(bool), false);
        RegisterEquipmentConstant(5, "ProcessTimeout", typeof(int), 3600, 60, 86400); // seconds
    }
}

/// <summary>
/// Status variable definition
/// </summary>
public class StatusVariable
{
    public int Id { get; set; }
    public string Name { get; set; }
    public Type DataType { get; set; }
    public object? Value { get; set; }
    public DateTime LastUpdate { get; set; }
    
    public StatusVariable(int id, string name, Type dataType)
    {
        Id = id;
        Name = name;
        DataType = dataType;
        LastUpdate = DateTime.UtcNow;
    }
}

/// <summary>
/// Equipment constant definition
/// </summary>
public class EquipmentConstant
{
    public int Id { get; set; }
    public string Name { get; set; }
    public Type DataType { get; set; }
    public object? Value { get; set; }
    public object? DefaultValue { get; set; }
    public object? MinValue { get; set; }
    public object? MaxValue { get; set; }
    public DateTime LastUpdate { get; set; }
    
    public EquipmentConstant(int id, string name, Type dataType)
    {
        Id = id;
        Name = name;
        DataType = dataType;
        LastUpdate = DateTime.UtcNow;
    }
}