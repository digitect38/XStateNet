namespace SemiFlow.Compiler.Lexer;

public record SourceLocation(string File, int Line, int Column)
{
    public static readonly SourceLocation Unknown = new("", 0, 0);

    public override string ToString() =>
        string.IsNullOrEmpty(File) ? $"({Line},{Column})" : $"{File}({Line},{Column})";
}
