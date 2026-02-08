using SemiFlow.Compiler.Ast;
using SemiFlow.Compiler.Lexer;

namespace SemiFlow.Compiler.Parser;

public class SflParser
{
    private readonly List<Token> _tokens;
    private int _pos;
    private readonly List<ParseError> _errors = new();

    public IReadOnlyList<ParseError> Errors => _errors;

    public SflParser(List<Token> tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    // --- Helpers ---

    private Token Current => _pos < _tokens.Count ? _tokens[_pos] : _tokens[^1];
    private Token Peek(int offset = 0) =>
        _pos + offset < _tokens.Count ? _tokens[_pos + offset] : _tokens[^1];
    private bool IsAtEnd => Current.Type == TokenType.EOF;
    private SourceLocation CurrentLocation => Current.Location;

    private Token Advance()
    {
        var t = Current;
        if (!IsAtEnd) _pos++;
        return t;
    }

    private bool Check(TokenType type) => Current.Type == type;
    private bool Check(params TokenType[] types) => types.Contains(Current.Type);

    private bool Match(TokenType type)
    {
        if (Check(type))
        {
            Advance();
            return true;
        }
        return false;
    }

    private Token Expect(TokenType type, string? message = null)
    {
        if (Check(type))
            return Advance();

        var msg = message ?? $"Expected {type}, got {Current.Type} '{Current.Value}'";
        throw new ParseError(msg, CurrentLocation);
    }

    private void AddError(string message, SourceLocation? loc = null, string code = "SFL000")
    {
        _errors.Add(new ParseError(message, loc ?? CurrentLocation, code));
    }

    private void SynchronizeTo(params TokenType[] types)
    {
        while (!IsAtEnd && !types.Contains(Current.Type))
        {
            // Also stop at known top-level keywords
            if (IsTopLevelKeyword(Current.Type))
                return;
            Advance();
        }
    }

    private static bool IsTopLevelKeyword(TokenType type) => type is
        TokenType.KW_MasterScheduler or TokenType.KW_WaferScheduler or
        TokenType.KW_RobotScheduler or TokenType.KW_Station or
        TokenType.KW_SystemArchitecture or TokenType.KW_MessageBroker or
        TokenType.KW_TransactionManager or TokenType.KW_TransactionFlow or
        TokenType.KW_PipelineSchedulingRules or TokenType.KW_Rule or
        TokenType.KW_Import or TokenType.KW_Schedule or
        TokenType.KW_FlowBlock or TokenType.KW_Crossover or
        TokenType.KW_Mutex or TokenType.KW_Constraints;

    // --- Parsing Methods ---

    public SflProgram ParseProgram()
    {
        var loc = CurrentLocation;
        var imports = new List<ImportDecl>();
        var schedulers = new List<SchedulerDef>();
        var messageBrokers = new List<MessageBrokerDef>();
        var transactionManagers = new List<TransactionManagerDef>();
        var transactionFlows = new List<TransactionFlowDef>();
        var rules = new List<RuleDef>();
        SystemArchitectureDef? sysArch = null;
        PipelineRulesDef? pipelineRules = null;
        FlowBlockDef? flowBlock = null;
        CrossoverDef? crossover = null;
        MutexDef? mutex = null;
        ConstraintsDef? constraints = null;

        while (!IsAtEnd)
        {
            try
            {
                switch (Current.Type)
                {
                    case TokenType.KW_Import:
                        imports.Add(ParseImport());
                        break;
                    case TokenType.KW_SystemArchitecture:
                        sysArch = ParseSystemArchitecture();
                        break;
                    case TokenType.KW_MasterScheduler:
                        schedulers.Add(ParseScheduler(SchedulerType.Master));
                        break;
                    case TokenType.KW_WaferScheduler:
                        schedulers.Add(ParseScheduler(SchedulerType.Wafer));
                        break;
                    case TokenType.KW_RobotScheduler:
                        schedulers.Add(ParseScheduler(SchedulerType.Robot));
                        break;
                    case TokenType.KW_Station:
                        schedulers.Add(ParseScheduler(SchedulerType.Station));
                        break;
                    case TokenType.KW_MessageBroker:
                        messageBrokers.Add(ParseMessageBroker());
                        break;
                    case TokenType.KW_TransactionManager:
                        transactionManagers.Add(ParseTransactionManager());
                        break;
                    case TokenType.KW_TransactionFlow:
                        transactionFlows.Add(ParseTransactionFlow());
                        break;
                    case TokenType.KW_PipelineSchedulingRules:
                        pipelineRules = ParsePipelineRules();
                        break;
                    case TokenType.KW_Rule:
                        rules.Add(ParseRule());
                        break;
                    case TokenType.KW_Schedule:
                        // Top-level SCHEDULE (e.g., test.sfl)
                        var schedDef = ParseTopLevelSchedule();
                        schedulers.Add(schedDef);
                        break;
                    case TokenType.KW_FlowBlock:
                        flowBlock = ParseFlowBlock();
                        break;
                    case TokenType.KW_Crossover:
                        crossover = ParseCrossoverBlock();
                        break;
                    case TokenType.KW_Mutex:
                        mutex = ParseMutexBlock();
                        break;
                    case TokenType.KW_Constraints:
                        constraints = ParseConstraintsBlock();
                        break;
                    default:
                        AddError($"Unexpected token '{Current.Value}' at top level");
                        Advance();
                        break;
                }
            }
            catch (ParseError ex)
            {
                _errors.Add(ex);
                SynchronizeTo(TokenType.RBrace);
                if (Check(TokenType.RBrace)) Advance();
            }
        }

        return new SflProgram(imports, sysArch, schedulers, messageBrokers,
            transactionManagers, transactionFlows, pipelineRules, rules,
            flowBlock, crossover, mutex, constraints, loc);
    }

    private ImportDecl ParseImport()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_Import);

        // import semiflow.algorithms.cyclic_zip
        var path = new System.Text.StringBuilder();
        path.Append(Expect(TokenType.Identifier).Value);
        while (Match(TokenType.Dot))
        {
            path.Append('.');
            path.Append(Expect(TokenType.Identifier).Value);
        }

        string? alias = null;
        if (Match(TokenType.KW_As))
        {
            alias = Expect(TokenType.Identifier).Value;
        }

        return new ImportDecl(path.ToString(), alias, loc);
    }

    private SystemArchitectureDef ParseSystemArchitecture()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_SystemArchitecture);
        Expect(TokenType.LBrace);
        var props = ParseKeyValuePairs();
        Expect(TokenType.RBrace);
        return new SystemArchitectureDef(props, loc);
    }

    private SchedulerDef ParseScheduler(SchedulerType type)
    {
        var loc = CurrentLocation;
        Advance(); // scheduler keyword

        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);

        string? layer = null;
        var properties = new List<ConfigItem>();
        ConfigBlock? config = null;
        var schedules = new List<ScheduleBlock>();
        var publishes = new List<PublishStmt>();
        var subscribes = new List<SubscribeStmt>();
        var transactions = new List<TransactionDef>();
        StateMachineDef? stateMachine = null;
        AssignedWafersBlock? assignedWafers = null;
        WaferSchedulersBlock? waferSchedulers = null;
        List<string>? controlledRobots = null;

        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            try
            {
                switch (Current.Type)
                {
                    case TokenType.KW_Layer:
                        Advance();
                        Expect(TokenType.Colon);
                        layer = Current.Value;
                        Advance();
                        break;

                    case TokenType.KW_Config:
                        config = ParseConfigBlock();
                        break;

                    case TokenType.KW_Schedule:
                        schedules.Add(ParseScheduleBlock());
                        break;

                    case TokenType.KW_Publish:
                        publishes.Add(ParsePublishStmt());
                        break;

                    case TokenType.KW_Subscribe:
                        subscribes.Add(ParseSubscribeStmt());
                        break;

                    case TokenType.KW_Transaction:
                        transactions.Add(ParseTransactionDef());
                        break;

                    case TokenType.KW_StateMachine:
                        stateMachine = ParseStateMachine();
                        break;

                    case TokenType.KW_AssignedWafers:
                        assignedWafers = ParseAssignedWafers();
                        break;

                    case TokenType.KW_WaferSchedulers:
                        waferSchedulers = ParseWaferSchedulers();
                        break;

                    case TokenType.KW_ControlledRobots:
                        controlledRobots = ParseControlledRobots();
                        break;

                    default:
                        // Try to parse as property: KEY: value
                        if (IsPropertyStart())
                        {
                            properties.Add(ParseConfigItem());
                        }
                        else
                        {
                            AddError($"Unexpected token '{Current.Value}' in scheduler body");
                            Advance();
                        }
                        break;
                }
            }
            catch (ParseError ex)
            {
                _errors.Add(ex);
                // Skip to next known construct
                while (!IsAtEnd && !Check(TokenType.RBrace) &&
                       !IsSchedulerBodyKeyword(Current.Type) && !IsPropertyStart())
                    Advance();
            }
        }

        Expect(TokenType.RBrace);

        return new SchedulerDef(type, name, layer, properties, config,
            schedules, publishes, subscribes, transactions, stateMachine,
            assignedWafers, waferSchedulers, controlledRobots, loc);
    }

    private SchedulerDef ParseTopLevelSchedule()
    {
        // Handle top-level SCHEDULE (like test.sfl) by wrapping in a pseudo scheduler
        var loc = CurrentLocation;
        var schedule = ParseScheduleBlock();
        return new SchedulerDef(
            SchedulerType.Master, schedule.Name, null,
            new List<ConfigItem>(), null,
            new List<ScheduleBlock> { schedule },
            new List<PublishStmt>(), new List<SubscribeStmt>(),
            new List<TransactionDef>(), null, null, null, null, loc);
    }

    private bool IsPropertyStart()
    {
        // Check if current token + colon looks like a property
        if (Current.Type == TokenType.Identifier || Current.IsKeyword)
        {
            return Peek(1).Type == TokenType.Colon;
        }
        return false;
    }

    private static bool IsSchedulerBodyKeyword(TokenType type) => type is
        TokenType.KW_Layer or TokenType.KW_Config or TokenType.KW_Schedule or
        TokenType.KW_Publish or TokenType.KW_Subscribe or TokenType.KW_Transaction or
        TokenType.KW_StateMachine or TokenType.KW_AssignedWafers or
        TokenType.KW_WaferSchedulers or TokenType.KW_ControlledRobots;

    private ConfigBlock ParseConfigBlock()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_Config);
        Expect(TokenType.LBrace);
        var items = ParseKeyValuePairs();
        Expect(TokenType.RBrace);
        return new ConfigBlock(items, loc);
    }

    private List<ConfigItem> ParseKeyValuePairs()
    {
        var items = new List<ConfigItem>();
        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            // Skip optional commas between key-value pairs
            Match(TokenType.Comma);
            if (Check(TokenType.RBrace)) break;

            try
            {
                items.Add(ParseConfigItem());
            }
            catch (ParseError ex)
            {
                _errors.Add(ex);
                // Skip to next line/property
                while (!IsAtEnd && !Check(TokenType.RBrace) && !Check(TokenType.Comma) && !IsPropertyStart())
                    Advance();
            }
        }
        return items;
    }

    private ConfigItem ParseConfigItem()
    {
        var loc = CurrentLocation;
        var key = Current.Value;
        Advance(); // key (identifier or keyword used as key)
        Expect(TokenType.Colon);
        var value = ParseValue();
        return new ConfigItem(key, value, loc);
    }

    private ScheduleBlock ParseScheduleBlock()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_Schedule);

        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);

        var properties = new List<ConfigItem>();
        var applyRules = new List<string>();
        VerifyBlock? verify = null;

        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            try
            {
                if (Check(TokenType.KW_ApplyRule))
                {
                    applyRules.Add(ParseApplyRule());
                }
                else if (Check(TokenType.KW_Verify))
                {
                    verify = ParseVerifyBlock();
                }
                else if (IsPropertyStart())
                {
                    properties.Add(ParseConfigItem());
                }
                else
                {
                    AddError($"Unexpected token '{Current.Value}' in schedule block");
                    Advance();
                }
            }
            catch (ParseError ex)
            {
                _errors.Add(ex);
                while (!IsAtEnd && !Check(TokenType.RBrace) &&
                       Current.Type != TokenType.KW_ApplyRule &&
                       Current.Type != TokenType.KW_Verify && !IsPropertyStart())
                    Advance();
            }
        }

        Expect(TokenType.RBrace);
        return new ScheduleBlock(name, properties, applyRules, verify, loc);
    }

    private string ParseApplyRule()
    {
        Expect(TokenType.KW_ApplyRule);
        Expect(TokenType.LParen);
        var ruleId = Expect(TokenType.StringLiteral).Value;
        Expect(TokenType.RParen);
        return ruleId;
    }

    private VerifyBlock ParseVerifyBlock()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_Verify);
        Expect(TokenType.LBrace);

        var constraints = new List<Constraint>();
        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            try
            {
                // constraint: "expression"
                var key = Current.Value;
                Advance();
                Expect(TokenType.Colon);
                var value = Expect(TokenType.StringLiteral).Value;
                constraints.Add(new Constraint(value, CurrentLocation));
            }
            catch (ParseError ex)
            {
                _errors.Add(ex);
                while (!IsAtEnd && !Check(TokenType.RBrace) && !IsPropertyStart())
                    Advance();
            }
        }

        Expect(TokenType.RBrace);
        return new VerifyBlock(constraints, loc);
    }

    private PublishStmt ParsePublishStmt()
    {
        // publish status to "wsc/001/status" @1;
        // publish position to "rsc/efem/position" @0, volatile;
        // publish state to "station/cmp01/state" @2, persistent;
        var loc = CurrentLocation;
        Expect(TokenType.KW_Publish);

        var msgType = Expect(TokenType.Identifier).Value;
        Expect(TokenType.KW_To);

        var topic = Expect(TokenType.StringLiteral).Value;

        int qos = 0;
        if (Match(TokenType.At))
        {
            qos = int.Parse(Expect(TokenType.IntegerLiteral).Value);
        }

        string? modifier = null;
        if (Match(TokenType.Comma))
        {
            if (Check(TokenType.KW_Volatile))
            {
                modifier = "volatile";
                Advance();
            }
            else if (Check(TokenType.KW_Persistent))
            {
                modifier = "persistent";
                Advance();
            }
        }

        Match(TokenType.Semicolon);
        return new PublishStmt(msgType, topic, qos, modifier, loc);
    }

    private SubscribeStmt ParseSubscribeStmt()
    {
        // subscribe to "msc/+/command" as msc_commands @2;
        var loc = CurrentLocation;
        Expect(TokenType.KW_Subscribe);
        Expect(TokenType.KW_To);

        var topic = Expect(TokenType.StringLiteral).Value;

        string alias = "";
        if (Match(TokenType.KW_As))
        {
            alias = Expect(TokenType.Identifier).Value;
        }

        int qos = 0;
        if (Match(TokenType.At))
        {
            qos = int.Parse(Expect(TokenType.IntegerLiteral).Value);
        }

        string? filter = null;
        if (Match(TokenType.KW_Where))
        {
            filter = Expect(TokenType.StringLiteral).Value;
        }

        Match(TokenType.Semicolon);
        return new SubscribeStmt(topic, alias, qos, filter, loc);
    }

    private TransactionDef ParseTransactionDef()
    {
        // transaction MOVE_WAFER { parent: TXN_MSC_001, command: move(...), timeout: 30s, retry: ... }
        var loc = CurrentLocation;
        Expect(TokenType.KW_Transaction);

        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);

        string? parent = null;
        string? command = null;
        ValueExpr? timeout = null;
        string? retry = null;

        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            try
            {
                var key = Current.Value;
                Advance();
                Expect(TokenType.Colon);

                switch (key)
                {
                    case "parent":
                        parent = ParseRawValue();
                        break;
                    case "command":
                        command = ParseRawValue();
                        break;
                    case "timeout":
                        timeout = ParseValue();
                        break;
                    case "retry":
                        retry = ParseRawValue();
                        break;
                    default:
                        ParseValue(); // consume value
                        break;
                }
            }
            catch (ParseError ex)
            {
                _errors.Add(ex);
                while (!IsAtEnd && !Check(TokenType.RBrace) && !IsPropertyStart())
                    Advance();
            }
        }

        Expect(TokenType.RBrace);
        return new TransactionDef(name, parent, command, timeout, retry, loc);
    }

    /// <summary>Reads tokens until next property or closing brace, returning raw text.</summary>
    private string ParseRawValue()
    {
        var sb = new System.Text.StringBuilder();
        int depth = 0;

        while (!IsAtEnd)
        {
            if (Current.Type == TokenType.LParen) depth++;
            if (Current.Type == TokenType.RParen) depth--;

            if (depth < 0) break;
            if (depth == 0 && (IsPropertyStart() || Check(TokenType.RBrace)))
                break;

            sb.Append(Current.Value);
            Advance();

            if (depth == 0 && sb.Length > 0)
                break;
        }

        return sb.ToString().Trim();
    }

    private StateMachineDef ParseStateMachine()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_StateMachine);
        Expect(TokenType.LBrace);

        string initial = "";
        var states = new Dictionary<string, StateDef>();

        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            try
            {
                var key = Current.Value;
                Advance();
                Expect(TokenType.Colon);

                if (key == "initial")
                {
                    initial = ParseStringOrIdentifier();
                }
                else if (key == "states")
                {
                    states = ParseStatesBlock();
                }
                else
                {
                    ParseValue(); // skip unknown
                }
            }
            catch (ParseError ex)
            {
                _errors.Add(ex);
                while (!IsAtEnd && !Check(TokenType.RBrace) && !IsPropertyStart())
                    Advance();
            }
        }

        Expect(TokenType.RBrace);
        return new StateMachineDef(initial, states, loc);
    }

    private Dictionary<string, StateDef> ParseStatesBlock()
    {
        Expect(TokenType.LBrace);
        var states = new Dictionary<string, StateDef>();

        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            try
            {
                var stateName = Current.Value;
                Advance();
                Expect(TokenType.Colon);

                Expect(TokenType.LBrace);
                Dictionary<string, string>? on = null;
                string? entry = null;
                string? exit = null;
                var sloc = CurrentLocation;

                while (!Check(TokenType.RBrace) && !IsAtEnd)
                {
                    var prop = Current.Value;
                    Advance();
                    Expect(TokenType.Colon);

                    if (prop == "on")
                    {
                        on = ParseOnBlock();
                    }
                    else if (prop == "entry")
                    {
                        entry = ParseStringOrIdentifier();
                    }
                    else if (prop == "exit")
                    {
                        exit = ParseStringOrIdentifier();
                    }
                    else
                    {
                        ParseValue(); // skip
                    }
                }

                Expect(TokenType.RBrace);
                states[stateName] = new StateDef(on, entry, exit, sloc);
            }
            catch (ParseError ex)
            {
                _errors.Add(ex);
                while (!IsAtEnd && !Check(TokenType.RBrace))
                    Advance();
                if (Check(TokenType.RBrace)) Advance();
            }
        }

        Expect(TokenType.RBrace);
        return states;
    }

    private Dictionary<string, string> ParseOnBlock()
    {
        Expect(TokenType.LBrace);
        var transitions = new Dictionary<string, string>();

        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            var eventName = Current.Value;
            Advance();
            Expect(TokenType.Colon);
            var target = ParseStringOrIdentifier();
            transitions[eventName] = target;
        }

        Expect(TokenType.RBrace);
        return transitions;
    }

    private AssignedWafersBlock ParseAssignedWafers()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_AssignedWafers);
        Expect(TokenType.LBrace);
        var props = ParseKeyValuePairs();
        Expect(TokenType.RBrace);
        return new AssignedWafersBlock(props, loc);
    }

    private WaferSchedulersBlock ParseWaferSchedulers()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_WaferSchedulers);
        Expect(TokenType.LBrace);

        var entries = new List<WaferSchedulerEntry>();
        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            try
            {
                var entryLoc = CurrentLocation;
                var name = Current.Value;
                Advance();
                Expect(TokenType.Colon);
                Expect(TokenType.LBrace);
                var props = ParseKeyValuePairs();
                Expect(TokenType.RBrace);
                entries.Add(new WaferSchedulerEntry(name, props, entryLoc));
            }
            catch (ParseError ex)
            {
                _errors.Add(ex);
                while (!IsAtEnd && !Check(TokenType.RBrace))
                    Advance();
                if (Check(TokenType.RBrace)) Advance();
            }
        }

        Expect(TokenType.RBrace);
        return new WaferSchedulersBlock(entries, loc);
    }

    private List<string> ParseControlledRobots()
    {
        Expect(TokenType.KW_ControlledRobots);
        Expect(TokenType.Colon);
        return ParseStringArray();
    }

    private MessageBrokerDef ParseMessageBroker()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_MessageBroker);
        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);

        var props = new List<ConfigItem>();
        TopicsBlock? topics = null;

        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            if (Check(TokenType.KW_Topics))
            {
                topics = ParseTopicsBlock();
            }
            else if (IsPropertyStart())
            {
                props.Add(ParseConfigItem());
            }
            else
            {
                AddError($"Unexpected token '{Current.Value}' in message broker");
                Advance();
            }
        }

        Expect(TokenType.RBrace);
        return new MessageBrokerDef(name, props, topics, loc);
    }

    private TopicsBlock ParseTopicsBlock()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_Topics);
        Expect(TokenType.LBrace);

        var topicDefs = new List<TopicDef>();
        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            try
            {
                var tLoc = CurrentLocation;
                var topicName = Current.Value;
                Advance();
                Expect(TokenType.LBrace);
                var props = ParseKeyValuePairs();
                Expect(TokenType.RBrace);
                topicDefs.Add(new TopicDef(topicName, props, tLoc));
            }
            catch (ParseError ex)
            {
                _errors.Add(ex);
                while (!IsAtEnd && !Check(TokenType.RBrace))
                    Advance();
                if (Check(TokenType.RBrace)) Advance();
            }
        }

        Expect(TokenType.RBrace);
        return new TopicsBlock(topicDefs, loc);
    }

    private TransactionManagerDef ParseTransactionManager()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_TransactionManager);
        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);

        var props = new List<ConfigItem>();
        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            if (IsPropertyStart())
            {
                props.Add(ParseConfigItem());
            }
            else if (Check(TokenType.KW_TransactionSchema))
            {
                // Parse TRANSACTION_SCHEMA as a nested object
                var schemaLoc = CurrentLocation;
                Advance();
                Expect(TokenType.LBrace);
                var schemaProps = ParseKeyValuePairs();
                Expect(TokenType.RBrace);
                props.Add(new ConfigItem("TRANSACTION_SCHEMA",
                    new ObjectValue(schemaProps, schemaLoc), schemaLoc));
            }
            else
            {
                AddError($"Unexpected token '{Current.Value}' in transaction manager");
                Advance();
            }
        }

        Expect(TokenType.RBrace);
        return new TransactionManagerDef(name, props, loc);
    }

    private TransactionFlowDef ParseTransactionFlow()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_TransactionFlow);
        Expect(TokenType.LBrace);
        var props = ParseKeyValuePairs();
        Expect(TokenType.RBrace);
        return new TransactionFlowDef(props, loc);
    }

    private PipelineRulesDef ParsePipelineRules()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_PipelineSchedulingRules);
        Expect(TokenType.LBrace);

        var rules = new List<RuleDef>();
        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            if (Check(TokenType.KW_Rule))
            {
                rules.Add(ParseRule());
            }
            else
            {
                AddError($"Expected RULE, got '{Current.Value}'");
                Advance();
            }
        }

        Expect(TokenType.RBrace);
        return new PipelineRulesDef(rules, loc);
    }

    private RuleDef ParseRule()
    {
        var loc = CurrentLocation;
        Expect(TokenType.KW_Rule);
        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);
        var props = ParseKeyValuePairs();
        Expect(TokenType.RBrace);
        return new RuleDef(name, props, loc);
    }

    // --- Pipeline Topology Blocks ---

    private FlowBlockDef ParseFlowBlock()
    {
        // FLOW PRODUCTION_LINE { SRC -> R1 -> POL -> ... -> DST }
        var loc = CurrentLocation;
        Expect(TokenType.KW_FlowBlock);

        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);

        var sequence = new List<string>();
        // First element
        sequence.Add(ParseFlowElement());

        // Subsequent elements: -> ELEMENT
        while (Match(TokenType.Arrow))
        {
            sequence.Add(ParseFlowElement());
        }

        Expect(TokenType.RBrace);
        return new FlowBlockDef(name, sequence, loc);
    }

    private string ParseFlowElement()
    {
        // A flow element can be an identifier or a keyword used as name (e.g., L1)
        var token = Current;
        if (token.Type == TokenType.Identifier || token.IsKeyword)
        {
            Advance();
            return token.Value;
        }
        throw new ParseError($"Expected flow element name, got {token.Type} '{token.Value}'", CurrentLocation);
    }

    private CrossoverDef ParseCrossoverBlock()
    {
        // CROSSOVER { POL: disabled  CLN: enabled  ... }
        var loc = CurrentLocation;
        Expect(TokenType.KW_Crossover);
        Expect(TokenType.LBrace);

        var entries = new List<CrossoverEntry>();
        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            try
            {
                var entryLoc = CurrentLocation;
                var stationName = Current.Value;
                Advance();
                Expect(TokenType.Colon);

                var enabledStr = Current.Value;
                Advance();
                bool enabled = string.Equals(enabledStr, "enabled", StringComparison.OrdinalIgnoreCase);
                entries.Add(new CrossoverEntry(stationName, enabled, entryLoc));
            }
            catch (ParseError ex)
            {
                _errors.Add(ex);
                while (!IsAtEnd && !Check(TokenType.RBrace) && !IsPropertyStart())
                    Advance();
            }
        }

        Expect(TokenType.RBrace);
        return new CrossoverDef(entries, loc);
    }

    private MutexDef ParseMutexBlock()
    {
        // MUTEX { group: L*.R1  group: L1.R2, L1.R3 }
        var loc = CurrentLocation;
        Expect(TokenType.KW_Mutex);
        Expect(TokenType.LBrace);

        var groups = new List<MutexGroup>();
        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            try
            {
                var groupLoc = CurrentLocation;
                // Expect "group" keyword (as identifier)
                var key = Current.Value;
                if (!string.Equals(key, "group", StringComparison.OrdinalIgnoreCase))
                {
                    AddError($"Expected 'group' in MUTEX block, got '{key}'");
                    Advance();
                    continue;
                }
                Advance();
                Expect(TokenType.Colon);

                var patterns = new List<string>();
                patterns.Add(ParseMutexPattern());

                while (Match(TokenType.Comma))
                {
                    patterns.Add(ParseMutexPattern());
                }

                groups.Add(new MutexGroup(patterns, groupLoc));
            }
            catch (ParseError ex)
            {
                _errors.Add(ex);
                while (!IsAtEnd && !Check(TokenType.RBrace) && !IsPropertyStart())
                    Advance();
            }
        }

        Expect(TokenType.RBrace);
        return new MutexDef(groups, loc);
    }

    private string ParseMutexPattern()
    {
        // Pattern: (L1|L*|*) . (R1|R*|*)
        // Tokens may be: KW_L1/Identifier("L")+Star/Star, then Dot, then Identifier/Star
        var sb = new System.Text.StringBuilder();

        // Left side
        if (Check(TokenType.Star))
        {
            sb.Append('*');
            Advance();
        }
        else
        {
            // Could be KW_L1..KW_L4 or Identifier possibly followed by Star
            var token = Current;
            if (token.Type is TokenType.KW_L1 or TokenType.KW_L2 or TokenType.KW_L3 or TokenType.KW_L4)
            {
                sb.Append(token.Value);
                Advance();
            }
            else if (token.Type == TokenType.Identifier || token.IsKeyword)
            {
                sb.Append(token.Value);
                Advance();
                if (Check(TokenType.Star))
                {
                    sb.Append('*');
                    Advance();
                }
            }
            else
            {
                throw new ParseError($"Expected mutex pattern element, got {token.Type} '{token.Value}'", CurrentLocation);
            }
        }

        Expect(TokenType.Dot);
        sb.Append('.');

        // Right side
        if (Check(TokenType.Star))
        {
            sb.Append('*');
            Advance();
        }
        else
        {
            var token = Current;
            if (token.Type == TokenType.Identifier || token.IsKeyword)
            {
                sb.Append(token.Value);
                Advance();
                if (Check(TokenType.Star))
                {
                    sb.Append('*');
                    Advance();
                }
            }
            else
            {
                throw new ParseError($"Expected mutex pattern element, got {token.Type} '{token.Value}'", CurrentLocation);
            }
        }

        return sb.ToString();
    }

    private ConstraintsDef ParseConstraintsBlock()
    {
        // CONSTRAINTS { no_wait: [R1, R2]  max_wip: 10  priority: "FIFO" }
        var loc = CurrentLocation;
        Expect(TokenType.KW_Constraints);
        Expect(TokenType.LBrace);
        var props = ParseKeyValuePairs();
        Expect(TokenType.RBrace);
        return new ConstraintsDef(props, loc);
    }

    // --- Value Parsing ---

    private ValueExpr ParseValue()
    {
        var loc = CurrentLocation;

        switch (Current.Type)
        {
            case TokenType.StringLiteral:
                var sv = Current.Value;
                Advance();
                return new StringValue(sv, loc);

            case TokenType.IntegerLiteral:
                var iv = int.Parse(Current.Value);
                Advance();
                return new IntValue(iv, loc);

            case TokenType.FloatLiteral:
                var fv = double.Parse(Current.Value, System.Globalization.CultureInfo.InvariantCulture);
                Advance();
                return new FloatValue(fv, loc);

            case TokenType.DurationLiteral:
                return ParseDuration(loc);

            case TokenType.FrequencyLiteral:
                return ParseFrequency(loc);

            case TokenType.BooleanLiteral:
                var bv = Current.Value == "true";
                Advance();
                return new BoolValue(bv, loc);

            case TokenType.LBracket:
                return ParseArray(loc);

            case TokenType.LBrace:
                return ParseObject(loc);

            case TokenType.KW_Formula:
                return ParseFormula(loc);

            default:
                // Identifier, keyword-as-value, or function call
                if (Current.Type == TokenType.Identifier || Current.IsKeyword)
                {
                    var name = Current.Value;
                    Advance();

                    // Function call: name(...)
                    if (Check(TokenType.LParen))
                    {
                        return ParseFunctionCall(name, loc);
                    }

                    return new IdentifierValue(name, loc);
                }

                AddError($"Expected value, got '{Current.Value}'");
                Advance();
                return new StringValue("", loc);
        }
    }

    private DurationValue ParseDuration(SourceLocation loc)
    {
        var raw = Current.Value;
        Advance();

        string unit;
        string numPart;
        if (raw.EndsWith("ms"))
        {
            unit = "ms";
            numPart = raw[..^2];
        }
        else
        {
            unit = raw[^1..];
            numPart = raw[..^1];
        }

        var amount = double.Parse(numPart, System.Globalization.CultureInfo.InvariantCulture);
        return new DurationValue(amount, unit, loc);
    }

    private FrequencyValue ParseFrequency(SourceLocation loc)
    {
        var raw = Current.Value;
        Advance();
        var hz = int.Parse(raw[..^2]); // strip "Hz"
        return new FrequencyValue(hz, loc);
    }

    private ArrayValue ParseArray(SourceLocation loc)
    {
        Expect(TokenType.LBracket);
        var elements = new List<ValueExpr>();

        while (!Check(TokenType.RBracket) && !IsAtEnd)
        {
            elements.Add(ParseValue());
            if (!Check(TokenType.RBracket))
                Match(TokenType.Comma);
        }

        Expect(TokenType.RBracket);
        return new ArrayValue(elements, loc);
    }

    private ObjectValue ParseObject(SourceLocation loc)
    {
        Expect(TokenType.LBrace);
        var props = ParseKeyValuePairs();
        Expect(TokenType.RBrace);
        return new ObjectValue(props, loc);
    }

    private FormulaExpr ParseFormula(SourceLocation loc)
    {
        Expect(TokenType.KW_Formula);
        Expect(TokenType.LParen);

        var name = Current.Value;
        Advance();

        var args = new List<ValueExpr>();
        while (Match(TokenType.Comma))
        {
            args.Add(ParseValue());
        }

        Expect(TokenType.RParen);
        return new FormulaExpr(name, args, loc);
    }

    private FunctionCallExpr ParseFunctionCall(string name, SourceLocation loc)
    {
        Expect(TokenType.LParen);
        var args = new List<ValueExpr>();

        if (!Check(TokenType.RParen))
        {
            args.Add(ParseValue());
            while (Match(TokenType.Comma))
            {
                args.Add(ParseValue());
            }
        }

        Expect(TokenType.RParen);
        return new FunctionCallExpr(name, args, loc);
    }

    private string ParseStringOrIdentifier()
    {
        if (Check(TokenType.StringLiteral))
        {
            var v = Current.Value;
            Advance();
            return v;
        }
        var id = Current.Value;
        Advance();
        return id;
    }

    private List<string> ParseStringArray()
    {
        Expect(TokenType.LBracket);
        var items = new List<string>();

        while (!Check(TokenType.RBracket) && !IsAtEnd)
        {
            items.Add(ParseStringOrIdentifier());
            if (!Check(TokenType.RBracket))
                Match(TokenType.Comma);
        }

        Expect(TokenType.RBracket);
        return items;
    }
}
