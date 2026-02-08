using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SemiFlow.Compiler.Ast;
using SemiFlow.Compiler.Lexer;

namespace SemiFlow.LanguageServer.Services;

/// <summary>
/// Indexes symbol names to their definition locations for go-to-definition.
/// Rebuilt after each compilation.
/// </summary>
public class SymbolIndex
{
    private readonly Dictionary<string, SymbolInfo> _symbols = new();

    public void Rebuild(SflProgram program, string fileUri)
    {
        _symbols.Clear();

        // Index all schedulers
        foreach (var sched in program.Schedulers)
        {
            _symbols[sched.Name] = new SymbolInfo(
                sched.Name,
                SymbolKindEx.Scheduler,
                sched.Type.ToString(),
                ToLocation(sched.Location, fileUri));
        }

        // Index rules from pipeline rules
        if (program.PipelineRules != null)
        {
            foreach (var rule in program.PipelineRules.Rules)
            {
                _symbols[rule.Name] = new SymbolInfo(
                    rule.Name,
                    SymbolKindEx.Rule,
                    "PipelineRule",
                    ToLocation(rule.Location, fileUri));
            }
        }

        // Index top-level rules
        foreach (var rule in program.Rules)
        {
            _symbols[rule.Name] = new SymbolInfo(
                rule.Name,
                SymbolKindEx.Rule,
                "Rule",
                ToLocation(rule.Location, fileUri));
        }

        // Index message brokers
        foreach (var broker in program.MessageBrokers)
        {
            _symbols[broker.Name] = new SymbolInfo(
                broker.Name,
                SymbolKindEx.MessageBroker,
                "MessageBroker",
                ToLocation(broker.Location, fileUri));
        }

        // Index transaction managers
        foreach (var tm in program.TransactionManagers)
        {
            _symbols[tm.Name] = new SymbolInfo(
                tm.Name,
                SymbolKindEx.TransactionManager,
                "TransactionManager",
                ToLocation(tm.Location, fileUri));
        }

        // Index transactions within schedulers
        foreach (var sched in program.Schedulers)
        {
            foreach (var txn in sched.Transactions)
            {
                _symbols[txn.Name] = new SymbolInfo(
                    txn.Name,
                    SymbolKindEx.Transaction,
                    "Transaction",
                    ToLocation(txn.Location, fileUri));
            }
        }
    }

    public SymbolInfo? Lookup(string name)
    {
        return _symbols.TryGetValue(name, out var info) ? info : null;
    }

    public IEnumerable<SymbolInfo> AllSymbols => _symbols.Values;

    public IEnumerable<SymbolInfo> GetByKind(SymbolKindEx kind) =>
        _symbols.Values.Where(s => s.Kind == kind);

    private static Location ToLocation(SourceLocation loc, string fileUri)
    {
        var line = Math.Max(0, loc.Line - 1); // 1-based → 0-based
        var col = Math.Max(0, loc.Column - 1);
        return new Location
        {
            Uri = fileUri,
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                new Position(line, col),
                new Position(line, col + 1))
        };
    }
}

public enum SymbolKindEx
{
    Scheduler,
    Rule,
    MessageBroker,
    TransactionManager,
    Transaction,
    Topic
}

public record SymbolInfo(
    string Name,
    SymbolKindEx Kind,
    string Detail,
    Location DefinitionLocation);
