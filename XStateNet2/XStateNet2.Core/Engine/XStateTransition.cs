using System.Text.Json.Serialization;

namespace XStateNet2.Core.Engine;

/// <summary>
/// XState transition - represents a transition from one state to another
/// </summary>
public class XStateTransition
{
    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("cond")]
    public string? Cond { get; set; }

    [JsonPropertyName("actions")]
    public List<object>? Actions { get; set; }

    [JsonPropertyName("internal")]
    public bool Internal { get; set; } = false;
}

/// <summary>
/// Inline action definition for assign, send, raise, etc.
/// </summary>
public class ActionDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// For assign action: key-value pairs to update context
    /// </summary>
    [JsonPropertyName("assignment")]
    public Dictionary<string, object>? Assignment { get; set; }

    /// <summary>
    /// For send/raise action: event type to send
    /// </summary>
    [JsonPropertyName("event")]
    public string? Event { get; set; }

    /// <summary>
    /// For send action: target actor ID
    /// </summary>
    [JsonPropertyName("to")]
    public string? To { get; set; }

    /// <summary>
    /// For send action: event data
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; set; }

    /// <summary>
    /// For send action: delay in milliseconds
    /// </summary>
    [JsonPropertyName("delay")]
    public int? Delay { get; set; }
}
