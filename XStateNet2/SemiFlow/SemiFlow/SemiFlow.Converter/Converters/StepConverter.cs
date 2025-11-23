using SemiFlow.Converter.Models;
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
        var state = new XStateNode
        {
            Entry = new List<object> { step.Action! }
        };

        // Add timeout handling
        if (step.Timeout.HasValue)
        {
            state.After = new Dictionary<int, List<XStateTransition>>
            {
                [step.Timeout.Value] = new List<XStateTransition>
                {
                    new XStateTransition { Target = nextStateId ?? GenerateStateId("timeout") }
                }
            };
        }

        // Add transition to next state
        if (nextStateId != null)
        {
            if (step.Async == true)
            {
                // For async actions, transition immediately
                state.Always = new List<XStateTransition>
                {
                    new XStateTransition { Target = nextStateId }
                };
            }
            else
            {
                // For sync actions, wait for completion event
                var completionEvent = $"{step.Action}_DONE";
                state.On = new Dictionary<string, List<XStateTransition>>
                {
                    [completionEvent] = new List<XStateTransition>
                    {
                        new XStateTransition { Target = nextStateId }
                    }
                };
            }
        }

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertUseStationStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var state = new XStateNode();

        // Entry actions: acquire station and start using it
        var entryActions = new List<object>
        {
            $"acquireStation_{step.Role}"
        };
        state.Entry = entryActions;

        // Create nested states for station usage
        state.States = new Dictionary<string, XStateNode>
        {
            ["acquiring"] = new XStateNode
            {
                Entry = new List<object> { $"requestStation_{step.Role}" },
                On = new Dictionary<string, List<XStateTransition>>
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
                }
            },
            ["waiting"] = new XStateNode
            {
                After = new Dictionary<int, List<XStateTransition>>
                {
                    [1000] = new List<XStateTransition>
                    {
                        new XStateTransition { Target = "acquiring" }
                    }
                }
            },
            ["using"] = new XStateNode
            {
                On = new Dictionary<string, List<XStateTransition>>
                {
                    ["USAGE_COMPLETE"] = new List<XStateTransition>
                    {
                        new XStateTransition
                        {
                            Target = nextStateId != null ? $"..{nextStateId}" : null,
                            Actions = new List<object> { $"releaseStation_{step.Role}" }
                        }
                    }
                }
            }
        };

        state.Initial = "acquiring";

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertReserveStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var resourceList = string.Join(",", step.Resources ?? new List<string>());

        var state = new XStateNode
        {
            Entry = new List<object> { $"reserveResources_{resourceList}" }
        };

        if (nextStateId != null)
        {
            state.Always = new List<XStateTransition>
            {
                new XStateTransition { Target = nextStateId }
            };
        }

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertReleaseStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var resourceList = string.Join(",", step.Resources ?? new List<string>());

        var state = new XStateNode
        {
            Entry = new List<object> { $"releaseResources_{resourceList}" }
        };

        if (nextStateId != null)
        {
            state.Always = new List<XStateTransition>
            {
                new XStateTransition { Target = nextStateId }
            };
        }

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertParallelStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var parallelStates = new Dictionary<string, XStateNode>();

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

            var branchState = new XStateNode
            {
                States = branchStates,
                Initial = branch.FirstOrDefault()?.Id ?? "final"
            };

            parallelStates[branchId] = branchState;
        }

        var state = new XStateNode
        {
            Type = "parallel",
            States = parallelStates,
            OnDone = nextStateId != null ? new XStateTransition { Target = nextStateId } : null
        };

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertLoopStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var bodySteps = step.Steps ?? new List<Step>();
        var bodyStateStates = new Dictionary<string, XStateNode>();

        // Convert loop body steps
        ConvertStepSequence(bodySteps, bodyStateStates, "loop_check");

        // Add loop check state
        bodyStateStates["loop_check"] = new XStateNode
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

        var bodyState = new XStateNode
        {
            States = bodyStateStates,
            Initial = bodySteps.FirstOrDefault()?.Id ?? "loop_check"
        };

        var loopStates = new Dictionary<string, XStateNode>
        {
            ["loop_body"] = bodyState
        };

        var state = new XStateNode
        {
            States = loopStates,
            Initial = "loop_body"
        };

        parentStates[stateId] = state;
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

                var caseState = new XStateNode
                {
                    States = caseStates,
                    Initial = branchCase.Steps.FirstOrDefault()?.Id ?? "empty"
                };

                branchStates[caseId] = caseState;

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

            var otherwiseState = new XStateNode
            {
                States = otherwiseStates,
                Initial = step.Otherwise.FirstOrDefault()?.Id ?? "empty"
            };

            branchStates[otherwiseId] = otherwiseState;

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

        var entryState = new XStateNode
        {
            Always = entryTransitions
        };

        branchStates["entry"] = entryState;

        var state = new XStateNode
        {
            States = branchStates,
            Initial = "entry"
        };

        parentStates[stateId] = state;
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

                var caseState = new XStateNode
                {
                    States = caseStates,
                    Initial = caseSteps.FirstOrDefault()?.Id ?? "empty"
                };

                switchStates[caseId] = caseState;

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

            var defaultState = new XStateNode
            {
                States = defaultStates,
                Initial = step.Default.FirstOrDefault()?.Id ?? "empty"
            };

            switchStates[defaultId] = defaultState;

            entryTransitions.Add(new XStateTransition
            {
                Target = defaultId
            });
        }

        var entryState = new XStateNode
        {
            Always = entryTransitions
        };

        switchStates["entry"] = entryState;

        var state = new XStateNode
        {
            States = switchStates,
            Initial = "entry"
        };

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertWaitStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var state = new XStateNode();

        if (step.Duration.HasValue)
        {
            // Duration-based wait
            state.After = new Dictionary<int, List<XStateTransition>>
            {
                [step.Duration.Value] = new List<XStateTransition>
                {
                    new XStateTransition { Target = nextStateId ?? GenerateStateId("after_wait") }
                }
            };
        }
        else if (!string.IsNullOrEmpty(step.Until))
        {
            // Condition-based wait
            state.Always = new List<XStateTransition>
            {
                new XStateTransition
                {
                    Target = nextStateId ?? GenerateStateId("after_wait"),
                    Guard = step.Until
                }
            };
        }

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertConditionStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var state = new XStateNode
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

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertSequenceStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var sequenceStates = new Dictionary<string, XStateNode>();

        ConvertStepSequence(step.Steps ?? new List<Step>(), sequenceStates, nextStateId);

        var state = new XStateNode
        {
            States = sequenceStates,
            Initial = step.Steps?.FirstOrDefault()?.Id ?? "empty"
        };

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertCallStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var state = new XStateNode
        {
            Invoke = new XStateInvoke
            {
                Src = step.Target,
                OnDone = nextStateId != null ? new XStateTransition { Target = nextStateId } : null
            }
        };

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertTryStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var tryStepStates = new Dictionary<string, XStateNode>();

        // Try block
        var tryStateStates = new Dictionary<string, XStateNode>();
        ConvertStepSequence(step.Try ?? new List<Step>(), tryStateStates, "try_success");

        // Try success goes to finally or next
        tryStateStates["try_success"] = new XStateNode
        {
            Always = new List<XStateTransition>
            {
                new XStateTransition
                {
                    Target = step.Finally != null ? "finally" : $"..{nextStateId ?? "done"}"
                }
            }
        };

        var tryState = new XStateNode
        {
            States = tryStateStates,
            Initial = step.Try?.FirstOrDefault()?.Id ?? "empty",
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["ERROR"] = new List<XStateTransition>
                {
                    new XStateTransition { Target = "catch" }
                }
            }
        };

        tryStepStates["try"] = tryState;

        // Catch block
        if (step.Catch != null)
        {
            var catchStateStates = new Dictionary<string, XStateNode>();
            ConvertStepSequence(step.Catch, catchStateStates, "catch_complete");

            catchStateStates["catch_complete"] = new XStateNode
            {
                Always = new List<XStateTransition>
                {
                    new XStateTransition
                    {
                        Target = step.Finally != null ? "finally" : $"..{nextStateId ?? "done"}"
                    }
                }
            };

            var catchState = new XStateNode
            {
                States = catchStateStates,
                Initial = step.Catch.FirstOrDefault()?.Id ?? "empty"
            };

            tryStepStates["catch"] = catchState;
        }

        // Finally block
        if (step.Finally != null)
        {
            var finallyStateStates = new Dictionary<string, XStateNode>();
            ConvertStepSequence(step.Finally, finallyStateStates, $"..{nextStateId ?? "done"}");

            var finallyState = new XStateNode
            {
                States = finallyStateStates,
                Initial = step.Finally.FirstOrDefault()?.Id ?? "empty"
            };

            tryStepStates["finally"] = finallyState;
        }

        var state = new XStateNode
        {
            States = tryStepStates,
            Initial = "try"
        };

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertEmitEventStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var state = new XStateNode
        {
            Entry = new List<object> { $"emitEvent_{step.Event}" }
        };

        if (nextStateId != null)
        {
            state.Always = new List<XStateTransition>
            {
                new XStateTransition { Target = nextStateId }
            };
        }

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertOnEventStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var onEventStates = new Dictionary<string, XStateNode>();

        // Waiting state
        var waitingState = new XStateNode
        {
            On = new Dictionary<string, List<XStateTransition>>
            {
                [step.Event!] = new List<XStateTransition>
                {
                    new XStateTransition
                    {
                        Target = "handling",
                        Guard = step.Filter
                    }
                }
            }
        };

        onEventStates["waiting"] = waitingState;

        // Handling state
        var handlingStateStates = new Dictionary<string, XStateNode>();
        ConvertStepSequence(step.Steps ?? new List<Step>(), handlingStateStates, step.Once == true ? $"..{nextStateId ?? "done"}" : "waiting");

        var handlingState = new XStateNode
        {
            States = handlingStateStates,
            Initial = step.Steps?.FirstOrDefault()?.Id ?? "empty"
        };

        onEventStates["handling"] = handlingState;

        var state = new XStateNode
        {
            States = onEventStates,
            Initial = "waiting"
        };

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertCollectMetricStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var state = new XStateNode
        {
            Entry = new List<object> { $"collectMetric_{step.Metric}" }
        };

        if (nextStateId != null)
        {
            state.Always = new List<XStateTransition>
            {
                new XStateTransition { Target = nextStateId }
            };
        }

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertRaceStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        // Similar to parallel but with race semantics
        // First branch to complete wins
        var stateId = step.Id;
        var raceStates = new Dictionary<string, XStateNode>();

        // Create a parallel region for each branch
        for (int i = 0; i < (step.Branches?.Count ?? 0); i++)
        {
            var branch = step.Branches![i];
            var branchId = $"race_{i}";
            var branchStates = new Dictionary<string, XStateNode>();

            ConvertStepSequence(branch, branchStates, "final");
            branchStates["final"] = new XStateNode { Type = "final" };

            var branchState = new XStateNode
            {
                States = branchStates,
                Initial = branch.FirstOrDefault()?.Id ?? "final"
            };

            raceStates[branchId] = branchState;
        }

        var state = new XStateNode
        {
            Type = "parallel",
            States = raceStates,
            OnDone = nextStateId != null ? new XStateTransition { Target = nextStateId } : null
        };

        parentStates[stateId] = state;
        return stateId;
    }

    private string ConvertTransactionStep(Step step, Dictionary<string, XStateNode> parentStates, string? nextStateId)
    {
        var stateId = step.Id;
        var transactionStates = new Dictionary<string, XStateNode>();

        // Transaction body
        var bodyStateStates = new Dictionary<string, XStateNode>();
        ConvertStepSequence(step.Steps ?? new List<Step>(), bodyStateStates, "commit");

        var bodyState = new XStateNode
        {
            States = bodyStateStates,
            Initial = step.Steps?.FirstOrDefault()?.Id ?? "empty",
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["ERROR"] = new List<XStateTransition>
                {
                    new XStateTransition { Target = "rollback" }
                }
            }
        };

        transactionStates["body"] = bodyState;

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
            var rollbackStateStates = new Dictionary<string, XStateNode>();
            ConvertStepSequence(step.Rollback, rollbackStateStates, $"..{nextStateId ?? "done"}");

            var rollbackState = new XStateNode
            {
                States = rollbackStateStates,
                Initial = step.Rollback.FirstOrDefault()?.Id ?? "empty",
                Entry = new List<object> { "rollbackTransaction" }
            };

            transactionStates["rollback"] = rollbackState;
        }

        var state = new XStateNode
        {
            States = transactionStates,
            Initial = "body",
            Entry = new List<object> { "beginTransaction" }
        };

        parentStates[stateId] = state;
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
