using System.Text.Json.Serialization;

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
    public XStateTransition? OnDone { get; set; }

    [JsonPropertyName("onError")]
    public XStateTransition? OnError { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}
