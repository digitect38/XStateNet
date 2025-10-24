using System.Text.Json.Serialization;
using XStateNet2.Core.Converters;

namespace XStateNet2.Core.Engine;

/// <summary>
/// XState node - represents a single state in the state machine
/// </summary>
public class XStateNode
{
    [JsonPropertyName("on")]
    [JsonConverter(typeof(TransitionDictionaryConverter))]
    public Dictionary<string, List<XStateTransition>>? On { get; set; }

    [JsonPropertyName("entry")]
    public List<object>? Entry { get; set; }

    [JsonPropertyName("exit")]
    public List<object>? Exit { get; set; }

    [JsonPropertyName("states")]
    public Dictionary<string, XStateNode>? States { get; set; }

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
    public Dictionary<int, XStateTransition>? After { get; set; }

    [JsonPropertyName("always")]
    [JsonConverter(typeof(AlwaysTransitionConverter))]
    public List<XStateTransition>? Always { get; set; }

    [JsonPropertyName("onDone")]
    public XStateTransition? OnDone { get; set; }
}
