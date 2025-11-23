using SemiFlow.Converter.Models;
using SemiFlow.Converter.Helpers;
using XStateNet2.Core.Engine;
using System.Text.Json;

namespace SemiFlow.Converter.Converters;

/// <summary>
/// Converts SemiFlow steps to XState states and transitions
/// </summary>
public class StepConverter
{
    private readonly ConversionContext _context;
    private int _stateCounter = 0;

    public StepConverter(ConversionContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Convert a SemiFlow step into XState state nodes
    /// Adds states to the provided states dictionary
    /// Returns the entry state ID for this step
    /// </summary>
    public string ConvertStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId = null)
    {
        if (!step.Enabled)
        {
            // Skip disabled steps, return next state
            return nextStateId ?? GenerateStateId("noop");
        }

        return step.Type switch
        {
            "action" => ConvertActionStep(step, parentStates, nextStateId),
            "useStation" => ConvertUseStationStep(step, parentStates, nextStateId),
            "reserve" => ConvertReserveStep(step, parentStates, nextStateId),
            "release" => ConvertReleaseStep(step, parentStates, nextStateId),
            "parallel" => ConvertParallelStep(step, parentStates, nextStateId),
            "loop" => ConvertLoopStep(step, parentStates, nextStateId),
            "branch" => ConvertBranchStep(step, parentStates, nextStateId),
            "switch" => ConvertSwitchStep(step, parentStates, nextStateId),
            "wait" => ConvertWaitStep(step, parentStates, nextStateId),
            "condition" => ConvertConditionStep(step, parentStates, nextStateId),
            "sequence" => ConvertSequenceStep(step, parentStates, nextStateId),
            "call" => ConvertCallStep(step, parentStates, nextStateId),
            "try" => ConvertTryStep(step, parentStates, nextStateId),
            "emitEvent" => ConvertEmitEventStep(step, parentStates, nextStateId),
            "onEvent" => ConvertOnEventStep(step, parentStates, nextStateId),
            "collectMetric" => ConvertCollectMetricStep(step, parentStates, nextStateId),
            "race" => ConvertRaceStep(step, parentStates, nextStateId),
            "transaction" => ConvertTransactionStep(step, parentStates, nextStateId),
            _ => throw new NotSupportedException($"Step type '{step.Type}' is not supported")
        };
    }

    private string ConvertActionStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var builder = new XStateNodeBuilder()
            .WithEntry(step.Action!);

        // Add timeout handling
        if (step.Timeout.HasValue)
        {
            var afterDict = new Dictionary<int, List<XStateTransition>>
            {
                [step.Timeout.Value] = new List<XStateTransition>
                {
                    new XStateTransition { Target = nextStateId ?? GenerateStateId("timeout") }
                }
            };
            builder.WithAfter(step.Timeout.Value, afterDict[step.Timeout.Value]);
        }

        // Add transition to next state
        if (nextStateId != null)
        {
            if (step.Async == true)
            {
                // For async actions, transition immediately
                builder.WithAlways(new XStateTransition { Target = nextStateId });
            }
            else
            {
                // For sync actions, wait for completion event
                var completionEvent = $"{step.Action}_DONE";
                builder.WithOn(completionEvent, new List<XStateTransition>
                {
                    new XStateTransition { Target = nextStateId }
                });
            }
        }

        parentStates[stateId] = builder.Build();
        return stateId;
    }

    private string ConvertUseStationStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var nestedStates = new Dictionary<string, XStateNode>();

        // Acquiring state
        var acquiringOn = new Dictionary<string, List<XStateTransition>>
        {
            ["STATION_ACQUIRED"] = new List<XStateTransition>
            {
                new XStateTransition { Target = "using" }
            },
            ["STATION_UNAVAILABLE"] = new List<XStateTransition>
            {
                new XStateTransition
                {
                    Target = step.WaitForAvailable == true ? "waiting" : $"..{nextStateId ?? "error"}"
                }
            }
        };

        nestedStates["acquiring"] = new XStateNode
        {
            Entry = new List<object> { $"requestStation_{step.Role}" },
            On = acquiringOn
        };

        // Waiting state
        var waitingAfter = new Dictionary<int, List<XStateTransition>>
        {
            [1000] = new List<XStateTransition>
            {
                new XStateTransition { Target = "acquiring" }
            }
        };
        nestedStates["waiting"] = new XStateNode
        {
            After = waitingAfter
        };

        // Using state
        var usingOn = new Dictionary<string, List<XStateTransition>>
        {
            ["USAGE_COMPLETE"] = new List<XStateTransition>
            {
                new XStateTransition
                {
                    Target = nextStateId != null ? $"..{nextStateId}" : null,
                    Actions = new List<string> { $"releaseStation_{step.Role}" }
                }
            }
        };
        nestedStates["using"] = new XStateNode
        {
            On = usingOn
        };

        parentStates[stateId] = new XStateNode
        {
            Entry = new List<object> { $"acquireStation_{step.Role}" },
            States = nestedStates,
            Initial = "acquiring"
        };

        return stateId;
    }

    private string ConvertReserveStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var resourceList = string.Join(",", step.Resources ?? new List<string>());

        var builder = new XStateNodeBuilder()
            .WithEntry($"reserveResources_{resourceList}");

        if (nextStateId != null)
        {
            builder.WithAlways(new XStateTransition { Target = nextStateId });
        }

        parentStates[stateId] = builder.Build();
        return stateId;
    }

    private string ConvertReleaseStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var resourceList = string.Join(",", step.Resources ?? new List<string>());

        var builder = new XStateNodeBuilder()
            .WithEntry($"releaseResources_{resourceList}");

        if (nextStateId != null)
        {
            builder.WithAlways(new XStateTransition { Target = nextStateId });
        }

        parentStates[stateId] = builder.Build();
        return stateId;
    }

    private string ConvertParallelStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var parallelRegions = new Dictionary<string, XStateNode>();

        // Create a parallel region for each branch
        for (int i = 0; i < (step.Branches?.Count ?? 0); i++)
        {
            var branch = step.Branches![i];
            var branchId = $"branch_{i}";
            var branchStates = new Dictionary<string, XStateNode>();

            // Convert branch steps sequentially
            ConvertStepSequence(branch, branchStates, "final");

            // Add final state for this branch
            branchStates["final"] = new XStateNode { Type = "final" };

            parallelRegions[branchId] = new XStateNode
            {
                States = branchStates,
                Initial = branch.FirstOrDefault()?.Id ?? "final"
            };
        }

        var stateBuilder = new XStateNodeBuilder()
            .WithType("parallel");

        foreach (var (key, value) in parallelRegions)
        {
            stateBuilder.WithState(key, value);
        }

        // Add completion transition based on wait mode
        if (nextStateId != null)
        {
            stateBuilder.WithOnDone(new XStateTransition { Target = nextStateId });
        }

        parentStates[stateId] = stateBuilder.Build();
        return stateId;
    }

    private string ConvertLoopStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var bodyStates = new Dictionary<string, XStateNode>();

        // Convert loop body steps
        var bodySteps = step.Steps ?? new List<Step>();
        ConvertStepSequence(bodySteps, bodyStates, "loop_check");

        // Add loop check state
        bodyStates["loop_check"] = new XStateNode
        {
            Always = new List<XStateTransition>
            {
                new XStateTransition
                {
                    Target = bodySteps.FirstOrDefault()?.Id ?? "loop_check",
                    Guard = step.Condition ?? "shouldContinueLoop"
                },
                new XStateTransition
                {
                    Target = $"..{nextStateId ?? "loop_exit"}"
                }
            }
        };

        var loopStates = new Dictionary<string, XStateNode>
        {
            ["loop_body"] = new XStateNode
            {
                States = bodyStates,
                Initial = bodySteps.FirstOrDefault()?.Id ?? "loop_check"
            }
        };

        parentStates[stateId] = new XStateNode
        {
            States = loopStates,
            Initial = "loop_body"
        };

        return stateId;
    }

    private string ConvertBranchStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var branchStates = new Dictionary<string, XStateNode>();
        var entryTransitions = new List<XStateTransition>();

        if (step.Cases is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            var cases = JsonSerializer.Deserialize<List<BranchCase>>(jsonElement.GetRawText());

            foreach (var branchCase in cases ?? new List<BranchCase>())
            {
                var caseId = GenerateStateId("case");
                var caseStates = new Dictionary<string, XStateNode>();

                ConvertStepSequence(branchCase.Steps, caseStates, nextStateId);

                branchStates[caseId] = new XStateNode
                {
                    States = caseStates,
                    Initial = branchCase.Steps.FirstOrDefault()?.Id ?? "empty"
                };

                entryTransitions.Add(new XStateTransition
                {
                    Target = caseId,
                    Guard = branchCase.When
                });
            }
        }

        // Add otherwise branch
        if (step.Otherwise != null && step.Otherwise.Count > 0)
        {
            var otherwiseId = GenerateStateId("otherwise");
            var otherwiseStates = new Dictionary<string, XStateNode>();

            ConvertStepSequence(step.Otherwise, otherwiseStates, nextStateId);

            branchStates[otherwiseId] = new XStateNode
            {
                States = otherwiseStates,
                Initial = step.Otherwise.FirstOrDefault()?.Id ?? "empty"
            };

            entryTransitions.Add(new XStateTransition
            {
                Target = otherwiseId
            });
        }
        else if (nextStateId != null)
        {
            // No otherwise, go to next state
            entryTransitions.Add(new XStateTransition
            {
                Target = $"..{nextStateId}"
            });
        }

        branchStates["entry"] = new XStateNode
        {
            Always = entryTransitions
        };

        parentStates[stateId] = new XStateNode
        {
            States = branchStates,
            Initial = "entry"
        };

        return stateId;
    }

    private string ConvertSwitchStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var switchStates = new Dictionary<string, XStateNode>();
        var entryTransitions = new List<XStateTransition>();

        if (step.Cases is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            var cases = JsonSerializer.Deserialize<Dictionary<string, List<Step>>>(jsonElement.GetRawText());

            foreach (var (caseValue, caseSteps) in cases ?? new Dictionary<string, List<Step>>())
            {
                var caseId = $"case_{caseValue}";
                var caseStates = new Dictionary<string, XStateNode>();

                ConvertStepSequence(caseSteps, caseStates, nextStateId);

                switchStates[caseId] = new XStateNode
                {
                    States = caseStates,
                    Initial = caseSteps.FirstOrDefault()?.Id ?? "empty"
                };

                entryTransitions.Add(new XStateTransition
                {
                    Target = caseId,
                    Guard = $"{step.Value}_equals_{caseValue}"
                });
            }
        }

        // Add default case
        if (step.Default != null && step.Default.Count > 0)
        {
            var defaultId = GenerateStateId("default");
            var defaultStates = new Dictionary<string, XStateNode>();

            ConvertStepSequence(step.Default, defaultStates, nextStateId);

            switchStates[defaultId] = new XStateNode
            {
                States = defaultStates,
                Initial = step.Default.FirstOrDefault()?.Id ?? "empty"
            };

            entryTransitions.Add(new XStateTransition
            {
                Target = defaultId
            });
        }

        switchStates["entry"] = new XStateNode
        {
            Always = entryTransitions
        };

        parentStates[stateId] = new XStateNode
        {
            States = switchStates,
            Initial = "entry"
        };

        return stateId;
    }

    private string ConvertWaitStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var builder = new XStateNodeBuilder();

        if (step.Duration.HasValue)
        {
            // Duration-based wait
            builder.WithAfter(step.Duration.Value, new List<XStateTransition>
            {
                new XStateTransition { Target = nextStateId ?? GenerateStateId("after_wait") }
            });
        }
        else if (!string.IsNullOrEmpty(step.Until))
        {
            // Condition-based wait
            builder.WithAlways(new XStateTransition
            {
                Target = nextStateId ?? GenerateStateId("after_wait"),
                Guard = step.Until
            });
        }

        parentStates[stateId] = builder.Build();
        return stateId;
    }

    private string ConvertConditionStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;

        parentStates[stateId] = new XStateNode
        {
            Always = new List<XStateTransition>
            {
                new XStateTransition
                {
                    Target = nextStateId ?? GenerateStateId("condition_passed"),
                    Guard = step.Expect
                }
            }
        };

        return stateId;
    }

    private string ConvertSequenceStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var sequenceStates = new Dictionary<string, XStateNode>();

        ConvertStepSequence(step.Steps ?? new List<Step>(), sequenceStates, nextStateId);

        parentStates[stateId] = new XStateNode
        {
            States = sequenceStates,
            Initial = step.Steps?.FirstOrDefault()?.Id ?? "empty"
        };

        return stateId;
    }

    private string ConvertCallStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;

        var invoke = new XStateInvoke
        {
            Src = step.Target
        };

        if (nextStateId != null)
        {
            invoke.OnDone = new List<XStateTransition>
            {
                new XStateTransition { Target = nextStateId }
            };
        }

        parentStates[stateId] = new XStateNode
        {
            Invoke = invoke
        };

        return stateId;
    }

    private string ConvertTryStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var tryStates = new Dictionary<string, XStateNode>();

        // Try block
        var tryBlockStates = new Dictionary<string, XStateNode>();
        ConvertStepSequence(step.Try ?? new List<Step>(), tryBlockStates, "try_success");

        tryBlockStates["try_success"] = new XStateNode
        {
            Always = new List<XStateTransition>
            {
                new XStateTransition
                {
                    Target = step.Finally != null ? "finally" : $"..{nextStateId ?? "done"}"
                }
            }
        };

        var tryOn = new Dictionary<string, List<XStateTransition>>
        {
            ["ERROR"] = new List<XStateTransition>
            {
                new XStateTransition { Target = "catch" }
            }
        };

        tryStates["try"] = new XStateNode
        {
            States = tryBlockStates,
            Initial = step.Try?.FirstOrDefault()?.Id ?? "empty",
            On = tryOn
        };

        // Catch block
        if (step.Catch != null)
        {
            var catchStates = new Dictionary<string, XStateNode>();
            ConvertStepSequence(step.Catch, catchStates, "catch_complete");

            catchStates["catch_complete"] = new XStateNode
            {
                Always = new List<XStateTransition>
                {
                    new XStateTransition
                    {
                        Target = step.Finally != null ? "finally" : $"..{nextStateId ?? "done"}"
                    }
                }
            };

            tryStates["catch"] = new XStateNode
            {
                States = catchStates,
                Initial = step.Catch.FirstOrDefault()?.Id ?? "empty"
            };
        }

        // Finally block
        if (step.Finally != null)
        {
            var finallyStates = new Dictionary<string, XStateNode>();
            ConvertStepSequence(step.Finally, finallyStates, $"..{nextStateId ?? "done"}");

            tryStates["finally"] = new XStateNode
            {
                States = finallyStates,
                Initial = step.Finally.FirstOrDefault()?.Id ?? "empty"
            };
        }

        parentStates[stateId] = new XStateNode
        {
            States = tryStates,
            Initial = "try"
        };

        return stateId;
    }

    private string ConvertEmitEventStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var builder = new XStateNodeBuilder()
            .WithEntry($"emitEvent_{step.Event}");

        if (nextStateId != null)
        {
            builder.WithAlways(new XStateTransition { Target = nextStateId });
        }

        parentStates[stateId] = builder.Build();
        return stateId;
    }

    private string ConvertOnEventStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var eventStates = new Dictionary<string, XStateNode>();

        // Waiting state
        var waitingOn = new Dictionary<string, List<XStateTransition>>
        {
            [step.Event!] = new List<XStateTransition>
            {
                new XStateTransition
                {
                    Target = "handling",
                    Guard = step.Filter
                }
            }
        };

        eventStates["waiting"] = new XStateNode
        {
            On = waitingOn
        };

        // Handling state
        var handlingStates = new Dictionary<string, XStateNode>();
        ConvertStepSequence(step.Steps ?? new List<Step>(), handlingStates,
            step.Once == true ? $"..{nextStateId ?? "done"}" : "waiting");

        eventStates["handling"] = new XStateNode
        {
            States = handlingStates,
            Initial = step.Steps?.FirstOrDefault()?.Id ?? "empty"
        };

        parentStates[stateId] = new XStateNode
        {
            States = eventStates,
            Initial = "waiting"
        };

        return stateId;
    }

    private string ConvertCollectMetricStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var builder = new XStateNodeBuilder()
            .WithEntry($"collectMetric_{step.Metric}");

        if (nextStateId != null)
        {
            builder.WithAlways(new XStateTransition { Target = nextStateId });
        }

        parentStates[stateId] = builder.Build();
        return stateId;
    }

    private string ConvertRaceStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var raceRegions = new Dictionary<string, XStateNode>();

        // Create a parallel region for each branch
        for (int i = 0; i < (step.Branches?.Count ?? 0); i++)
        {
            var branch = step.Branches![i];
            var branchId = $"race_{i}";
            var branchStates = new Dictionary<string, XStateNode>();

            ConvertStepSequence(branch, branchStates, "final");
            branchStates["final"] = new XStateNode { Type = "final" };

            raceRegions[branchId] = new XStateNode
            {
                States = branchStates,
                Initial = branch.FirstOrDefault()?.Id ?? "final"
            };
        }

        var builder = new XStateNodeBuilder()
            .WithType("parallel");

        foreach (var (key, value) in raceRegions)
        {
            builder.WithState(key, value);
        }

        // First to complete triggers exit
        if (nextStateId != null)
        {
            builder.WithOnDone(new XStateTransition { Target = nextStateId });
        }

        parentStates[stateId] = builder.Build();
        return stateId;
    }

    private string ConvertTransactionStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var transactionStates = new Dictionary<string, XStateNode>();

        // Transaction body
        var bodyStates = new Dictionary<string, XStateNode>();
        ConvertStepSequence(step.Steps ?? new List<Step>(), bodyStates, "commit");

        var bodyOn = new Dictionary<string, List<XStateTransition>>
        {
            ["ERROR"] = new List<XStateTransition>
            {
                new XStateTransition { Target = "rollback" }
            }
        };

        transactionStates["body"] = new XStateNode
        {
            States = bodyStates,
            Initial = step.Steps?.FirstOrDefault()?.Id ?? "empty",
            On = bodyOn
        };

        // Commit state
        transactionStates["commit"] = new XStateNode
        {
            Entry = new List<object> { "commitTransaction" },
            Always = new List<XStateTransition>
            {
                new XStateTransition { Target = $"..{nextStateId ?? "done"}" }
            }
        };

        // Rollback state
        if (step.Rollback != null)
        {
            var rollbackStates = new Dictionary<string, XStateNode>();
            ConvertStepSequence(step.Rollback, rollbackStates, $"..{nextStateId ?? "done"}");

            transactionStates["rollback"] = new XStateNode
            {
                States = rollbackStates,
                Entry = new List<object> { "rollbackTransaction" },
                Initial = step.Rollback.FirstOrDefault()?.Id ?? "empty"
            };
        }

        parentStates[stateId] = new XStateNode
        {
            States = transactionStates,
            Initial = "body",
            Entry = new List<object> { "beginTransaction" }
        };

        return stateId;
    }

    /// <summary>
    /// Convert a sequence of steps, linking them together
    /// </summary>
    public void ConvertStepSequence(List<Step> steps, Dictionary<string, XStateNode> parentStates, string? finalTarget)
    {
        if (steps.Count == 0)
        {
            parentStates["empty"] = new XStateNode
            {
                Always = finalTarget != null ? new List<XStateTransition>
                {
                    new XStateTransition { Target = finalTarget }
                } : null
            };
            return;
        }

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var nextStateId = i < steps.Count - 1
                ? steps[i + 1].Id
                : finalTarget;

            ConvertStep(step, parentStates, nextStateId);
        }
    }

    private string GenerateStateId(string prefix)
    {
        return $"{prefix}_{_stateCounter++}";
    }
}

/// <summary>
/// Context for conversion process
/// </summary>
public class ConversionContext
{
    public Dictionary<string, Station> Stations { get; set; } = new();
    public Dictionary<string, ResourceGroup> ResourceGroups { get; set; } = new();
    public Dictionary<string, EventDef> Events { get; set; } = new();
    public Dictionary<string, MetricDef> Metrics { get; set; } = new();
    public Dictionary<string, object> GlobalVars { get; set; } = new();
}
