using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using SemiFlow.Compiler;
using SemiFlow.LanguageServer.Handlers;
using SemiFlow.LanguageServer.Services;

namespace SemiFlow.LanguageServer;

class Program
{
    static async Task Main(string[] args)
    {
        var server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(options =>
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(x => x
                    .AddLanguageProtocolLogging()
                    .SetMinimumLevel(LogLevel.Information))
                .WithServices(ConfigureServices)
                .WithHandler<TextDocumentSyncHandler>()
                .WithHandler<SflCompletionHandler>()
                .WithHandler<SflHoverHandler>()
                .WithHandler<SflDefinitionHandler>()
                .WithHandler<SflDocumentSymbolHandler>()
                .WithHandler<CompileHandler>()
        ).ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SflCompiler>();
        services.AddSingleton<DocumentManager>();
        services.AddSingleton<SymbolIndex>();
    }
}
