using System.Text.Json;
using System.Text.Json.Serialization;

namespace XStateNet2.Core.Converters;

/// <summary>
/// JSON converter that handles both string and array of strings for target property
/// Supports XState V5 specification for single and multiple targets
/// </summary>
public class StringOrArrayConverter : JsonConverter<List<string>?>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            // Single target: "target": "stateName"
            var value = reader.GetString();
            return value != null ? new List<string> { value } : null;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            // Multiple targets: "target": ["state1", "state2"]
            var list = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return list;
                }

                if (reader.TokenType == JsonTokenType.String)
                {
                    var value = reader.GetString();
                    if (value != null)
                    {
                        list.Add(value);
                    }
                }
            }

            throw new JsonException("Unexpected end of JSON array");
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} for target property. Expected string or array of strings.");
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.Count == 1)
        {
            // Write as single string for cleaner JSON
            writer.WriteStringValue(value[0]);
        }
        else
        {
            // Write as array
            writer.WriteStartArray();
            foreach (var item in value)
            {
                writer.WriteStringValue(item);
            }
            writer.WriteEndArray();
        }
    }
}
