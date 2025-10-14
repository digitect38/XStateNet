using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CMPSimulator.SchedulingRules;

/// <summary>
/// Root configuration for declarative scheduling rules
/// </summary>
public class SchedulingRulesConfiguration
{
    [JsonProperty("$schema")]
    public string? Schema { get; set; }

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("configuration")]
    public SchedulingConfiguration Configuration { get; set; } = new();

    [JsonProperty("resources")]
    public ResourcesDefinition Resources { get; set; } = new();

    [JsonProperty("rules")]
    public List<SchedulingRule> Rules { get; set; } = new();

    [JsonProperty("robotBehaviors")]
    public Dictionary<string, RobotBehavior> RobotBehaviors { get; set; } = new();

    [JsonProperty("stateMapping")]
    public Dictionary<string, Dictionary<string, List<string>>> StateMapping { get; set; } = new();
}

public class SchedulingConfiguration
{
    [JsonProperty("mode")]
    public string Mode { get; set; } = "parallel";

    [JsonProperty("conflictResolution")]
    public string ConflictResolution { get; set; } = "priority";

    [JsonProperty("enableParallelExecution")]
    public bool EnableParallelExecution { get; set; } = true;
}

public class ResourcesDefinition
{
    [JsonProperty("robots")]
    public List<string> Robots { get; set; } = new();

    [JsonProperty("stations")]
    public List<string> Stations { get; set; } = new();

    [JsonProperty("queues")]
    public List<string> Queues { get; set; } = new();
}

/// <summary>
/// A single scheduling rule (like P1, P2, P3, P4)
/// </summary>
public class SchedulingRule
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("priority")]
    public int Priority { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("robot")]
    public string Robot { get; set; } = string.Empty;

    [JsonProperty("from")]
    public string From { get; set; } = string.Empty;

    [JsonProperty("to")]
    public string To { get; set; } = string.Empty;

    [JsonProperty("conditions")]
    public Condition Conditions { get; set; } = new();

    [JsonProperty("action")]
    public RuleAction Action { get; set; } = new();

    [JsonProperty("effects")]
    public List<Effect>? Effects { get; set; }

    [JsonProperty("behavior")]
    public RuleBehavior? Behavior { get; set; }
}

/// <summary>
/// Condition tree (and/or/not logic)
/// </summary>
public class Condition
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty; // "and", "or", "not", "stationState", "robotState", "queueCount", "comment"

    [JsonProperty("rules")]
    public List<Condition>? Rules { get; set; }

    [JsonProperty("station")]
    public string? Station { get; set; }

    [JsonProperty("robot")]
    public string? Robot { get; set; }

    [JsonProperty("queue")]
    public string? Queue { get; set; }

    [JsonProperty("operator")]
    public string? Operator { get; set; } // "equals", "greaterThan", "lessThan"

    [JsonProperty("value")]
    public object? Value { get; set; }
}

public class RuleAction
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty; // "transfer", "automatic"

    [JsonProperty("command")]
    public string? Command { get; set; } // "TRANSFER", "PICK", "PLACE"

    [JsonProperty("parameters")]
    public Dictionary<string, object>? Parameters { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }
}

public class Effect
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty; // "queueOperation", "checkCompletion"

    [JsonProperty("operation")]
    public string? Operation { get; set; } // "add", "removeFirst", "remove"

    [JsonProperty("queue")]
    public string? Queue { get; set; }

    [JsonProperty("value")]
    public object? Value { get; set; }

    [JsonProperty("condition")]
    public string? Condition { get; set; }

    [JsonProperty("event")]
    public string? Event { get; set; }
}

public class RuleBehavior
{
    [JsonProperty("pickImmediately")]
    public bool PickImmediately { get; set; }

    [JsonProperty("waitForDestination")]
    public bool WaitForDestination { get; set; }

    [JsonProperty("waitInHoldingState")]
    public bool WaitInHoldingState { get; set; }
}

public class RobotBehavior
{
    [JsonProperty("waitConditions")]
    public Dictionary<string, WaitCondition> WaitConditions { get; set; } = new();
}

public class WaitCondition
{
    [JsonProperty("readyStates")]
    public List<string>? ReadyStates { get; set; }

    [JsonProperty("alwaysReady")]
    public bool AlwaysReady { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }
}
