using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SemiStandard;

namespace SemiStandard.E40
{
    /// <summary>
    /// SEMI E40 Process Job Management
    /// Process Job은 특정 Recipe를 사용하여 Material(웨이퍼)을 처리하는 작업 단위
    /// </summary>
    public class E40ProcessJob
    {
        private readonly StateMachineAdapter _stateMachine;
        private readonly string _processJobId;
        private readonly List<string> _materialIds = new();
        private string? _recipeId;
        private ConcurrentDictionary<string, object> _recipeParameters = new();
        private DateTime? _startTime;
        private DateTime? _endTime;
        
        public string ProcessJobId => _processJobId;
        public string? RecipeId => _recipeId;
        public IReadOnlyList<string> MaterialIds => _materialIds.AsReadOnly();
        public ProcessJobState CurrentState { get; private set; }
        
        public enum ProcessJobState
        {
            NoState,
            Queued,
            SettingUp,
            WaitingForStart,
            Processing,
            ProcessingComplete,
            Pausing,
            Paused,
            Stopping,
            Aborting,
            Stopped,
            Aborted
        }

        public E40ProcessJob(string processJobId)
        {
            _processJobId = processJobId;
            
            // Load configuration from JSON file
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "SemiStandard.XStateScripts.E40ProcessJob.json";
            string config;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    // Fallback to file system if embedded resource not found
                    var configPath = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? "", "XStateScripts", "E40ProcessJob.json");
                    config = File.ReadAllText(configPath);
                }
                else
                {
                    using (var reader = new StreamReader(stream))
                    {
                        config = reader.ReadToEnd();
                    }
                }
            }

            _stateMachine = StateMachineFactory.Create(config);
            
            // Register actions
            _stateMachine.RegisterAction("assignProcessJobData", (ctx, evt) =>
            {
                if (evt.Data is ProcessJobData data)
                {
                    _recipeId = data.RecipeId;
                    _materialIds.Clear();
                    _materialIds.AddRange(data.MaterialIds);
                    _recipeParameters = data.RecipeParameters ?? new ConcurrentDictionary<string, object>();
                }
            });
            
            _stateMachine.RegisterAction("recordStartTime", (ctx, evt) =>
            {
                _startTime = DateTime.UtcNow;
            });
            
            _stateMachine.RegisterAction("recordEndTime", (ctx, evt) =>
            {
                _endTime = DateTime.UtcNow;
            });
            
            _stateMachine.RegisterAction("prepareResources", (ctx, evt) =>
            {
                Console.WriteLine($"[E40] Process Job {_processJobId}: Preparing resources for recipe {_recipeId}");
            });
            
            _stateMachine.RegisterAction("startProcessing", (ctx, evt) =>
            {
                Console.WriteLine($"[E40] Process Job {_processJobId}: Starting processing with recipe {_recipeId}");
            });
            
            _stateMachine.RegisterAction("stopProcessing", (ctx, evt) =>
            {
                Console.WriteLine($"[E40] Process Job {_processJobId}: Stopping processing");
            });
            
            _stateMachine.RegisterAction("abortProcessing", (ctx, evt) =>
            {
                Console.WriteLine($"[E40] Process Job {_processJobId}: Aborting processing");
            });
            
            _stateMachine.RegisterAction("logQueued", (ctx, evt) =>
            {
                Console.WriteLine($"[E40] Process Job {_processJobId}: Queued");
            });
            
            _stateMachine.RegisterAction("logComplete", (ctx, evt) =>
            {
                var duration = _endTime - _startTime;
                Console.WriteLine($"[E40] Process Job {_processJobId}: Completed. Duration: {duration?.TotalSeconds:F1} seconds");
            });
            
            _stateMachine.RegisterAction("logStopped", (ctx, evt) =>
            {
                Console.WriteLine($"[E40] Process Job {_processJobId}: Stopped");
            });
            
            _stateMachine.RegisterAction("logAborted", (ctx, evt) =>
            {
                Console.WriteLine($"[E40] Process Job {_processJobId}: Aborted");
            });
            
            _stateMachine.RegisterAction("recordError", (ctx, evt) =>
            {
                if (evt.Data is ErrorData error)
                {
                    Console.WriteLine($"[E40] Process Job {_processJobId}: Error - {error.ErrorCode}: {error.ErrorMessage}");
                }
            });
            
            _stateMachine.Start();
            UpdateState();
        }
        
        private void UpdateState()
        {
            var state = _stateMachine.CurrentStates.FirstOrDefault()?.Name;
            if (string.IsNullOrEmpty(state) || state == "Unknown")
            {
                CurrentState = ProcessJobState.NoState;
            }
            else
            {
                // Handle state names that include machine ID prefix
                if (state.Contains('.'))
                {
                    state = state.Split('.').Last();
                }
                // Also handle state names that start with # and contain the actual state after underscore
                if (state.Contains('_'))
                {
                    var parts = state.Split('_');
                    var lastPart = parts.Last();
                    if (Enum.TryParse<ProcessJobState>(lastPart, out var parsedState))
                    {
                        CurrentState = parsedState;
                        return;
                    }
                }

                if (Enum.TryParse<ProcessJobState>(state, out var processJobState))
                {
                    CurrentState = processJobState;
                }
                else
                {
                    CurrentState = ProcessJobState.NoState;
                }
            }
        }
        
        public async Task CreateAsync(string recipeId, List<string> materialIds, ConcurrentDictionary<string, object>? recipeParameters = null)
        {
            await _stateMachine.SendAsync(new StateMachineEvent
            {
                Name = "CREATE",
                Data = new ProcessJobData
                {
                    RecipeId = recipeId,
                    MaterialIds = materialIds,
                    RecipeParameters = recipeParameters ?? new ConcurrentDictionary<string, object>()
                }
            });
            UpdateState();
        }

        public void Create(string recipeId, List<string> materialIds, ConcurrentDictionary<string, object>? recipeParameters = null)
        {
            CreateAsync(recipeId, materialIds, recipeParameters).GetAwaiter().GetResult();
        }
        
        public async Task SetupAsync()
        {
            await _stateMachine.SendAsync("SETUP");
            UpdateState();
        }

        public void Setup()
        {
            SetupAsync().GetAwaiter().GetResult();
        }
        
        public async Task SetupCompleteAsync()
        {
            await _stateMachine.SendAsync("SETUP_COMPLETE");
            UpdateState();
        }

        public void SetupComplete()
        {
            SetupCompleteAsync().GetAwaiter().GetResult();
        }
        
        public async Task StartAsync()
        {
            await _stateMachine.SendAsync("START");
            UpdateState();
        }

        public void Start()
        {
            StartAsync().GetAwaiter().GetResult();
        }
        
        public async Task ProcessingCompleteAsync()
        {
            await _stateMachine.SendAsync("PROCESSING_COMPLETE");
            UpdateState();
        }

        public void ProcessingComplete()
        {
            ProcessingCompleteAsync().GetAwaiter().GetResult();
        }
        
        public async Task PauseRequestAsync()
        {
            await _stateMachine.SendAsync("PAUSE_REQUEST");
            UpdateState();
        }

        public void PauseRequest()
        {
            PauseRequestAsync().GetAwaiter().GetResult();
        }
        
        public async Task PauseCompleteAsync()
        {
            await _stateMachine.SendAsync("PAUSE_COMPLETE");
            UpdateState();
        }

        public void PauseComplete()
        {
            PauseCompleteAsync().GetAwaiter().GetResult();
        }
        
        public async Task ResumeAsync()
        {
            await _stateMachine.SendAsync("RESUME");
            UpdateState();
        }

        public void Resume()
        {
            ResumeAsync().GetAwaiter().GetResult();
        }
        
        public async Task StopAsync()
        {
            await _stateMachine.SendAsync("STOP");
            UpdateState();
        }

        public void Stop()
        {
            StopAsync().GetAwaiter().GetResult();
        }
        
        public async Task StopCompleteAsync()
        {
            await _stateMachine.SendAsync("STOP_COMPLETE");
            UpdateState();
        }

        public void StopComplete()
        {
            StopCompleteAsync().GetAwaiter().GetResult();
        }
        
        public async Task AbortAsync()
        {
            await _stateMachine.SendAsync("ABORT");
            UpdateState();
        }

        public void Abort()
        {
            AbortAsync().GetAwaiter().GetResult();
        }
        
        public async Task AbortCompleteAsync()
        {
            await _stateMachine.SendAsync("ABORT_COMPLETE");
            UpdateState();
        }

        public void AbortComplete()
        {
            AbortCompleteAsync().GetAwaiter().GetResult();
        }
        
        public async Task RemoveAsync()
        {
            await _stateMachine.SendAsync("REMOVE");
            UpdateState();
        }

        public void Remove()
        {
            RemoveAsync().GetAwaiter().GetResult();
        }
        
        public async Task ErrorAsync(string errorCode, string errorMessage)
        {
            await _stateMachine.SendAsync(new StateMachineEvent
            {
                Name = "ERROR",
                Data = new ErrorData { ErrorCode = errorCode, ErrorMessage = errorMessage }
            });
            UpdateState();
        }

        public void Error(string errorCode, string errorMessage)
        {
            ErrorAsync(errorCode, errorMessage).GetAwaiter().GetResult();
        }
        
        public TimeSpan? GetProcessingTime()
        {
            if (_startTime.HasValue && _endTime.HasValue)
                return _endTime.Value - _startTime.Value;
            if (_startTime.HasValue)
                return DateTime.UtcNow - _startTime.Value;
            return null;
        }
        
        public class ProcessJobData
        {
            public string RecipeId { get; set; } = string.Empty;
            public List<string> MaterialIds { get; set; } = new();
            public ConcurrentDictionary<string, object> RecipeParameters { get; set; } = new();
        }
        
        public class ErrorData
        {
            public string ErrorCode { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
        }
    }
}
