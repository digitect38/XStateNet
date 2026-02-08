using SemiFlow.Compiler.Lexer;

namespace SemiFlow.Compiler.Ast;

// Base node
public abstract record AstNode(SourceLocation Location);

// Top-level program
public record SflProgram(
    List<ImportDecl> Imports,
    SystemArchitectureDef? SystemArchitecture,
    List<SchedulerDef> Schedulers,
    List<MessageBrokerDef> MessageBrokers,
    List<TransactionManagerDef> TransactionManagers,
    List<TransactionFlowDef> TransactionFlows,
    PipelineRulesDef? PipelineRules,
    List<RuleDef> Rules,
    FlowBlockDef? FlowBlock,
    CrossoverDef? Crossover,
    MutexDef? Mutex,
    ConstraintsDef? Constraints,
    SourceLocation Location
) : AstNode(Location);

// import semiflow.algorithms.cyclic_zip
public record ImportDecl(
    string ModulePath,
    string? Alias,
    SourceLocation Location
) : AstNode(Location);

// SYSTEM_ARCHITECTURE { ... }
public record SystemArchitectureDef(
    List<ConfigItem> Properties,
    SourceLocation Location
) : AstNode(Location);

// Scheduler types
public enum SchedulerType { Master, Wafer, Robot, Station }

// MASTER_SCHEDULER MSC_001 { ... }, etc.
public record SchedulerDef(
    SchedulerType Type,
    string Name,
    string? Layer,
    List<ConfigItem> Properties,
    ConfigBlock? Config,
    List<ScheduleBlock> Schedules,
    List<PublishStmt> Publishes,
    List<SubscribeStmt> Subscribes,
    List<TransactionDef> Transactions,
    StateMachineDef? StateMachine,
    AssignedWafersBlock? AssignedWafers,
    WaferSchedulersBlock? WaferSchedulers,
    List<string>? ControlledRobots,
    SourceLocation Location
) : AstNode(Location);

// CONFIG { ... }
public record ConfigBlock(
    List<ConfigItem> Items,
    SourceLocation Location
) : AstNode(Location);

// key: value
public record ConfigItem(
    string Key,
    ValueExpr Value,
    SourceLocation Location
) : AstNode(Location);

// SCHEDULE name { ... }
public record ScheduleBlock(
    string Name,
    List<ConfigItem> Properties,
    List<string> ApplyRules,
    VerifyBlock? Verify,
    SourceLocation Location
) : AstNode(Location);

// VERIFY { ... }
public record VerifyBlock(
    List<Constraint> Constraints,
    SourceLocation Location
) : AstNode(Location);

// constraint: "..."
public record Constraint(
    string Expression,
    SourceLocation Location
) : AstNode(Location);

// publish status to "wsc/001/status" @1;
public record PublishStmt(
    string MessageType,
    string Topic,
    int Qos,
    string? Modifier,
    SourceLocation Location
) : AstNode(Location);

// subscribe to "msc/+/command" as msc_commands @2;
public record SubscribeStmt(
    string Topic,
    string Alias,
    int Qos,
    string? Filter,
    SourceLocation Location
) : AstNode(Location);

// transaction MOVE_WAFER { ... }
public record TransactionDef(
    string Name,
    string? Parent,
    string? Command,
    ValueExpr? Timeout,
    string? Retry,
    SourceLocation Location
) : AstNode(Location);

// STATE_MACHINE { ... }
public record StateMachineDef(
    string Initial,
    Dictionary<string, StateDef> States,
    SourceLocation Location
) : AstNode(Location);

// State definition within STATE_MACHINE
public record StateDef(
    Dictionary<string, string>? On,
    string? Entry,
    string? Exit,
    SourceLocation Location
) : AstNode(Location);

// ASSIGNED_WAFERS { ... }
public record AssignedWafersBlock(
    List<ConfigItem> Properties,
    SourceLocation Location
) : AstNode(Location);

// WAFER_SCHEDULERS { ... }
public record WaferSchedulersBlock(
    List<WaferSchedulerEntry> Entries,
    SourceLocation Location
) : AstNode(Location);

// WSC_001: { priority: 1, wafers: [...] }
public record WaferSchedulerEntry(
    string Name,
    List<ConfigItem> Properties,
    SourceLocation Location
) : AstNode(Location);

// MESSAGE_BROKER name { ... }
public record MessageBrokerDef(
    string Name,
    List<ConfigItem> Properties,
    TopicsBlock? Topics,
    SourceLocation Location
) : AstNode(Location);

// TOPICS { ... }
public record TopicsBlock(
    List<TopicDef> Topics,
    SourceLocation Location
) : AstNode(Location);

// topic definition within TOPICS
public record TopicDef(
    string Name,
    List<ConfigItem> Properties,
    SourceLocation Location
) : AstNode(Location);

// TRANSACTION_MANAGER name { ... }
public record TransactionManagerDef(
    string Name,
    List<ConfigItem> Properties,
    SourceLocation Location
) : AstNode(Location);

// TRANSACTION_FLOW { ... }
public record TransactionFlowDef(
    List<ConfigItem> Properties,
    SourceLocation Location
) : AstNode(Location);

// PIPELINE_SCHEDULING_RULES { ... }
public record PipelineRulesDef(
    List<RuleDef> Rules,
    SourceLocation Location
) : AstNode(Location);

// RULE name { ... }
public record RuleDef(
    string Name,
    List<ConfigItem> Properties,
    SourceLocation Location
) : AstNode(Location);

// ---- Pipeline Topology Blocks ----

// FLOW PRODUCTION_LINE { SRC -> R1 -> POL -> ... -> DST }
public record FlowBlockDef(
    string Name,
    List<string> Sequence,
    SourceLocation Location
) : AstNode(Location);

// CROSSOVER { POL: disabled  CLN: enabled ... }
public record CrossoverDef(
    List<CrossoverEntry> Entries,
    SourceLocation Location
) : AstNode(Location);

public record CrossoverEntry(
    string StationName,
    bool Enabled,
    SourceLocation Location
) : AstNode(Location);

// MUTEX { group: L*.R1  group: L1.R2, L1.R3 }
public record MutexDef(
    List<MutexGroup> Groups,
    SourceLocation Location
) : AstNode(Location);

public record MutexGroup(
    List<string> Patterns,
    SourceLocation Location
) : AstNode(Location);

// CONSTRAINTS { no_wait: [R1, R2]  max_wip: 10  priority: "FIFO" }
public record ConstraintsDef(
    List<ConfigItem> Properties,
    SourceLocation Location
) : AstNode(Location);

// ---- Value Expressions ----

public abstract record ValueExpr(SourceLocation Location) : AstNode(Location);

public record StringValue(string Value, SourceLocation Location) : ValueExpr(Location);
public record IntValue(int Value, SourceLocation Location) : ValueExpr(Location);
public record FloatValue(double Value, SourceLocation Location) : ValueExpr(Location);

public record DurationValue(double Amount, string Unit, SourceLocation Location) : ValueExpr(Location)
{
    public int ToMilliseconds() => Unit switch
    {
        "ms" => (int)Amount,
        "s" => (int)(Amount * 1000),
        "m" => (int)(Amount * 60_000),
        "h" => (int)(Amount * 3_600_000),
        _ => (int)Amount
    };
}

public record FrequencyValue(int Hz, SourceLocation Location) : ValueExpr(Location);
public record BoolValue(bool Value, SourceLocation Location) : ValueExpr(Location);
public record IdentifierValue(string Name, SourceLocation Location) : ValueExpr(Location);
public record ArrayValue(List<ValueExpr> Elements, SourceLocation Location) : ValueExpr(Location);
public record ObjectValue(List<ConfigItem> Properties, SourceLocation Location) : ValueExpr(Location);
public record FormulaExpr(string Name, List<ValueExpr> Args, SourceLocation Location) : ValueExpr(Location);
public record FunctionCallExpr(string Name, List<ValueExpr> Args, SourceLocation Location) : ValueExpr(Location);
