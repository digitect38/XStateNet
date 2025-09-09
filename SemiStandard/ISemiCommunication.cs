using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XStateNet.Semi;

/// <summary>
/// Interface for SECS/GEM communication in SEMI equipment
/// </summary>
public interface ISemiCommunication
{
    /// <summary>
    /// Send a SECS message
    /// </summary>
    Task<LegacySecsMessage?> SendMessage(int stream, int function, object data);
    
    /// <summary>
    /// Handle incoming SECS message
    /// </summary>
    void RegisterMessageHandler(int stream, int function, Func<LegacySecsMessage, Task<LegacySecsMessage?>> handler);
    
    /// <summary>
    /// Report collection event
    /// </summary>
    Task ReportEvent(int ceid, Dictionary<int, object>? variables = null);
    
    /// <summary>
    /// Set alarm
    /// </summary>
    Task SetAlarm(int alid, bool set = true);
    
    /// <summary>
    /// Update status variable
    /// </summary>
    void UpdateStatusVariable(int svid, object value);
    
    /// <summary>
    /// Get status variable value
    /// </summary>
    object? GetStatusVariable(int svid);
}

/// <summary>
/// Legacy SECS message representation (deprecated - use XStateNet.Semi.Secs.SecsMessage instead)
/// </summary>
public class LegacySecsMessage
{
    public int Stream { get; set; }
    public int Function { get; set; }
    public bool ReplyExpected { get; set; }
    public object? Data { get; set; }
    public int TransactionId { get; set; }
    
    public LegacySecsMessage(int stream, int function, bool replyExpected = false)
    {
        Stream = stream;
        Function = function;
        ReplyExpected = replyExpected;
    }
}

/// <summary>
/// Collection event data
/// </summary>
public class CollectionEvent
{
    public int Id { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; }
    public List<int> LinkedReports { get; set; }
    
    public CollectionEvent(int id, string name)
    {
        Id = id;
        Name = name;
        Enabled = true;
        LinkedReports = new List<int>();
    }
}

/// <summary>
/// Alarm definition
/// </summary>
public class AlarmDefinition
{
    public int Id { get; set; }
    public string Text { get; set; }
    public AlarmSeverity Severity { get; set; }
    public bool Set { get; set; }
    public DateTime? SetTime { get; set; }
    public DateTime? ClearTime { get; set; }
    
    public AlarmDefinition(int id, string text, AlarmSeverity severity = AlarmSeverity.Warning)
    {
        Id = id;
        Text = text;
        Severity = severity;
        Set = false;
    }
}

/// <summary>
/// Alarm severity levels
/// </summary>
public enum AlarmSeverity
{
    Information = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}