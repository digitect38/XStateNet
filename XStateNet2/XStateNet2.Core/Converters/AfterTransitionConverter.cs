using System.Text.Json;
using System.Text.Json.Serialization;
using XStateNet2.Core.Engine;

namespace XStateNet2.Core.Converters;

/// <summary>
/// Converter for delayed transitions (after)
/// Handles formats:
/// - "after": { "1000": "targetState" }
/// - "after": { "1000": { "target": "..." } }
/// - "after": { "1000": [{ "target": "...", "guard": "..." }, { "target": "..." }] }
/// </summary>
public class AfterTransitionConverter : JsonConverter<IReadOnlyDictionary<int, List<XStateTransition>>>
{
    public override IReadOnlyDictionary<int, List<XStateTransition>>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            return null;

        var result = new Dictionary<int, List<XStateTransition>>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return result;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected property name");

            var delayStr = reader.GetString() ?? string.Empty;
            if (!int.TryParse(delayStr, out var delay))
                throw new JsonException($"Invalid delay value: {delayStr}");

            reader.Read();

            List<XStateTransition> transitions;

            if (reader.TokenType == JsonTokenType.String)
            {
                // Simple format: "1000": "targetState"
                transitions = new List<XStateTransition>
                {
                    new XStateTransition
                    {
                        Target = reader.GetString()
                    }
                };
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Complex format: "1000": { "target": "...", "cond": "..." }
                var transition = JsonSerializer.Deserialize<XStateTransition>(ref reader, options)
                    ?? throw new JsonException("Failed to deserialize transition");
                transitions = new List<XStateTransition> { transition };
            }
            else if (reader.TokenType == JsonTokenType.StartArray)
            {
                // Array format: "1000": [{ "target": "...", "guard": "..." }, { "target": "..." }]
                transitions = new List<XStateTransition>();

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                        break;

                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        var transition = JsonSerializer.Deserialize<XStateTransition>(ref reader, options)
                            ?? throw new JsonException("Failed to deserialize transition in array");
                        transitions.Add(transition);
                    }
                    else
                    {
                        throw new JsonException($"Unexpected token type in after transition array: {reader.TokenType}");
                    }
                }
            }
            else
            {
                throw new JsonException($"Unexpected token type for after transition: {reader.TokenType}");
            }

            result[delay] = transitions;
        }

        throw new JsonException("Unexpected end of JSON");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<int, List<XStateTransition>> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (delay, transitions) in value)
        {
            writer.WritePropertyName(delay.ToString());

            if (transitions.Count == 1)
            {
                var transition = transitions[0];
                // If transition only has a target, use simple format
                if (!string.IsNullOrEmpty(transition.Target) &&
                    string.IsNullOrEmpty(transition.Cond) &&
                    (transition.Actions == null || transition.Actions.Count == 0) &&
                    !transition.Internal)
                {
                    writer.WriteStringValue(transition.Target);
                }
                else
                {
                    JsonSerializer.Serialize(writer, transition, options);
                }
            }
            else
            {
                // Multiple transitions - write as array
                writer.WriteStartArray();
                foreach (var transition in transitions)
                {
                    JsonSerializer.Serialize(writer, transition, options);
                }
                writer.WriteEndArray();
            }
        }

        writer.WriteEndObject();
    }
}
