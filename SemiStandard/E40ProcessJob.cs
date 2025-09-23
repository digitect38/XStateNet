using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
                CurrentState = Enum.Parse<ProcessJobState>(state);
            }
        }
        
        public void Create(string recipeId, List<string> materialIds, ConcurrentDictionary<string, object>? recipeParameters = null)
        {
            _stateMachine.Send(new StateMachineEvent
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
        
        public void Setup()
        {
            _stateMachine.Send("SETUP");
            UpdateState();
        }
        
        public void SetupComplete()
        {
            _stateMachine.Send("SETUP_COMPLETE");
            UpdateState();
        }
        
        public void Start()
        {
            _stateMachine.Send("START");
            UpdateState();
        }
        
        public void ProcessingComplete()
        {
            _stateMachine.Send("PROCESSING_COMPLETE");
            UpdateState();
        }
        
        public void PauseRequest()
        {
            _stateMachine.Send("PAUSE_REQUEST");
            UpdateState();
        }
        
        public void PauseComplete()
        {
            _stateMachine.Send("PAUSE_COMPLETE");
            UpdateState();
        }
        
        public void Resume()
        {
            _stateMachine.Send("RESUME");
            UpdateState();
        }
        
        public void Stop()
        {
            _stateMachine.Send("STOP");
            UpdateState();
        }
        
        public void StopComplete()
        {
            _stateMachine.Send("STOP_COMPLETE");
            UpdateState();
        }
        
        public void Abort()
        {
            _stateMachine.Send("ABORT");
            UpdateState();
        }
        
        public void AbortComplete()
        {
            _stateMachine.Send("ABORT_COMPLETE");
            UpdateState();
        }
        
        public void Remove()
        {
            _stateMachine.Send("REMOVE");
            UpdateState();
        }
        
        public void Error(string errorCode, string errorMessage)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "ERROR",
                Data = new ErrorData { ErrorCode = errorCode, ErrorMessage = errorMessage }
            });
            UpdateState();
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
