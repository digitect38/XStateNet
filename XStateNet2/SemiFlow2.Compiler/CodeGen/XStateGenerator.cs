using SemiFlow.Compiler.Ast;
using SemiFlow.Compiler.Lexer;
using XStateNet2.Core.Engine;

namespace SemiFlow.Compiler.CodeGen;

/// <summary>
/// Generates XStateMachineScript from an SFL AST.
/// The entire SFL file becomes one parallel XState machine,
/// with each scheduler becoming a parallel region.
/// </summary>
public class XStateGenerator
{
    private readonly List<Diagnostics.Diagnostic> _diagnostics = new();
    public IReadOnlyList<Diagnostics.Diagnostic> Diagnostics => _diagnostics;

    public XStateMachineScript Generate(SflProgram program)
    {
        var script = new XStateMachineScript
        {
            Id = "sfl_system",
            Type = "parallel",
            Initial = "",
            States = new Dictionary<string, XStateNode>()
        };

        // Build context from system architecture
        var context = new Dictionary<string, object>();
        if (program.SystemArchitecture != null)
        {
            foreach (var prop in program.SystemArchitecture.Properties)
            {
                context[prop.Key] = ResolveValue(prop.Value);
            }
        }

        if (context.Count > 0)
            script.Context = context;

        // Build meta from pipeline topology blocks
        var meta = new Dictionary<string, object>();
        if (program.FlowBlock != null)
        {
            meta["flowName"] = program.FlowBlock.Name;
            meta["flow"] = program.FlowBlock.Sequence;
        }
        if (program.Crossover != null)
        {
            var crossoverDict = new Dictionary<string, object>();
            foreach (var entry in program.Crossover.Entries)
                crossoverDict[entry.StationName] = entry.Enabled;
            meta["crossover"] = crossoverDict;
        }
        if (program.Mutex != null)
        {
            var mutexList = new List<object>();
            foreach (var group in program.Mutex.Groups)
                mutexList.Add(new Dictionary<string, object>
                {
                    ["patterns"] = group.Patterns,
                    ["isGlobal"] = group.Patterns.Any(p => p.Contains('*'))
                });
            meta["mutex"] = mutexList;
        }
        if (program.Constraints != null)
        {
            var constraintsDict = new Dictionary<string, object>();
            foreach (var prop in program.Constraints.Properties)
                constraintsDict[prop.Key] = ResolveValue(prop.Value);
            meta["constraints"] = constraintsDict;
        }
        if (meta.Count > 0)
            script.Meta = meta;

        // Each scheduler becomes a parallel region
        var states = new Dictionary<string, XStateNode>();
        foreach (var scheduler in program.Schedulers)
        {
            var node = GenerateSchedulerRegion(scheduler);
            states[scheduler.Name] = node;
        }

        // Message brokers as metadata
        foreach (var broker in program.MessageBrokers)
        {
            var node = GenerateMessageBrokerRegion(broker);
            states[broker.Name] = node;
        }

        script.States = states;

        // Global event handlers from pub/sub
        var globalOn = new Dictionary<string, List<XStateTransition>>();
        foreach (var scheduler in program.Schedulers)
        {
            foreach (var sub in scheduler.Subscribes)
            {
                var transition = new XStateTransition
                {
                    Actions = new List<object> { $"handle_{sub.Alias}" }
                };
                globalOn[sub.Topic] = new List<XStateTransition> { transition };
            }
        }

        if (globalOn.Count > 0)
            script.On = globalOn;

        return script;
    }

    private XStateNode GenerateSchedulerRegion(SchedulerDef scheduler)
    {
        var node = new XStateNode();

        // Meta with layer info
        var meta = new Dictionary<string, object>();
        meta["schedulerType"] = scheduler.Type.ToString();
        if (scheduler.Layer != null)
            meta["layer"] = scheduler.Layer;
        foreach (var prop in scheduler.Properties)
        {
            meta[prop.Key] = ResolveValue(prop.Value);
        }
        node.Meta = meta;

        // Context from CONFIG block
        if (scheduler.Config != null)
        {
            var configMeta = new Dictionary<string, object>(meta);
            foreach (var item in scheduler.Config.Items)
            {
                configMeta[$"config.{item.Key}"] = ResolveValue(item.Value);
            }
            node.Meta = configMeta;
        }

        // If scheduler has embedded STATE_MACHINE, use it directly
        if (scheduler.StateMachine != null)
        {
            return GenerateFromStateMachine(scheduler, scheduler.StateMachine);
        }

        // Generate states based on scheduler type
        var states = new Dictionary<string, XStateNode>();

        switch (scheduler.Type)
        {
            case SchedulerType.Master:
                states = GenerateMasterStates(scheduler);
                break;
            case SchedulerType.Wafer:
                states = GenerateWaferStates(scheduler);
                break;
            case SchedulerType.Robot:
                states = GenerateRobotStates(scheduler);
                break;
            case SchedulerType.Station:
                states = GenerateDefaultStationStates(scheduler);
                break;
        }

        node.Initial = "idle";
        node.States = states;

        // Event handlers from subscribe statements
        var onHandlers = new Dictionary<string, List<XStateTransition>>();
        foreach (var sub in scheduler.Subscribes)
        {
            var transition = new XStateTransition
            {
                Actions = new List<object> { $"handle_{sub.Alias}" }
            };
            onHandlers[sub.Topic] = new List<XStateTransition> { transition };
        }
        if (onHandlers.Count > 0)
            node.On = onHandlers;

        return node;
    }

    private XStateNode GenerateFromStateMachine(SchedulerDef scheduler, StateMachineDef sm)
    {
        var node = new XStateNode
        {
            Initial = sm.Initial,
            States = new Dictionary<string, XStateNode>()
        };

        var meta = new Dictionary<string, object>();
        meta["schedulerType"] = scheduler.Type.ToString();
        if (scheduler.Layer != null)
            meta["layer"] = scheduler.Layer;
        node.Meta = meta;

        var states = new Dictionary<string, XStateNode>();
        foreach (var (stateName, stateDef) in sm.States)
        {
            var stateNode = new XStateNode();

            if (stateDef.On != null && stateDef.On.Count > 0)
            {
                var transitions = new Dictionary<string, List<XStateTransition>>();
                foreach (var (eventName, target) in stateDef.On)
                {
                    transitions[eventName] = new List<XStateTransition>
                    {
                        new() { Target = target }
                    };
                }
                stateNode.On = transitions;
            }

            if (stateDef.Entry != null)
                stateNode.Entry = new List<object> { stateDef.Entry };
            if (stateDef.Exit != null)
                stateNode.Exit = new List<object> { stateDef.Exit };

            states[stateName] = stateNode;
        }

        node.States = states;
        return node;
    }

    private Dictionary<string, XStateNode> GenerateMasterStates(SchedulerDef scheduler)
    {
        var states = new Dictionary<string, XStateNode>();

        // idle state
        states["idle"] = new XStateNode
        {
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["START"] = new() { new XStateTransition { Target = "scheduling" } }
            }
        };

        // scheduling state with apply rules as entry actions
        var entryActions = new List<object>();
        foreach (var schedule in scheduler.Schedules)
        {
            foreach (var rule in schedule.ApplyRules)
            {
                entryActions.Add($"applyRule_{rule}");
            }
        }

        states["scheduling"] = new XStateNode
        {
            Entry = entryActions.Count > 0 ? entryActions : null,
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["DONE"] = new() { new XStateTransition { Target = "monitoring" } }
            }
        };

        // monitoring state
        states["monitoring"] = new XStateNode
        {
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["WAFER_COMPLETE"] = new() { new XStateTransition { Target = "monitoring" } },
                ["ALL_DONE"] = new() { new XStateTransition { Target = "completed" } }
            }
        };

        // completed (final) state
        states["completed"] = new XStateNode
        {
            Type = "final"
        };

        return states;
    }

    private Dictionary<string, XStateNode> GenerateWaferStates(SchedulerDef scheduler)
    {
        var states = new Dictionary<string, XStateNode>();

        states["idle"] = new XStateNode
        {
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["ASSIGN_WAFERS"] = new() { new XStateTransition { Target = "processing" } }
            }
        };

        states["processing"] = new XStateNode
        {
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["WAFER_DONE"] = new() { new XStateTransition { Target = "processing" } },
                ["ALL_WAFERS_DONE"] = new() { new XStateTransition { Target = "completed" } },
                ["ERROR"] = new() { new XStateTransition { Target = "error" } }
            }
        };

        states["error"] = new XStateNode
        {
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["RETRY"] = new() { new XStateTransition { Target = "processing" } },
                ["ABORT"] = new() { new XStateTransition { Target = "idle" } }
            }
        };

        states["completed"] = new XStateNode
        {
            Type = "final"
        };

        return states;
    }

    private Dictionary<string, XStateNode> GenerateRobotStates(SchedulerDef scheduler)
    {
        var states = new Dictionary<string, XStateNode>();

        states["idle"] = new XStateNode
        {
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["MOVE_COMMAND"] = new() { new XStateTransition { Target = "moving" } }
            }
        };

        // Generate transaction states
        if (scheduler.Transactions.Count > 0)
        {
            states["moving"] = GenerateTransactionState(scheduler.Transactions[0]);
        }
        else
        {
            states["moving"] = new XStateNode
            {
                On = new Dictionary<string, List<XStateTransition>>
                {
                    ["ARRIVED"] = new() { new XStateTransition { Target = "idle" } },
                    ["ERROR"] = new() { new XStateTransition { Target = "error" } }
                }
            };
        }

        states["error"] = new XStateNode
        {
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["RESET"] = new() { new XStateTransition { Target = "idle" } }
            }
        };

        return states;
    }

    private XStateNode GenerateTransactionState(TransactionDef txn)
    {
        // Transaction becomes nested states: begin → execute → commit/rollback
        var node = new XStateNode
        {
            Initial = "begin",
            States = new Dictionary<string, XStateNode>
            {
                ["begin"] = new XStateNode
                {
                    Entry = new List<object> { $"beginTxn_{txn.Name}" },
                    On = new Dictionary<string, List<XStateTransition>>
                    {
                        ["TXN_STARTED"] = new() { new XStateTransition { Target = "execute" } }
                    }
                },
                ["execute"] = new XStateNode
                {
                    Entry = txn.Command != null ? new List<object> { txn.Command } : null,
                    On = new Dictionary<string, List<XStateTransition>>
                    {
                        ["SUCCESS"] = new() { new XStateTransition { Target = "commit" } },
                        ["FAILURE"] = new() { new XStateTransition { Target = "rollback" } }
                    }
                },
                ["commit"] = new XStateNode
                {
                    Entry = new List<object> { $"commitTxn_{txn.Name}" },
                    Type = "final"
                },
                ["rollback"] = new XStateNode
                {
                    Entry = new List<object> { $"rollbackTxn_{txn.Name}" },
                    On = new Dictionary<string, List<XStateTransition>>
                    {
                        ["RETRY"] = new() { new XStateTransition { Target = "begin" } }
                    }
                }
            }
        };

        // Add timeout if specified
        if (txn.Timeout is DurationValue dur)
        {
            var executeState = ((Dictionary<string, XStateNode>)node.States)["execute"];
            executeState.After = new Dictionary<int, List<XStateTransition>>
            {
                [dur.ToMilliseconds()] = new() { new XStateTransition { Target = "rollback" } }
            };
        }

        return node;
    }

    private Dictionary<string, XStateNode> GenerateDefaultStationStates(SchedulerDef scheduler)
    {
        // Default station states: idle → processing → idle + alarm
        var states = new Dictionary<string, XStateNode>();

        states["idle"] = new XStateNode
        {
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["RECEIVE_WAFER"] = new() { new XStateTransition { Target = "processing" } }
            }
        };

        var processingNode = new XStateNode
        {
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["ERROR"] = new() { new XStateTransition { Target = "alarm" } }
            }
        };

        // Add after delay if process_time is configured
        var processTime = FindConfigValue(scheduler, "process_time");
        if (processTime is DurationValue duration)
        {
            processingNode.After = new Dictionary<int, List<XStateTransition>>
            {
                [duration.ToMilliseconds()] = new() { new XStateTransition { Target = "idle" } }
            };
        }
        else
        {
            // Default: transition on DONE event
            var on = new Dictionary<string, List<XStateTransition>>(
                (Dictionary<string, List<XStateTransition>>)processingNode.On!);
            on["DONE"] = new() { new XStateTransition { Target = "idle" } };
            processingNode.On = on;
        }

        states["processing"] = processingNode;

        states["alarm"] = new XStateNode
        {
            On = new Dictionary<string, List<XStateTransition>>
            {
                ["RESET"] = new() { new XStateTransition { Target = "idle" } }
            }
        };

        return states;
    }

    private XStateNode GenerateMessageBrokerRegion(MessageBrokerDef broker)
    {
        // Message broker becomes a simple idle state with metadata
        var meta = new Dictionary<string, object>();
        foreach (var prop in broker.Properties)
        {
            meta[prop.Key] = ResolveValue(prop.Value);
        }

        return new XStateNode
        {
            Initial = "active",
            Meta = meta,
            States = new Dictionary<string, XStateNode>
            {
                ["active"] = new XStateNode
                {
                    Description = $"Message broker {broker.Name} active"
                }
            }
        };
    }

    private ValueExpr? FindConfigValue(SchedulerDef scheduler, string key)
    {
        if (scheduler.Config == null) return null;
        return scheduler.Config.Items.FirstOrDefault(i =>
            string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private object ResolveValue(ValueExpr expr) => expr switch
    {
        StringValue s => s.Value,
        IntValue i => i.Value,
        FloatValue f => f.Value,
        BoolValue b => b.Value,
        DurationValue d => $"{d.Amount}{d.Unit}",
        FrequencyValue f => $"{f.Hz}Hz",
        IdentifierValue id => id.Name,
        ArrayValue arr => arr.Elements.Select(ResolveValue).ToList(),
        ObjectValue obj => obj.Properties.ToDictionary(p => p.Key, p => ResolveValue(p.Value)),
        FormulaExpr formula => $"FORMULA({formula.Name},{string.Join(",", formula.Args.Select(ResolveValue))})",
        FunctionCallExpr fn => $"{fn.Name}({string.Join(",", fn.Args.Select(ResolveValue))})",
        _ => expr.ToString()!
    };
}
