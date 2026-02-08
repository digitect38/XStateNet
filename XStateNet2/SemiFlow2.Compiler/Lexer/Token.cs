namespace SemiFlow.Compiler.Lexer;

public enum TokenType
{
    // Literals
    StringLiteral,
    IntegerLiteral,
    FloatLiteral,
    DurationLiteral,
    FrequencyLiteral,
    BooleanLiteral,

    // Identifiers & Keywords
    Identifier,

    // Scheduler keywords
    KW_MasterScheduler,
    KW_WaferScheduler,
    KW_RobotScheduler,
    KW_Station,

    // Block keywords
    KW_Layer,
    KW_Config,
    KW_Schedule,
    KW_Verify,
    KW_Import,
    KW_SystemArchitecture,
    KW_MessageBroker,
    KW_TransactionManager,
    KW_TransactionFlow,
    KW_TransactionSchema,
    KW_PipelineSchedulingRules,
    KW_AssignedWafers,
    KW_WaferSchedulers,
    KW_StateMachine,
    KW_Rule,
    KW_Topics,

    // Pipeline topology block keywords
    KW_FlowBlock,       // "FLOW" (uppercase top-level, distinct from lowercase "flow" property keyword)
    KW_Crossover,       // "CROSSOVER"
    KW_Mutex,           // "MUTEX"
    KW_Constraints,     // "CONSTRAINTS"

    // Statement keywords
    KW_ApplyRule,
    KW_Formula,
    KW_Publish,
    KW_Subscribe,
    KW_Transaction,
    KW_To,
    KW_As,
    KW_Where,
    KW_From,

    // Modifiers
    KW_Volatile,
    KW_Persistent,

    // Control
    KW_Parallel,
    KW_Sequential,
    KW_Retry,
    KW_Timeout,
    KW_Await,

    // Layers
    KW_L1,
    KW_L2,
    KW_L3,
    KW_L4,

    // Property keywords (commonly used in SFL)
    KW_Name,
    KW_Version,
    KW_Type,
    KW_Id,
    KW_Master,
    KW_Parent,
    KW_Command,
    KW_Initial,
    KW_States,
    KW_On,
    KW_Entry,
    KW_Exit,
    KW_Capabilities,
    KW_ControlledRobots,
    KW_Communication,
    KW_Layers,
    KW_Flow,
    KW_Structure,

    // Operators & Punctuation
    Colon,
    Semicolon,
    Comma,
    Dot,
    LBrace,
    RBrace,
    LBracket,
    RBracket,
    LParen,
    RParen,
    At,
    Arrow,
    FatArrow,
    Equals,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    LessThan,
    LessEqual,
    GreaterThan,
    GreaterEqual,
    EqualEqual,
    NotEqual,
    QuestionMark,
    Pipe,
    PipeGreater,

    // Special
    Comment,
    EOF,
    Error
}

public record Token(TokenType Type, string Value, SourceLocation Location)
{
    public bool IsKeyword => Type >= TokenType.KW_MasterScheduler && Type <= TokenType.KW_Structure;
    public bool IsLiteral => Type >= TokenType.StringLiteral && Type <= TokenType.BooleanLiteral;

    public override string ToString() => $"{Type}({Value}) at {Location}";
}
