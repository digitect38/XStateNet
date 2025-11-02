using System.Collections.Frozen;
using XStateNet2.Core.Engine;

namespace XStateNet2.Core.Parser;

/// <summary>
/// Optimizes XStateMachineScript by converting mutable dictionaries to FrozenDictionary
/// for 2-3x faster lookups (20-40ns vs 50-100ns) in read-heavy workloads
/// </summary>
public static class ScriptOptimizer
{
    /// <summary>
    /// Freezes all dictionaries in the script for optimal read performance.
    /// Call this after JSON deserialization and before creating the StateMachineActor.
    /// </summary>
    public static void Freeze(XStateMachineScript script)
    {
        if (script == null)
            throw new ArgumentNullException(nameof(script));

        // Freeze root-level dictionaries
        if (script.Context is Dictionary<string, object> context)
        {
            script.Context = context.ToFrozenDictionary();
        }

        if (script.On is Dictionary<string, List<XStateTransition>> on)
        {
            script.On = on.ToFrozenDictionary();
        }

        if (script.States is Dictionary<string, XStateNode> states)
        {
            // Freeze the root states dictionary
            script.States = states.ToFrozenDictionary();

            // Recursively freeze nested state nodes
            foreach (var state in states.Values)
            {
                FreezeNode(state);
            }
        }
    }

    /// <summary>
    /// Recursively freezes all dictionaries in a state node and its children
    /// </summary>
    private static void FreezeNode(XStateNode node)
    {
        if (node == null)
            return;

        // Freeze transitions
        if (node.On is Dictionary<string, List<XStateTransition>> on)
        {
            node.On = on.ToFrozenDictionary();
        }

        // Freeze delayed transitions
        if (node.After is Dictionary<int, XStateTransition> after)
        {
            node.After = after.ToFrozenDictionary();
        }

        // Freeze metadata
        if (node.Meta is Dictionary<string, object> meta)
        {
            node.Meta = meta.ToFrozenDictionary();
        }

        // Recursively freeze child states
        if (node.States is Dictionary<string, XStateNode> states)
        {
            node.States = states.ToFrozenDictionary();

            foreach (var childNode in states.Values)
            {
                FreezeNode(childNode);
            }
        }

        // Freeze action assignments (if they're Dictionary)
        FreezeActions(node.Entry);
        FreezeActions(node.Exit);
        FreezeActions(node.Always?.SelectMany(t => t.Actions ?? new List<object>()));
    }

    /// <summary>
    /// Freezes assignment dictionaries in action definitions
    /// </summary>
    private static void FreezeActions(IEnumerable<object>? actions)
    {
        if (actions == null)
            return;

        foreach (var action in actions)
        {
            if (action is ActionDefinition actionDef)
            {
                if (actionDef.Assignment is Dictionary<string, object> assignment)
                {
                    actionDef.Assignment = assignment.ToFrozenDictionary();
                }
            }
        }
    }
}
