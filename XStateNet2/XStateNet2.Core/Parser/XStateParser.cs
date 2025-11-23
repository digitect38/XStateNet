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

            // Normalize relative paths before freezing
            NormalizeRelativePaths(script);

            // OPTIMIZATION: Freeze dictionaries for 2-3x faster lookups
            ScriptOptimizer.Freeze(script);

            return script;
        }
        catch (JsonException ex)
        {
            throw new XStateParseException($"Invalid XState JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Normalize relative paths in transitions (XState v5 feature)
    /// Converts ".childState" to "parentState.childState"
    /// </summary>
    private void NormalizeRelativePaths(XStateMachineScript script)
    {
        if (script.States == null) return;

        foreach (var (stateName, stateNode) in script.States)
        {
            NormalizeStateRelativePaths(stateNode, stateName);
        }
    }

    private void NormalizeStateRelativePaths(XStateNode state, string currentStatePath)
    {
        // Normalize transitions in the "on" handler
        if (state.On != null)
        {
            foreach (var transitions in state.On.Values)
            {
                foreach (var transition in transitions)
                {
                    if (!string.IsNullOrEmpty(transition.Target))
                    {
                        transition.Target = ResolveTargetPath(transition.Target, currentStatePath);
                    }
                }
            }
        }

        // Normalize transitions in "always" handlers
        if (state.Always != null)
        {
            foreach (var transition in state.Always)
            {
                if (!string.IsNullOrEmpty(transition.Target))
                {
                    transition.Target = ResolveTargetPath(transition.Target, currentStatePath);
                }
            }
        }

        // Recursively normalize child states
        if (state.States != null)
        {
            foreach (var (childName, childState) in state.States)
            {
                var childPath = $"{currentStatePath}.{childName}";
                NormalizeStateRelativePaths(childState, childPath);
            }
        }
    }

    private string ResolveTargetPath(string target, string currentStatePath)
    {
        // Handle multiple targets (comma-separated)
        if (target.Contains(','))
        {
            var targets = target.Split(',', StringSplitOptions.TrimEntries);
            var resolvedTargets = targets.Select(t => ResolveSingleTargetPath(t, currentStatePath));
            return string.Join(", ", resolvedTargets);
        }

        return ResolveSingleTargetPath(target, currentStatePath);
    }

    private string ResolveSingleTargetPath(string target, string currentStatePath)
    {
        // Relative path: .childState -> currentStatePath.childState
        if (target.StartsWith("."))
        {
            var childName = target.Substring(1);
            return $"{currentStatePath}.{childName}";
        }

        // Absolute reference: #machineId.stateName -> stateName
        if (target.StartsWith("#"))
        {
            var dotIndex = target.IndexOf('.');
            if (dotIndex > 0 && dotIndex < target.Length - 1)
            {
                return target.Substring(dotIndex + 1);
            }
            return target.Substring(1);
        }

        // Already absolute or simple state name
        return target;
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
