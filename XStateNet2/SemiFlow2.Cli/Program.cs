using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SemiFlow.Compiler;
using SemiFlow.Compiler.Diagnostics;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

var command = args[0];

switch (command)
{
    case "compile":
        return HandleCompile(args[1..]);
    case "check":
        return HandleCheck(args[1..]);
    case "visualize":
        return HandleVisualize(args[1..]);
    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
}

static int HandleCompile(string[] args)
{
    string? outputPath = null;
    string? inputPath = null;
    bool quiet = false;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "-o" or "--output":
                if (i + 1 < args.Length) outputPath = args[++i];
                else { Console.Error.WriteLine("Missing value for -o"); return 1; }
                break;
            case "-q" or "--quiet":
                quiet = true;
                break;
            default:
                if (args[i].StartsWith('-'))
                {
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 1;
                }
                inputPath = args[i];
                break;
        }
    }

    if (inputPath == null)
    {
        Console.Error.WriteLine("No input file specified.");
        return 1;
    }

    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"File not found: {inputPath}");
        return 1;
    }

    var compiler = new SflCompiler();
    var result = compiler.CompileFile(inputPath);

    // Print diagnostics to stderr
    if (!quiet)
    {
        foreach (var d in result.Diagnostics)
        {
            var prefix = d.Severity == DiagnosticSeverity.Error ? "error" : "warning";
            Console.Error.WriteLine($"{d.Location}: {prefix} {d.Code}: {d.Message}");
        }
    }

    var json = result.ToJson();

    if (outputPath != null)
    {
        File.WriteAllText(outputPath, json);
        if (!quiet)
            Console.Error.WriteLine(result.Success
                ? $"Compiled to {outputPath}"
                : $"Compilation failed. Diagnostics written to {outputPath}");
    }
    else
    {
        Console.WriteLine(json);
    }

    return result.Success ? 0 : 1;
}

static int HandleCheck(string[] args)
{
    string? inputPath = null;

    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith('-'))
            inputPath = args[i];
    }

    if (inputPath == null)
    {
        Console.Error.WriteLine("No input file specified.");
        return 1;
    }

    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"File not found: {inputPath}");
        return 1;
    }

    var compiler = new SflCompiler();
    var result = compiler.CompileFile(inputPath);

    foreach (var d in result.Diagnostics)
    {
        var prefix = d.Severity == DiagnosticSeverity.Error ? "error" : "warning";
        Console.Error.WriteLine($"{d.Location}: {prefix} {d.Code}: {d.Message}");
    }

    var errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
    var warnings = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

    Console.WriteLine(errors == 0
        ? $"OK ({warnings} warning{(warnings == 1 ? "" : "s")})"
        : $"FAILED ({errors} error{(errors == 1 ? "" : "s")}, {warnings} warning{(warnings == 1 ? "" : "s")})");

    return result.Success ? 0 : 1;
}

static int HandleVisualize(string[] args)
{
    string? inputPath = null;
    string? outputPath = null;
    bool openBrowser = true;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "-o" or "--output":
                if (i + 1 < args.Length) outputPath = args[++i];
                else { Console.Error.WriteLine("Missing value for -o"); return 1; }
                break;
            case "--no-open":
                openBrowser = false;
                break;
            default:
                if (args[i].StartsWith('-'))
                {
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 1;
                }
                inputPath = args[i];
                break;
        }
    }

    if (inputPath == null)
    {
        Console.Error.WriteLine("No input file specified.");
        return 1;
    }

    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"File not found: {inputPath}");
        return 1;
    }

    string jsonStr;
    // Determine if input is .sfl or .json
    var ext = Path.GetExtension(inputPath).ToLowerInvariant();
    if (ext == ".sfl")
    {
        var compiler = new SflCompiler();
        var result = compiler.CompileFile(inputPath);
        foreach (var d in result.Diagnostics)
        {
            var prefix = d.Severity == DiagnosticSeverity.Error ? "error" : "warning";
            Console.Error.WriteLine($"{d.Location}: {prefix} {d.Code}: {d.Message}");
        }
        if (!result.Success)
        {
            Console.Error.WriteLine("Compilation failed. Cannot visualize.");
            return 1;
        }
        jsonStr = result.ToJson();
    }
    else
    {
        jsonStr = File.ReadAllText(inputPath);
    }

    var mermaidCode = XStateToMermaid(jsonStr);
    var html = GenerateHtml(mermaidCode, jsonStr, Path.GetFileName(inputPath));

    var htmlPath = outputPath ?? Path.Combine(Path.GetTempPath(), $"sflc_visualize_{Path.GetFileNameWithoutExtension(inputPath)}.html");
    File.WriteAllText(htmlPath, html);
    Console.Error.WriteLine($"Visualization written to {htmlPath}");

    if (openBrowser)
    {
        try
        {
            Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
        }
        catch
        {
            Console.Error.WriteLine("Could not open browser. Open the HTML file manually.");
        }
    }

    return 0;
}

static string XStateToMermaid(string jsonStr)
{
    try
    {
        using var doc = JsonDocument.Parse(jsonStr);
        var root = doc.RootElement;
        var lines = new List<string> { "stateDiagram-v2" };

        var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

        if (type == "parallel" && root.TryGetProperty("states", out var parallelStates))
        {
            foreach (var region in parallelStates.EnumerateObject())
            {
                lines.Add($"    state {region.Name} {{");
                RenderStateContent(region.Value, lines, "        ", region.Name);
                lines.Add("    }");
                lines.Add("");
            }
        }
        else if (root.TryGetProperty("states", out var states))
        {
            if (root.TryGetProperty("initial", out var init) && init.GetString() is string initStr && initStr.Length > 0)
                lines.Add($"    [*] --> {initStr}");

            foreach (var state in states.EnumerateObject())
                RenderStateTransitions(state.Name, state.Value, lines, "    ", null);
        }

        return string.Join("\n", lines);
    }
    catch
    {
        return "stateDiagram-v2\n    note right of [*] : Failed to parse XState JSON";
    }
}

static void RenderStateContent(JsonElement node, List<string> lines, string indent, string? prefix)
{
    if (!node.TryGetProperty("states", out var states)) return;

    // Declare state aliases so Mermaid shows clean names
    if (prefix != null)
    {
        foreach (var state in states.EnumerateObject())
            lines.Add($"{indent}state \"{state.Name}\" as {prefix}_{state.Name}");
    }

    if (node.TryGetProperty("initial", out var init) && init.GetString() is string initStr && initStr.Length > 0)
        lines.Add($"{indent}[*] --> {Prefixed(initStr, prefix)}");

    foreach (var state in states.EnumerateObject())
    {
        var sid = Prefixed(state.Name, prefix);

        // Nested compound state
        if (state.Value.TryGetProperty("states", out _))
        {
            lines.Add($"{indent}state {sid} {{");
            var nestedPrefix = prefix != null ? $"{prefix}_{state.Name}" : state.Name;
            RenderStateContent(state.Value, lines, indent + "    ", nestedPrefix);
            lines.Add($"{indent}}}");
        }

        RenderStateTransitions(sid, state.Value, lines, indent, prefix);
    }
}

static string Prefixed(string name, string? prefix) =>
    prefix != null ? $"{prefix}_{name}" : name;

static void RenderStateTransitions(string name, JsonElement node, List<string> lines, string indent, string? prefix)
{
    // Final state
    if (node.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "final")
        lines.Add($"{indent}{name} --> [*]");

    // Regular transitions (on)
    if (node.TryGetProperty("on", out var on))
    {
        foreach (var evt in on.EnumerateObject())
        {
            var targets = ResolveTargets(evt.Value);
            var safeEvent = evt.Name.Replace("/", "_").Replace("+", "_").Replace("#", "_");
            foreach (var target in targets)
            {
                var prefixedTarget = Prefixed(target, prefix);
                if (prefixedTarget != name)
                    lines.Add($"{indent}{name} --> {prefixedTarget} : {safeEvent}");
            }
        }
    }

    // Delayed transitions (after)
    if (node.TryGetProperty("after", out var after))
    {
        foreach (var delay in after.EnumerateObject())
        {
            var targets = ResolveTargets(delay.Value);
            foreach (var target in targets)
                lines.Add($"{indent}{name} --> {Prefixed(target, prefix)} : after {delay.Name}ms");
        }
    }
}

static List<string> ResolveTargets(JsonElement value)
{
    var targets = new List<string>();

    switch (value.ValueKind)
    {
        case JsonValueKind.String:
            var s = value.GetString();
            if (s != null) targets.Add(s);
            break;
        case JsonValueKind.Object:
            if (value.TryGetProperty("target", out var t))
            {
                if (t.ValueKind == JsonValueKind.String && t.GetString() is string ts)
                    targets.Add(ts);
                else if (t.ValueKind == JsonValueKind.Array)
                    foreach (var item in t.EnumerateArray())
                        if (item.GetString() is string its) targets.Add(its);
            }
            break;
        case JsonValueKind.Array:
            foreach (var item in value.EnumerateArray())
                targets.AddRange(ResolveTargets(item));
            break;
        case JsonValueKind.Null:
            break;
    }

    return targets;
}

static string GenerateHtml(string mermaidCode, string xstateJson, string fileName)
{
    var escapedMermaid = mermaidCode.Replace("\\", "\\\\").Replace("`", "\\`").Replace("$", "\\$");
    var escapedJson = xstateJson.Replace("\\", "\\\\").Replace("`", "\\`").Replace("$", "\\$");

    return $$"""
    <!DOCTYPE html>
    <html lang="en">
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <title>SFL Visualizer — {{fileName}}</title>
        <style>
            * { margin: 0; padding: 0; box-sizing: border-box; }
            body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #1e1e2e; color: #cdd6f4; }
            .header { background: #181825; padding: 12px 24px; display: flex; align-items: center; gap: 16px; border-bottom: 1px solid #313244; }
            .header h1 { font-size: 16px; font-weight: 600; }
            .tabs { display: flex; gap: 2px; }
            .tab { padding: 8px 20px; cursor: pointer; border: none; background: transparent; color: #a6adc8; font-size: 13px; border-bottom: 2px solid transparent; transition: all 0.15s; }
            .tab:hover { color: #cdd6f4; background: #313244; }
            .tab.active { border-bottom-color: #89b4fa; color: #89b4fa; }
            .content { display: none; padding: 24px; height: calc(100vh - 52px); overflow: auto; }
            .content.active { display: block; }
            #diagram { text-align: center; }
            #diagram svg { max-width: 100%; }
            pre { background: #11111b; padding: 20px; border-radius: 8px; font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace; font-size: 13px; line-height: 1.6; overflow: auto; white-space: pre; color: #cdd6f4; }
            .error { color: #f38ba8; padding: 20px; }
        </style>
    </head>
    <body>
        <div class="header">
            <h1>{{fileName}}</h1>
            <div class="tabs">
                <button class="tab active" onclick="showTab('diagram', this)">Diagram</button>
                <button class="tab" onclick="showTab('mermaid', this)">Mermaid</button>
                <button class="tab" onclick="showTab('json', this)">XState JSON</button>
            </div>
        </div>
        <div id="diagram-tab" class="content active">
            <div id="diagram"></div>
        </div>
        <div id="mermaid-tab" class="content">
            <pre id="mermaid-source"></pre>
        </div>
        <div id="json-tab" class="content">
            <pre id="json-source"></pre>
        </div>

        <script src="https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js"></script>
        <script>
            const mermaidCode = `{{escapedMermaid}}`;
            const xstateJson = `{{escapedJson}}`;

            mermaid.initialize({
                startOnLoad: false,
                theme: 'dark',
                securityLevel: 'loose',
                stateDiagram: { useMaxWidth: true }
            });

            async function renderDiagram() {
                try {
                    const { svg } = await mermaid.render('mermaid-svg', mermaidCode);
                    document.getElementById('diagram').innerHTML = svg;
                } catch (e) {
                    document.getElementById('diagram').innerHTML =
                        '<div class="error">Failed to render: ' + e.message + '</div>' +
                        '<pre>' + mermaidCode + '</pre>';
                }
            }

            document.getElementById('mermaid-source').textContent = mermaidCode;
            try {
                document.getElementById('json-source').textContent = JSON.stringify(JSON.parse(xstateJson), null, 2);
            } catch {
                document.getElementById('json-source').textContent = xstateJson;
            }

            renderDiagram();

            function showTab(name, btn) {
                document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
                document.querySelectorAll('.content').forEach(c => c.classList.remove('active'));
                document.getElementById(name + '-tab').classList.add('active');
                btn.classList.add('active');
            }
        </script>
    </body>
    </html>
    """;
}

static void PrintUsage()
{
    Console.WriteLine("""
    sflc - SemiFlow Language Compiler

    Usage:
      sflc compile <file.sfl> [-o output.json] [-q]
      sflc check <file.sfl>
      sflc visualize <file.sfl|file.json> [-o output.html] [--no-open]
      sflc --help

    Commands:
      compile    Compile SFL to XState JSON
      check      Validate SFL without generating output
      visualize  Generate Mermaid state diagram and open in browser

    Options:
      -o, --output <path>   Write output to file instead of stdout
      -q, --quiet           Suppress diagnostic messages
      --no-open             Don't open browser (visualize only)
      -h, --help            Show this help
    """);
}
