using System.Text.Json.Serialization;
using XStateNet2.Core.Converters;

namespace XStateNet2.Core.Engine;

/// <summary>
/// XState machine script root - represents a complete state machine definition
/// </summary>
public class XStateMachineScript
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("initial")]
    public string Initial { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("context")]
    public Dictionary<string, object>? Context { get; set; }

    [JsonPropertyName("on")]
    [JsonConverter(typeof(TransitionDictionaryConverter))]
    public Dictionary<string, List<XStateTransition>>? On { get; set; }

    [JsonPropertyName("states")]
    public Dictionary<string, XStateNode> States { get; set; } = new();
}
