using System.Collections.Frozen;
using System.Text.Json.Serialization;
using XStateNet2.Core.Converters;

namespace XStateNet2.Core.Engine;

/// <summary>
/// XState node - represents a single state in the state machine
/// Optimized with FrozenDictionary for read-heavy workloads after parsing
/// </summary>
public class XStateNode
{
    [JsonPropertyName("on")]
    [JsonConverter(typeof(TransitionDictionaryConverter))]
    public IReadOnlyDictionary<string, List<XStateTransition>>? On { get; set; }

    [JsonPropertyName("entry")]
    public List<object>? Entry { get; set; }

    [JsonPropertyName("exit")]
    public List<object>? Exit { get; set; }

    [JsonPropertyName("states")]
    public IReadOnlyDictionary<string, XStateNode>? States { get; set; }

    [JsonPropertyName("initial")]
    public string? Initial { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("history")]
    public string? History { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("invoke")]
    public XStateInvoke? Invoke { get; set; }

    [JsonPropertyName("after")]
    [JsonConverter(typeof(AfterTransitionConverter))]
    public IReadOnlyDictionary<int, XStateTransition>? After { get; set; }

    [JsonPropertyName("always")]
    [JsonConverter(typeof(AlwaysTransitionConverter))]
    public List<XStateTransition>? Always { get; set; }

    [JsonPropertyName("onDone")]
    [JsonConverter(typeof(StringOrTransitionConverter))]
    public XStateTransition? OnDone { get; set; }

    /// <summary>
    /// XState V5: Metadata about this state node
    /// Useful for UI components, analytics, and documentation
    /// </summary>
    [JsonPropertyName("meta")]
    public IReadOnlyDictionary<string, object>? Meta { get; set; }

    /// <summary>
    /// XState V5: Human-readable description of this state
    /// Supports markdown in Stately Studio
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// XState V5: Tags to categorize this state node
    /// Useful for grouping and filtering states
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>
    /// XState V5: Output data when this final state is reached
    /// Only relevant for final states (type: "final")
    /// </summary>
    [JsonPropertyName("output")]
    public object? Output { get; set; }
}
