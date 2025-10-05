using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XStateNet.GPU.Core
{
    /// <summary>
    /// Analyzes state machine complexity to determine GPU suitability
    /// </summary>
    public class StateMachineComplexityAnalyzer
    {
        private static readonly ILogger _logger = Log.ForContext<StateMachineComplexityAnalyzer>();

        public enum ComplexityLevel
        {
            Simple,      // Suitable for GPU
            Moderate,    // GPU with limitations
            Complex,     // CPU recommended
            VeryComplex  // CPU required
        }

        public class ComplexityReport
        {
            public ComplexityLevel Level { get; set; }
            public bool GpuSuitable { get; set; }
            public List<string> Reasons { get; set; } = new List<string>();
            public ConcurrentDictionary<string, int> Metrics { get; set; } = new ConcurrentDictionary<string, int>();

            // Feature flags
            public bool HasHierarchicalStates { get; set; }
            public bool HasParallelStates { get; set; }
            public bool HasHistory { get; set; }
            public bool HasInvoke { get; set; }
            public bool HasDelayedTransitions { get; set; }
            public bool HasComplexGuards { get; set; }
            public bool HasComplexActions { get; set; }
            public bool HasDynamicTransitions { get; set; }
        }

        /// <summary>
        /// Analyze a state machine JSON definition for complexity
        /// </summary>
        public static ComplexityReport Analyze(string jsonDefinition)
        {
            var report = new ComplexityReport();

            try
            {
                var json = JObject.Parse(jsonDefinition);

                // Basic metrics
                var states = ExtractStates(json);
                var events = ExtractEvents(json);
                var transitions = CountTransitions(json);

                report.Metrics["StateCount"] = states.Count;
                report.Metrics["EventCount"] = events.Count;
                report.Metrics["TransitionCount"] = transitions;

                _logger.Information("Analyzing state machine complexity:");
                _logger.Information("  - States: {StateCount}", states.Count);
                _logger.Information("  - Events: {EventCount}", events.Count);
                _logger.Information("  - Transitions: {TransitionCount}", transitions);

                // Check for complex features
                CheckHierarchicalStates(json, report);
                CheckParallelStates(json, report);
                CheckHistoryStates(json, report);
                CheckInvokeServices(json, report);
                CheckDelayedTransitions(json, report);
                CheckComplexGuards(json, report);
                CheckComplexActions(json, report);
                CheckDynamicFeatures(json, report);

                // Calculate overall complexity
                DetermineComplexityLevel(report);

                // Log the decision
                LogComplexityDecision(report);

                return report;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to analyze state machine complexity");
                report.Level = ComplexityLevel.VeryComplex;
                report.GpuSuitable = false;
                report.Reasons.Add($"Analysis failed: {ex.Message}");
                return report;
            }
        }

        private static HashSet<string> ExtractStates(JObject json)
        {
            var states = new HashSet<string>();

            // Add initial state
            var initial = json["initial"]?.ToString();
            if (!string.IsNullOrEmpty(initial))
                states.Add(initial);

            // Extract all state names
            var statesObj = json["states"] as JObject;
            if (statesObj != null)
            {
                foreach (var state in statesObj.Properties())
                {
                    states.Add(state.Name);

                    // Check for nested states (hierarchical)
                    var stateValue = state.Value as JObject;
                    if (stateValue?.ContainsKey("states") == true)
                    {
                        var nestedStates = stateValue["states"] as JObject;
                        if (nestedStates != null)
                        {
                            foreach (var nested in nestedStates.Properties())
                            {
                                states.Add($"{state.Name}.{nested.Name}");
                            }
                        }
                    }
                }
            }

            return states;
        }

        private static HashSet<string> ExtractEvents(JObject json)
        {
            var events = new HashSet<string>();
            var statesObj = json["states"] as JObject;

            if (statesObj != null)
            {
                foreach (var state in statesObj.Properties())
                {
                    ExtractEventsFromState(state.Value as JObject, events);
                }
            }

            return events;
        }

        private static void ExtractEventsFromState(JObject stateObj, HashSet<string> events)
        {
            if (stateObj == null) return;

            // Check "on" transitions
            var on = stateObj["on"] as JObject;
            if (on != null)
            {
                foreach (var transition in on.Properties())
                {
                    events.Add(transition.Name);
                }
            }

            // Check nested states
            var states = stateObj["states"] as JObject;
            if (states != null)
            {
                foreach (var nestedState in states.Properties())
                {
                    ExtractEventsFromState(nestedState.Value as JObject, events);
                }
            }
        }

        private static int CountTransitions(JObject json)
        {
            int count = 0;
            var statesObj = json["states"] as JObject;

            if (statesObj != null)
            {
                foreach (var state in statesObj.Properties())
                {
                    count += CountTransitionsInState(state.Value as JObject);
                }
            }

            return count;
        }

        private static int CountTransitionsInState(JObject stateObj)
        {
            if (stateObj == null) return 0;

            int count = 0;

            var on = stateObj["on"] as JObject;
            if (on != null)
            {
                count += on.Properties().Count();
            }

            // Check nested states
            var states = stateObj["states"] as JObject;
            if (states != null)
            {
                foreach (var nestedState in states.Properties())
                {
                    count += CountTransitionsInState(nestedState.Value as JObject);
                }
            }

            return count;
        }

        private static void CheckHierarchicalStates(JObject json, ComplexityReport report)
        {
            var statesObj = json["states"] as JObject;
            if (statesObj != null)
            {
                foreach (var state in statesObj.Properties())
                {
                    var stateValue = state.Value as JObject;
                    if (stateValue?.ContainsKey("states") == true)
                    {
                        report.HasHierarchicalStates = true;
                        report.Reasons.Add("Contains hierarchical/nested states (not GPU optimized)");
                        _logger.Information("  ⚠ Found hierarchical states in '{StateName}'", state.Name);
                        break;
                    }
                }
            }
        }

        private static void CheckParallelStates(JObject json, ComplexityReport report)
        {
            var statesObj = json["states"] as JObject;
            if (statesObj != null)
            {
                foreach (var state in statesObj.Properties())
                {
                    var stateValue = state.Value as JObject;
                    if (stateValue?.ContainsKey("type") == true &&
                        stateValue["type"]?.ToString() == "parallel")
                    {
                        report.HasParallelStates = true;
                        report.Reasons.Add("Contains parallel states (requires CPU)");
                        _logger.Information("  ⚠ Found parallel states in '{StateName}'", state.Name);
                        break;
                    }
                }
            }
        }

        private static void CheckHistoryStates(JObject json, ComplexityReport report)
        {
            var statesObj = json["states"] as JObject;
            if (statesObj != null)
            {
                foreach (var state in statesObj.Properties())
                {
                    var stateValue = state.Value as JObject;
                    if (stateValue?.ContainsKey("history") == true ||
                        stateValue?.ContainsKey("type") == true &&
                        stateValue["type"]?.ToString() == "history")
                    {
                        report.HasHistory = true;
                        report.Reasons.Add("Contains history states (complex memory management)");
                        _logger.Information("  ⚠ Found history state in '{StateName}'", state.Name);
                        break;
                    }
                }
            }
        }

        private static void CheckInvokeServices(JObject json, ComplexityReport report)
        {
            var jsonString = json.ToString();
            if (jsonString.Contains("\"invoke\"") || jsonString.Contains("\"spawn\""))
            {
                report.HasInvoke = true;
                report.Reasons.Add("Contains invoke/spawn services (requires CPU for async operations)");
                _logger.Information("  ⚠ Found invoke/spawn services");
            }
        }

        private static void CheckDelayedTransitions(JObject json, ComplexityReport report)
        {
            var jsonString = json.ToString();
            if (jsonString.Contains("\"after\"") || jsonString.Contains("\"delay\""))
            {
                report.HasDelayedTransitions = true;
                report.Reasons.Add("Contains delayed transitions (requires timer management)");
                _logger.Information("  ⚠ Found delayed transitions");
            }
        }

        private static void CheckComplexGuards(JObject json, ComplexityReport report)
        {
            var jsonString = json.ToString();
            if (jsonString.Contains("\"cond\"") || jsonString.Contains("\"guard\""))
            {
                report.HasComplexGuards = true;
                report.Reasons.Add("Contains guard conditions (may require CPU evaluation)");
                _logger.Information("  ⚠ Found guard conditions");
            }
        }

        private static void CheckComplexActions(JObject json, ComplexityReport report)
        {
            var jsonString = json.ToString();
            if (jsonString.Contains("\"actions\"") || jsonString.Contains("\"entry\"") ||
                jsonString.Contains("\"exit\"") || jsonString.Contains("\"activities\""))
            {
                report.HasComplexActions = true;
                // This is moderate complexity - actions run on CPU anyway
                _logger.Information("  ℹ Found actions/activities (will execute on CPU)");
            }
        }

        private static void CheckDynamicFeatures(JObject json, ComplexityReport report)
        {
            var jsonString = json.ToString();
            if (jsonString.Contains("\"assign\"") || jsonString.Contains("\"context\""))
            {
                report.HasDynamicTransitions = true;
                report.Reasons.Add("Contains context/assign operations (requires complex state management)");
                _logger.Information("  ⚠ Found context/assign operations");
            }
        }

        private static void DetermineComplexityLevel(ComplexityReport report)
        {
            int complexityScore = 0;

            // Critical features that require CPU
            if (report.HasParallelStates || report.HasInvoke)
            {
                complexityScore += 10;
            }

            // Features that significantly increase complexity
            if (report.HasHierarchicalStates || report.HasHistory || report.HasDelayedTransitions)
            {
                complexityScore += 5;
            }

            // Moderate complexity features
            if (report.HasComplexGuards || report.HasDynamicTransitions)
            {
                complexityScore += 3;
            }

            // Size-based complexity
            if (report.Metrics["StateCount"] > 50) complexityScore += 3;
            if (report.Metrics["EventCount"] > 100) complexityScore += 2;
            if (report.Metrics["TransitionCount"] > 200) complexityScore += 2;

            // Determine level
            if (complexityScore >= 10)
            {
                report.Level = ComplexityLevel.VeryComplex;
                report.GpuSuitable = false;
            }
            else if (complexityScore >= 5)
            {
                report.Level = ComplexityLevel.Complex;
                report.GpuSuitable = false;
            }
            else if (complexityScore >= 3)
            {
                report.Level = ComplexityLevel.Moderate;
                report.GpuSuitable = true; // Can try GPU with limitations
            }
            else
            {
                report.Level = ComplexityLevel.Simple;
                report.GpuSuitable = true;
            }

            report.Metrics["ComplexityScore"] = complexityScore;
        }

        private static void LogComplexityDecision(ComplexityReport report)
        {
            _logger.Information("=== Complexity Analysis Result ===");
            _logger.Information("  Level: {Level}", report.Level);
            _logger.Information("  GPU Suitable: {GpuSuitable}", report.GpuSuitable);
            _logger.Information("  Complexity Score: {Score}", report.Metrics["ComplexityScore"]);

            if (report.Reasons.Any())
            {
                _logger.Information("  Reasons:");
                foreach (var reason in report.Reasons)
                {
                    _logger.Information("    - {Reason}", reason);
                }
            }

            if (report.GpuSuitable)
            {
                _logger.Information("  ✓ State machine is suitable for GPU acceleration");
            }
            else
            {
                _logger.Warning("  ✗ State machine is too complex for GPU - will use CPU");
            }
            _logger.Information("==================================");
        }
    }
}