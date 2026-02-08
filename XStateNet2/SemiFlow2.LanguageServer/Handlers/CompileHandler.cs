using MediatR;
using OmniSharp.Extensions.JsonRpc;
using SemiFlow.Compiler;
using SemiFlow.LanguageServer.Services;

namespace SemiFlow.LanguageServer.Handlers;

/// <summary>
/// Handles the custom semiflow/compile request from the VS Code extension.
/// Compiles the given SFL source and returns the XState JSON.
/// </summary>
[Method("semiflow/compile", Direction.ClientToServer)]
public record CompileParams : IRequest<CompileResponse>
{
    public string Uri { get; init; } = "";
    public string Text { get; init; } = "";
}

public record CompileResponse
{
    public bool Success { get; init; }
    public string Json { get; init; } = "";
    public int DiagnosticCount { get; init; }
}

public class CompileHandler : IJsonRpcRequestHandler<CompileParams, CompileResponse>
{
    private readonly SflCompiler _compiler = new();

    public Task<CompileResponse> Handle(CompileParams request, CancellationToken cancellationToken)
    {
        var fileName = System.IO.Path.GetFileName(request.Uri);
        var result = _compiler.Compile(request.Text, fileName);

        return Task.FromResult(new CompileResponse
        {
            Success = result.Success,
            Json = result.ToJson(),
            DiagnosticCount = result.Diagnostics.Count
        });
    }
}
