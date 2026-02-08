using SemiFlow.Compiler.Ast;
using SemiFlow.Compiler.Diagnostics;
using SemiFlow.Compiler.Lexer;

namespace SemiFlow.Compiler.Analysis;

/// <summary>
/// Validates SFL programs against semantic rules.
/// Error codes: SFL001-SFL013
/// </summary>
public class SemanticAnalyzer
{
    private readonly List<Diagnostic> _diagnostics = new();
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    private static readonly HashSet<string> ValidSchedulerLayers = new()
    {
        "L1", "L2", "L3", "L4"
    };

    private static readonly Dictionary<SchedulerType, string> ExpectedLayers = new()
    {
        [SchedulerType.Master] = "L1",
        [SchedulerType.Wafer] = "L2",
        [SchedulerType.Robot] = "L3",
        [SchedulerType.Station] = "L4"
    };

    public void Analyze(SflProgram program)
    {
        _diagnostics.Clear();

        foreach (var scheduler in program.Schedulers)
        {
            ValidateSchedulerType(scheduler);
            ValidateLayerHierarchy(scheduler);
            ValidateRuleReferences(scheduler, program);
            ValidatePubSubQos(scheduler);
            ValidateTransactionTimeouts(scheduler);
        }

        ValidateWaferConflicts(program);
        ValidatePipelineDepth(program);

        ValidateFlowBlock(program);
        ValidateCrossover(program);
        ValidateMutex(program);
        ValidateConstraints(program);
    }

    /// <summary>SFL001: Valid scheduler type</summary>
    private void ValidateSchedulerType(SchedulerDef scheduler)
    {
        // SchedulerType enum handles this, but warn if name doesn't match convention
        var prefix = scheduler.Type switch
        {
            SchedulerType.Master => "MSC",
            SchedulerType.Wafer => "WSC",
            SchedulerType.Robot => "RSC",
            SchedulerType.Station => "STN",
            _ => null
        };

        if (prefix != null && !scheduler.Name.StartsWith(prefix) &&
            !scheduler.Name.StartsWith("CMP") && !scheduler.Name.StartsWith("WTR"))
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                "SFL001",
                $"Scheduler '{scheduler.Name}' of type {scheduler.Type} should have prefix '{prefix}_'",
                scheduler.Location));
        }
    }

    /// <summary>SFL002: Layer hierarchy - MSC=L1, WSC=L2, RSC=L3, STN=L4</summary>
    private void ValidateLayerHierarchy(SchedulerDef scheduler)
    {
        if (scheduler.Layer == null) return;

        if (!ValidSchedulerLayers.Contains(scheduler.Layer))
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "SFL002",
                $"Invalid layer '{scheduler.Layer}'. Must be L1, L2, L3, or L4",
                scheduler.Location));
            return;
        }

        if (ExpectedLayers.TryGetValue(scheduler.Type, out var expected) && scheduler.Layer != expected)
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "SFL002",
                $"Scheduler type {scheduler.Type} should use layer {expected}, not {scheduler.Layer}",
                scheduler.Location));
        }
    }

    /// <summary>SFL003: Rule references must exist</summary>
    private void ValidateRuleReferences(SchedulerDef scheduler, SflProgram program)
    {
        var availableRules = new HashSet<string>();

        // Built-in rules
        availableRules.Add("WAR_001");
        availableRules.Add("PSR_001");
        availableRules.Add("SSR_001");

        // Rules from PIPELINE_SCHEDULING_RULES
        if (program.PipelineRules != null)
        {
            foreach (var rule in program.PipelineRules.Rules)
                availableRules.Add(rule.Name);
        }

        // Top-level rules
        foreach (var rule in program.Rules)
            availableRules.Add(rule.Name);

        foreach (var schedule in scheduler.Schedules)
        {
            foreach (var ruleRef in schedule.ApplyRules)
            {
                if (!availableRules.Contains(ruleRef))
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        "SFL003",
                        $"Rule '{ruleRef}' referenced in APPLY_RULE not found. " +
                        "It may be defined in an imported module.",
                        schedule.Location));
                }
            }
        }
    }

    /// <summary>SFL004: No wafer conflicts - same wafer not assigned to multiple WSCs</summary>
    private void ValidateWaferConflicts(SflProgram program)
    {
        var waferAssignments = new Dictionary<string, string>(); // wafer → scheduler

        foreach (var scheduler in program.Schedulers.Where(s => s.Type == SchedulerType.Wafer))
        {
            if (scheduler.AssignedWafers == null) continue;

            var waferList = scheduler.AssignedWafers.Properties
                .FirstOrDefault(p => p.Key == "wafer_list");

            if (waferList?.Value is ArrayValue arr)
            {
                foreach (var elem in arr.Elements)
                {
                    var waferId = elem switch
                    {
                        IdentifierValue id => id.Name,
                        StringValue s => s.Value,
                        _ => elem.ToString()
                    };

                    if (waferId != null && waferAssignments.TryGetValue(waferId, out var existing))
                    {
                        _diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Error,
                            "SFL004",
                            $"Wafer '{waferId}' assigned to both '{existing}' and '{scheduler.Name}'",
                            scheduler.Location));
                    }
                    else if (waferId != null)
                    {
                        waferAssignments[waferId] = scheduler.Name;
                    }
                }
            }
        }
    }

    /// <summary>SFL005: Valid QoS - must be 0, 1, or 2</summary>
    private void ValidatePubSubQos(SchedulerDef scheduler)
    {
        foreach (var pub in scheduler.Publishes)
        {
            if (pub.Qos < 0 || pub.Qos > 2)
            {
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "SFL005",
                    $"Invalid QoS level {pub.Qos}. Must be 0, 1, or 2",
                    pub.Location));
            }
        }

        foreach (var sub in scheduler.Subscribes)
        {
            if (sub.Qos < 0 || sub.Qos > 2)
            {
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "SFL005",
                    $"Invalid QoS level {sub.Qos}. Must be 0, 1, or 2",
                    sub.Location));
            }
        }
    }

    /// <summary>SFL006: Timeout bounds check</summary>
    private void ValidateTransactionTimeouts(SchedulerDef scheduler)
    {
        foreach (var txn in scheduler.Transactions)
        {
            if (txn.Timeout is DurationValue dur)
            {
                var ms = dur.ToMilliseconds();
                if (ms <= 0)
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "SFL006",
                        $"Transaction '{txn.Name}' timeout must be positive",
                        txn.Location));
                }
                else if (ms > 600_000) // 10 minutes
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        "SFL006",
                        $"Transaction '{txn.Name}' timeout of {dur.Amount}{dur.Unit} is very long",
                        txn.Location));
                }
            }
        }
    }

    /// <summary>SFL007: (reserved)</summary>

    /// <summary>SFL008: Pipeline depth max 3</summary>
    private void ValidatePipelineDepth(SflProgram program)
    {
        foreach (var scheduler in program.Schedulers)
        {
            foreach (var schedule in scheduler.Schedules)
            {
                var depthProp = schedule.Properties
                    .FirstOrDefault(p => p.Key == "pipeline_depth");

                if (depthProp?.Value is IntValue depth && depth.Value > 3)
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        "SFL008",
                        $"Pipeline depth {depth.Value} exceeds recommended maximum of 3",
                        depthProp.Location));
                }
            }
        }
    }

    /// <summary>SFL009: Flow must have >= 3 elements, should start with SRC and end with DST</summary>
    private void ValidateFlowBlock(SflProgram program)
    {
        if (program.FlowBlock == null) return;

        var flow = program.FlowBlock;
        if (flow.Sequence.Count < 3)
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "SFL009",
                $"Flow '{flow.Name}' must have at least 3 elements (source, process, destination), has {flow.Sequence.Count}",
                flow.Location));
        }

        if (flow.Sequence.Count > 0 && flow.Sequence[0] != "SRC")
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                "SFL009",
                $"Flow '{flow.Name}' should start with 'SRC', starts with '{flow.Sequence[0]}'",
                flow.Location));
        }

        if (flow.Sequence.Count > 0 && flow.Sequence[^1] != "DST")
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                "SFL009",
                $"Flow '{flow.Name}' should end with 'DST', ends with '{flow.Sequence[^1]}'",
                flow.Location));
        }
    }

    /// <summary>SFL010: Crossover entries should reference stations in flow</summary>
    private void ValidateCrossover(SflProgram program)
    {
        if (program.Crossover == null) return;

        var flowStations = program.FlowBlock?.Sequence.ToHashSet() ?? new HashSet<string>();

        foreach (var entry in program.Crossover.Entries)
        {
            if (flowStations.Count > 0 && !flowStations.Contains(entry.StationName))
            {
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "SFL010",
                    $"Crossover station '{entry.StationName}' not found in flow sequence",
                    entry.Location));
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex MutexPatternRegex =
        new(@"^(L\d+|L\*|\*)\.(R\d+|R\*|\*|\w+)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>SFL011: Mutex patterns must match (L\d+|L*|*).(R\d+|R*|*)</summary>
    private void ValidateMutex(SflProgram program)
    {
        if (program.Mutex == null) return;

        foreach (var group in program.Mutex.Groups)
        {
            foreach (var pattern in group.Patterns)
            {
                if (!MutexPatternRegex.IsMatch(pattern))
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "SFL011",
                        $"Invalid mutex pattern '{pattern}'. Expected format: (L<n>|L*|*).(R<n>|R*|*)",
                        group.Location));
                }
            }
        }
    }

    private static readonly HashSet<string> ValidSchedulingModes = new()
    {
        "TAKT_TIME", "EVENT_DRIVEN", "DEADLINE_DRIVEN"
    };

    private static readonly HashSet<string> ValidDeadlinePolicies = new()
    {
        "EDF", "LLF"
    };

    /// <summary>SFL012: Constraint validations - max_wip must be positive</summary>
    /// <summary>SFL013: Scheduling mode and deadline policy validation</summary>
    private void ValidateConstraints(SflProgram program)
    {
        if (program.Constraints == null) return;

        string? schedulingMode = null;

        foreach (var prop in program.Constraints.Properties)
        {
            if (prop.Key == "max_wip" && prop.Value is IntValue maxWip && maxWip.Value <= 0)
            {
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "SFL012",
                    $"Constraint 'max_wip' must be positive, got {maxWip.Value}",
                    prop.Location));
            }

            if (prop.Key == "no_wait" && prop.Value is ArrayValue noWait)
            {
                var flowStations = program.FlowBlock?.Sequence.ToHashSet() ?? new HashSet<string>();
                foreach (var elem in noWait.Elements)
                {
                    var name = elem switch
                    {
                        IdentifierValue id => id.Name,
                        StringValue s => s.Value,
                        _ => null
                    };
                    if (name != null && flowStations.Count > 0 && !flowStations.Contains(name))
                    {
                        _diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Warning,
                            "SFL012",
                            $"Constraint 'no_wait' references '{name}' which is not in the flow sequence",
                            prop.Location));
                    }
                }
            }

            if (prop.Key == "scheduling_mode" && prop.Value is StringValue modeVal)
            {
                schedulingMode = modeVal.Value;
                if (!ValidSchedulingModes.Contains(modeVal.Value))
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "SFL013",
                        $"Invalid scheduling_mode '{modeVal.Value}'. Must be TAKT_TIME, EVENT_DRIVEN, or DEADLINE_DRIVEN",
                        prop.Location));
                }
            }

            if (prop.Key == "deadline_policy" && prop.Value is StringValue policyVal)
            {
                if (!ValidDeadlinePolicies.Contains(policyVal.Value))
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "SFL013",
                        $"Invalid deadline_policy '{policyVal.Value}'. Must be EDF or LLF",
                        prop.Location));
                }
            }

            if (prop.Key == "takt_interval" && prop.Value is DurationValue taktDur)
            {
                if (taktDur.ToMilliseconds() <= 0)
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "SFL013",
                        $"Constraint 'takt_interval' must be a positive duration",
                        prop.Location));
                }
            }
        }

        // Warning: deadline_policy is only meaningful with DEADLINE_DRIVEN mode
        var hasDeadlinePolicy = program.Constraints.Properties.Any(p => p.Key == "deadline_policy");
        if (hasDeadlinePolicy && schedulingMode != null && schedulingMode != "DEADLINE_DRIVEN")
        {
            var policyProp = program.Constraints.Properties.First(p => p.Key == "deadline_policy");
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                "SFL013",
                $"'deadline_policy' is only meaningful when scheduling_mode is DEADLINE_DRIVEN (current: {schedulingMode})",
                policyProp.Location));
        }
    }
}
