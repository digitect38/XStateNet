using System.Text.Json;
using System.Text.Json.Serialization;
using XStateNet2.Core.Engine;

namespace XStateNet2.Core.Converters;

/// <summary>
/// Converter for delayed transitions (after)
/// Handles format: "after": { "1000": "targetState", "2000": { "target": "..." } }
/// </summary>
public class AfterTransitionConverter : JsonConverter<Dictionary<int, XStateTransition>>
{
    public override Dictionary<int, XStateTransition>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            return null;

        var result = new Dictionary<int, XStateTransition>();

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

            XStateTransition transition;

            if (reader.TokenType == JsonTokenType.String)
            {
                // Simple format: "1000": "targetState"
                transition = new XStateTransition
                {
                    Target = reader.GetString()
                };
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Complex format: "1000": { "target": "...", "cond": "..." }
                transition = JsonSerializer.Deserialize<XStateTransition>(ref reader, options)
                    ?? throw new JsonException("Failed to deserialize transition");
            }
            else
            {
                throw new JsonException($"Unexpected token type for after transition: {reader.TokenType}");
            }

            result[delay] = transition;
        }

        throw new JsonException("Unexpected end of JSON");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<int, XStateTransition> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (delay, transition) in value)
        {
            writer.WritePropertyName(delay.ToString());

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

        writer.WriteEndObject();
    }
}
