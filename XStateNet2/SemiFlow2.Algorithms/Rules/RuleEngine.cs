namespace SemiFlow.Algorithms.Rules;

/// <summary>
/// Rule engine for applying and validating SemiFlow2 scheduling rules
/// </summary>
public class RuleEngine
{
    private readonly Dictionary<string, ISchedulingRule> _rules = new();

    public RuleEngine()
    {
        // Register all built-in rules
        foreach (var rule in WaferAssignmentRules.All)
            _rules[rule.Id] = rule;

        foreach (var rule in PipelineSlotRules.All)
            _rules[rule.Id] = rule;

        foreach (var rule in SteadyStateRules.All)
            _rules[rule.Id] = rule;
    }

    /// <summary>
    /// Get a rule by ID
    /// </summary>
    public ISchedulingRule? GetRule(string id)
        => _rules.TryGetValue(id, out var rule) ? rule : null;

    /// <summary>
    /// Get a typed rule by ID
    /// </summary>
    public T? GetRule<T>(string id) where T : class, ISchedulingRule
        => GetRule(id) as T;

    /// <summary>
    /// Check if a rule exists
    /// </summary>
    public bool HasRule(string id) => _rules.ContainsKey(id);

    /// <summary>
    /// Get all rules
    /// </summary>
    public IEnumerable<ISchedulingRule> AllRules => _rules.Values;

    /// <summary>
    /// Get rules by category
    /// </summary>
    public IEnumerable<ISchedulingRule> GetRulesByCategory(RuleCategory category)
        => _rules.Values.Where(r => r.Category == category);

    /// <summary>
    /// Apply a rule by ID (for validatable rules)
    /// </summary>
    public ValidationResult ValidateRule(string id, RuleContext context)
    {
        var rule = GetRule(id);
        if (rule == null)
            return ValidationResult.Failure($"Rule '{id}' not found");

        if (rule is IValidatableRule validatable)
            return validatable.Validate(context);

        return ValidationResult.Success();
    }

    /// <summary>
    /// Apply multiple rules in sequence
    /// </summary>
    public RuleApplicationResult ApplyRules(IEnumerable<string> ruleIds, RuleContext context)
    {
        var results = new List<(string RuleId, ValidationResult Result)>();
        bool allValid = true;

        foreach (var id in ruleIds)
        {
            var result = ValidateRule(id, context);
            results.Add((id, result));
            if (!result.IsValid)
                allValid = false;
        }

        return new RuleApplicationResult(allValid, results);
    }

    /// <summary>
    /// Register a custom rule
    /// </summary>
    public void RegisterRule(ISchedulingRule rule)
    {
        _rules[rule.Id] = rule;
    }

    /// <summary>
    /// Get rules sorted by priority
    /// </summary>
    public IEnumerable<ISchedulingRule> GetRulesByPriority()
        => _rules.Values.OrderBy(r => r.Priority);
}

/// <summary>
/// Result of applying multiple rules
/// </summary>
public class RuleApplicationResult
{
    public bool AllValid { get; }
    public IReadOnlyList<(string RuleId, ValidationResult Result)> Results { get; }

    public RuleApplicationResult(
        bool allValid,
        IEnumerable<(string, ValidationResult)> results)
    {
        AllValid = allValid;
        Results = results.ToList();
    }

    public IEnumerable<string> FailedRules
        => Results.Where(r => !r.Result.IsValid).Select(r => r.RuleId);

    public IEnumerable<string> AllErrors
        => Results.SelectMany(r => r.Result.Errors);

    public IEnumerable<string> AllWarnings
        => Results.SelectMany(r => r.Result.Warnings);

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Rule Application: {(AllValid ? "SUCCESS" : "FAILED")}");
        foreach (var (ruleId, result) in Results)
        {
            sb.AppendLine($"  {ruleId}: {result}");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Fluent API for rule application
/// </summary>
public class RuleApplicationBuilder
{
    private readonly RuleEngine _engine;
    private readonly RuleContext _context;
    private readonly List<string> _ruleIds = new();

    public RuleApplicationBuilder(RuleEngine? engine = null)
    {
        _engine = engine ?? new RuleEngine();
        _context = new RuleContext();
    }

    public RuleApplicationBuilder WithWafers(int count)
    {
        _context.SetProperty("TotalWafers", count);
        return this;
    }

    public RuleApplicationBuilder WithSchedulers(int count)
    {
        _context.SetProperty("SchedulerCount", count);
        return this;
    }

    public RuleApplicationBuilder WithPipelineDepth(int depth)
    {
        _context.SetProperty("PipelineDepth", depth);
        return this;
    }

    public RuleApplicationBuilder ApplyRule(string ruleId)
    {
        _ruleIds.Add(ruleId);
        return this;
    }

    public RuleApplicationBuilder ApplyWaferAssignmentRules()
    {
        _ruleIds.AddRange(WaferAssignmentRules.All.Select(r => r.Id));
        return this;
    }

    public RuleApplicationBuilder ApplyPipelineSlotRules()
    {
        _ruleIds.AddRange(PipelineSlotRules.All.Select(r => r.Id));
        return this;
    }

    public RuleApplicationBuilder ApplySteadyStateRules()
    {
        _ruleIds.AddRange(SteadyStateRules.All.Select(r => r.Id));
        return this;
    }

    public RuleApplicationResult Execute()
    {
        // Build context from properties
        var context = new RuleContext
        {
            TotalWafers = _context.GetProperty<int>("TotalWafers", 25),
            SchedulerCount = _context.GetProperty<int>("SchedulerCount", 3),
            PipelineDepth = _context.GetProperty<int>("PipelineDepth", 3)
        };

        return _engine.ApplyRules(_ruleIds, context);
    }
}
