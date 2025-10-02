using System;
using System.Text.RegularExpressions;

namespace XStateNet;

/// <summary>
/// Factory for creating thread-safe state machines
/// </summary>
public static class StateMachineFactory
{
    /// <summary>
    /// Replace machine ID references in JSON script
    /// - IDs starting with # are references to existing machines
    /// - IDs without # are definitions of new machines
    /// </summary>
    /// <param name="jsonScript">The JSON script containing machine IDs</param>
    /// <param name="newMachineId">The new machine ID to use</param>
    /// <param name="preserveReferences">If true, references (#id) are preserved; if false, all IDs are replaced</param>
    /// <returns>Modified JSON script with replaced machine IDs</returns>
    public static string ReplaceMachineId(string jsonScript, string newMachineId, bool preserveReferences = true)
    {
        if (string.IsNullOrEmpty(jsonScript))
            return jsonScript;

        // Pattern to match "id": "value" where value may or may not start with #
        var pattern = @"""id""\s*:\s*""([^""]*)""";

        return Regex.Replace(jsonScript, pattern, match =>
        {
            var currentId = match.Groups[1].Value;

            // If it starts with #, it's a reference
            if (currentId.StartsWith("#"))
            {
                if (preserveReferences)
                {
                    // Keep references as-is
                    return match.Value;
                }
                else
                {
                    // Replace reference with new ID (keeping the # prefix)
                    return $@"""id"": ""#{newMachineId}""";
                }
            }
            else
            {
                // It's a definition, replace with new ID
                return $@"""id"": ""{newMachineId}""";
            }
        });
    }

    /// <summary>
    /// Isolates machine ID by extracting it from the script and adding a GUID tail.
    /// Replaces both the machine ID definition and all references to it.
    /// </summary>
    /// <param name="jsonScript">The JSON script containing the machine ID</param>
    /// <returns>Modified JSON script with isolated machine ID (original ID + GUID)</returns>
    public static string MachineIdIsolatedScript(string jsonScript)
    {
        if (string.IsNullOrEmpty(jsonScript))
            return jsonScript;

        // First, extract the machine ID from the script
        // Pattern to match "id": "value" where value doesn't start with #
        var idPattern = @"""id""\s*:\s*""([^#][^""]*)""";
        var match = Regex.Match(jsonScript, idPattern);

        if (!match.Success)
            return jsonScript; // No machine ID found

        var originalId = match.Groups[1].Value;
        var newId = $"{originalId}_{Guid.NewGuid():N}";

        // Replace the machine ID definition (only first occurrence)
        var result = jsonScript;
        var definitionMatch = Regex.Match(result, idPattern);
        if (definitionMatch.Success && definitionMatch.Groups[1].Value == originalId)
        {
            result = result.Substring(0, definitionMatch.Index) +
                     $@"""id"": ""{newId}""" +
                     result.Substring(definitionMatch.Index + definitionMatch.Length);
        }

        // Now replace all references to the original machine ID (those starting with #)
        var referencePattern = $@"""#({Regex.Escape(originalId)})([^""]*?)""";
        result = Regex.Replace(result, referencePattern, $@"""#{newId}$2""");

        // Also handle references in 'in' conditions and other contexts
        // Pattern for references like "in": "#machineId.state"
        var inPattern = $@"#({Regex.Escape(originalId)})(\.)";
        result = Regex.Replace(result, inPattern, $@"#{newId}$2");

        return result;
    }

    /// <summary>
    /// Create a state machine from script with machine ID replacement
    /// </summary>
    [Obsolete("Use ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices with EventBusOrchestrator instead. Direct StateMachine creation bypasses the orchestrator and can lead to deadlocks.")]
    public static StateMachine CreateFromScript(
        StateMachine sm,
        string? jsonScript,
        bool threadSafe = false,
        bool guidIsolate = false,
        ActionMap? actionCallbacks = null,
        GuardMap? guardCallbacks = null,
        ServiceMap? serviceCallbacks = null,
        DelayMap? delayCallbacks = null,
        ActivityMap? activityCallbacks = null)
    {
        if (guidIsolate) { 
            jsonScript = MachineIdIsolatedScript(jsonScript);
        }
        StateMachine.ParseStateMachine(sm, jsonScript, guidIsolate, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks, activityCallbacks);
        sm.EnableThreadSafety = threadSafe;

        return sm;
    }
    /// <summary>
    /// Create a thread-safe state machine from file
    /// </summary>
    public static StateMachine CreateFromFile(
        string jsonFilePath,
        bool threadSafe = false,
        bool guidIsolate = false,
        ActionMap? actionCallbacks = null,
        GuardMap? guardCallbacks = null,
        ServiceMap? serviceCallbacks = null,
        DelayMap? delayCallbacks = null,
         ActivityMap? activityCallbacks = null)
    {
        var sm = new StateMachine() { };
        var jsonScript = Security.SafeReadFile(jsonFilePath);
        StateMachine.ParseStateMachine(sm, jsonScript, guidIsolate, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks, activityCallbacks);
        sm.EnableThreadSafety = threadSafe;
        return sm;
    }
    
    /// <summary>
    /// Create a thread-safe state machine from script
    /// </summary>
    [Obsolete("Use ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices with EventBusOrchestrator instead. Direct StateMachine creation bypasses the orchestrator and can lead to deadlocks.")]
    public static StateMachine CreateFromScript(
        string? jsonScript,
        bool threadSafe = false,
        bool guidIsolate = false,
        ActionMap? actionCallbacks = null,
        GuardMap? guardCallbacks = null,
        ServiceMap? serviceCallbacks = null,
        DelayMap? delayCallbacks = null,
        ActivityMap? activityCallbacks = null
        )
    {
        //var sm = StateMachine.CreateFromScript(jsonScript, guidIsolate, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks);
        if (guidIsolate) { 
            jsonScript = MachineIdIsolatedScript(jsonScript);
        }
        var sm = new StateMachine() { };
        StateMachine.ParseStateMachine(sm, jsonScript, guidIsolate, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks, activityCallbacks);
        sm.EnableThreadSafety = threadSafe;
        return sm;
    }
}