using System.Text.Json.Serialization;

namespace SemiFlow.Converter.Models;

/// <summary>
/// Root SemiFlow document structure (v1.0)
/// </summary>
public class SemiFlowDocument
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("vars")]
    public Dictionary<string, object>? Vars { get; set; }

    [JsonPropertyName("constants")]
    public Dictionary<string, object>? Constants { get; set; }

    [JsonPropertyName("stations")]
    public List<Station>? Stations { get; set; }

    [JsonPropertyName("globalStationPools")]
    public Dictionary<string, List<string>>? GlobalStationPools { get; set; }

    [JsonPropertyName("resourceGroups")]
    public List<ResourceGroup>? ResourceGroups { get; set; }

    [JsonPropertyName("events")]
    public List<EventDef>? Events { get; set; }

    [JsonPropertyName("metrics")]
    public List<MetricDef>? Metrics { get; set; }

    [JsonPropertyName("lanes")]
    public List<Lane> Lanes { get; set; } = new();

    [JsonPropertyName("globalHandlers")]
    public GlobalHandlers? GlobalHandlers { get; set; }
}

public class Station
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "dedicated"; // dedicated, shared, swappable

    [JsonPropertyName("lanes")]
    public List<string>? Lanes { get; set; }

    [JsonPropertyName("capacity")]
    public int Capacity { get; set; } = 1;

    [JsonPropertyName("state")]
    public string State { get; set; } = "idle"; // idle, busy, maintenance, error

    [JsonPropertyName("healthCheck")]
    public HealthCheck? HealthCheck { get; set; }

    [JsonPropertyName("capabilities")]
    public List<string>? Capabilities { get; set; }

    [JsonPropertyName("meta")]
    public Dictionary<string, object>? Meta { get; set; }
}

public class HealthCheck
{
    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }
}

public class ResourceGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("resources")]
    public List<string> Resources { get; set; } = new();

    [JsonPropertyName("strategy")]
    public string Strategy { get; set; } = "any"; // all, any, roundRobin, leastBusy
}

public class EventDef
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // system, station, wafer, user

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("schema")]
    public Dictionary<string, object>? Schema { get; set; }
}

public class MetricDef
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // counter, gauge, histogram, timer

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("aggregation")]
    public string Aggregation { get; set; } = "avg"; // sum, avg, min, max, p50, p95, p99
}

public class Lane
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("maxConcurrentWafers")]
    public int MaxConcurrentWafers { get; set; } = 1;

    [JsonPropertyName("vars")]
    public Dictionary<string, object>? Vars { get; set; }

    [JsonPropertyName("stationPools")]
    public Dictionary<string, List<string>>? StationPools { get; set; }

    [JsonPropertyName("eventHandlers")]
    public List<EventHandler>? EventHandlers { get; set; }

    [JsonPropertyName("workflow")]
    public Workflow Workflow { get; set; } = new();
}

public class EventHandler
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    [JsonPropertyName("steps")]
    public List<Step> Steps { get; set; } = new();
}

public class GlobalHandlers
{
    [JsonPropertyName("onError")]
    public List<Step>? OnError { get; set; }

    [JsonPropertyName("onTimeout")]
    public List<Step>? OnTimeout { get; set; }

    [JsonPropertyName("onEmergencyStop")]
    public List<Step>? OnEmergencyStop { get; set; }
}

public class Workflow
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("vars")]
    public Dictionary<string, object>? Vars { get; set; }

    [JsonPropertyName("preconditions")]
    public List<string>? Preconditions { get; set; }

    [JsonPropertyName("postconditions")]
    public List<string>? Postconditions { get; set; }

    [JsonPropertyName("steps")]
    public List<Step> Steps { get; set; } = new();
}
