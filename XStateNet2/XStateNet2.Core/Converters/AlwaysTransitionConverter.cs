using System.Text.Json;
using System.Text.Json.Serialization;
using XStateNet2.Core.Engine;

namespace XStateNet2.Core.Converters;

/// <summary>
/// Converter for always (eventless) transitions
/// Handles formats:
/// - "always": { "target": "nextState" }
/// - "always": [{ "target": "state1", "cond": "guard1" }, { "target": "state2" }]
/// - "always": "nextState"
/// </summary>
public class AlwaysTransitionConverter : JsonConverter<List<XStateTransition>>
{
    public override List<XStateTransition>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new List<XStateTransition>();

        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            // Simple format: "always": "targetState"
            var target = reader.GetString();
            if (!string.IsNullOrEmpty(target))
            {
                result.Add(new XStateTransition { Target = target });
            }
            return result;
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Single transition object: "always": { "target": "..." }
            var transition = JsonSerializer.Deserialize<XStateTransition>(ref reader, options)
                ?? throw new JsonException("Failed to deserialize always transition");
            result.Add(transition);
            return result;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            // Array of transitions: "always": [{ ... }, { ... }]
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    return result;

                if (reader.TokenType == JsonTokenType.String)
                {
                    // String in array: ["targetState"]
                    var target = reader.GetString();
                    if (!string.IsNullOrEmpty(target))
                    {
                        result.Add(new XStateTransition { Target = target });
                    }
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    // Object in array: [{ "target": "...", "cond": "..." }]
                    var transition = JsonSerializer.Deserialize<XStateTransition>(ref reader, options)
                        ?? throw new JsonException("Failed to deserialize always transition");
                    result.Add(transition);
                }
            }

            throw new JsonException("Unexpected end of JSON array");
        }

        throw new JsonException($"Unexpected token type for always transition: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, List<XStateTransition> value, JsonSerializerOptions options)
    {
        if (value == null || value.Count == 0)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.Count == 1)
        {
            // Single transition - write as object or string
            var transition = value[0];
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
            foreach (var transition in value)
            {
                JsonSerializer.Serialize(writer, transition, options);
            }
            writer.WriteEndArray();
        }
    }
}
