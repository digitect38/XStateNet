namespace SemiFlow.Compiler.Lexer;

public class SflLexer
{
    private readonly string _source;
    private readonly string _fileName;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        // Scheduler keywords
        ["MASTER_SCHEDULER"] = TokenType.KW_MasterScheduler,
        ["WAFER_SCHEDULER"] = TokenType.KW_WaferScheduler,
        ["ROBOT_SCHEDULER"] = TokenType.KW_RobotScheduler,
        ["STATION"] = TokenType.KW_Station,

        // Block keywords
        ["LAYER"] = TokenType.KW_Layer,
        ["CONFIG"] = TokenType.KW_Config,
        ["SCHEDULE"] = TokenType.KW_Schedule,
        ["VERIFY"] = TokenType.KW_Verify,
        ["import"] = TokenType.KW_Import,
        ["SYSTEM_ARCHITECTURE"] = TokenType.KW_SystemArchitecture,
        ["MESSAGE_BROKER"] = TokenType.KW_MessageBroker,
        ["TRANSACTION_MANAGER"] = TokenType.KW_TransactionManager,
        ["TRANSACTION_FLOW"] = TokenType.KW_TransactionFlow,
        ["TRANSACTION_SCHEMA"] = TokenType.KW_TransactionSchema,
        ["PIPELINE_SCHEDULING_RULES"] = TokenType.KW_PipelineSchedulingRules,
        ["ASSIGNED_WAFERS"] = TokenType.KW_AssignedWafers,
        ["WAFER_SCHEDULERS"] = TokenType.KW_WaferSchedulers,
        ["STATE_MACHINE"] = TokenType.KW_StateMachine,
        ["RULE"] = TokenType.KW_Rule,
        ["TOPICS"] = TokenType.KW_Topics,

        // Pipeline topology block keywords
        ["FLOW"] = TokenType.KW_FlowBlock,
        ["CROSSOVER"] = TokenType.KW_Crossover,
        ["MUTEX"] = TokenType.KW_Mutex,
        ["CONSTRAINTS"] = TokenType.KW_Constraints,

        // Statement keywords
        ["APPLY_RULE"] = TokenType.KW_ApplyRule,
        ["FORMULA"] = TokenType.KW_Formula,
        ["publish"] = TokenType.KW_Publish,
        ["subscribe"] = TokenType.KW_Subscribe,
        ["transaction"] = TokenType.KW_Transaction,
        ["to"] = TokenType.KW_To,
        ["as"] = TokenType.KW_As,
        ["where"] = TokenType.KW_Where,
        ["from"] = TokenType.KW_From,

        // Modifiers
        ["volatile"] = TokenType.KW_Volatile,
        ["persistent"] = TokenType.KW_Persistent,

        // Control
        ["parallel"] = TokenType.KW_Parallel,
        ["sequential"] = TokenType.KW_Sequential,
        ["retry"] = TokenType.KW_Retry,
        ["timeout"] = TokenType.KW_Timeout,
        ["await"] = TokenType.KW_Await,

        // Layers
        ["L1"] = TokenType.KW_L1,
        ["L2"] = TokenType.KW_L2,
        ["L3"] = TokenType.KW_L3,
        ["L4"] = TokenType.KW_L4,

        // Property keywords
        ["NAME"] = TokenType.KW_Name,
        ["VERSION"] = TokenType.KW_Version,
        ["TYPE"] = TokenType.KW_Type,
        ["ID"] = TokenType.KW_Id,
        ["MASTER"] = TokenType.KW_Master,
        ["parent"] = TokenType.KW_Parent,
        ["command"] = TokenType.KW_Command,
        ["initial"] = TokenType.KW_Initial,
        ["states"] = TokenType.KW_States,
        ["on"] = TokenType.KW_On,
        ["entry"] = TokenType.KW_Entry,
        ["exit"] = TokenType.KW_Exit,
        ["CAPABILITIES"] = TokenType.KW_Capabilities,
        ["CONTROLLED_ROBOTS"] = TokenType.KW_ControlledRobots,
        ["COMMUNICATION"] = TokenType.KW_Communication,
        ["LAYERS"] = TokenType.KW_Layers,
        ["flow"] = TokenType.KW_Flow,
        ["structure"] = TokenType.KW_Structure,

        // Boolean literals
        ["true"] = TokenType.BooleanLiteral,
        ["false"] = TokenType.BooleanLiteral,
    };

    public SflLexer(string source, string? fileName = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _fileName = fileName ?? "";
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_pos < _source.Length)
        {
            SkipWhitespace();
            if (_pos >= _source.Length) break;

            var token = NextToken();
            if (token.Type != TokenType.Comment)
                tokens.Add(token);
        }

        tokens.Add(new Token(TokenType.EOF, "", CurrentLocation()));
        return tokens;
    }

    private Token NextToken()
    {
        var loc = CurrentLocation();
        char c = _source[_pos];

        // Comments
        if (c == '/' && _pos + 1 < _source.Length)
        {
            if (_source[_pos + 1] == '/')
                return ReadLineComment(loc);
            if (_source[_pos + 1] == '*')
                return ReadBlockComment(loc);
        }

        // String literals
        if (c == '"')
            return ReadString(loc);

        // Numbers (possibly duration/frequency)
        if (char.IsDigit(c) || (c == '-' && _pos + 1 < _source.Length && char.IsDigit(_source[_pos + 1])))
            return ReadNumber(loc);

        // Identifiers and keywords
        if (IsIdentStart(c))
            return ReadIdentifierOrKeyword(loc);

        // Operators and punctuation
        return ReadOperator(loc);
    }

    private Token ReadLineComment(SourceLocation loc)
    {
        var start = _pos;
        while (_pos < _source.Length && _source[_pos] != '\n')
            Advance();
        return new Token(TokenType.Comment, _source[start.._pos], loc);
    }

    private Token ReadBlockComment(SourceLocation loc)
    {
        var start = _pos;
        Advance(); // /
        Advance(); // *
        while (_pos + 1 < _source.Length && !(_source[_pos] == '*' && _source[_pos + 1] == '/'))
            Advance();
        if (_pos + 1 < _source.Length)
        {
            Advance(); // *
            Advance(); // /
        }
        return new Token(TokenType.Comment, _source[start.._pos], loc);
    }

    private Token ReadString(SourceLocation loc)
    {
        Advance(); // opening quote
        var start = _pos;
        while (_pos < _source.Length && _source[_pos] != '"')
        {
            if (_source[_pos] == '\\' && _pos + 1 < _source.Length)
                Advance(); // skip escaped char
            Advance();
        }
        var value = _source[start.._pos];
        if (_pos < _source.Length)
            Advance(); // closing quote
        return new Token(TokenType.StringLiteral, value, loc);
    }

    private Token ReadNumber(SourceLocation loc)
    {
        var start = _pos;
        bool negative = _source[_pos] == '-';
        if (negative) Advance();

        while (_pos < _source.Length && char.IsDigit(_source[_pos]))
            Advance();

        bool isFloat = false;
        if (_pos < _source.Length && _source[_pos] == '.' && _pos + 1 < _source.Length && char.IsDigit(_source[_pos + 1]))
        {
            isFloat = true;
            Advance(); // .
            while (_pos < _source.Length && char.IsDigit(_source[_pos]))
                Advance();
        }

        var numStr = _source[start.._pos];

        // Check for duration suffix: ms, s, m, h
        if (_pos < _source.Length)
        {
            if (_source[_pos] == 'm' && _pos + 1 < _source.Length && _source[_pos + 1] == 's')
            {
                Advance(); Advance();
                return new Token(TokenType.DurationLiteral, _source[start.._pos], loc);
            }
            if (_source[_pos] == 's' && !IsIdentContinue(PeekNext()))
            {
                Advance();
                return new Token(TokenType.DurationLiteral, _source[start.._pos], loc);
            }
            if (_source[_pos] == 'm' && !IsIdentContinue(PeekNext()))
            {
                Advance();
                return new Token(TokenType.DurationLiteral, _source[start.._pos], loc);
            }
            if (_source[_pos] == 'h' && !IsIdentContinue(PeekNext()))
            {
                Advance();
                return new Token(TokenType.DurationLiteral, _source[start.._pos], loc);
            }
            // Check for frequency suffix: Hz
            if (_source[_pos] == 'H' && _pos + 1 < _source.Length && _source[_pos + 1] == 'z')
            {
                Advance(); Advance();
                return new Token(TokenType.FrequencyLiteral, _source[start.._pos], loc);
            }
        }

        return new Token(isFloat ? TokenType.FloatLiteral : TokenType.IntegerLiteral, numStr, loc);
    }

    private Token ReadIdentifierOrKeyword(SourceLocation loc)
    {
        var start = _pos;
        while (_pos < _source.Length && IsIdentContinue(_source[_pos]))
            Advance();

        var value = _source[start.._pos];

        // Check for compound keywords that use underscores (already handled as single identifiers)
        if (Keywords.TryGetValue(value, out var type))
            return new Token(type, value, loc);

        // Handle special case: identifiers like STRING?, ENUM, ARRAY<...> are just identifiers
        return new Token(TokenType.Identifier, value, loc);
    }

    private Token ReadOperator(SourceLocation loc)
    {
        char c = _source[_pos];
        Advance();

        switch (c)
        {
            case ':': return new Token(TokenType.Colon, ":", loc);
            case ';': return new Token(TokenType.Semicolon, ";", loc);
            case ',': return new Token(TokenType.Comma, ",", loc);
            case '.': return new Token(TokenType.Dot, ".", loc);
            case '{': return new Token(TokenType.LBrace, "{", loc);
            case '}': return new Token(TokenType.RBrace, "}", loc);
            case '[': return new Token(TokenType.LBracket, "[", loc);
            case ']': return new Token(TokenType.RBracket, "]", loc);
            case '(': return new Token(TokenType.LParen, "(", loc);
            case ')': return new Token(TokenType.RParen, ")", loc);
            case '@': return new Token(TokenType.At, "@", loc);
            case '+': return new Token(TokenType.Plus, "+", loc);
            case '*': return new Token(TokenType.Star, "*", loc);
            case '%': return new Token(TokenType.Percent, "%", loc);
            case '?': return new Token(TokenType.QuestionMark, "?", loc);
            case '-':
                if (_pos < _source.Length && _source[_pos] == '>')
                {
                    Advance();
                    return new Token(TokenType.Arrow, "->", loc);
                }
                return new Token(TokenType.Minus, "-", loc);
            case '=':
                if (_pos < _source.Length && _source[_pos] == '>')
                {
                    Advance();
                    return new Token(TokenType.FatArrow, "=>", loc);
                }
                if (_pos < _source.Length && _source[_pos] == '=')
                {
                    Advance();
                    return new Token(TokenType.EqualEqual, "==", loc);
                }
                return new Token(TokenType.Equals, "=", loc);
            case '<':
                if (_pos < _source.Length && _source[_pos] == '=')
                {
                    Advance();
                    return new Token(TokenType.LessEqual, "<=", loc);
                }
                return new Token(TokenType.LessThan, "<", loc);
            case '>':
                if (_pos < _source.Length && _source[_pos] == '=')
                {
                    Advance();
                    return new Token(TokenType.GreaterEqual, ">=", loc);
                }
                return new Token(TokenType.GreaterThan, ">", loc);
            case '!':
                if (_pos < _source.Length && _source[_pos] == '=')
                {
                    Advance();
                    return new Token(TokenType.NotEqual, "!=", loc);
                }
                return new Token(TokenType.Error, "!", loc);
            case '|':
                if (_pos < _source.Length && _source[_pos] == '>')
                {
                    Advance();
                    return new Token(TokenType.PipeGreater, "|>", loc);
                }
                return new Token(TokenType.Pipe, "|", loc);
            case '/':
                return new Token(TokenType.Slash, "/", loc);
            default:
                // Handle special unicode chars like arrows: →
                if (c == '\u2192') // →
                    return new Token(TokenType.Arrow, "→", loc);
                return new Token(TokenType.Error, c.ToString(), loc);
        }
    }

    private void SkipWhitespace()
    {
        while (_pos < _source.Length && char.IsWhiteSpace(_source[_pos]))
            Advance();
    }

    private void Advance()
    {
        if (_pos < _source.Length)
        {
            if (_source[_pos] == '\n')
            {
                _line++;
                _col = 1;
            }
            else
            {
                _col++;
            }
            _pos++;
        }
    }

    private char PeekNext()
    {
        return _pos + 1 < _source.Length ? _source[_pos + 1] : '\0';
    }

    private SourceLocation CurrentLocation() => new(_fileName, _line, _col);

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentContinue(char c) => char.IsLetterOrDigit(c) || c == '_';
}
