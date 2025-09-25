using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using XStateNet.Distributed.Resilience;

namespace TimelineWPF
{
    public class CircuitBreakerSimulatorViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ICircuitBreaker _circuitBreaker;
        private readonly SimulatedService _simulatedService;
        private readonly bool _useXStateNet = true; // Toggle to use XStateNet implementation
        private readonly ObservableCollection<LogEntry> _eventLog;
        private readonly Dispatcher _dispatcher;
        private readonly Stopwatch _stateTimer;
        private Timer? _autoSendTimer;

        // Configuration properties
        private int _failureThreshold = 5;
        private int _breakDurationSeconds = 10;
        private int _successCountInHalfOpen = 3;
        private double _serviceFailureRate = 30;
        private bool _autoSendEnabled;
        private int _requestIntervalMs = 500;

        // Metrics
        private int _totalRequests;
        private int _successfulRequests;
        private int _failedRequests;
        private int _rejectedRequests;
        private int _consecutiveFailures;
        private int _recoveryAttempts;
        private CircuitState _currentState = CircuitState.Closed;
        private DateTime _lastStateChangeTime = DateTime.UtcNow;

        public CircuitBreakerSimulatorViewModel()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _eventLog = new ObservableCollection<LogEntry>();
            _simulatedService = new SimulatedService();
            _stateTimer = new Stopwatch();
            _stateTimer.Start();

            // Initialize Circuit Breaker
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = _failureThreshold,
                BreakDuration = TimeSpan.FromSeconds(_breakDurationSeconds),
                SuccessCountInHalfOpen = _successCountInHalfOpen
            };

            // Choose implementation based on toggle
            if (_useXStateNet)
            {
                _circuitBreaker = new XStateNetCircuitBreaker("SimulatorCB_XStateNet", options);
                LogEvent("Using XStateNet-based Circuit Breaker implementation", LogSeverity.Info);
            }
            else
            {
                _circuitBreaker = new CircuitBreaker("SimulatorCB", options);
                LogEvent("Using traditional Circuit Breaker implementation", LogSeverity.Info);
            }

            _circuitBreaker.StateChanged += OnCircuitBreakerStateChanged;

            // Initialize commands
            SendRequestCommand = new RelayCommand(async () => await SendRequest());
            SendBurstCommand = new RelayCommand(async () => await SendBurst());
            SimulateRecoveryCommand = new RelayCommand(() => SimulateRecovery());
            SimulateFailureCommand = new RelayCommand(() => SimulateFailure());
            ResetCommand = new RelayCommand(() => Reset());

            LogEvent("Simulator initialized", LogSeverity.Info);
        }

        #region Properties

        public int FailureThreshold
        {
            get => _failureThreshold;
            set
            {
                if (_failureThreshold != value)
                {
                    _failureThreshold = value;
                    UpdateCircuitBreakerConfig();
                    OnPropertyChanged();
                }
            }
        }

        public int BreakDurationSeconds
        {
            get => _breakDurationSeconds;
            set
            {
                if (_breakDurationSeconds != value)
                {
                    _breakDurationSeconds = value;
                    UpdateCircuitBreakerConfig();
                    OnPropertyChanged();
                }
            }
        }

        public int SuccessCountInHalfOpen
        {
            get => _successCountInHalfOpen;
            set
            {
                if (_successCountInHalfOpen != value)
                {
                    _successCountInHalfOpen = value;
                    UpdateCircuitBreakerConfig();
                    OnPropertyChanged();
                }
            }
        }

        public double ServiceFailureRate
        {
            get => _serviceFailureRate;
            set
            {
                if (_serviceFailureRate != value)
                {
                    _serviceFailureRate = value;
                    _simulatedService.FailureRate = value / 100.0;
                    OnPropertyChanged();
                }
            }
        }

        public bool AutoSendEnabled
        {
            get => _autoSendEnabled;
            set
            {
                if (_autoSendEnabled != value)
                {
                    _autoSendEnabled = value;
                    UpdateAutoSend();
                    OnPropertyChanged();
                }
            }
        }

        public int RequestIntervalMs
        {
            get => _requestIntervalMs;
            set
            {
                if (_requestIntervalMs != value)
                {
                    _requestIntervalMs = value;
                    if (_autoSendEnabled)
                        UpdateAutoSend();
                    OnPropertyChanged();
                }
            }
        }

        public int TotalRequests
        {
            get => _totalRequests;
            private set { _totalRequests = value; OnPropertyChanged(); }
        }

        public int ConsecutiveFailures
        {
            get => _consecutiveFailures;
            private set { _consecutiveFailures = value; OnPropertyChanged(); }
        }

        public int RecoveryAttempts
        {
            get => _recoveryAttempts;
            private set { _recoveryAttempts = value; OnPropertyChanged(); }
        }

        public double SuccessRate =>
            _totalRequests == 0 ? 0 : (_successfulRequests * 100.0 / _totalRequests);

        public double RejectionRate =>
            _totalRequests == 0 ? 0 : (_rejectedRequests * 100.0 / _totalRequests);

        public double TimeInCurrentState =>
            (DateTime.UtcNow - _lastStateChangeTime).TotalSeconds;

        public CircuitState CurrentState
        {
            get => _currentState;
            private set
            {
                if (_currentState != value)
                {
                    _currentState = value;
                    _lastStateChangeTime = DateTime.UtcNow;
                    _stateTimer.Restart();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsClosedState));
                    OnPropertyChanged(nameof(IsOpenState));
                    OnPropertyChanged(nameof(IsHalfOpenState));
                    OnPropertyChanged(nameof(CurrentStateText));
                }
            }
        }

        public bool IsClosedState => CurrentState == CircuitState.Closed;
        public bool IsOpenState => CurrentState == CircuitState.Open;
        public bool IsHalfOpenState => CurrentState == CircuitState.HalfOpen;

        public string CurrentStateText => CurrentState.ToString().ToUpper();

        public string StatusMessage { get; private set; } = "Ready";

        public ObservableCollection<LogEntry> EventLog => _eventLog;

        #endregion

        #region Commands

        public ICommand SendRequestCommand { get; }
        public ICommand SendBurstCommand { get; }
        public ICommand SimulateRecoveryCommand { get; }
        public ICommand SimulateFailureCommand { get; }
        public ICommand ResetCommand { get; }

        #endregion

        #region Methods

        private async Task SendRequest()
        {
            TotalRequests++;
            UpdateStatus("Sending request...");

            try
            {
                var result = await _circuitBreaker.ExecuteAsync(async () =>
                {
                    // Simulate service call
                    return await _simulatedService.CallServiceAsync();
                });

                _successfulRequests++;
                ConsecutiveFailures = 0;
                LogEvent($"Request successful: {result}", LogSeverity.Success);
                UpdateStatus("Request succeeded");
            }
            catch (CircuitBreakerOpenException)
            {
                _rejectedRequests++;
                LogEvent("Request rejected - Circuit is OPEN", LogSeverity.Warning);
                UpdateStatus("Request rejected (Circuit Open)");
            }
            catch (Exception ex)
            {
                _failedRequests++;
                ConsecutiveFailures++;
                LogEvent($"Request failed: {ex.Message}", LogSeverity.Error);
                UpdateStatus("Request failed");
            }

            UpdateMetrics();
        }

        private async Task SendBurst()
        {
            LogEvent("Sending burst of 10 requests", LogSeverity.Info);
            var tasks = Enumerable.Range(0, 10).Select(async i =>
            {
                await Task.Delay(i * 50); // Slight delay between requests
                await SendRequest();
            });

            await Task.WhenAll(tasks);
            LogEvent("Burst completed", LogSeverity.Info);
        }

        private void SimulateRecovery()
        {
            _simulatedService.FailureRate = 0;
            ServiceFailureRate = 0;
            LogEvent("Service recovery simulated - Failure rate set to 0%", LogSeverity.Success);
            UpdateStatus("Service recovered");

            if (CurrentState == CircuitState.HalfOpen)
            {
                RecoveryAttempts++;
            }
        }

        private void SimulateFailure()
        {
            _simulatedService.FailureRate = 1.0;
            ServiceFailureRate = 100;
            LogEvent("Service failure simulated - Failure rate set to 100%", LogSeverity.Error);
            UpdateStatus("Service is failing");
        }

        private void Reset()
        {
            // Dispose and recreate circuit breaker
            _circuitBreaker.StateChanged -= OnCircuitBreakerStateChanged;
            _circuitBreaker.Dispose();

            var options = new CircuitBreakerOptions
            {
                FailureThreshold = _failureThreshold,
                BreakDuration = TimeSpan.FromSeconds(_breakDurationSeconds),
                SuccessCountInHalfOpen = _successCountInHalfOpen
            };

            // Recreate with same implementation type
            ICircuitBreaker newCircuitBreaker;
            if (_useXStateNet)
            {
                newCircuitBreaker = new XStateNetCircuitBreaker("SimulatorCB_XStateNet", options);
            }
            else
            {
                newCircuitBreaker = new CircuitBreaker("SimulatorCB", options);
            }
            newCircuitBreaker.StateChanged += OnCircuitBreakerStateChanged;

            // Reset metrics
            _totalRequests = 0;
            _successfulRequests = 0;
            _failedRequests = 0;
            _rejectedRequests = 0;
            _consecutiveFailures = 0;
            _recoveryAttempts = 0;
            CurrentState = CircuitState.Closed;

            // Clear log
            _dispatcher.Invoke(() => _eventLog.Clear());

            LogEvent("Simulator reset", LogSeverity.Info);
            UpdateStatus("Reset complete");
            UpdateMetrics();
        }

        private void UpdateCircuitBreakerConfig()
        {
            LogEvent("Circuit Breaker configuration updated - Reset required to apply changes", LogSeverity.Warning);
        }

        private void UpdateAutoSend()
        {
            _autoSendTimer?.Dispose();

            if (_autoSendEnabled)
            {
                _autoSendTimer = new Timer(async _ => await SendRequest(), null,
                    TimeSpan.FromMilliseconds(_requestIntervalMs),
                    TimeSpan.FromMilliseconds(_requestIntervalMs));
                LogEvent("Auto-send enabled", LogSeverity.Info);
            }
            else
            {
                LogEvent("Auto-send disabled", LogSeverity.Info);
            }
        }

        private void OnCircuitBreakerStateChanged(object? sender, CircuitStateChangedEventArgs e)
        {
            CurrentState = e.ToState;

            string message = $"Circuit Breaker state changed: {e.FromState} → {e.ToState}";
            if (e.LastException != null)
            {
                message += $" (Last error: {e.LastException.Message})";
            }

            LogEvent(message, GetSeverityForState(e.ToState));

            if (e.ToState == CircuitState.HalfOpen)
            {
                RecoveryAttempts++;
                LogEvent("Entering recovery testing phase", LogSeverity.Info);
            }
        }

        private LogSeverity GetSeverityForState(CircuitState state)
        {
            return state switch
            {
                CircuitState.Closed => LogSeverity.Success,
                CircuitState.Open => LogSeverity.Error,
                CircuitState.HalfOpen => LogSeverity.Warning,
                _ => LogSeverity.Info
            };
        }

        private void LogEvent(string message, LogSeverity severity)
        {
            _dispatcher.BeginInvoke(() =>
            {
                _eventLog.Insert(0, new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = message,
                    Severity = severity
                });

                // Keep only last 100 entries
                while (_eventLog.Count > 100)
                {
                    _eventLog.RemoveAt(_eventLog.Count - 1);
                }
            });
        }

        private void UpdateStatus(string message)
        {
            StatusMessage = message;
            OnPropertyChanged(nameof(StatusMessage));
        }

        private void UpdateMetrics()
        {
            OnPropertyChanged(nameof(SuccessRate));
            OnPropertyChanged(nameof(RejectionRate));
        }

        public void UpdateTimeBasedMetrics()
        {
            OnPropertyChanged(nameof(TimeInCurrentState));
        }

        public void Dispose()
        {
            _autoSendTimer?.Dispose();
            _circuitBreaker?.Dispose();
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    #region Support Classes

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; } = string.Empty;
        public LogSeverity Severity { get; set; }
    }

    public enum LogSeverity
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class SimulatedService
    {
        private readonly Random _random = new Random();
        public double FailureRate { get; set; } = 0.3; // 30% failure rate by default

        public async Task<string> CallServiceAsync()
        {
            // Simulate network latency
            await Task.Delay(_random.Next(10, 50));

            if (_random.NextDouble() < FailureRate)
            {
                throw new InvalidOperationException("Service call failed");
            }

            var guid = Guid.NewGuid().ToString("N");
            return $"Response-{guid.Substring(0, 8)}";
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();
    }

    #endregion
}