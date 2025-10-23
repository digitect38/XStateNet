using System.Text.Json;
using System.Text.Json.Serialization;
using XStateNet2.Core.Engine;

namespace XStateNet2.Core.Converters;

/// <summary>
/// Converter for transitions that can be either a string (simple target), an object (full transition), or an array of transitions
/// Handles both formats:
/// - "on": { "EVENT": "targetState" }
/// - "on": { "EVENT": { "target": "targetState", "cond": "guardName" } }
/// - "on": { "EVENT": [{ "target": "done", "cond": "guard1" }, { "target": "other" }] }
/// </summary>
public class TransitionDictionaryConverter : JsonConverter<Dictionary<string, List<XStateTransition>>>
{
    public override Dictionary<string, List<XStateTransition>>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            return null;

        var result = new Dictionary<string, List<XStateTransition>>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return result;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected property name");

            var eventName = reader.GetString() ?? string.Empty;
            reader.Read();

            List<XStateTransition> transitions;

            if (reader.TokenType == JsonTokenType.String)
            {
                // Simple format: "EVENT": "targetState"
                transitions = new List<XStateTransition>
                {
                    new XStateTransition { Target = reader.GetString() }
                };
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Complex format: "EVENT": { "target": "...", "cond": "..." }
                var transition = JsonSerializer.Deserialize<XStateTransition>(ref reader, options)
                    ?? throw new JsonException("Failed to deserialize transition");
                transitions = new List<XStateTransition> { transition };
            }
            else if (reader.TokenType == JsonTokenType.StartArray)
            {
                // Array format for multiple transitions: "EVENT": [{ "target": "...", "cond": "..." }, ...]
                transitions = new List<XStateTransition>();

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        var transition = JsonSerializer.Deserialize<XStateTransition>(ref reader, options)
                            ?? throw new JsonException("Failed to deserialize transition in array");
                        transitions.Add(transition);
                    }
                    else if (reader.TokenType == JsonTokenType.String)
                    {
                        // Allow simple string format in array too
                        transitions.Add(new XStateTransition { Target = reader.GetString() });
                    }
                }
            }
            else
            {
                throw new JsonException($"Unexpected token type for transition: {reader.TokenType}");
            }

            result[eventName] = transitions;
        }

        throw new JsonException("Unexpected end of JSON");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, List<XStateTransition>> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (eventName, transitions) in value)
        {
            writer.WritePropertyName(eventName);

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
