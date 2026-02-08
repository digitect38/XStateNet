using SemiFlow.Compiler.Lexer;

namespace SemiFlow.Compiler.Diagnostics;

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    SourceLocation Location
)
{
    public override string ToString() =>
        $"{Severity} {Code} at {Location}: {Message}";
}
