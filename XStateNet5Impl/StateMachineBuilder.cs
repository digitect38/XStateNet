using System;
using System.Collections.Generic;

namespace XStateNet
{
    /// <summary>
    /// Builder pattern for creating state machines with built-in isolation support
    /// </summary>
    public class StateMachineBuilder
    {
        private string _jsonScript = string.Empty;
        private string _baseId = string.Empty;
        private IsolationMode _isolationMode = IsolationMode.None;
        private string? _isolationPrefix;
        private ActionMap? _actionMap;
        private Dictionary<string, object> _context = new();
        private bool _autoStart = false;

        /// <summary>
        /// Isolation modes for state machine instances
        /// </summary>
        public enum IsolationMode
        {
            /// <summary>No isolation - use base ID as-is</summary>
            None,
            /// <summary>Add GUID suffix for uniqueness</summary>
            Guid,
            /// <summary>Add timestamp suffix for uniqueness</summary>
            Timestamp,
            /// <summary>Add counter suffix for uniqueness</summary>
            Counter,
            /// <summary>Custom isolation with provided prefix</summary>
            Custom,
            /// <summary>Test mode - combines prefix with GUID for maximum isolation</summary>
            Test
        }

        private static int _instanceCounter = 0;
        private static readonly object _counterLock = new object();

        /// <summary>
        /// Set the JSON script for the state machine
        /// </summary>
        public StateMachineBuilder WithJsonScript(string jsonScript)
        {
            _jsonScript = jsonScript;
            return this;
        }

        /// <summary>
        /// Set the base ID that will be replaced in the JSON
        /// </summary>
        public StateMachineBuilder WithBaseId(string baseId)
        {
            _baseId = baseId;
            return this;
        }

        /// <summary>
        /// Configure isolation mode for this state machine
        /// </summary>
        public StateMachineBuilder WithIsolation(IsolationMode mode, string? prefix = null)
        {
            _isolationMode = mode;
            _isolationPrefix = prefix;
            return this;
        }

        /// <summary>
        /// Set the action map for the state machine
        /// </summary>
        public StateMachineBuilder WithActionMap(ActionMap actionMap)
        {
            _actionMap = actionMap;
            return this;
        }

        /// <summary>
        /// Add context data to the state machine
        /// </summary>
        public StateMachineBuilder WithContext(string key, object value)
        {
            _context[key] = value;
            return this;
        }

        /// <summary>
        /// Configure whether to auto-start the state machine
        /// </summary>
        public StateMachineBuilder WithAutoStart(bool autoStart = true)
        {
            _autoStart = autoStart;
            return this;
        }

        /// <summary>
        /// Build the state machine with configured options
        /// </summary>
        public StateMachine Build(string? instanceId = null)
        {
            if (string.IsNullOrEmpty(_jsonScript))
                throw new InvalidOperationException("JSON script is required");

            var processedScript = ProcessJsonScript(instanceId);
            var stateMachine = StateMachine.CreateFromScript(processedScript, _actionMap);

            // Apply context
            foreach (var kvp in _context)
            {
                stateMachine.Context[kvp.Key] = kvp.Value;
            }

            if (_autoStart)
            {
                stateMachine.Start();
            }

            return stateMachine;
        }

        /// <summary>
        /// Process the JSON script with isolation
        /// </summary>
        private string ProcessJsonScript(string? instanceId)
        {
            if (_isolationMode == IsolationMode.None || string.IsNullOrEmpty(_baseId))
                return _jsonScript;

            var uniqueId = GenerateUniqueId(instanceId);
            var searchPattern = $"\"id\": \"{_baseId}\"";
            var replacement = $"\"id\": \"{uniqueId}\"";
            
            return _jsonScript.Replace(searchPattern, replacement);
        }

        /// <summary>
        /// Generate a unique ID based on isolation mode
        /// </summary>
        private string GenerateUniqueId(string? instanceId)
        {
            var baseId = string.IsNullOrEmpty(instanceId) ? _baseId : instanceId;
            
            return _isolationMode switch
            {
                IsolationMode.Guid => $"{baseId}_{Guid.NewGuid():N}".Substring(0, Math.Min(baseId.Length + 33, 64)),
                IsolationMode.Timestamp => $"{baseId}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                IsolationMode.Counter => $"{baseId}_{GetNextCounter()}",
                IsolationMode.Custom => string.IsNullOrEmpty(_isolationPrefix) ? baseId : $"{_isolationPrefix}_{baseId}",
                IsolationMode.Test => $"{_isolationPrefix ?? "Test"}_{baseId}_{Guid.NewGuid().ToString("N")[..8]}",
                _ => baseId
            };
        }

        /// <summary>
        /// Get next counter value (thread-safe)
        /// </summary>
        private static int GetNextCounter()
        {
            lock (_counterLock)
            {
                return ++_instanceCounter;
            }
        }

        /// <summary>
        /// Create a builder configured for test scenarios
        /// </summary>
        public static StateMachineBuilder ForTesting()
        {
            return new StateMachineBuilder()
                .WithIsolation(IsolationMode.Test, "Test")
                .WithAutoStart(true);
        }

        /// <summary>
        /// Create a builder configured for production with isolation
        /// </summary>
        public static StateMachineBuilder ForProduction()
        {
            return new StateMachineBuilder()
                .WithIsolation(IsolationMode.Guid)
                .WithAutoStart(true);
        }
    }

    /// <summary>
    /// Extension methods for StateMachine creation with isolation
    /// </summary>
    public static class StateMachineExtensions
    {
        /// <summary>
        /// Create a state machine from script with automatic GUID isolation
        /// </summary>
        public static StateMachine CreateFromScriptWithIsolation(
            string jsonScript, 
            string baseId, 
            string? instanceId = null,
            ActionMap? actionMap = null)
        {
            return new StateMachineBuilder()
                .WithJsonScript(jsonScript)
                .WithBaseId(baseId)
                .WithIsolation(StateMachineBuilder.IsolationMode.Guid)
                .WithActionMap(actionMap ?? new ActionMap())
                .WithAutoStart(true)
                .Build(instanceId);
        }

        /// <summary>
        /// Create a test-isolated state machine
        /// </summary>
        public static StateMachine CreateForTesting(
            string jsonScript,
            string baseId,
            string testName,
            ActionMap? actionMap = null)
        {
            return StateMachineBuilder.ForTesting()
                .WithJsonScript(jsonScript)
                .WithBaseId(baseId)
                .WithActionMap(actionMap ?? new ActionMap())
                .Build($"{testName}_{Guid.NewGuid().ToString("N")[..8]}");
        }
    }
}