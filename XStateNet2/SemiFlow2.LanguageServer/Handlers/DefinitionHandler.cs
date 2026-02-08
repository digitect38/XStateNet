using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SemiFlow.LanguageServer.Services;

namespace SemiFlow.LanguageServer.Handlers;

/// <summary>
/// Provides go-to-definition for SFL symbols.
/// </summary>
public class SflDefinitionHandler : DefinitionHandlerBase
{
    private readonly DocumentManager _documentManager;
    private readonly SymbolIndex _symbolIndex;

    public SflDefinitionHandler(DocumentManager documentManager, SymbolIndex symbolIndex)
    {
        _documentManager = documentManager;
        _symbolIndex = symbolIndex;
    }

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken ct)
    {
        var state = _documentManager.Get(request.TextDocument.Uri);
        if (state == null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var word = GetWordAtPosition(state.Text, request.Position);
        if (string.IsNullOrEmpty(word))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        // Look up in symbol index
        var symbol = _symbolIndex.Lookup(word);
        if (symbol != null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(
                new LocationOrLocationLinks(symbol.DefinitionLocation));
        }

        // Check for rule references inside APPLY_RULE("...")
        var lineText = GetLineText(state.Text, (int)request.Position.Line);
        if (lineText.Contains("APPLY_RULE"))
        {
            // The word might be inside a string literal - try to extract it
            var ruleSymbol = _symbolIndex.Lookup(word);
            if (ruleSymbol != null)
            {
                return Task.FromResult<LocationOrLocationLinks?>(
                    new LocationOrLocationLinks(ruleSymbol.DefinitionLocation));
            }
        }

        return Task.FromResult<LocationOrLocationLinks?>(null);
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("semiflow")
        };
    }

    private static string GetWordAtPosition(string text, Position position)
    {
        var lines = text.Split('\n');
        var lineIdx = (int)position.Line;
        if (lineIdx >= lines.Length) return "";

        var line = lines[lineIdx];
        var col = (int)position.Character;
        if (col >= line.Length) return "";

        var start = col;
        while (start > 0 && IsWordChar(line[start - 1]))
            start--;

        var end = col;
        while (end < line.Length && IsWordChar(line[end]))
            end++;

        return line[start..end];
    }

    private static string GetLineText(string text, int line)
    {
        var lines = text.Split('\n');
        return line < lines.Length ? lines[line] : "";
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
