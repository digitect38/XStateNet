using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XStateNet;

namespace SemiStandard
{
    /// <summary>
    /// Refactored E40 Process Job implementation that directly exposes XStateNet state machine
    /// without dual state management (no enum mapping, no UpdateState method)
    /// </summary>
    public class RefactoredE40ProcessJob : IDisposable
    {
        private readonly StateMachine _stateMachine;
        private readonly string _processJobId;
        private bool _disposed;

        public string ProcessJobId => _processJobId;

        /// <summary>
        /// Direct access to the state machine for full XStateNet capabilities
        /// </summary>
        public StateMachine StateMachine => _stateMachine;

        /// <summary>
        /// Get current state name directly from state machine
        /// </summary>
        public string CurrentStateName
        {
            get
            {
                var stateString = _stateMachine.GetActiveStateNames();
                if (string.IsNullOrEmpty(stateString))
                    return string.Empty;

                // Extract state name after the last dot (e.g., "#E40_ProcessJob.NoState" -> "NoState")
                if (stateString.Contains('.'))
                    return stateString.Substring(stateString.LastIndexOf('.') + 1);

                return stateString;
            }
        }

        /// <summary>
        /// Check if in a specific state
        /// </summary>
        public bool IsInState(string stateName)
        {
            // Try with the full state path first (e.g., "#E40_ProcessJob.Aborting")
            var fullStateName = $"#E40_ProcessJob.{stateName}";
            try
            {
                return _stateMachine.IsInState(fullStateName);
            }
            catch
            {
                // If that fails, try just the state name
                try
                {
                    return _stateMachine.IsInState(stateName);
                }
                catch
                {
                    // State doesn't exist, return false
                    return false;
                }
            }
        }

        /// <summary>
        /// Get all active states (useful for parallel states)
        /// </summary>
        public List<CompoundState> ActiveStates
        {
            get
            {
                var states = _stateMachine.GetActiveStates();
                // If no active states found, try to get the root state at least
                if (states == null || states.Count == 0)
                {
                    if (_stateMachine.RootState != null)
                    {
                        states = new List<CompoundState> { _stateMachine.RootState };
                    }
                }
                return states ?? new List<CompoundState>();
            }
        }

        /// <summary>
        /// Event raised when state changes
        /// </summary>
        public event Action<string>? StateChanged;

        /// <summary>
        /// Event raised when an error occurs
        /// </summary>
        public event Action<Exception>? ErrorOccurred;

        public RefactoredE40ProcessJob(string processJobId)
        {
            _processJobId = processJobId ?? throw new ArgumentNullException(nameof(processJobId));

            // Load script from external JSON file
            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "XStateScripts", "E40ProcessJob.json");

            if (!File.Exists(scriptPath))
            {
                // Fallback to relative path if not found in base directory
                scriptPath = Path.Combine("..", "..", "..", "SemiStandard",
                    "XStateScripts", "E40ProcessJob.json");
            }

            // Create actions for the state machine
            var actions = new ActionMap
            {
                ["logStateEntry"] = new List<NamedAction> {
                    new NamedAction("logStateEntry", sm =>
                        LogMessage($"Entering state"))
                },
                ["logStateExit"] = new List<NamedAction> {
                    new NamedAction("logStateExit", sm =>
                        LogMessage($"Exiting state"))
                },
                ["validateProcessJob"] = new List<NamedAction> {
                    new NamedAction("validateProcessJob", sm => ValidateProcessJob(sm))
                },
                ["allocateResources"] = new List<NamedAction> {
                    new NamedAction("allocateResources", sm => AllocateResources(sm))
                },
                ["startProcessing"] = new List<NamedAction> {
                    new NamedAction("startProcessing", sm => StartProcessing(sm))
                },
                ["recordStartTime"] = new List<NamedAction> {
                    new NamedAction("recordStartTime", sm => RecordStartTime(sm))
                },
                ["recordEndTime"] = new List<NamedAction> {
                    new NamedAction("recordEndTime", sm => RecordEndTime(sm))
                },
                ["assignProcessJobData"] = new List<NamedAction> {
                    new NamedAction("assignProcessJobData", sm => AssignProcessJobData(sm))
                },
                ["recordError"] = new List<NamedAction> {
                    new NamedAction("recordError", sm => RecordError(sm))
                },
                ["completeProcessing"] = new List<NamedAction> {
                    new NamedAction("completeProcessing", sm => CompleteProcessing(sm))
                },
                ["releaseResources"] = new List<NamedAction> {
                    new NamedAction("releaseResources", sm => ReleaseResources(sm))
                },
                ["handleError"] = new List<NamedAction> {
                    new NamedAction("handleError", sm => HandleError(sm))
                },
                ["handleAbort"] = new List<NamedAction> {
                    new NamedAction("handleAbort", sm => HandleAbort(sm))
                }
            };

            // Create guards for conditional transitions
            var guards = new GuardMap
            {
                // Guards are not defined in the E40ProcessJob.json, so we'll leave this empty
                // The state machine will work without guards
            };

            // Create the state machine from the external script file            
            var jsonScript = Security.SafeReadFile(scriptPath);
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(jsonScript, false, false, actions, guards, null, null, null);

            // Initialize context to match JSON schema
            _stateMachine.ContextMap["processJobId"] = _processJobId;
            _stateMachine.ContextMap["recipeId"] = null;
            _stateMachine.ContextMap["materialIds"] = new List<string>();
            _stateMachine.ContextMap["startTime"] = null;
            _stateMachine.ContextMap["endTime"] = null;
            _stateMachine.ContextMap["errorCode"] = null;

            // Wire up state machine events
            _stateMachine.StateChanged += OnStateMachineStateChanged;
            _stateMachine.ErrorOccurred += OnStateMachineError;

            // Start the state machine to set initial state
            _stateMachine.Start();
        }

        private void OnStateMachineStateChanged(string newState)
        {
            // Extract state name after the last dot (e.g., "#E40_ProcessJob.NoState" -> "NoState")
            if (newState.Contains('.'))
                newState = newState.Substring(newState.LastIndexOf('.') + 1);

            StateChanged?.Invoke(newState);
        }

        private void OnStateMachineError(Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
        }

        /// <summary>
        /// Start the process job (already started in constructor, this just returns current state)
        /// </summary>
        public async Task<string> StartAsync()
        {
            // State machine is already started in constructor
            return await Task.FromResult(CurrentStateName);
        }

        /// <summary>
        /// Send an event to the state machine
        /// </summary>
        public async Task SendEventAsync(string eventName, object? eventData = null)
        {
            await _stateMachine.SendAsync(eventName, eventData);
        }

        /// <summary>
        /// Send an event and get the resulting state
        /// </summary>
        public async Task<string> SendEventWithStateAsync(string eventName, object? eventData = null)
        {
            return await _stateMachine.SendAsync(eventName, eventData);
        }

        /// <summary>
        /// Create and queue the process job for execution
        /// </summary>
        public async Task CreateAsync()
        {
            await SendEventAsync("CREATE");
        }

        /// <summary>
        /// Setup the job
        /// </summary>
        public async Task SetupAsync()
        {
            await SendEventAsync("SETUP");
        }

        /// <summary>
        /// Complete setup
        /// </summary>
        public async Task CompleteSetupAsync()
        {
            await SendEventAsync("SETUP_COMPLETE");
        }

        /// <summary>
        /// Start processing the job
        /// </summary>
        public async Task StartProcessingAsync()
        {
            await SendEventAsync("START");
        }

        /// <summary>
        /// Complete processing
        /// </summary>
        public async Task CompleteProcessingAsync()
        {
            await SendEventAsync("PROCESSING_COMPLETE");
        }

        /// <summary>
        /// Abort the process job
        /// </summary>
        public async Task AbortAsync(string reason)
        {
            _stateMachine.ContextMap["abortReason"] = reason;
            await SendEventAsync("ABORT");
        }

        /// <summary>
        /// Handle an error in processing
        /// </summary>
        public async Task ReportErrorAsync(Exception error)
        {
            _stateMachine.ContextMap["errorCode"] = error.Message;
            await SendEventAsync("ERROR");
        }

        // Action implementations
        private void ValidateProcessJob(StateMachine sm)
        {
            LogMessage($"Validating process job {_processJobId}");
            // Validation logic here
        }

        private void AllocateResources(StateMachine sm)
        {
            LogMessage($"Allocating resources for job {_processJobId}");
            // Resource allocation logic here
        }

        private void StartProcessing(StateMachine sm)
        {
            LogMessage($"Starting processing for job {_processJobId}");
            sm.ContextMap["currentStep"] = 0;
        }

        private void AssignProcessJobData(StateMachine sm)
        {
            LogMessage($"Assigning process job data for {_processJobId}");
            // Process job data is already set in constructor
        }

        private void RecordStartTime(StateMachine sm)
        {
            sm.ContextMap["startTime"] = DateTime.UtcNow;
            LogMessage($"Started processing at {sm.ContextMap["startTime"]}");
        }

        private void RecordEndTime(StateMachine sm)
        {
            sm.ContextMap["endTime"] = DateTime.UtcNow;
            LogMessage($"Ended processing at {sm.ContextMap["endTime"]}");
        }

        private void RecordError(StateMachine sm)
        {
            LogMessage($"Error recorded: {sm.ContextMap["errorCode"]}");
        }

        private void CompleteProcessing(StateMachine sm)
        {
            LogMessage($"Completing processing for job {_processJobId}");
        }

        private void ReleaseResources(StateMachine sm)
        {
            LogMessage($"Releasing resources for job {_processJobId}");
        }

        private void HandleError(StateMachine sm)
        {
            var errorCode = sm.ContextMap["errorCode"];
            LogMessage($"Error occurred in job {_processJobId}. Error code: {errorCode}");
        }

        private void HandleAbort(StateMachine sm)
        {
            // In a real implementation, the abort reason would be passed through context
            var reason = sm.ContextMap["abortReason"] ?? "Unknown";
            LogMessage($"Aborting job {_processJobId}. Reason: {reason}");
        }

        // No guard implementations needed as the E40ProcessJob.json doesn't define guards

        private void LogMessage(string message)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stateMachine.StateChanged -= OnStateMachineStateChanged;
                _stateMachine.ErrorOccurred -= OnStateMachineError;
                _stateMachine.Dispose();
                _disposed = true;
            }
        }
    }
}