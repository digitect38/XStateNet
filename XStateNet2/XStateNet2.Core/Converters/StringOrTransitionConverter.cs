using System.Text.Json;
using System.Text.Json.Serialization;
using XStateNet2.Core.Engine;

namespace XStateNet2.Core.Converters;

/// <summary>
/// JSON converter that handles both string and object for onDone/onError properties
/// Supports XState specification where onDone can be either:
/// - A simple string: "onDone": "targetState"
/// - A transition object: "onDone": { "target": "targetState", "actions": [...] }
/// </summary>
public class StringOrTransitionConverter : JsonConverter<XStateTransition?>
{
    public override XStateTransition? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            // Simple string target: "onDone": "targetState"
            var target = reader.GetString();
            if (target == null)
            {
                return null;
            }

            return new XStateTransition
            {
                Targets = new List<string> { target }
            };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Full transition object: "onDone": { "target": "...", "actions": [...], ... }
            return JsonSerializer.Deserialize<XStateTransition>(ref reader, options);
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} for onDone/onError property. Expected string or object.");
    }

    public override void Write(Utf8JsonWriter writer, XStateTransition? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        // If transition has only a target and no other properties, write as simple string
        if (value.Targets?.Count == 1 &&
            value.Cond == null &&
            value.In == null &&
            value.Actions == null &&
            !value.Internal)
        {
            writer.WriteStringValue(value.Targets[0]);
        }
        else
        {
            // Write as full transition object
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
