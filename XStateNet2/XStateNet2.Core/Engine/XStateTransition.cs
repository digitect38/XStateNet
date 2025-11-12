using System.Text.Json.Serialization;
using XStateNet2.Core.Converters;

namespace XStateNet2.Core.Engine;

/// <summary>
/// XState transition - represents a transition from one state to another
/// </summary>
public class XStateTransition
{
    /// <summary>
    /// Target state(s) for this transition.
    /// Can be a single target (string) or multiple targets (array of strings).
    /// Multiple targets allow transitioning multiple parallel regions simultaneously.
    /// </summary>
    [JsonPropertyName("target")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public List<string>? Targets { get; set; }

    /// <summary>
    /// Legacy property for backward compatibility.
    /// Returns the first target if multiple targets exist, or null.
    /// </summary>
    [JsonIgnore]
    public string? Target
    {
        get => Targets?.FirstOrDefault();
        set => Targets = value != null ? new List<string> { value } : null;
    }

    /// <summary>
    /// Guard condition name.
    /// XState v4 uses "cond", v5 uses "guard" - we support both.
    /// </summary>
    [JsonPropertyName("cond")]
    public string? Cond { get; set; }

    /// <summary>
    /// Guard condition name (XState v5 naming).
    /// This is an alias for Cond to support both v4 and v5 JSON formats.
    /// </summary>
    [JsonPropertyName("guard")]
    public string? Guard
    {
        get => Cond;
        set => Cond = value;
    }

    [JsonPropertyName("in")]
    public string? In { get; set; }

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
    public IReadOnlyDictionary<string, object>? Assignment { get; set; }

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

    /// <summary>
    /// For spawn action: source machine configuration
    /// Can be a string reference to registered machine or inline configuration
    /// </summary>
    [JsonPropertyName("src")]
    public object? Src { get; set; }

    /// <summary>
    /// For spawn action: ID to assign to spawned actor
    /// Stored in context if specified
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}
