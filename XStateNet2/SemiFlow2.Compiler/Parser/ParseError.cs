using SemiFlow.Compiler.Lexer;

namespace SemiFlow.Compiler.Parser;

public class ParseError : Exception
{
    public SourceLocation Location { get; }
    public string Code { get; }

    public ParseError(string message, SourceLocation location, string code = "SFL000")
        : base(message)
    {
        Location = location;
        Code = code;
    }

    public override string ToString() => $"{Code} at {Location}: {Message}";
}
