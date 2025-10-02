using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.IO;

namespace XStateNet.Visualization
{
    /// <summary>
    /// Tools for visualizing state machines in various formats
    /// </summary>
    public class StateMachineVisualizer
    {
        /// <summary>
        /// Generate Mermaid diagram from state machine JSON
        /// </summary>
        public static string GenerateMermaidDiagram(string stateMachineJson, VisualizationOptions? options = null)
        {
            options ??= new VisualizationOptions();

            var document = JsonDocument.Parse(stateMachineJson);
            var root = document.RootElement;

            var sb = new StringBuilder();
            sb.AppendLine("```mermaid");
            sb.AppendLine("stateDiagram-v2");

            var machineId = root.GetProperty("id").GetString() ?? "StateMachine";
            var initialState = root.GetProperty("initial").GetString() ?? "initial";

            // Add initial state marker
            sb.AppendLine($"    [*] --> {SanitizeStateName(initialState)}");

            // Process states
            if (root.TryGetProperty("states", out var states))
            {
                ProcessStatesForMermaid(sb, states, "", options);
            }

            sb.AppendLine("```");
            return sb.ToString();
        }

        /// <summary>
        /// Generate Graphviz DOT format diagram
        /// </summary>
        public static string GenerateDotDiagram(string stateMachineJson, VisualizationOptions? options = null)
        {
            options ??= new VisualizationOptions();

            var document = JsonDocument.Parse(stateMachineJson);
            var root = document.RootElement;

            var sb = new StringBuilder();
            var machineId = root.GetProperty("id").GetString() ?? "StateMachine";

            sb.AppendLine($"digraph \"{machineId}\" {{");
            sb.AppendLine("    rankdir=TB;");
            sb.AppendLine("    node [shape=rectangle, style=rounded];");
            sb.AppendLine("    edge [fontsize=10];");
            sb.AppendLine();

            var initialState = root.GetProperty("initial").GetString() ?? "initial";

            // Add initial state
            sb.AppendLine($"    start [shape=circle, style=filled, fillcolor=black, label=\"\", width=0.3, height=0.3];");
            sb.AppendLine($"    start -> \"{initialState}\";");
            sb.AppendLine();

            // Process states
            if (root.TryGetProperty("states", out var states))
            {
                ProcessStatesForDot(sb, states, "", options);
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// Generate PlantUML diagram
        /// </summary>
        public static string GeneratePlantUmlDiagram(string stateMachineJson, VisualizationOptions? options = null)
        {
            options ??= new VisualizationOptions();

            var document = JsonDocument.Parse(stateMachineJson);
            var root = document.RootElement;

            var sb = new StringBuilder();
            sb.AppendLine("@startuml");

            var machineId = root.GetProperty("id").GetString() ?? "StateMachine";
            var initialState = root.GetProperty("initial").GetString() ?? "initial";

            sb.AppendLine($"title {machineId} State Machine");
            sb.AppendLine();
            sb.AppendLine($"[*] --> {SanitizeStateName(initialState)}");

            // Process states
            if (root.TryGetProperty("states", out var states))
            {
                ProcessStatesForPlantUml(sb, states, "", options);
            }

            sb.AppendLine("@enduml");
            return sb.ToString();
        }

        /// <summary>
        /// Generate HTML interactive diagram
        /// </summary>
        public static string GenerateHtmlDiagram(string stateMachineJson, VisualizationOptions? options = null)
        {
            options ??= new VisualizationOptions();

            var document = JsonDocument.Parse(stateMachineJson);
            var root = document.RootElement;

            var machineId = root.GetProperty("id").GetString() ?? "StateMachine";
            var stateData = ExtractStateData(root);

            var html = GenerateInteractiveHtml(machineId, stateData, options);
            return html;
        }

        /// <summary>
        /// Generate comprehensive visualization report
        /// </summary>
        public static VisualizationReport GenerateVisualizationReport(string stateMachineJson, VisualizationOptions? options = null)
        {
            options ??= new VisualizationOptions();

            var document = JsonDocument.Parse(stateMachineJson);
            var root = document.RootElement;

            var machineId = root.GetProperty("id").GetString() ?? "StateMachine";
            var analysis = AnalyzeStateMachine(root);

            return new VisualizationReport
            {
                MachineId = machineId,
                Analysis = analysis,
                MermaidDiagram = GenerateMermaidDiagram(stateMachineJson, options),
                DotDiagram = GenerateDotDiagram(stateMachineJson, options),
                PlantUmlDiagram = GeneratePlantUmlDiagram(stateMachineJson, options),
                HtmlDiagram = GenerateHtmlDiagram(stateMachineJson, options),
                GeneratedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Export visualization to files
        /// </summary>
        public static void ExportVisualization(string stateMachineJson, string outputDirectory, VisualizationOptions? options = null)
        {
            options ??= new VisualizationOptions();

            Directory.CreateDirectory(outputDirectory);

            var document = JsonDocument.Parse(stateMachineJson);
            var root = document.RootElement;
            var machineId = root.GetProperty("id").GetString() ?? "StateMachine";

            // Generate all diagram formats
            var mermaid = GenerateMermaidDiagram(stateMachineJson, options);
            var dot = GenerateDotDiagram(stateMachineJson, options);
            var plantuml = GeneratePlantUmlDiagram(stateMachineJson, options);
            var html = GenerateHtmlDiagram(stateMachineJson, options);

            // Write files
            File.WriteAllText(Path.Combine(outputDirectory, $"{machineId}_mermaid.md"), mermaid);
            File.WriteAllText(Path.Combine(outputDirectory, $"{machineId}_diagram.dot"), dot);
            File.WriteAllText(Path.Combine(outputDirectory, $"{machineId}_diagram.puml"), plantuml);
            File.WriteAllText(Path.Combine(outputDirectory, $"{machineId}_interactive.html"), html);

            // Generate comprehensive report
            var report = GenerateVisualizationReport(stateMachineJson, options);
            var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(outputDirectory, $"{machineId}_report.json"), reportJson);

            Console.WriteLine($"✅ Visualization files exported to: {outputDirectory}");
            Console.WriteLine($"   📄 {machineId}_mermaid.md - Mermaid diagram");
            Console.WriteLine($"   🔗 {machineId}_diagram.dot - Graphviz DOT");
            Console.WriteLine($"   🌿 {machineId}_diagram.puml - PlantUML");
            Console.WriteLine($"   🌐 {machineId}_interactive.html - Interactive HTML");
            Console.WriteLine($"   📊 {machineId}_report.json - Analysis report");
        }

        // Private helper methods
        private static void ProcessStatesForMermaid(StringBuilder sb, JsonElement states, string parentPrefix, VisualizationOptions options)
        {
            foreach (var stateProperty in states.EnumerateObject())
            {
                var stateName = stateProperty.Name;
                var stateValue = stateProperty.Value;
                var fullStateName = string.IsNullOrEmpty(parentPrefix) ? stateName : $"{parentPrefix}_{stateName}";

                // Check if it's a final state
                if (stateValue.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "final")
                {
                    sb.AppendLine($"    {SanitizeStateName(fullStateName)} --> [*]");
                }

                // Add state with description if available
                if (options.IncludeDescriptions && stateValue.TryGetProperty("meta", out var meta) &&
                    meta.TryGetProperty("description", out var desc))
                {
                    var description = desc.GetString();
                    sb.AppendLine($"    {SanitizeStateName(fullStateName)} : {description}");
                }

                // Process transitions
                if (stateValue.TryGetProperty("on", out var transitions))
                {
                    ProcessTransitionsForMermaid(sb, transitions, fullStateName, options);
                }

                // Process after transitions (delayed transitions)
                if (stateValue.TryGetProperty("after", out var afterTransitions))
                {
                    ProcessAfterTransitionsForMermaid(sb, afterTransitions, fullStateName, options);
                }

                // Process nested states (compound states)
                if (stateValue.TryGetProperty("states", out var nestedStates))
                {
                    sb.AppendLine($"    state {SanitizeStateName(fullStateName)} {{");
                    ProcessStatesForMermaid(sb, nestedStates, fullStateName, options);
                    sb.AppendLine("    }");
                }
            }
        }

        private static void ProcessTransitionsForMermaid(StringBuilder sb, JsonElement transitions, string fromState, VisualizationOptions options)
        {
            foreach (var transition in transitions.EnumerateObject())
            {
                var eventName = transition.Name;
                var transitionValue = transition.Value;

                if (transitionValue.ValueKind == JsonValueKind.String)
                {
                    // Simple string target
                    var targetState = transitionValue.GetString();
                    sb.AppendLine($"    {SanitizeStateName(fromState)} --> {SanitizeStateName(targetState)} : {eventName}");
                }
                else if (transitionValue.ValueKind == JsonValueKind.Object)
                {
                    // Object with target and possibly conditions
                    if (transitionValue.TryGetProperty("target", out var targetElement))
                    {
                        var targetState = targetElement.GetString();
                        var label = eventName;

                        // Add condition to label if present
                        if (options.IncludeGuards && transitionValue.TryGetProperty("cond", out var condElement))
                        {
                            var condition = condElement.GetString();
                            label += $" [{condition}]";
                        }

                        sb.AppendLine($"    {SanitizeStateName(fromState)} --> {SanitizeStateName(targetState)} : {label}");
                    }
                }
                else if (transitionValue.ValueKind == JsonValueKind.Array)
                {
                    // Array of transition objects
                    foreach (var arrayTransition in transitionValue.EnumerateArray())
                    {
                        if (arrayTransition.TryGetProperty("target", out var targetElement))
                        {
                            var targetState = targetElement.GetString();
                            var label = eventName;

                            if (options.IncludeGuards && arrayTransition.TryGetProperty("cond", out var condElement))
                            {
                                var condition = condElement.GetString();
                                label += $" [{condition}]";
                            }

                            sb.AppendLine($"    {SanitizeStateName(fromState)} --> {SanitizeStateName(targetState)} : {label}");
                        }
                    }
                }
            }
        }

        private static void ProcessAfterTransitionsForMermaid(StringBuilder sb, JsonElement afterTransitions, string fromState, VisualizationOptions options)
        {
            foreach (var after in afterTransitions.EnumerateObject())
            {
                var delay = after.Name;
                var targetState = after.Value.GetString();
                sb.AppendLine($"    {SanitizeStateName(fromState)} --> {SanitizeStateName(targetState)} : after {delay}ms");
            }
        }

        private static void ProcessStatesForDot(StringBuilder sb, JsonElement states, string parentPrefix, VisualizationOptions options)
        {
            foreach (var stateProperty in states.EnumerateObject())
            {
                var stateName = stateProperty.Name;
                var stateValue = stateProperty.Value;
                var fullStateName = string.IsNullOrEmpty(parentPrefix) ? stateName : $"{parentPrefix}_{stateName}";

                // Define state with styling
                var stateLabel = stateName;
                var stateStyle = "style=rounded";

                // Check if it's a final state
                if (stateValue.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "final")
                {
                    stateStyle = "style=filled, fillcolor=lightgray, shape=doublecircle";
                    sb.AppendLine($"    \"{fullStateName}\" [label=\"{stateLabel}\", {stateStyle}];");
                    sb.AppendLine($"    \"{fullStateName}\" -> end [style=invisible];");
                    sb.AppendLine($"    end [shape=circle, style=filled, fillcolor=black, label=\"\", width=0.3, height=0.3];");
                }
                else
                {
                    // Add description to label if available
                    if (options.IncludeDescriptions && stateValue.TryGetProperty("meta", out var meta) &&
                        meta.TryGetProperty("description", out var desc))
                    {
                        stateLabel += $"\\n{desc.GetString()}";
                    }

                    sb.AppendLine($"    \"{fullStateName}\" [label=\"{stateLabel}\", {stateStyle}];");
                }

                // Process transitions
                if (stateValue.TryGetProperty("on", out var transitions))
                {
                    ProcessTransitionsForDot(sb, transitions, fullStateName, options);
                }

                // Process after transitions
                if (stateValue.TryGetProperty("after", out var afterTransitions))
                {
                    ProcessAfterTransitionsForDot(sb, afterTransitions, fullStateName, options);
                }
            }
        }

        private static void ProcessTransitionsForDot(StringBuilder sb, JsonElement transitions, string fromState, VisualizationOptions options)
        {
            foreach (var transition in transitions.EnumerateObject())
            {
                var eventName = transition.Name;
                var transitionValue = transition.Value;

                if (transitionValue.ValueKind == JsonValueKind.String)
                {
                    var targetState = transitionValue.GetString();
                    sb.AppendLine($"    \"{fromState}\" -> \"{targetState}\" [label=\"{eventName}\"];");
                }
                else if (transitionValue.ValueKind == JsonValueKind.Object && transitionValue.TryGetProperty("target", out var targetElement))
                {
                    var targetState = targetElement.GetString();
                    var label = eventName;

                    if (options.IncludeGuards && transitionValue.TryGetProperty("cond", out var condElement))
                    {
                        label += $"\\n[{condElement.GetString()}]";
                    }

                    sb.AppendLine($"    \"{fromState}\" -> \"{targetState}\" [label=\"{label}\"];");
                }
            }
        }

        private static void ProcessAfterTransitionsForDot(StringBuilder sb, JsonElement afterTransitions, string fromState, VisualizationOptions options)
        {
            foreach (var after in afterTransitions.EnumerateObject())
            {
                var delay = after.Name;
                var targetState = after.Value.GetString();
                sb.AppendLine($"    \"{fromState}\" -> \"{targetState}\" [label=\"after {delay}ms\", style=dashed];");
            }
        }

        private static void ProcessStatesForPlantUml(StringBuilder sb, JsonElement states, string parentPrefix, VisualizationOptions options)
        {
            foreach (var stateProperty in states.EnumerateObject())
            {
                var stateName = stateProperty.Name;
                var stateValue = stateProperty.Value;

                // Check if it's a final state
                if (stateValue.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "final")
                {
                    sb.AppendLine($"{SanitizeStateName(stateName)} --> [*]");
                }

                // Process transitions
                if (stateValue.TryGetProperty("on", out var transitions))
                {
                    foreach (var transition in transitions.EnumerateObject())
                    {
                        var eventName = transition.Name;
                        var transitionValue = transition.Value;

                        if (transitionValue.ValueKind == JsonValueKind.String)
                        {
                            var targetState = transitionValue.GetString();
                            sb.AppendLine($"{SanitizeStateName(stateName)} --> {SanitizeStateName(targetState)} : {eventName}");
                        }
                    }
                }
            }
        }

        private static StateAnalysis AnalyzeStateMachine(JsonElement root)
        {
            var analysis = new StateAnalysis();

            if (root.TryGetProperty("states", out var states))
            {
                AnalyzeStatesRecursively(states, analysis, "");
            }

            return analysis;
        }

        private static void AnalyzeStatesRecursively(JsonElement states, StateAnalysis analysis, string parentPrefix)
        {
            foreach (var stateProperty in states.EnumerateObject())
            {
                var stateName = stateProperty.Name;
                var stateValue = stateProperty.Value;

                analysis.TotalStates++;

                // Check state type
                if (stateValue.TryGetProperty("type", out var typeElement))
                {
                    var stateType = typeElement.GetString();
                    switch (stateType)
                    {
                        case "final":
                            analysis.FinalStates++;
                            break;
                        case "compound":
                            analysis.CompoundStates++;
                            break;
                        case "parallel":
                            analysis.ParallelStates++;
                            break;
                    }
                }

                // Count transitions
                if (stateValue.TryGetProperty("on", out var transitions))
                {
                    foreach (var transition in transitions.EnumerateObject())
                    {
                        analysis.TotalTransitions++;
                        analysis.Events.Add(transition.Name);
                    }
                }

                // Count after transitions
                if (stateValue.TryGetProperty("after", out var afterTransitions))
                {
                    analysis.TotalTransitions += afterTransitions.GetArrayLength();
                    analysis.DelayedTransitions += afterTransitions.GetArrayLength();
                }

                // Count actions
                if (stateValue.TryGetProperty("entry", out var entryActions))
                {
                    analysis.TotalActions += CountActions(entryActions);
                }

                if (stateValue.TryGetProperty("exit", out var exitActions))
                {
                    analysis.TotalActions += CountActions(exitActions);
                }

                // Recursively analyze nested states
                if (stateValue.TryGetProperty("states", out var nestedStates))
                {
                    AnalyzeStatesRecursively(nestedStates, analysis, $"{parentPrefix}{stateName}_");
                }
            }
        }

        private static int CountActions(JsonElement actionsElement)
        {
            return actionsElement.ValueKind == JsonValueKind.Array
                ? actionsElement.GetArrayLength()
                : 1;
        }

        private static StateData ExtractStateData(JsonElement root)
        {
            var stateData = new StateData
            {
                Id = root.GetProperty("id").GetString() ?? "StateMachine",
                Initial = root.GetProperty("initial").GetString() ?? "initial",
                States = new Dictionary<string, object>()
            };

            if (root.TryGetProperty("states", out var states))
            {
                ExtractStatesData(states, stateData.States);
            }

            return stateData;
        }

        private static void ExtractStatesData(JsonElement states, Dictionary<string, object> statesDict)
        {
            foreach (var stateProperty in states.EnumerateObject())
            {
                var stateName = stateProperty.Name;
                var stateValue = stateProperty.Value;

                var stateInfo = new Dictionary<string, object>();

                if (stateValue.TryGetProperty("type", out var typeElement))
                {
                    stateInfo["type"] = typeElement.GetString() ?? "";
                }

                if (stateValue.TryGetProperty("on", out var transitions))
                {
                    var transitionsDict = new Dictionary<string, object>();
                    foreach (var transition in transitions.EnumerateObject())
                    {
                        transitionsDict[transition.Name] = transition.Value.ToString();
                    }
                    stateInfo["on"] = transitionsDict;
                }

                statesDict[stateName] = stateInfo;
            }
        }

        private static string GenerateInteractiveHtml(string machineId, StateData stateData, VisualizationOptions options)
        {
            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{machineId} - Interactive State Machine</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            margin: 0;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .container {{
            max-width: 1200px;
            margin: 0 auto;
            background: white;
            border-radius: 8px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
            padding: 20px;
        }}
        .header {{
            text-align: center;
            border-bottom: 2px solid #eee;
            padding-bottom: 20px;
            margin-bottom: 30px;
        }}
        .diagram-area {{
            display: flex;
            gap: 20px;
        }}
        .states-panel {{
            flex: 1;
            border: 1px solid #ddd;
            border-radius: 4px;
            padding: 15px;
        }}
        .visualization-panel {{
            flex: 2;
            border: 1px solid #ddd;
            border-radius: 4px;
            padding: 15px;
            min-height: 400px;
        }}
        .state-item {{
            padding: 8px 12px;
            margin: 5px 0;
            border-radius: 4px;
            border: 1px solid #ccc;
            cursor: pointer;
            transition: all 0.2s;
        }}
        .state-item:hover {{
            background-color: #f0f0f0;
        }}
        .state-item.active {{
            background-color: #4CAF50;
            color: white;
            border-color: #45a049;
        }}
        .state-item.final {{
            background-color: #f44336;
            color: white;
        }}
        .controls {{
            margin-top: 20px;
            text-align: center;
        }}
        .btn {{
            padding: 10px 20px;
            margin: 0 5px;
            border: none;
            border-radius: 4px;
            background-color: #2196F3;
            color: white;
            cursor: pointer;
            transition: background-color 0.2s;
        }}
        .btn:hover {{
            background-color: #1976D2;
        }}
        .event-input {{
            padding: 8px;
            margin: 0 10px;
            border: 1px solid #ccc;
            border-radius: 4px;
        }}
        .log {{
            margin-top: 20px;
            padding: 15px;
            background-color: #f9f9f9;
            border-radius: 4px;
            max-height: 200px;
            overflow-y: auto;
            font-family: monospace;
            font-size: 12px;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>🎯 {machineId}</h1>
            <p>Interactive State Machine Visualization</p>
        </div>

        <div class=""diagram-area"">
            <div class=""states-panel"">
                <h3>States</h3>
                <div id=""statesList""></div>
            </div>

            <div class=""visualization-panel"">
                <h3>Current State: <span id=""currentState"">{stateData.Initial}</span></h3>
                <div id=""visualization"">
                    <p>Visual representation would be rendered here using a graph library like D3.js or vis.js</p>
                </div>
            </div>
        </div>

        <div class=""controls"">
            <input type=""text"" id=""eventInput"" class=""event-input"" placeholder=""Enter event name"">
            <button class=""btn"" onclick=""sendEvent()"">Send Event</button>
            <button class=""btn"" onclick=""resetMachine()"">Reset</button>
        </div>

        <div class=""log"" id=""eventLog"">
            <strong>Event Log:</strong><br>
            Machine initialized in state: {stateData.Initial}<br>
        </div>
    </div>

    <script>
        const stateData = {JsonSerializer.Serialize(stateData)};
        let currentState = '{stateData.Initial}';
        let eventCount = 0;

        function initializeStates() {{
            const statesList = document.getElementById('statesList');
            Object.keys(stateData.states).forEach(stateName => {{
                const stateElement = document.createElement('div');
                stateElement.className = 'state-item';
                stateElement.id = 'state-' + stateName;
                stateElement.textContent = stateName;
                stateElement.onclick = () => highlightState(stateName);

                const stateInfo = stateData.states[stateName];
                if (stateInfo.type === 'final') {{
                    stateElement.classList.add('final');
                }}

                statesList.appendChild(stateElement);
            }});

            updateCurrentState(currentState);
        }}

        function highlightState(stateName) {{
            // Remove previous highlights
            document.querySelectorAll('.state-item').forEach(el => el.classList.remove('active'));

            // Highlight selected state
            document.getElementById('state-' + stateName).classList.add('active');
        }}

        function updateCurrentState(newState) {{
            currentState = newState;
            document.getElementById('currentState').textContent = newState;
            highlightState(newState);
        }}

        function sendEvent() {{
            const eventInput = document.getElementById('eventInput');
            const eventName = eventInput.value.trim();

            if (!eventName) return;

            // Simulate state transition (in a real implementation, this would call the actual state machine)
            const currentStateInfo = stateData.states[currentState];
            if (currentStateInfo && currentStateInfo.on && currentStateInfo.on[eventName]) {{
                const targetState = currentStateInfo.on[eventName];
                updateCurrentState(targetState);
                logEvent(`Event '${{eventName}}' sent: ${{currentState}} -> ${{targetState}}`);
            }} else {{
                logEvent(`Event '${{eventName}}' ignored in state '${{currentState}}'`);
            }}

            eventInput.value = '';
            eventCount++;
        }}

        function resetMachine() {{
            updateCurrentState('{stateData.Initial}');
            logEvent('Machine reset to initial state');
            eventCount = 0;
        }}

        function logEvent(message) {{
            const log = document.getElementById('eventLog');
            const timestamp = new Date().toLocaleTimeString();
            log.innerHTML += `<br>[${{timestamp}}] ${{message}}`;
            log.scrollTop = log.scrollHeight;
        }}

        // Handle Enter key in event input
        document.getElementById('eventInput').addEventListener('keypress', function(e) {{
            if (e.key === 'Enter') {{
                sendEvent();
            }}
        }});

        // Initialize the visualization
        initializeStates();
    </script>
</body>
</html>";
        }

        private static string SanitizeStateName(string stateName)
        {
            return stateName.Replace("-", "_").Replace(".", "_").Replace(" ", "_");
        }
    }

    // Configuration and data models
    public class VisualizationOptions
    {
        public bool IncludeDescriptions { get; set; } = true;
        public bool IncludeGuards { get; set; } = true;
        public bool IncludeActions { get; set; } = true;
        public bool UseColorCoding { get; set; } = true;
        public string Theme { get; set; } = "default";
        public bool CompactMode { get; set; } = false;
    }

    public class StateData
    {
        public string Id { get; set; } = "";
        public string Initial { get; set; } = "";
        public Dictionary<string, object> States { get; set; } = new();
    }

    public class StateAnalysis
    {
        public int TotalStates { get; set; }
        public int FinalStates { get; set; }
        public int CompoundStates { get; set; }
        public int ParallelStates { get; set; }
        public int TotalTransitions { get; set; }
        public int DelayedTransitions { get; set; }
        public int TotalActions { get; set; }
        public HashSet<string> Events { get; set; } = new();
        public int ComplexityScore => TotalStates + TotalTransitions + CompoundStates * 2 + ParallelStates * 3;
    }

    public class VisualizationReport
    {
        public string MachineId { get; set; } = "";
        public StateAnalysis Analysis { get; set; } = new();
        public string MermaidDiagram { get; set; } = "";
        public string DotDiagram { get; set; } = "";
        public string PlantUmlDiagram { get; set; } = "";
        public string HtmlDiagram { get; set; } = "";
        public DateTime GeneratedAt { get; set; }
    }
}