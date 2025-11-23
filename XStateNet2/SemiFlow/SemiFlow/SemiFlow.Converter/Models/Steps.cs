using System.Text.Json.Serialization;

namespace SemiFlow.Converter.Models;

/// <summary>
/// Base step with common properties
/// </summary>
public class Step
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("retry")]
    public RetryPolicy? Retry { get; set; }

    [JsonPropertyName("timeout")]
    public int? Timeout { get; set; }

    [JsonPropertyName("onTimeout")]
    public List<Step>? OnTimeout { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    // Type-specific properties
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("args")]
    public Dictionary<string, object>? Args { get; set; }

    [JsonPropertyName("assignResult")]
    public string? AssignResult { get; set; }

    [JsonPropertyName("async")]
    public bool? Async { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("capability")]
    public string? Capability { get; set; }

    [JsonPropertyName("preferred")]
    public List<string>? Preferred { get; set; }

    [JsonPropertyName("fallback")]
    public List<string>? Fallback { get; set; }

    [JsonPropertyName("exclude")]
    public List<string>? Exclude { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("assignToVar")]
    public string? AssignToVar { get; set; }

    [JsonPropertyName("waitForAvailable")]
    public bool? WaitForAvailable { get; set; }

    [JsonPropertyName("maxWaitTime")]
    public int? MaxWaitTime { get; set; }

    [JsonPropertyName("resources")]
    public List<string>? Resources { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [JsonPropertyName("branches")]
    public List<List<Step>>? Branches { get; set; }

    [JsonPropertyName("wait")]
    public string? Wait { get; set; }

    [JsonPropertyName("maxConcurrency")]
    public int? MaxConcurrency { get; set; }

    [JsonPropertyName("condition")]
    public string? Condition { get; set; }

    [JsonPropertyName("count")]
    public int? Count { get; set; }

    [JsonPropertyName("items")]
    public string? Items { get; set; }

    [JsonPropertyName("itemVar")]
    public string? ItemVar { get; set; }

    [JsonPropertyName("indexVar")]
    public string? IndexVar { get; set; }

    [JsonPropertyName("maxIterations")]
    public int? MaxIterations { get; set; }

    [JsonPropertyName("steps")]
    public List<Step>? Steps { get; set; }

    [JsonPropertyName("cases")]
    public object? Cases { get; set; } // Can be List<BranchCase> or Dictionary<string, List<Step>>

    [JsonPropertyName("otherwise")]
    public List<Step>? Otherwise { get; set; }

    [JsonPropertyName("default")]
    public List<Step>? Default { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("until")]
    public string? Until { get; set; }

    [JsonPropertyName("pollInterval")]
    public int? PollInterval { get; set; }

    [JsonPropertyName("expect")]
    public string? Expect { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("continueOnError")]
    public bool? ContinueOnError { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("try")]
    public List<Step>? Try { get; set; }

    [JsonPropertyName("catch")]
    public List<Step>? Catch { get; set; }

    [JsonPropertyName("finally")]
    public List<Step>? Finally { get; set; }

    [JsonPropertyName("catchOn")]
    public List<string>? CatchOn { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("payload")]
    public Dictionary<string, object>? Payload { get; set; }

    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    [JsonPropertyName("once")]
    public bool? Once { get; set; }

    [JsonPropertyName("metric")]
    public string? Metric { get; set; }

    [JsonPropertyName("cancelOthers")]
    public bool? CancelOthers { get; set; }

    [JsonPropertyName("assignWinner")]
    public string? AssignWinner { get; set; }

    [JsonPropertyName("rollback")]
    public List<Step>? Rollback { get; set; }

    [JsonPropertyName("isolationLevel")]
    public string? IsolationLevel { get; set; }
}

public class BranchCase
{
    [JsonPropertyName("when")]
    public string When { get; set; } = string.Empty;

    [JsonPropertyName("steps")]
    public List<Step> Steps { get; set; } = new();
}

public class RetryPolicy
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("delay")]
    public int Delay { get; set; }

    [JsonPropertyName("strategy")]
    public string Strategy { get; set; } = "fixed"; // fixed, exponential, linear

    [JsonPropertyName("maxDelay")]
    public int? MaxDelay { get; set; }

    [JsonPropertyName("jitter")]
    public bool Jitter { get; set; }

    [JsonPropertyName("retryOn")]
    public List<string>? RetryOn { get; set; }
}
