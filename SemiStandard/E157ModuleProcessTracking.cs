using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SemiStandard;

namespace SemiStandard.E157
{
    /// <summary>
    /// SEMI E157 Module Process Tracking
    /// 모듈 단위의 실시간 공정 추적 및 이력 관리
    /// Process Module 내에서 Material(웨이퍼) 처리 상태를 추적
    /// </summary>
    public class E157ModuleProcessTracking
    {
        private readonly StateMachineAdapter _stateMachine;
        private readonly string _moduleId;
        private readonly List<ProcessStep> _processHistory = new();
        private string? _currentMaterialId;
        private string? _currentRecipeStep;
        private DateTime? _stepStartTime;
        
        public string ModuleId => _moduleId;
        public string? CurrentMaterialId => _currentMaterialId;
        public ModuleState CurrentState { get; private set; }
        public IReadOnlyList<ProcessStep> ProcessHistory => _processHistory.AsReadOnly();
        
        public enum ModuleState
        {
            Idle,
            MaterialArrived,
            PreProcessing,
            Processing,
            PostProcessing,
            MaterialComplete,
            Error,
            Maintenance
        }
        
        public class ProcessStep
        {
            public string MaterialId { get; set; } = string.Empty;
            public string StepName { get; set; } = string.Empty;
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public Dictionary<string, object> Parameters { get; set; } = new();
            public Dictionary<string, object> Results { get; set; } = new();
            public string? ErrorCode { get; set; }
        }

        public E157ModuleProcessTracking(string moduleId)
        {
            _moduleId = moduleId;
            
            // Load configuration from JSON file
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "SemiStandard.XStateScripts.E157ModuleProcessTracking.json";
            string config;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    // Fallback to file system if embedded resource not found
                    var configPath = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? "", "XStateScripts", "E157ModuleProcessTracking.json");
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
            
            // Register conditions
            _stateMachine.RegisterCondition("noMaterialPresent", (ctx, evt) => _currentMaterialId == null);
            _stateMachine.RegisterCondition("materialPresent", (ctx, evt) => _currentMaterialId != null);
            
            // Register actions
            _stateMachine.RegisterAction("recordMaterialArrival", (ctx, evt) =>
            {
                if (evt.Data is MaterialData data)
                {
                    _currentMaterialId = data.MaterialId;
                    _currentRecipeStep = data.RecipeStep;
                    Console.WriteLine($"[E157] Module {_moduleId}: Material {_currentMaterialId} arrived for step {_currentRecipeStep}");
                }
            });
            
            _stateMachine.RegisterAction("startPreProcess", (ctx, evt) =>
            {
                _stepStartTime = DateTime.UtcNow;
                var step = new ProcessStep
                {
                    MaterialId = _currentMaterialId ?? "",
                    StepName = "PreProcess",
                    StartTime = _stepStartTime.Value
                };
                _processHistory.Add(step);
                Console.WriteLine($"[E157] Module {_moduleId}: Starting pre-process for {_currentMaterialId}");
            });
            
            _stateMachine.RegisterAction("startMainProcess", (ctx, evt) =>
            {
                _stepStartTime = DateTime.UtcNow;
                var step = new ProcessStep
                {
                    MaterialId = _currentMaterialId ?? "",
                    StepName = _currentRecipeStep ?? "MainProcess",
                    StartTime = _stepStartTime.Value
                };
                _processHistory.Add(step);
                Console.WriteLine($"[E157] Module {_moduleId}: Starting main process {_currentRecipeStep} for {_currentMaterialId}");
            });
            
            _stateMachine.RegisterAction("startPostProcess", (ctx, evt) =>
            {
                _stepStartTime = DateTime.UtcNow;
                var step = new ProcessStep
                {
                    MaterialId = _currentMaterialId ?? "",
                    StepName = "PostProcess",
                    StartTime = _stepStartTime.Value
                };
                _processHistory.Add(step);
                Console.WriteLine($"[E157] Module {_moduleId}: Starting post-process for {_currentMaterialId}");
            });
            
            _stateMachine.RegisterAction("recordPreProcessResults", (ctx, evt) =>
            {
                var lastStep = _processHistory.LastOrDefault();
                if (lastStep != null)
                {
                    lastStep.EndTime = DateTime.UtcNow;
                    if (evt.Data is ProcessResults results)
                    {
                        lastStep.Results = results.Data;
                    }
                }
            });
            
            _stateMachine.RegisterAction("recordProcessResults", (ctx, evt) =>
            {
                var lastStep = _processHistory.LastOrDefault();
                if (lastStep != null)
                {
                    lastStep.EndTime = DateTime.UtcNow;
                    if (evt.Data is ProcessResults results)
                    {
                        lastStep.Results = results.Data;
                    }
                }
            });
            
            _stateMachine.RegisterAction("recordPostProcessResults", (ctx, evt) =>
            {
                var lastStep = _processHistory.LastOrDefault();
                if (lastStep != null)
                {
                    lastStep.EndTime = DateTime.UtcNow;
                    if (evt.Data is ProcessResults results)
                    {
                        lastStep.Results = results.Data;
                    }
                }
            });
            
            _stateMachine.RegisterAction("recordError", (ctx, evt) =>
            {
                var lastStep = _processHistory.LastOrDefault();
                if (lastStep != null && evt.Data is ErrorInfo error)
                {
                    lastStep.ErrorCode = error.Code;
                    lastStep.EndTime = DateTime.UtcNow;
                    Console.WriteLine($"[E157] Module {_moduleId}: Error {error.Code} - {error.Message}");
                }
            });
            
            _stateMachine.RegisterAction("finalizeMaterialProcessing", (ctx, evt) =>
            {
                var totalTime = _processHistory
                    .Where(s => s.MaterialId == _currentMaterialId && s.EndTime.HasValue)
                    .Sum(s => (s.EndTime!.Value - s.StartTime).TotalSeconds);
                    
                Console.WriteLine($"[E157] Module {_moduleId}: Material {_currentMaterialId} complete. Total time: {totalTime:F1}s");
            });
            
            _stateMachine.RegisterAction("generateReport", (ctx, evt) =>
            {
                var report = GenerateProcessReport(_currentMaterialId!);
                OnReportGenerated?.Invoke(this, report);
            });
            
            _stateMachine.RegisterAction("clearMaterial", (ctx, evt) =>
            {
                _currentMaterialId = null;
                _currentRecipeStep = null;
                Console.WriteLine($"[E157] Module {_moduleId}: Material cleared");
            });
            
            _stateMachine.RegisterAction("logIdle", (ctx, evt) =>
            {
                Console.WriteLine($"[E157] Module {_moduleId}: Idle");
            });
            
            _stateMachine.RegisterAction("validateMaterial", (ctx, evt) =>
            {
                Console.WriteLine($"[E157] Module {_moduleId}: Validating material {_currentMaterialId}");
            });
            
            _stateMachine.RegisterAction("rejectMaterial", (ctx, evt) =>
            {
                Console.WriteLine($"[E157] Module {_moduleId}: Material {_currentMaterialId} rejected");
                _currentMaterialId = null;
            });
            
            _stateMachine.RegisterAction("abortAndClear", (ctx, evt) =>
            {
                Console.WriteLine($"[E157] Module {_moduleId}: Aborting and clearing material {_currentMaterialId}");
                _currentMaterialId = null;
            });
            
            _stateMachine.RegisterAction("notifyError", (ctx, evt) =>
            {
                OnError?.Invoke(this, new ErrorEventArgs { ModuleId = _moduleId, MaterialId = _currentMaterialId });
            });
            
            _stateMachine.RegisterAction("startMaintenance", (ctx, evt) =>
            {
                Console.WriteLine($"[E157] Module {_moduleId}: Starting maintenance");
            });
            
            _stateMachine.RegisterAction("endMaintenance", (ctx, evt) =>
            {
                Console.WriteLine($"[E157] Module {_moduleId}: Maintenance completed");
            });
            
            // Register activities (continuous monitoring)
            _stateMachine.RegisterActivity("monitorPreProcess", (ctx, evt) =>
            {
                // Continuous monitoring during pre-process
            });
            
            _stateMachine.RegisterActivity("monitorProcess", (ctx, evt) =>
            {
                // Continuous monitoring during main process
            });
            
            _stateMachine.RegisterActivity("monitorPostProcess", (ctx, evt) =>
            {
                // Continuous monitoring during post-process
            });
            
            _stateMachine.RegisterAction("logError", (ctx, evt) =>
            {
                Console.WriteLine($"[E157] Module {_moduleId}: Error logged");
            });
            
            _stateMachine.RegisterAction("notifyError", (ctx, evt) =>
            {
                Console.WriteLine($"[E157] Module {_moduleId}: Error notification sent");
                OnError?.Invoke(this, new ErrorEventArgs 
                { 
                    ModuleId = _moduleId,
                    MaterialId = _currentMaterialId
                });
            });
            
            _stateMachine.RegisterAction("recordProcessEnd", (ctx, evt) =>
            {
                Console.WriteLine($"[E157] Module {_moduleId}: Process ended at {DateTime.UtcNow}");
            });
            
            _stateMachine.Start();
            UpdateState();
        }
        
        private void UpdateState()
        {
            var state = _stateMachine.CurrentStates.FirstOrDefault()?.Name;
            if (string.IsNullOrEmpty(state) || state == "Unknown")
            {
                CurrentState = ModuleState.Idle;
                return;
            }
            
            if (state.Contains('.'))
                state = state.Split('.')[0]; // Handle nested states
            
            if (Enum.TryParse<ModuleState>(state, out var moduleState))
                CurrentState = moduleState;
        }
        
        // Material Management
        public void MaterialArrive(string materialId, string recipeStep)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "MATERIAL_ARRIVE",
                Data = new MaterialData { MaterialId = materialId, RecipeStep = recipeStep }
            });
            UpdateState();
        }
        
        public void StartPreProcess()
        {
            _stateMachine.Send("START_PRE_PROCESS");
            UpdateState();
        }
        
        public void SkipPreProcess()
        {
            _stateMachine.Send("SKIP_PRE_PROCESS");
            UpdateState();
        }
        
        public void PreProcessComplete(Dictionary<string, object>? results = null)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "PRE_PROCESS_COMPLETE",
                Data = new ProcessResults { Data = results ?? new Dictionary<string, object>() }
            });
            UpdateState();
        }
        
        public void ProcessComplete(Dictionary<string, object>? results = null)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "PROCESS_COMPLETE",
                Data = new ProcessResults { Data = results ?? new Dictionary<string, object>() }
            });
            UpdateState();
        }
        
        public void PostProcessComplete(Dictionary<string, object>? results = null)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "POST_PROCESS_COMPLETE",
                Data = new ProcessResults { Data = results ?? new Dictionary<string, object>() }
            });
            UpdateState();
        }
        
        public void SkipPostProcess()
        {
            _stateMachine.Send("SKIP_POST_PROCESS");
            UpdateState();
        }
        
        public void MaterialRemove()
        {
            _stateMachine.Send("MATERIAL_REMOVE");
            UpdateState();
        }
        
        public void NextMaterial(string materialId, string recipeStep)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "NEXT_MATERIAL",
                Data = new MaterialData { MaterialId = materialId, RecipeStep = recipeStep }
            });
            UpdateState();
        }
        
        // Process Control
        public void Pause()
        {
            _stateMachine.Send("PAUSE");
            UpdateState();
        }
        
        public void Resume()
        {
            _stateMachine.Send("RESUME");
            UpdateState();
        }
        
        public void ReportError(string code, string message)
        {
            var eventName = CurrentState switch
            {
                ModuleState.PreProcessing => "PRE_PROCESS_ERROR",
                ModuleState.Processing => "PROCESS_ERROR",
                ModuleState.PostProcessing => "POST_PROCESS_ERROR",
                _ => "ERROR"
            };
            
            _stateMachine.Send(new StateMachineEvent
            {
                Name = eventName,
                Data = new ErrorInfo { Code = code, Message = message }
            });
            UpdateState();
        }
        
        public void ClearError()
        {
            _stateMachine.Send("ERROR_CLEAR");
            UpdateState();
        }
        
        public void Abort()
        {
            _stateMachine.Send("ABORT");
            UpdateState();
        }
        
        // Maintenance
        public void StartMaintenance()
        {
            _stateMachine.Send("MAINTENANCE_START");
            UpdateState();
        }
        
        public void MaintenanceComplete()
        {
            _stateMachine.Send("MAINTENANCE_COMPLETE");
            UpdateState();
        }
        
        // Reporting
        public ProcessReport GenerateProcessReport(string materialId)
        {
            var steps = _processHistory.Where(s => s.MaterialId == materialId).ToList();
            
            return new ProcessReport
            {
                ModuleId = _moduleId,
                MaterialId = materialId,
                Steps = steps,
                TotalProcessTime = steps
                    .Where(s => s.EndTime.HasValue)
                    .Sum(s => (s.EndTime!.Value - s.StartTime).TotalSeconds),
                Success = steps.All(s => string.IsNullOrEmpty(s.ErrorCode))
            };
        }
        
        public IEnumerable<ProcessStep> GetMaterialHistory(string materialId)
        {
            return _processHistory.Where(s => s.MaterialId == materialId);
        }
        
        // Events
        public event EventHandler<ProcessReport>? OnReportGenerated;
        public event EventHandler<ErrorEventArgs>? OnError;
        
        // Data Classes
        public class MaterialData
        {
            public string MaterialId { get; set; } = string.Empty;
            public string RecipeStep { get; set; } = string.Empty;
        }
        
        public class ProcessResults
        {
            public Dictionary<string, object> Data { get; set; } = new();
        }
        
        public class ErrorInfo
        {
            public string Code { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
        }
        
        public class ProcessReport
        {
            public string ModuleId { get; set; } = string.Empty;
            public string MaterialId { get; set; } = string.Empty;
            public List<ProcessStep> Steps { get; set; } = new();
            public double TotalProcessTime { get; set; }
            public bool Success { get; set; }
        }
        
        public class ErrorEventArgs : EventArgs
        {
            public string ModuleId { get; set; } = string.Empty;
            public string? MaterialId { get; set; }
        }
    }
}
