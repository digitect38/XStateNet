using OmniSharp.Extensions.LanguageServer.Protocol;
using SemiFlow.Compiler;

namespace SemiFlow.LanguageServer.Services;

/// <summary>
/// Tracks open SFL documents, caches compilation results per URI.
/// </summary>
public class DocumentManager
{
    private readonly SflCompiler _compiler = new();
    private readonly Dictionary<DocumentUri, DocumentState> _documents = new();

    public void Open(DocumentUri uri, string text)
    {
        var state = new DocumentState(text, Compile(uri, text));
        _documents[uri] = state;
    }

    public void Update(DocumentUri uri, string text)
    {
        var state = new DocumentState(text, Compile(uri, text));
        _documents[uri] = state;
    }

    public void Close(DocumentUri uri)
    {
        _documents.Remove(uri);
    }

    public DocumentState? Get(DocumentUri uri)
    {
        return _documents.TryGetValue(uri, out var state) ? state : null;
    }

    public IEnumerable<DocumentState> All => _documents.Values;

    private CompilationResult Compile(DocumentUri uri, string text)
    {
        var fileName = Path.GetFileName(uri.GetFileSystemPath());
        return _compiler.Compile(text, fileName);
    }
}

public record DocumentState(string Text, CompilationResult Compilation);
