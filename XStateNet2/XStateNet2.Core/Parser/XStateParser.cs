using System.Text.Json;
using System.Text.Json.Serialization;
using XStateNet2.Core.Engine;

namespace XStateNet2.Core.Parser;

/// <summary>
/// Parser for XState JSON definitions
/// </summary>
public class XStateParser
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public XStateMachineScript Parse(string json)
    {
        try
        {
            var script = JsonSerializer.Deserialize<XStateMachineScript>(json, _options);
            if (script == null)
                throw new XStateParseException("Failed to parse XState JSON");

            Validate(script);
            return script;
        }
        catch (JsonException ex)
        {
            throw new XStateParseException($"Invalid XState JSON: {ex.Message}", ex);
        }
    }

    public XStateMachineScript ParseFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var json = File.ReadAllText(filePath);
        return Parse(json);
    }

    private void Validate(XStateMachineScript script)
    {
        if (string.IsNullOrEmpty(script.Id))
            throw new XStateParseException("Machine ID is required");

        // Parallel states don't require an initial state (all regions start simultaneously)
        bool isParallel = script.Type?.Equals("parallel", StringComparison.OrdinalIgnoreCase) == true;

        if (!isParallel && string.IsNullOrEmpty(script.Initial))
            throw new XStateParseException("Initial state is required");

        if (script.States == null || script.States.Count == 0)
            throw new XStateParseException("At least one state is required");

        if (!isParallel && !string.IsNullOrEmpty(script.Initial) && !script.States.ContainsKey(script.Initial))
            throw new XStateParseException($"Initial state '{script.Initial}' not found in states");
    }
}

public class XStateParseException : Exception
{
    public XStateParseException(string message) : base(message) { }
    public XStateParseException(string message, Exception innerException) : base(message, innerException) { }
}
