using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SemiFlow.LanguageServer.Services;
using DiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

namespace SemiFlow.LanguageServer.Handlers;

/// <summary>
/// Handles document open/change/close and publishes diagnostics.
/// </summary>
public class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly DocumentManager _documentManager;
    private readonly SymbolIndex _symbolIndex;
    private readonly ILanguageServerFacade _server;

    public TextDocumentSyncHandler(
        DocumentManager documentManager,
        SymbolIndex symbolIndex,
        ILanguageServerFacade server)
    {
        _documentManager = documentManager;
        _symbolIndex = symbolIndex;
        _server = server;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) =>
        new(uri, "semiflow");

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri;
        _documentManager.Open(uri, request.TextDocument.Text);
        PublishDiagnostics(uri);
        RebuildSymbolIndex(uri);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri;
        var text = request.ContentChanges.FirstOrDefault()?.Text;
        if (text != null)
        {
            _documentManager.Update(uri, text);
            PublishDiagnostics(uri);
            RebuildSymbolIndex(uri);
        }
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken ct)
    {
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri;
        _documentManager.Close(uri);

        // Clear diagnostics on close
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>()
        });

        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("semiflow"),
            Change = TextDocumentSyncKind.Full,
            Save = new SaveOptions { IncludeText = false }
        };
    }

    private void PublishDiagnostics(DocumentUri uri)
    {
        var state = _documentManager.Get(uri);
        if (state == null) return;

        var diagnostics = state.Compilation.Diagnostics.Select(d =>
        {
            var line = Math.Max(0, d.Location.Line - 1);
            var col = Math.Max(0, d.Location.Column - 1);

            return new Diagnostic
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    new Position(line, col),
                    new Position(line, col + 10)),
                Severity = d.Severity switch
                {
                    Compiler.Diagnostics.DiagnosticSeverity.Error => DiagnosticSeverity.Error,
                    Compiler.Diagnostics.DiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
                    Compiler.Diagnostics.DiagnosticSeverity.Info => DiagnosticSeverity.Information,
                    _ => DiagnosticSeverity.Information
                },
                Code = d.Code,
                Source = "sfl",
                Message = d.Message
            };
        }).ToArray();

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>(diagnostics)
        });
    }

    private void RebuildSymbolIndex(DocumentUri uri)
    {
        var state = _documentManager.Get(uri);
        if (state?.Compilation.Ast != null)
        {
            _symbolIndex.Rebuild(state.Compilation.Ast, uri.ToString());
        }
    }
}
