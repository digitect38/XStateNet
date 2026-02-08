using SemiFlow.Compiler.Lexer;

namespace SemiFlow.Compiler.Tests;

public class LexerTests
{
    [Fact]
    public void Tokenize_Keywords()
    {
        var lexer = new SflLexer("MASTER_SCHEDULER WAFER_SCHEDULER ROBOT_SCHEDULER STATION");
        var tokens = lexer.Tokenize();

        tokens.Should().HaveCount(5); // 4 keywords + EOF
        tokens[0].Type.Should().Be(TokenType.KW_MasterScheduler);
        tokens[1].Type.Should().Be(TokenType.KW_WaferScheduler);
        tokens[2].Type.Should().Be(TokenType.KW_RobotScheduler);
        tokens[3].Type.Should().Be(TokenType.KW_Station);
        tokens[4].Type.Should().Be(TokenType.EOF);
    }

    [Fact]
    public void Tokenize_StringLiteral()
    {
        var lexer = new SflLexer("\"hello world\"");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.StringLiteral);
        tokens[0].Value.Should().Be("hello world");
    }

    [Fact]
    public void Tokenize_IntegerLiteral()
    {
        var lexer = new SflLexer("42");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.IntegerLiteral);
        tokens[0].Value.Should().Be("42");
    }

    [Fact]
    public void Tokenize_FloatLiteral()
    {
        var lexer = new SflLexer("2.0");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.FloatLiteral);
        tokens[0].Value.Should().Be("2.0");
    }

    [Fact]
    public void Tokenize_DurationLiterals()
    {
        var lexer = new SflLexer("30s 100ms 5m 2h");
        var tokens = lexer.Tokenize();

        tokens.Where(t => t.Type == TokenType.DurationLiteral).Should().HaveCount(4);
        tokens[0].Value.Should().Be("30s");
        tokens[1].Value.Should().Be("100ms");
        tokens[2].Value.Should().Be("5m");
        tokens[3].Value.Should().Be("2h");
    }

    [Fact]
    public void Tokenize_FrequencyLiteral()
    {
        var lexer = new SflLexer("10Hz");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.FrequencyLiteral);
        tokens[0].Value.Should().Be("10Hz");
    }

    [Fact]
    public void Tokenize_BooleanLiterals()
    {
        var lexer = new SflLexer("true false");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.BooleanLiteral);
        tokens[0].Value.Should().Be("true");
        tokens[1].Type.Should().Be(TokenType.BooleanLiteral);
        tokens[1].Value.Should().Be("false");
    }

    [Fact]
    public void Tokenize_Operators()
    {
        var lexer = new SflLexer(": ; , . { } [ ] ( ) @ -> => = < <= > >=");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.Colon);
        tokens[1].Type.Should().Be(TokenType.Semicolon);
        tokens[2].Type.Should().Be(TokenType.Comma);
        tokens[3].Type.Should().Be(TokenType.Dot);
        tokens[4].Type.Should().Be(TokenType.LBrace);
        tokens[5].Type.Should().Be(TokenType.RBrace);
        tokens[6].Type.Should().Be(TokenType.LBracket);
        tokens[7].Type.Should().Be(TokenType.RBracket);
        tokens[8].Type.Should().Be(TokenType.LParen);
        tokens[9].Type.Should().Be(TokenType.RParen);
        tokens[10].Type.Should().Be(TokenType.At);
        tokens[11].Type.Should().Be(TokenType.Arrow);
        tokens[12].Type.Should().Be(TokenType.FatArrow);
        tokens[13].Type.Should().Be(TokenType.Equals);
        tokens[14].Type.Should().Be(TokenType.LessThan);
        tokens[15].Type.Should().Be(TokenType.LessEqual);
        tokens[16].Type.Should().Be(TokenType.GreaterThan);
        tokens[17].Type.Should().Be(TokenType.GreaterEqual);
    }

    [Fact]
    public void Tokenize_SkipsLineComments()
    {
        var lexer = new SflLexer("foo // comment\nbar");
        var tokens = lexer.Tokenize();

        tokens.Should().HaveCount(3); // foo, bar, EOF
        tokens[0].Value.Should().Be("foo");
        tokens[1].Value.Should().Be("bar");
    }

    [Fact]
    public void Tokenize_SkipsBlockComments()
    {
        var lexer = new SflLexer("foo /* block comment */ bar");
        var tokens = lexer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens[0].Value.Should().Be("foo");
        tokens[1].Value.Should().Be("bar");
    }

    [Fact]
    public void Tokenize_LayerKeywords()
    {
        var lexer = new SflLexer("L1 L2 L3 L4");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.KW_L1);
        tokens[1].Type.Should().Be(TokenType.KW_L2);
        tokens[2].Type.Should().Be(TokenType.KW_L3);
        tokens[3].Type.Should().Be(TokenType.KW_L4);
    }

    [Fact]
    public void Tokenize_Import()
    {
        var lexer = new SflLexer("import semiflow.algorithms.cyclic_zip");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.KW_Import);
        tokens[1].Type.Should().Be(TokenType.Identifier);
        tokens[1].Value.Should().Be("semiflow");
        tokens[2].Type.Should().Be(TokenType.Dot);
        tokens[3].Value.Should().Be("algorithms");
        tokens[4].Type.Should().Be(TokenType.Dot);
        tokens[5].Value.Should().Be("cyclic_zip");
    }

    [Fact]
    public void Tokenize_PublishStatement()
    {
        var lexer = new SflLexer("publish status to \"wsc/001/status\" @1;");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.KW_Publish);
        tokens[1].Type.Should().Be(TokenType.Identifier);
        tokens[1].Value.Should().Be("status");
        tokens[2].Type.Should().Be(TokenType.KW_To);
        tokens[3].Type.Should().Be(TokenType.StringLiteral);
        tokens[3].Value.Should().Be("wsc/001/status");
        tokens[4].Type.Should().Be(TokenType.At);
        tokens[5].Type.Should().Be(TokenType.IntegerLiteral);
        tokens[5].Value.Should().Be("1");
        tokens[6].Type.Should().Be(TokenType.Semicolon);
    }

    [Fact]
    public void Tokenize_TracksLineAndColumn()
    {
        var lexer = new SflLexer("foo\nbar", "test.sfl");
        var tokens = lexer.Tokenize();

        tokens[0].Location.Line.Should().Be(1);
        tokens[0].Location.Column.Should().Be(1);
        tokens[1].Location.Line.Should().Be(2);
        tokens[1].Location.Column.Should().Be(1);
        tokens[0].Location.File.Should().Be("test.sfl");
    }

    [Fact]
    public void Tokenize_SchedulerDeclaration()
    {
        var lexer = new SflLexer("MASTER_SCHEDULER MSC_001 {");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.KW_MasterScheduler);
        tokens[1].Type.Should().Be(TokenType.Identifier);
        tokens[1].Value.Should().Be("MSC_001");
        tokens[2].Type.Should().Be(TokenType.LBrace);
    }

    [Fact]
    public void Tokenize_ApplyRule()
    {
        var lexer = new SflLexer("APPLY_RULE(\"WAR_001\")");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.KW_ApplyRule);
        tokens[1].Type.Should().Be(TokenType.LParen);
        tokens[2].Type.Should().Be(TokenType.StringLiteral);
        tokens[2].Value.Should().Be("WAR_001");
        tokens[3].Type.Should().Be(TokenType.RParen);
    }

    [Fact]
    public void Tokenize_Formula()
    {
        var lexer = new SflLexer("FORMULA(CYCLIC_ZIP, 0, 3, 25)");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.KW_Formula);
        tokens[1].Type.Should().Be(TokenType.LParen);
        tokens[2].Type.Should().Be(TokenType.Identifier);
        tokens[2].Value.Should().Be("CYCLIC_ZIP");
    }

    [Fact]
    public void Tokenize_PipeOperator()
    {
        var lexer = new SflLexer("|>");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.PipeGreater);
    }

    [Fact]
    public void Tokenize_TopologyBlockKeywords()
    {
        var lexer = new SflLexer("FLOW CROSSOVER MUTEX CONSTRAINTS");
        var tokens = lexer.Tokenize();

        tokens.Should().HaveCount(5); // 4 keywords + EOF
        tokens[0].Type.Should().Be(TokenType.KW_FlowBlock);
        tokens[1].Type.Should().Be(TokenType.KW_Crossover);
        tokens[2].Type.Should().Be(TokenType.KW_Mutex);
        tokens[3].Type.Should().Be(TokenType.KW_Constraints);
    }

    [Fact]
    public void Tokenize_LowercaseFlow_IsPropertyKeyword()
    {
        // lowercase "flow" should still map to KW_Flow (property keyword), not KW_FlowBlock
        var lexer = new SflLexer("flow");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.KW_Flow);
        tokens[0].Value.Should().Be("flow");
    }

    [Fact]
    public void Tokenize_FlowSequence_WithArrows()
    {
        var lexer = new SflLexer("SRC -> R1 -> POL -> DST");
        var tokens = lexer.Tokenize();

        tokens[0].Type.Should().Be(TokenType.Identifier);
        tokens[0].Value.Should().Be("SRC");
        tokens[1].Type.Should().Be(TokenType.Arrow);
        tokens[2].Type.Should().Be(TokenType.Identifier);
        tokens[2].Value.Should().Be("R1");
        tokens[3].Type.Should().Be(TokenType.Arrow);
    }
}
