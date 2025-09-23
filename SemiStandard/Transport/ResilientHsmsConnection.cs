using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;

namespace XStateNet.Semi.Transport
{
    /// <summary>
    /// Resilient HSMS connection with automatic retry, circuit breaker, and reconnection
    /// </summary>
    public class ResilientHsmsConnection : IDisposable
    {
        private HsmsConnection? _connection;
        private readonly IPEndPoint _endpoint;
        private readonly HsmsConnection.HsmsConnectionMode _mode;
        private readonly ILogger<ResilientHsmsConnection>? _logger;
        private readonly ConnectionHealthMonitor _healthMonitor;
        private readonly IAsyncPolicy<bool> _retryPolicy;
        private readonly IAsyncPolicy<bool> _circuitBreakerPolicy;
        private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _supervisorTask;
        private Task? _monitoringTask;
        private bool _disposed;
        private int _reconnectAttempts;
        private TaskCompletionSource<bool>? _selectionTcs;
        private uint _selectionSystemBytes;
        private readonly TaskCompletionSource<bool> _initialConnectionSignal = new();
        private readonly TaskCompletionSource<bool> _disconnectedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Configuration
        public int MaxRetryAttempts { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;
        public int MaxReconnectAttempts { get; set; } = 10;
        public int ReconnectDelayMs { get; set; } = 5000;
        public int HealthCheckIntervalMs { get; set; } = 30000;
        public int CircuitBreakerThreshold { get; set; } = 5;
        public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromMinutes(1);
        
        // Connection parameters
        public int T5Timeout { get; set; } = 10000;
        public int T6Timeout { get; set; } = 5000;
        public int T7Timeout { get; set; } = 10000;
        public int T8Timeout { get; set; } = 5000;
        public int LinktestInterval { get; set; } = 30000;
        
        // State
        public bool IsConnected => _connection?.IsConnected ?? false;
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public ConnectionHealth Health => _healthMonitor.CurrentHealth;
        
        // Events
        public event EventHandler<HsmsMessage>? MessageReceived;
        public event EventHandler<ConnectionState>? StateChanged;
        public event EventHandler<ConnectionHealth>? HealthChanged;
        public event EventHandler<Exception>? ErrorOccurred;
        public event EventHandler? Reconnected;
        
        public enum ConnectionState
        {
            Disconnected,
            Connecting,
            Connected,
            Selected,
            Reconnecting,
            Failed,
            CircuitOpen
        }
        
        public ResilientHsmsConnection(
            IPEndPoint endpoint,
            HsmsConnection.HsmsConnectionMode mode,
            ILogger<ResilientHsmsConnection>? logger = null)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _mode = mode;
            _logger = logger;
            _healthMonitor = new ConnectionHealthMonitor(logger);
            
            // Setup retry policy with exponential backoff
            _retryPolicy = Policy<bool>
                .HandleResult(r => !r)
                .Or<Exception>(ex => IsTransientException(ex))
                .WaitAndRetryAsync(
                    MaxRetryAttempts,
                    retryAttempt => TimeSpan.FromMilliseconds(RetryDelayMs * Math.Pow(2, retryAttempt - 1)),
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                        var reason = outcome.Exception?.Message ?? "Connection failed";
                        _logger?.LogWarning("Retry attempt {RetryCount} after {Delay}ms. Reason: {Reason}",
                            retryCount, timespan.TotalMilliseconds, reason);
                    });
            
            // Setup circuit breaker
            _circuitBreakerPolicy = Policy<bool>
                .HandleResult(r => !r)
                .CircuitBreakerAsync(
                    CircuitBreakerThreshold,
                    CircuitBreakerDuration,
                    onBreak: (result, duration) =>
                    {
                        _logger?.LogError("Circuit breaker opened for {Duration}s", duration.TotalSeconds);
                        SetState(ConnectionState.CircuitOpen);
                    },
                    onReset: () =>
                    {
                        _logger?.LogInformation("Circuit breaker reset");
                        SetState(ConnectionState.Disconnected);
                    },
                    onHalfOpen: () =>
                    {
                        _logger?.LogInformation("Circuit breaker half-open, testing connection");
                    });
        }
        
        /// <summary>
        /// Connect with resilience (retry and circuit breaker)
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResilientHsmsConnection));

            await _connectionSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_supervisorTask != null)
                {
                    // Already connecting or connected
                    return await _initialConnectionSignal.Task;
                }

                SetState(ConnectionState.Connecting);
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // Start the supervisor task
                _supervisorTask = Task.Run(() => ConnectionSupervisorAsync(_cancellationTokenSource.Token));

                // Wait for the initial connection signal
                return await _initialConnectionSignal.Task;
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        private async Task ConnectionSupervisorAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await Policy.WrapAsync(_circuitBreakerPolicy, _retryPolicy)
                        .ExecuteAsync(async (ct) => await ConnectInternalAsync(ct), cancellationToken);

                    if (result)
                    {
                        SetState(ConnectionState.Connected);
                        StartHealthMonitoring();
                        _reconnectAttempts = 0;
                        _logger?.LogInformation("Successfully connected to {Endpoint}", _endpoint);
                        _initialConnectionSignal.TrySetResult(true); // Signal successful initial connection

                        // Wait here until we get a disconnection signal
                        await _disconnectedSignal.Task; 
                    }
                    else
                    {
                        SetState(ConnectionState.Failed);
                        _logger?.LogError("Failed to connect to {Endpoint} after {Attempts} attempts",
                            _endpoint, MaxRetryAttempts);
                        _initialConnectionSignal.TrySetResult(false); // Signal failed initial connection
                        await Task.Delay(ReconnectDelayMs, cancellationToken); // Wait before retrying the whole policy
                    }
                }
                catch (BrokenCircuitException)
                {
                    _logger?.LogWarning("Cannot connect - circuit breaker is open. Waiting for it to close.");
                    _initialConnectionSignal.TrySetResult(false);
                    await Task.Delay(CircuitBreakerDuration, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break; // Exit loop on cancellation
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "An unexpected error occurred in the Connection Supervisor.");
                    _initialConnectionSignal.TrySetResult(false);
                    // Wait before retrying
                    await Task.Delay(ReconnectDelayMs, cancellationToken);
                }
                finally
                {
                    // Clean up before next connection attempt
                    if (_connection != null)
                    {
                        await _connection.DisconnectAsync();
                        _connection.Dispose();
                        _connection = null;
                    }
                    SetState(ConnectionState.Disconnected);
                }
            }
        }
        
        /// <summary>
        /// Internal connection logic
        /// </summary>
        private async Task<bool> ConnectInternalAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Clean up any existing connection
                if (_connection != null)
                {
                    _connection.MessageReceived -= OnMessageReceived;
                    _connection.StateChanged -= OnConnectionStateChanged;
                    _connection.ErrorOccurred -= OnConnectionError;
                    _connection.Dispose();
                }
                
                // Create new connection
                // Pass null for logger to avoid circular dependencies
                // The HsmsConnection will use its own internal logging if needed
                _connection = new HsmsConnection(_endpoint, _mode, null);
                _connection.T5Timeout = T5Timeout;
                _connection.T6Timeout = T6Timeout;
                _connection.T7Timeout = T7Timeout;
                _connection.T8Timeout = T8Timeout;
                
                // Subscribe to events
                _connection.MessageReceived += OnMessageReceived;
                _connection.StateChanged += OnConnectionStateChanged;
                _connection.ErrorOccurred += OnConnectionError;
                
                // Connect
                await _connection.ConnectAsync(cancellationToken);
                
                // Perform selection if active mode
                if (_mode == HsmsConnection.HsmsConnectionMode.Active)
                {
                    await SelectAsync(cancellationToken);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Connection attempt failed");
                _healthMonitor.RecordFailure(ex);
                return false;
            }
        }
        
        /// <summary>
        /// Send message with retry
        /// </summary>
        public async Task<bool> SendMessageAsync(HsmsMessage message, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected. The connection supervisor will handle reconnection.");
            }
            
            return await _retryPolicy.ExecuteAsync(async (ct) =>
            {
                try
                {
                    _logger?.LogDebug("Sending message via ResilientHsmsConnection: Type={Type}, Stream={Stream}, Function={Function}", 
                        message.MessageType, message.Stream, message.Function);
                    await _connection!.SendMessageAsync(message, ct);
                    _healthMonitor.RecordSuccess();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to send message");
                    _healthMonitor.RecordFailure(ex);
                    
                    // Signal disconnection to the supervisor
                    if (IsConnectionException(ex))
                    {
                        _disconnectedSignal.TrySetResult(true);
                    }
                    
                    return false;
                }
            }, cancellationToken);
        }
        
        
        
        /// <summary>
        /// Start health monitoring
        /// </summary>
        private void StartHealthMonitoring()
        {
            _monitoringTask = Task.Run(async () =>
            {
                while (!_cancellationTokenSource!.Token.IsCancellationRequested && IsConnected)
                {
                    try
                    {
                        await Task.Delay(HealthCheckIntervalMs, _cancellationTokenSource.Token);
                        
                        // Send linktest
                        var linktest = new HsmsMessage
                        {
                            MessageType = HsmsMessageType.LinktestReq,
                            SystemBytes = (uint)DateTime.UtcNow.Ticks
                        };
                        
                        var success = await SendMessageAsync(linktest, _cancellationTokenSource.Token);
                        if (!success)
                        {
                            _logger?.LogWarning("Linktest failed");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Health check error");
                    }
                }
            }, _cancellationTokenSource!.Token);
        }
        
        /// <summary>
        /// Perform HSMS selection
        /// </summary>
        private async Task SelectAsync(CancellationToken cancellationToken)
        {
            var selectReq = new HsmsMessage
            {
                MessageType = HsmsMessageType.SelectReq,
                SystemBytes = (uint)Random.Shared.Next(1, 65536)  // Use 16-bit range for SEMI compatibility
            };
            
            // Set up the TaskCompletionSource for selection response
            _selectionTcs = new TaskCompletionSource<bool>();
            _selectionSystemBytes = selectReq.SystemBytes;
            
            try
            {
                // Send SelectReq - OnMessageReceived will handle the response
                await _connection!.SendMessageAsync(selectReq, cancellationToken);
                
                // Wait for SelectRsp with timeout
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(T6Timeout);
                    await _selectionTcs.Task.WaitAsync(cts.Token);
                }
                
                SetState(ConnectionState.Selected);
                _logger?.LogInformation("HSMS selection successful");
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Selection timeout - no response received");
            }
            finally
            {
                _selectionTcs = null; // Clear the TCS
            }
        }
        
        /// <summary>
        /// Disconnect gracefully
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_disposed)
                return;

            SetState(ConnectionState.Disconnected);
            _cancellationTokenSource?.Cancel();

            if (_supervisorTask != null)
            {
                await Task.WhenAny(_supervisorTask, Task.Delay(TimeSpan.FromSeconds(5)));
            }
        }
        
        private void OnMessageReceived(object? sender, HsmsMessage message)
        {
            _healthMonitor.RecordSuccess();
            
            // Handle SelectRsp internally
            if (_selectionTcs != null && 
                message.MessageType == HsmsMessageType.SelectRsp && 
                message.SystemBytes == _selectionSystemBytes)
            {
                _selectionTcs.TrySetResult(true);
                return; // Don't forward SelectRsp to external handlers
            }
            else if (_selectionTcs != null && 
                     message.MessageType == HsmsMessageType.RejectReq && 
                     message.SystemBytes == _selectionSystemBytes)
            {
                _selectionTcs.TrySetException(new InvalidOperationException("Selection rejected by equipment"));
                return; // Don't forward reject to external handlers
            }
            
            MessageReceived?.Invoke(this, message);
        }
        
        private void OnConnectionStateChanged(object? sender, HsmsConnection.HsmsConnectionState state)
        {
            if (state == HsmsConnection.HsmsConnectionState.Error ||
                state == HsmsConnection.HsmsConnectionState.NotConnected)
            {
                _disconnectedSignal.TrySetResult(true);
            }
        }
        
        private void OnConnectionError(object? sender, Exception ex)
        {
            _healthMonitor.RecordFailure(ex);
            ErrorOccurred?.Invoke(this, ex);
            
            if (IsConnectionException(ex))
            {
                _disconnectedSignal.TrySetResult(true);
            }
        }
        
        private void SetState(ConnectionState newState)
        {
            if (State != newState)
            {
                State = newState;
                StateChanged?.Invoke(this, newState);
                _logger?.LogInformation("Connection state changed to {State}", newState);
            }
        }
        
        private bool IsTransientException(Exception ex)
        {
            return ex is TimeoutException ||
                   ex is System.Net.Sockets.SocketException ||
                   ex is System.IO.IOException ||
                   ex is OperationCanceledException;
        }
        
        private bool IsConnectionException(Exception ex)
        {
            return ex is System.Net.Sockets.SocketException ||
                   ex is System.IO.EndOfStreamException ||
                   ex is System.IO.IOException;
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            _cancellationTokenSource?.Cancel();
            try
            {
                _supervisorTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Exception while waiting for supervisor task to complete during dispose.");
            }
            _cancellationTokenSource?.Dispose();
            _connection?.Dispose();
            _connectionSemaphore?.Dispose();
            _healthMonitor?.Dispose();
        }
    }
    
    /// <summary>
    /// Connection health monitoring
    /// </summary>
    public class ConnectionHealthMonitor : IDisposable
    {
        private readonly ILogger? _logger;
        private readonly object _lock = new();
        private int _successCount;
        private int _failureCount;
        private DateTime _lastSuccess = DateTime.UtcNow;
        private DateTime _lastFailure;
        private readonly Queue<HealthEvent> _recentEvents = new();
        private readonly int _maxEventHistory = 100;
        
        public ConnectionHealth CurrentHealth { get; private set; } = ConnectionHealth.Unknown;
        public double SuccessRate => _successCount + _failureCount > 0 
            ? (double)_successCount / (_successCount + _failureCount) 
            : 0;
        public TimeSpan TimeSinceLastSuccess => DateTime.UtcNow - _lastSuccess;
        public TimeSpan TimeSinceLastFailure => DateTime.UtcNow - _lastFailure;
        
        public event EventHandler<ConnectionHealth>? HealthChanged;
        
        public ConnectionHealthMonitor(ILogger? logger)
        {
            _logger = logger;
        }
        
        public void RecordSuccess()
        {
            lock (_lock)
            {
                _successCount++;
                _lastSuccess = DateTime.UtcNow;
                AddEvent(new HealthEvent { Type = EventType.Success, Timestamp = DateTime.UtcNow });
                UpdateHealth();
            }
        }
        
        public void RecordFailure(Exception? ex = null)
        {
            lock (_lock)
            {
                _failureCount++;
                _lastFailure = DateTime.UtcNow;
                AddEvent(new HealthEvent 
                { 
                    Type = EventType.Failure, 
                    Timestamp = DateTime.UtcNow,
                    Exception = ex 
                });
                UpdateHealth();
            }
        }
        
        private void AddEvent(HealthEvent evt)
        {
            _recentEvents.Enqueue(evt);
            while (_recentEvents.Count > _maxEventHistory)
                _recentEvents.Dequeue();
        }
        
        private void UpdateHealth()
        {
            var oldHealth = CurrentHealth;
            
            if (SuccessRate > 0.95)
                CurrentHealth = ConnectionHealth.Healthy;
            else if (SuccessRate > 0.8)
                CurrentHealth = ConnectionHealth.Degraded;
            else if (SuccessRate > 0.5)
                CurrentHealth = ConnectionHealth.Poor;
            else
                CurrentHealth = ConnectionHealth.Critical;
                
            if (TimeSinceLastSuccess > TimeSpan.FromMinutes(5))
                CurrentHealth = ConnectionHealth.Critical;
                
            if (oldHealth != CurrentHealth)
            {
                _logger?.LogWarning("Connection health changed from {OldHealth} to {NewHealth} (Success rate: {Rate:P})",
                    oldHealth, CurrentHealth, SuccessRate);
                HealthChanged?.Invoke(this, CurrentHealth);
            }
        }
        
        public void Reset()
        {
            lock (_lock)
            {
                _successCount = 0;
                _failureCount = 0;
                _recentEvents.Clear();
                CurrentHealth = ConnectionHealth.Unknown;
            }
        }
        
        public void Dispose()
        {
            // Cleanup if needed
        }
        
        private class HealthEvent
        {
            public EventType Type { get; set; }
            public DateTime Timestamp { get; set; }
            public Exception? Exception { get; set; }
        }
        
        private enum EventType
        {
            Success,
            Failure
        }
    }
    
    public enum ConnectionHealth
    {
        Unknown,
        Healthy,
        Degraded,
        Poor,
        Critical
    }
}