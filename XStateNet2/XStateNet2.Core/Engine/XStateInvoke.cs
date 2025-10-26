using System.Text.Json.Serialization;
using XStateNet2.Core.Converters;

namespace XStateNet2.Core.Engine;

/// <summary>
/// XState invoke - represents a service invocation
/// </summary>
public class XStateInvoke
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("src")]
    public string Src { get; set; } = string.Empty;

    [JsonPropertyName("onDone")]
    [JsonConverter(typeof(StringOrTransitionConverter))]
    public XStateTransition? OnDone { get; set; }

    [JsonPropertyName("onError")]
    [JsonConverter(typeof(StringOrTransitionConverter))]
    public XStateTransition? OnError { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}
