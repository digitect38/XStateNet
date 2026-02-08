namespace SemiFlow.Algorithms.Rules;

/// <summary>
/// Base interface for all scheduling rules
/// </summary>
public interface ISchedulingRule
{
    /// <summary>
    /// Rule identifier (e.g., "WAR_001", "PSR_001")
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Human-readable name
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Rule category
    /// </summary>
    RuleCategory Category { get; }

    /// <summary>
    /// Priority (lower = higher priority)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Description of what the rule does
    /// </summary>
    string Description { get; }
}

/// <summary>
/// Rule that can be validated
/// </summary>
public interface IValidatableRule : ISchedulingRule
{
    /// <summary>
    /// Validate the rule constraints
    /// </summary>
    ValidationResult Validate(RuleContext context);
}

/// <summary>
/// Rule categories as defined in SemiFlow2
/// </summary>
public enum RuleCategory
{
    /// <summary>
    /// WAR - Wafer Assignment Rules
    /// </summary>
    WaferAssignment,

    /// <summary>
    /// PSR - Pipeline Slot Rules
    /// </summary>
    PipelineSlot,

    /// <summary>
    /// SSR - Steady State Rules
    /// </summary>
    SteadyState,

    /// <summary>
    /// WTR - Wafer Transfer Rules
    /// </summary>
    WaferTransfer
}

/// <summary>
/// Context for rule evaluation
/// </summary>
public class RuleContext
{
    public int TotalWafers { get; init; }
    public int SchedulerCount { get; init; }
    public int PipelineDepth { get; init; } = 3;
    public Dictionary<string, object> Properties { get; } = new();

    public T GetProperty<T>(string key, T defaultValue = default!)
        => Properties.TryGetValue(key, out var value) && value is T typed
            ? typed
            : defaultValue;

    public void SetProperty<T>(string key, T value)
        => Properties[key] = value!;
}

/// <summary>
/// Result of rule validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> Warnings { get; }

    private ValidationResult(bool isValid, IEnumerable<string>? errors = null, IEnumerable<string>? warnings = null)
    {
        IsValid = isValid;
        Errors = (errors?.ToList() ?? new List<string>()).AsReadOnly();
        Warnings = (warnings?.ToList() ?? new List<string>()).AsReadOnly();
    }

    public static ValidationResult Success(IEnumerable<string>? warnings = null)
        => new(true, null, warnings);

    public static ValidationResult Failure(params string[] errors)
        => new(false, errors);

    public static ValidationResult Failure(IEnumerable<string> errors)
        => new(false, errors);

    public override string ToString()
        => IsValid
            ? $"Valid{(Warnings.Count > 0 ? $" (warnings: {Warnings.Count})" : "")}"
            : $"Invalid: {string.Join(", ", Errors)}";
}
