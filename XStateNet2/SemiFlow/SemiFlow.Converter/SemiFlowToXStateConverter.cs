using System.Text.Json;
using SemiFlow.Converter.Models;
using SemiFlow.Converter.Converters;
using XStateNet2.Core.Engine;

namespace SemiFlow.Converter;

/// <summary>
/// Converts SemiFlow DSL documents to XState machine definitions
/// </summary>
public class SemiFlowToXStateConverter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// Convert a SemiFlow JSON string to an XState machine script
    /// </summary>
    public XStateMachineScript Convert(string semiFlowJson)
    {
        var semiFlow = JsonSerializer.Deserialize<SemiFlowDocument>(semiFlowJson, _jsonOptions);
        if (semiFlow == null)
            throw new ArgumentException("Failed to parse SemiFlow JSON", nameof(semiFlowJson));

        return ConvertDocument(semiFlow);
    }

    /// <summary>
    /// Convert a SemiFlow document to XState machine script
    /// </summary>
    public XStateMachineScript ConvertDocument(SemiFlowDocument semiFlow)
    {
        // Build conversion context
        var context = BuildContext(semiFlow);

        // Determine the conversion strategy based on the number of lanes
        if (semiFlow.Lanes.Count == 1)
        {
            // Single lane - direct conversion
            return ConvertSingleLane(semiFlow, semiFlow.Lanes[0], context);
        }
        else
        {
            // Multiple lanes - create parallel machine
            return ConvertMultiLane(semiFlow, context);
        }
    }

    /// <summary>
    /// Build conversion context from SemiFlow document
    /// </summary>
    private ConversionContext BuildContext(SemiFlowDocument semiFlow)
    {
        var context = new ConversionContext();

        // Index stations
        if (semiFlow.Stations != null)
        {
            foreach (var station in semiFlow.Stations)
            {
                context.Stations[station.Id] = station;
            }
        }

        // Index resource groups
        if (semiFlow.ResourceGroups != null)
        {
            foreach (var group in semiFlow.ResourceGroups)
            {
                context.ResourceGroups[group.Id] = group;
            }
        }

        // Index events
        if (semiFlow.Events != null)
        {
            foreach (var evt in semiFlow.Events)
            {
                context.Events[evt.Name] = evt;
            }
        }

        // Index metrics
        if (semiFlow.Metrics != null)
        {
            foreach (var metric in semiFlow.Metrics)
            {
                context.Metrics[metric.Name] = metric;
            }
        }

        // Store global vars
        if (semiFlow.Vars != null)
        {
            context.GlobalVars = new Dictionary<string, object>(semiFlow.Vars);
        }

        return context;
    }

    /// <summary>
    /// Convert single lane SemiFlow to XState machine
    /// </summary>
    private XStateMachineScript ConvertSingleLane(SemiFlowDocument semiFlow, Lane lane, ConversionContext context)
    {
        var script = new XStateMachineScript
        {
            Id = lane.Workflow.Id,
            Context = BuildMachineContext(semiFlow, lane)
        };

        // Create root states dictionary
        var rootStates = new Dictionary<string, XStateNode>();

        // Convert workflow steps
        var stepConverter = new StepConverter(context);
        var workflow = lane.Workflow;

        if (workflow.Steps.Count > 0)
        {
            stepConverter.ConvertStepSequence(workflow.Steps, rootStates, "completed");

            // Add completed state
            rootStates["completed"] = new XStateNode
            {
                Type = "final",
                Entry = new List<object> { "logWorkflowCompleted" }
            };

            // Set initial state
            script.Initial = workflow.Steps[0].Id;
        }
        else
        {
            // Empty workflow
            script.Initial = "idle";
            rootStates["idle"] = new XStateNode
            {
                Type = "final"
            };
        }

        script.States = rootStates;

        // Add global event handlers
        AddGlobalHandlers(script, semiFlow.GlobalHandlers);

        // Add lane event handlers
        AddLaneEventHandlers(rootStates, lane);

        return script;
    }

    /// <summary>
    /// Convert multi-lane SemiFlow to parallel XState machine
    /// </summary>
    private XStateMachineScript ConvertMultiLane(SemiFlowDocument semiFlow, ConversionContext context)
    {
        var machineStates = new Dictionary<string, XStateNode>();

        // Create a parallel region for each lane
        foreach (var lane in semiFlow.Lanes)
        {
            var laneStates = new Dictionary<string, XStateNode>();

            var stepConverter = new StepConverter(context);
            var workflow = lane.Workflow;

            if (workflow.Steps.Count > 0)
            {
                stepConverter.ConvertStepSequence(workflow.Steps, laneStates, "completed");

                // Add completed state
                laneStates["completed"] = new XStateNode
                {
                    Type = "final",
                    Entry = new List<object> { $"logLane{lane.Id}Completed" }
                };
            }
            else
            {
                laneStates["idle"] = new XStateNode
                {
                    Type = "final"
                };
            }

            var laneState = new XStateNode
            {
                States = laneStates,
                Initial = workflow.Steps.Count > 0 ? workflow.Steps[0].Id : "idle"
            };

            // Add lane event handlers
            AddLaneEventHandlers(laneStates, lane);

            machineStates[lane.Id] = laneState;
        }

        var script = new XStateMachineScript
        {
            Id = semiFlow.Name,
            Type = "parallel",
            Context = BuildMachineContext(semiFlow, null),
            States = machineStates
        };

        // Add global event handlers
        AddGlobalHandlers(script, semiFlow.GlobalHandlers);

        return script;
    }

    /// <summary>
    /// Build machine context from SemiFlow document
    /// </summary>
    private Dictionary<string, object> BuildMachineContext(SemiFlowDocument semiFlow, Lane? lane)
    {
        var context = new Dictionary<string, object>();

        // Add global vars
        if (semiFlow.Vars != null)
        {
            foreach (var (key, value) in semiFlow.Vars)
            {
                context[key] = value;
            }
        }

        // Add global constants
        if (semiFlow.Constants != null)
        {
            foreach (var (key, value) in semiFlow.Constants)
            {
                context[key] = value;
            }
        }

        // Add workflow vars if single lane
        if (lane != null)
        {
            if (lane.Vars != null)
            {
                foreach (var (key, value) in lane.Vars)
                {
                    context[$"lane_{key}"] = value;
                }
            }

            if (lane.Workflow.Vars != null)
            {
                foreach (var (key, value) in lane.Workflow.Vars)
                {
                    context[$"workflow_{key}"] = value;
                }
            }
        }

        // Add station tracking
        context["stations"] = new Dictionary<string, object>
        {
            ["available"] = semiFlow.Stations?.Select(s => s.Id).ToList() ?? new List<string>(),
            ["inUse"] = new List<string>()
        };

        // Add resource tracking
        context["resources"] = new Dictionary<string, object>
        {
            ["reserved"] = new List<string>()
        };

        return context;
    }

    /// <summary>
    /// Add global error/timeout handlers to machine
    /// </summary>
    private void AddGlobalHandlers(XStateMachineScript script, GlobalHandlers? handlers)
    {
        if (handlers == null) return;

        // Note: XState doesn't have direct global handlers
        // These would need to be implemented via invoked services or context
        // For now, we'll add them as entry actions on the root
    }

    /// <summary>
    /// Add lane-specific event handlers
    /// </summary>
    private void AddLaneEventHandlers(Dictionary<string, XStateNode> laneStates, Lane lane)
    {
        if (lane.EventHandlers == null || lane.EventHandlers.Count == 0)
            return;

        // Event handlers would need to be added to specific states
        // For now, we skip this as it requires more complex state manipulation
        // TODO: Implement proper event handler integration
    }

    /// <summary>
    /// Serialize XState machine to JSON
    /// </summary>
    public string SerializeToJson(XStateMachineScript script)
    {
        return JsonSerializer.Serialize(script, _jsonOptions);
    }

    /// <summary>
    /// Convert SemiFlow JSON file to XState JSON file
    /// </summary>
    public void ConvertFile(string inputPath, string outputPath)
    {
        var semiFlowJson = File.ReadAllText(inputPath);
        var xstateMachine = Convert(semiFlowJson);
        var xstateJson = SerializeToJson(xstateMachine);
        File.WriteAllText(outputPath, xstateJson);
    }
}
