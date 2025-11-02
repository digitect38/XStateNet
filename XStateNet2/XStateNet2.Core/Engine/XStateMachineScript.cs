using System.Collections.Frozen;
using System.Text.Json.Serialization;
using XStateNet2.Core.Converters;

namespace XStateNet2.Core.Engine;

/// <summary>
/// XState machine script root - represents a complete state machine definition
/// Optimized with FrozenDictionary for read-heavy workloads after parsing
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
    public IReadOnlyDictionary<string, object>? Context { get; set; }

    [JsonPropertyName("on")]
    [JsonConverter(typeof(TransitionDictionaryConverter))]
    public IReadOnlyDictionary<string, List<XStateTransition>>? On { get; set; }

    [JsonPropertyName("states")]
    public IReadOnlyDictionary<string, XStateNode> States { get; set; } = new Dictionary<string, XStateNode>();
}
