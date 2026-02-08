using System.Text.Json;
using System.Text.Json.Serialization;
using SemiFlow.Compiler.Analysis;
using SemiFlow.Compiler.Ast;
using SemiFlow.Compiler.CodeGen;
using SemiFlow.Compiler.Diagnostics;
using SemiFlow.Compiler.Lexer;
using SemiFlow.Compiler.Parser;
using XStateNet2.Core.Engine;

namespace SemiFlow.Compiler;

/// <summary>
/// Main entry point for the SFL compiler.
/// Compiles SFL source code to XStateMachineScript.
/// </summary>
public class SflCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Compile SFL source code to an XStateMachineScript.
    /// </summary>
    public CompilationResult Compile(string source, string? fileName = null)
    {
        var diagnostics = new List<Diagnostic>();

        try
        {
            // Phase 1: Lex
            var lexer = new SflLexer(source, fileName);
            var tokens = lexer.Tokenize();

            // Phase 2: Parse
            var parser = new SflParser(tokens);
            var program = parser.ParseProgram();

            // Collect parse errors
            foreach (var error in parser.Errors)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error, error.Code, error.Message, error.Location));
            }

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                return new CompilationResult(false, null, program, diagnostics);
            }

            // Phase 3: Semantic analysis
            var analyzer = new SemanticAnalyzer();
            analyzer.Analyze(program);
            diagnostics.AddRange(analyzer.Diagnostics);

            bool hasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

            // Phase 4: Code generation (even with warnings, generate if no errors)
            XStateMachineScript? machine = null;
            if (!hasErrors)
            {
                var generator = new XStateGenerator();
                machine = generator.Generate(program);
                diagnostics.AddRange(generator.Diagnostics);
            }

            hasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
            return new CompilationResult(!hasErrors, machine, program, diagnostics);
        }
        catch (ParseError ex)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error, ex.Code, ex.Message, ex.Location));
            return new CompilationResult(false, null, null, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error, "SFL999",
                $"Internal compiler error: {ex.Message}",
                SourceLocation.Unknown));
            return new CompilationResult(false, null, null, diagnostics);
        }
    }

    /// <summary>
    /// Compile an SFL file from disk.
    /// </summary>
    public CompilationResult CompileFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new CompilationResult(false, null, null, new List<Diagnostic>
            {
                new(DiagnosticSeverity.Error, "SFL000",
                    $"File not found: {filePath}", SourceLocation.Unknown)
            });
        }

        var source = File.ReadAllText(filePath);
        return Compile(source, Path.GetFileName(filePath));
    }

    /// <summary>
    /// Compile SFL source directly to JSON string.
    /// </summary>
    public string CompileToJson(string source, string? fileName = null)
    {
        var result = Compile(source, fileName);
        return result.ToJson();
    }
}

/// <summary>
/// Result of an SFL compilation.
/// </summary>
public class CompilationResult
{
    public bool Success { get; }
    public XStateMachineScript? Machine { get; }
    public SflProgram? Ast { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CompilationResult(
        bool success,
        XStateMachineScript? machine,
        SflProgram? ast,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        Success = success;
        Machine = machine;
        Ast = ast;
        Diagnostics = diagnostics;
    }

    public IEnumerable<Diagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    public IEnumerable<Diagnostic> Warnings =>
        Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning);

    /// <summary>
    /// Serialize the compiled machine to JSON.
    /// Returns error details if compilation failed.
    /// </summary>
    public string ToJson()
    {
        if (Machine != null)
        {
            return JsonSerializer.Serialize(Machine, JsonOptions);
        }

        // Return diagnostics as JSON if compilation failed
        return JsonSerializer.Serialize(new
        {
            error = true,
            diagnostics = Diagnostics.Select(d => new
            {
                severity = d.Severity.ToString(),
                code = d.Code,
                message = d.Message,
                location = d.Location.ToString()
            })
        }, JsonOptions);
    }
}
