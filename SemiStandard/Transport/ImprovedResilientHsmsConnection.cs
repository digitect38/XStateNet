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
    /// Improved resilient HSMS connection with proper async patterns and disposal
    /// </summary>
    public class ImprovedResilientHsmsConnection : IAsyncDisposable, IDisposable
    {
        private HsmsConnection? _connection;
        private readonly IPEndPoint _endpoint;
        private readonly HsmsConnection.HsmsConnectionMode _mode;
        private readonly ILogger<ImprovedResilientHsmsConnection>? _logger;
        private readonly ConnectionHealthMonitor _healthMonitor;
        private readonly IAsyncPolicy<bool> _retryPolicy;
        private readonly IAsyncPolicy<bool> _circuitBreakerPolicy;
        private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _supervisorTask;
        private Task? _monitoringTask;
        private readonly object _stateLock = new();
        private volatile bool _disposed;
        private int _reconnectAttempts;
        private TaskCompletionSource<bool>? _selectionTcs;
        private uint _selectionSystemBytes;
        private TaskCompletionSource<bool> _initialConnectionSignal = new();
        private TaskCompletionSource<bool> _disconnectedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        private ConnectionState _state = ConnectionState.Disconnected;
        public ConnectionState State
        {
            get
            {
                lock (_stateLock) return _state;
            }
            private set
            {
                lock (_stateLock)
                {
                    if (_state != value)
                    {
                        _state = value;
                        // Fire event outside of lock to prevent deadlock
                        Task.Run(() => StateChanged?.Invoke(this, value));
                        _logger?.LogInformation("Connection state changed to {State}", value);
                    }
                }
            }
        }

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

        public ImprovedResilientHsmsConnection(
            IPEndPoint endpoint,
            HsmsConnection.HsmsConnectionMode mode,
            ILogger<ImprovedResilientHsmsConnection>? logger = null)
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
                        State = ConnectionState.CircuitOpen;
                    },
                    onReset: () =>
                    {
                        _logger?.LogInformation("Circuit breaker reset");
                        State = ConnectionState.Disconnected;
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
            ThrowIfDisposed();

            await _connectionSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_supervisorTask != null && !_supervisorTask.IsCompleted)
                {
                    // Already connecting or connected
                    return await _initialConnectionSignal.Task.ConfigureAwait(false);
                }

                // Reset signals for new connection attempt
                if (_initialConnectionSignal.Task.IsCompleted)
                {
                    _initialConnectionSignal = new TaskCompletionSource<bool>();
                }
                if (_disconnectedSignal.Task.IsCompleted)
                {
                    _disconnectedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                State = ConnectionState.Connecting;
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // Start the supervisor task - properly await and handle exceptions
                _supervisorTask = ConnectionSupervisorAsync(_cancellationTokenSource.Token);

                // Wait for the initial connection signal
                var connected = await _initialConnectionSignal.Task.ConfigureAwait(false);

                if (connected)
                {
                    Reconnected?.Invoke(this, EventArgs.Empty);
                }

                return connected;
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
                        .ExecuteAsync(async (ct) => await ConnectInternalAsync(ct), cancellationToken)
                        .ConfigureAwait(false);

                    if (result)
                    {
                        State = ConnectionState.Connected;
                        StartHealthMonitoring();
                        _reconnectAttempts = 0;
                        _logger?.LogInformation("Successfully connected to {Endpoint}", _endpoint);
                        _initialConnectionSignal.TrySetResult(true);

                        // Wait for disconnection signal
                        await _disconnectedSignal.Task.ConfigureAwait(false);

                        // Reset for next reconnection attempt
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            _disconnectedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            State = ConnectionState.Reconnecting;
                            _logger?.LogInformation("Connection lost, attempting to reconnect...");
                        }
                    }
                    else
                    {
                        State = ConnectionState.Failed;
                        _logger?.LogError("Failed to connect to {Endpoint} after {Attempts} attempts",
                            _endpoint, MaxRetryAttempts);
                        _initialConnectionSignal.TrySetResult(false);

                        if (_reconnectAttempts++ < MaxReconnectAttempts)
                        {
                            await Task.Delay(ReconnectDelayMs, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            _logger?.LogError("Maximum reconnection attempts reached. Giving up.");
                            break;
                        }
                    }
                }
                catch (BrokenCircuitException)
                {
                    _logger?.LogWarning("Cannot connect - circuit breaker is open. Waiting for it to close.");
                    _initialConnectionSignal.TrySetResult(false);
                    await Task.Delay(CircuitBreakerDuration, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogInformation("Connection supervisor cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Unexpected error in connection supervisor");
                    _initialConnectionSignal.TrySetResult(false);
                    ErrorOccurred?.Invoke(this, ex);

                    await Task.Delay(ReconnectDelayMs, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    // Clean up before next connection attempt
                    await CleanupConnectionAsync().ConfigureAwait(false);
                }
            }

            _logger?.LogInformation("Connection supervisor exiting");
        }

        private async Task CleanupConnectionAsync()
        {
            if (_connection != null)
            {
                try
                {
                    _connection.MessageReceived -= OnMessageReceived;
                    _connection.StateChanged -= OnConnectionStateChanged;
                    _connection.ErrorOccurred -= OnConnectionError;

                    if (_connection.IsConnected)
                    {
                        await _connection.DisconnectAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error during connection cleanup");
                }
                finally
                {
                    _connection.Dispose();
                    _connection = null;
                }
            }

            State = ConnectionState.Disconnected;
        }

        /// <summary>
        /// Internal connection logic
        /// </summary>
        private async Task<bool> ConnectInternalAsync(CancellationToken cancellationToken)
        {
            try
            {
                await CleanupConnectionAsync().ConfigureAwait(false);

                // Create new connection
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
                await _connection.ConnectAsync(cancellationToken).ConfigureAwait(false);

                // Perform selection if active mode
                if (_mode == HsmsConnection.HsmsConnectionMode.Active)
                {
                    await SelectAsync(cancellationToken).ConfigureAwait(false);
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
            ThrowIfDisposed();

            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
            }

            return await _retryPolicy.ExecuteAsync(async (ct) =>
            {
                try
                {
                    if (_connection == null)
                        return false;

                    await _connection.SendMessageAsync(message, ct).ConfigureAwait(false);
                    _healthMonitor.RecordSuccess();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to send message");
                    _healthMonitor.RecordFailure(ex);

                    if (IsConnectionException(ex))
                    {
                        _disconnectedSignal.TrySetResult(true);
                    }

                    return false;
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Start health monitoring
        /// </summary>
        private void StartHealthMonitoring()
        {
            if (_monitoringTask != null && !_monitoringTask.IsCompleted)
                return;

            _monitoringTask = HealthMonitoringAsync(_cancellationTokenSource!.Token);
        }

        private async Task HealthMonitoringAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                try
                {
                    await Task.Delay(HealthCheckIntervalMs, cancellationToken).ConfigureAwait(false);

                    if (!IsConnected)
                        break;

                    // Send linktest
                    var linktest = new HsmsMessage
                    {
                        MessageType = HsmsMessageType.LinktestReq,
                        SystemBytes = (uint)DateTime.UtcNow.Ticks
                    };

                    var success = await SendMessageAsync(linktest, cancellationToken).ConfigureAwait(false);
                    if (!success)
                    {
                        _logger?.LogWarning("Linktest failed");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Health check error");
                }
            }
        }

        /// <summary>
        /// Perform HSMS selection
        /// </summary>
        private async Task SelectAsync(CancellationToken cancellationToken)
        {
            var selectReq = new HsmsMessage
            {
                MessageType = HsmsMessageType.SelectReq,
                SystemBytes = (uint)Random.Shared.Next(1, 65536)
            };

            _selectionTcs = new TaskCompletionSource<bool>();
            _selectionSystemBytes = selectReq.SystemBytes;

            try
            {
                await _connection!.SendMessageAsync(selectReq, cancellationToken).ConfigureAwait(false);

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(T6Timeout);
                    await _selectionTcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                }

                State = ConnectionState.Selected;
                _logger?.LogInformation("HSMS selection successful");
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Selection timeout - no response received");
            }
            finally
            {
                _selectionTcs = null;
            }
        }

        /// <summary>
        /// Disconnect gracefully
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_disposed)
                return;

            State = ConnectionState.Disconnected;
            _cancellationTokenSource?.Cancel();

            if (_supervisorTask != null)
            {
                try
                {
                    await _supervisorTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger?.LogWarning("Supervisor task did not complete within timeout during disconnect");
                }
            }

            await CleanupConnectionAsync().ConfigureAwait(false);
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
                return;
            }
            else if (_selectionTcs != null &&
                     message.MessageType == HsmsMessageType.RejectReq &&
                     message.SystemBytes == _selectionSystemBytes)
            {
                _selectionTcs.TrySetException(new InvalidOperationException("Selection rejected by equipment"));
                return;
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

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ImprovedResilientHsmsConnection));
        }

        /// <summary>
        /// Async disposal pattern
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                await DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error during async disposal");
            }

            _cancellationTokenSource?.Dispose();
            _connectionSemaphore?.Dispose();
            _healthMonitor?.Dispose();

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Synchronous disposal (try to avoid using this)
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            // Try async disposal with timeout
            try
            {
                var disposeTask = DisposeAsync();
                if (!disposeTask.IsCompleted)
                {
                    // Wait with timeout to prevent hanging
                    disposeTask.AsTask().Wait(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error during synchronous disposal");
            }

            GC.SuppressFinalize(this);
        }
    }
}