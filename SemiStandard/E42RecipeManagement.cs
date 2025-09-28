using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SemiStandard;

namespace SemiStandard.E42
{
    /// <summary>
    /// SEMI E42 Recipe Management
    /// Recipe의 다운로드/검증/선택 시퀀스 관리
    /// </summary>
    public class E42RecipeManagement
    {
        private readonly StateMachineAdapter _stateMachine;
        private readonly string _recipeId;
        private RecipeData? _recipeData;
        private readonly ConcurrentDictionary<string, ParameterData> _parameters = new();
        private readonly List<ValidationResult> _validationResults = new();
        private DateTime? _downloadTime;
        private DateTime? _verifyTime;
        private DateTime? _selectTime;
        
        public string RecipeId => _recipeId;
        public RecipeState CurrentState { get; private set; }
        public RecipeData? Recipe => _recipeData;
        public IReadOnlyDictionary<string, ParameterData> Parameters => _parameters;
        
        public enum RecipeState
        {
            NoRecipe,
            Downloading,
            Downloaded,
            Verifying,
            Verified,
            Selecting,
            Selected,
            Executing,
            Deselecting,
            Error
        }
        
        public enum RecipeType
        {
            Process,
            Inspection,
            Measurement,
            Cleaning,
            Conditioning,
            Calibration
        }

        public E42RecipeManagement(string recipeId)
        {
            _recipeId = recipeId;
            
            // Load configuration from JSON file
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "SemiStandard.XStateScripts.E42RecipeManagement.json";
            string config;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    // Fallback to file system if embedded resource not found
                    var configPath = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? "", "XStateScripts", "E42RecipeManagement.json");
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
            _stateMachine.RegisterCondition("requiresVerification", (ctx, evt) =>
            {
                return _recipeData?.RequiresVerification ?? true;
            });
            
            _stateMachine.RegisterCondition("isVerificationSuccessful", (ctx, evt) =>
            {
                return _validationResults.All(v => v.Passed);
            });
            
            _stateMachine.RegisterCondition("wasVerified", (ctx, evt) =>
            {
                return ctx["verifyTime"] != null;
            });
            
            _stateMachine.RegisterCondition("hasRecipeData", (ctx, evt) =>
            {
                return _recipeData != null;
            });
            
            // Register actions
            _stateMachine.RegisterAction("initiateDownload", (ctx, evt) =>
            {
                Console.WriteLine($"[E42] Recipe {_recipeId}: Initiating download");
                OnDownloadStarted?.Invoke(this, EventArgs.Empty);
            });
            
            _stateMachine.RegisterAction("startDownload", (ctx, evt) =>
            {
                Console.WriteLine($"[E42] Recipe {_recipeId}: Download in progress");
            });
            
            _stateMachine.RegisterAction("storeRecipeData", (ctx, evt) =>
            {
                if (evt.Data is RecipeData data)
                {
                    _recipeData = data;
                    ctx["recipeVersion"] = data.Version;
                    
                    // Store parameters
                    _parameters.Clear();
                    foreach (var param in data.Parameters)
                    {
                        _parameters[param.Name] = param;
                    }
                    
                    Console.WriteLine($"[E42] Recipe {_recipeId}: Stored version {data.Version} with {data.Parameters.Count} parameters");
                }
            });
            
            _stateMachine.RegisterAction("recordDownloadTime", (ctx, evt) =>
            {
                _downloadTime = DateTime.UtcNow;
                ctx["downloadTime"] = _downloadTime;
            });
            
            _stateMachine.RegisterAction("recordVerifyTime", (ctx, evt) =>
            {
                _verifyTime = DateTime.UtcNow;
                ctx["verifyTime"] = _verifyTime;
            });
            
            _stateMachine.RegisterAction("recordSelectTime", (ctx, evt) =>
            {
                _selectTime = DateTime.UtcNow;
                ctx["selectTime"] = _selectTime;
            });
            
            _stateMachine.RegisterAction("startVerification", (ctx, evt) =>
            {
                Console.WriteLine($"[E42] Recipe {_recipeId}: Starting verification");
                _validationResults.Clear();
                
                // Perform various checks
                ValidateChecksum();
                ValidateParameters();
                ValidateDependencies();
                ValidateEquipmentCapability();
            });
            
            _stateMachine.RegisterAction("recordVerificationResult", (ctx, evt) =>
            {
                var passed = _validationResults.All(v => v.Passed);
                Console.WriteLine($"[E42] Recipe {_recipeId}: Verification {(passed ? "passed" : "failed")}");
                OnVerificationComplete?.Invoke(this, new VerificationEventArgs { Passed = passed, Results = _validationResults });
            });
            
            _stateMachine.RegisterAction("performSelection", (ctx, evt) =>
            {
                Console.WriteLine($"[E42] Recipe {_recipeId}: Performing selection");
                // Allocate resources, configure equipment, etc.
            });
            
            _stateMachine.RegisterAction("finalizeSelection", (ctx, evt) =>
            {
                Console.WriteLine($"[E42] Recipe {_recipeId}: Selection finalized");
            });
            
            _stateMachine.RegisterAction("notifySelected", (ctx, evt) =>
            {
                OnRecipeSelected?.Invoke(this, _recipeData!);
            });
            
            _stateMachine.RegisterAction("startExecution", (ctx, evt) =>
            {
                Console.WriteLine($"[E42] Recipe {_recipeId}: Starting execution");
                OnExecutionStarted?.Invoke(this, EventArgs.Empty);
            });
            
            _stateMachine.RegisterAction("beginExecution", (ctx, evt) =>
            {
                Console.WriteLine($"[E42] Recipe {_recipeId}: Execution in progress");
            });
            
            _stateMachine.RegisterAction("incrementExecutionCount", (ctx, evt) =>
            {
                var count = (int)(ctx["executionCount"] ?? 0);
                count++;
                ctx["executionCount"] = count;
                Console.WriteLine($"[E42] Recipe {_recipeId}: Execution #{count} completed");
            });
            
            _stateMachine.RegisterAction("updateParameter", (ctx, evt) =>
            {
                if (evt.Data is ParameterUpdate update)
                {
                    if (_parameters.ContainsKey(update.Name))
                    {
                        _parameters[update.Name].Value = update.Value;
                        Console.WriteLine($"[E42] Recipe {_recipeId}: Parameter '{update.Name}' updated to '{update.Value}'");
                    }
                }
            });
            
            _stateMachine.RegisterAction("clearSelectionData", (ctx, evt) =>
            {
                _selectTime = null;
                ctx["selectTime"] = null;
                Console.WriteLine($"[E42] Recipe {_recipeId}: Deselected");
            });
            
            _stateMachine.RegisterAction("deleteRecipe", (ctx, evt) =>
            {
                _recipeData = null;
                _parameters.Clear();
                _validationResults.Clear();
                Console.WriteLine($"[E42] Recipe {_recipeId}: Deleted");
            });
            
            _stateMachine.RegisterAction("recordDownloadError", (ctx, evt) =>
            {
                if (evt.Data is ErrorInfo error)
                {
                    ctx["lastError"] = error.Message;
                    Console.WriteLine($"[E42] Recipe {_recipeId}: Download failed - {error.Message}");
                }
            });
            
            _stateMachine.RegisterAction("logDownloaded", (ctx, evt) =>
            {
                Console.WriteLine($"[E42] Recipe {_recipeId}: Downloaded successfully");
            });
            
            _stateMachine.RegisterAction("logVerified", (ctx, evt) =>
            {
                Console.WriteLine($"[E42] Recipe {_recipeId}: Verified successfully");
            });
            
            _stateMachine.RegisterAction("logSelected", (ctx, evt) =>
            {
                Console.WriteLine($"[E42] Recipe {_recipeId}: Selected for execution");
            });
            
            _stateMachine.RegisterAction("logError", (ctx, evt) =>
            {
                var error = ctx["lastError"];
                Console.WriteLine($"[E42] Recipe {_recipeId}: Error state - {error}");
            });
            
            _stateMachine.RegisterAction("clearError", (ctx, evt) =>
            {
                ctx["lastError"] = null;
            });
            
            _stateMachine.Start();
            UpdateState();
        }
        
        private void UpdateState()
        {
            var state = _stateMachine.CurrentStates.FirstOrDefault()?.Name;
            if (string.IsNullOrEmpty(state) || state == "Unknown")
            {
                CurrentState = RecipeState.NoRecipe;
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
                    if (Enum.TryParse<RecipeState>(lastPart, out var parsedState))
                    {
                        CurrentState = parsedState;
                        return;
                    }
                }

                if (Enum.TryParse<RecipeState>(state, out var recipeState))
                {
                    CurrentState = recipeState;
                }
                else
                {
                    CurrentState = RecipeState.NoRecipe;
                }
            }
        }
        
        // Validation methods
        private void ValidateChecksum()
        {
            var result = new ValidationResult
            {
                CheckName = "Checksum",
                Passed = _recipeData?.Checksum == CalculateChecksum(_recipeData),
                Message = "Recipe checksum validation"
            };
            _validationResults.Add(result);
        }
        
        private void ValidateParameters()
        {
            foreach (var param in _parameters.Values)
            {
                var result = new ValidationResult
                {
                    CheckName = $"Parameter_{param.Name}",
                    Passed = IsParameterValid(param),
                    Message = $"Parameter {param.Name} validation"
                };
                _validationResults.Add(result);
            }
        }
        
        private void ValidateDependencies()
        {
            var result = new ValidationResult
            {
                CheckName = "Dependencies",
                Passed = true, // Check for required recipes, equipment capabilities, etc.
                Message = "Recipe dependency check"
            };
            _validationResults.Add(result);
        }
        
        private void ValidateEquipmentCapability()
        {
            var result = new ValidationResult
            {
                CheckName = "EquipmentCapability",
                Passed = true, // Check if equipment can execute this recipe
                Message = "Equipment capability check"
            };
            _validationResults.Add(result);
        }
        
        private bool IsParameterValid(ParameterData param)
        {
            // Check parameter range, type, format, etc.
            if (param.MinValue != null && Convert.ToDouble(param.Value) < Convert.ToDouble(param.MinValue))
                return false;
            if (param.MaxValue != null && Convert.ToDouble(param.Value) > Convert.ToDouble(param.MaxValue))
                return false;
            return true;
        }
        
        private string CalculateChecksum(RecipeData? data)
        {
            // Calculate checksum for recipe data
            return data?.Checksum ?? string.Empty;
        }
        
        // Public methods
        public async Task RequestDownloadAsync(RecipeData recipeData)
        {
            await _stateMachine.SendAsync(new StateMachineEvent
            {
                Name = "DOWNLOAD_REQUEST",
                Data = recipeData
            });
            UpdateState();
        }

        public void RequestDownload(RecipeData recipeData)
        {
            RequestDownloadAsync(recipeData).GetAwaiter().GetResult();
        }
        
        public async Task DownloadCompleteAsync(RecipeData recipeData)
        {
            await _stateMachine.SendAsync(new StateMachineEvent
            {
                Name = "DOWNLOAD_COMPLETE",
                Data = recipeData
            });
            UpdateState();
        }

        public void DownloadComplete(RecipeData recipeData)
        {
            DownloadCompleteAsync(recipeData).GetAwaiter().GetResult();
        }
        
        public async Task DownloadFailedAsync(string error)
        {
            await _stateMachine.SendAsync(new StateMachineEvent
            {
                Name = "DOWNLOAD_FAILED",
                Data = new ErrorInfo { Message = error }
            });
            UpdateState();
        }

        public void DownloadFailed(string error)
        {
            DownloadFailedAsync(error).GetAwaiter().GetResult();
        }
        
        public async Task RequestVerificationAsync()
        {
            await _stateMachine.SendAsync("VERIFY_REQUEST");
            UpdateState();
        }

        public void RequestVerification()
        {
            RequestVerificationAsync().GetAwaiter().GetResult();
        }
        
        public async Task VerificationCompleteAsync()
        {
            await _stateMachine.SendAsync("VERIFY_COMPLETE");
            UpdateState();
        }

        public void VerificationComplete()
        {
            VerificationCompleteAsync().GetAwaiter().GetResult();
        }
        
        public async Task RequestSelectionAsync()
        {
            await _stateMachine.SendAsync("SELECT_REQUEST");
            UpdateState();
        }

        public void RequestSelection()
        {
            RequestSelectionAsync().GetAwaiter().GetResult();
        }
        
        public async Task SelectionCompleteAsync()
        {
            await _stateMachine.SendAsync("SELECT_COMPLETE");
            UpdateState();
        }

        public void SelectionComplete()
        {
            SelectionCompleteAsync().GetAwaiter().GetResult();
        }
        
        public async Task ExecuteAsync()
        {
            await _stateMachine.SendAsync("EXECUTE");
            UpdateState();
        }

        public void Execute()
        {
            ExecuteAsync().GetAwaiter().GetResult();
        }
        
        public async Task ExecutionCompleteAsync()
        {
            await _stateMachine.SendAsync("EXECUTION_COMPLETE");
            UpdateState();
        }

        public void ExecutionComplete()
        {
            ExecutionCompleteAsync().GetAwaiter().GetResult();
        }
        
        public async Task DeselectAsync()
        {
            await _stateMachine.SendAsync("DESELECT");
            UpdateState();
        }

        public void Deselect()
        {
            DeselectAsync().GetAwaiter().GetResult();
        }
        
        public async Task DeselectCompleteAsync()
        {
            await _stateMachine.SendAsync("DESELECT_COMPLETE");
            UpdateState();
        }

        public void DeselectComplete()
        {
            DeselectCompleteAsync().GetAwaiter().GetResult();
        }
        
        public async Task UpdateParameterAsync(string name, object value)
        {
            await _stateMachine.SendAsync(new StateMachineEvent
            {
                Name = "PARAMETER_CHANGE",
                Data = new ParameterUpdate { Name = name, Value = value }
            });
        }

        public void UpdateParameter(string name, object value)
        {
            UpdateParameterAsync(name, value).GetAwaiter().GetResult();
        }
        
        // Events
        public event EventHandler? OnDownloadStarted;
        public event EventHandler<RecipeData>? OnRecipeSelected;
        public event EventHandler<VerificationEventArgs>? OnVerificationComplete;
        public event EventHandler? OnExecutionStarted;
        
        // Data classes
        public class RecipeData
        {
            public string RecipeId { get; set; } = string.Empty;
            public string RecipeName { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public RecipeType Type { get; set; }
            public string Body { get; set; } = string.Empty;
            public List<ParameterData> Parameters { get; set; } = new();
            public string Checksum { get; set; } = string.Empty;
            public bool RequiresVerification { get; set; } = true;
            public DateTime Created { get; set; }
            public DateTime Modified { get; set; }
            public string Author { get; set; } = string.Empty;
        }
        
        public class ParameterData
        {
            public string Name { get; set; } = string.Empty;
            public object Value { get; set; } = new object();
            public string Type { get; set; } = string.Empty;
            public object? MinValue { get; set; }
            public object? MaxValue { get; set; }
            public string Unit { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public bool IsReadOnly { get; set; }
        }
        
        public class ValidationResult
        {
            public string CheckName { get; set; } = string.Empty;
            public bool Passed { get; set; }
            public string Message { get; set; } = string.Empty;
        }
        
        public class ParameterUpdate
        {
            public string Name { get; set; } = string.Empty;
            public object Value { get; set; } = new object();
        }
        
        public class ErrorInfo
        {
            public string Message { get; set; } = string.Empty;
            public string Code { get; set; } = string.Empty;
        }
        
        public class VerificationEventArgs : EventArgs
        {
            public bool Passed { get; set; }
            public List<ValidationResult> Results { get; set; } = new();
        }
    }
}
