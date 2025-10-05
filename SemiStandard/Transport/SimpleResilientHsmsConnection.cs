using Microsoft.Extensions.Logging;
using System.Net;

namespace XStateNet.Semi.Transport
{
    /// <summary>
    /// Simplified resilient HSMS connection without complex retry policies
    /// </summary>
    public class SimpleResilientHsmsConnection : IDisposable
    {
        private readonly IPEndPoint _endpoint;
        private readonly HsmsConnection.HsmsConnectionMode _mode;
        private readonly ILogger<SimpleResilientHsmsConnection>? _logger;
        private HsmsConnection? _connection;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private bool _disposed;
        private TaskCompletionSource<HsmsMessage>? _pendingResponse;
        private uint _pendingSystemBytes;

        // Configuration
        public int ConnectTimeoutMs { get; set; } = 10000;
        public int SelectTimeoutMs { get; set; } = 5000;
        public int RetryDelayMs { get; set; } = 1000;
        public int MaxRetries { get; set; } = 3;

        // State
        public bool IsConnected => _connection?.IsConnected ?? false;
        public bool IsSelected { get; private set; }

        // Events
        public event EventHandler<HsmsMessage>? MessageReceived;
        public event EventHandler<Exception>? ErrorOccurred;
        public event EventHandler? Connected;
        public event EventHandler? Disconnected;

        public SimpleResilientHsmsConnection(
            IPEndPoint endpoint,
            HsmsConnection.HsmsConnectionMode mode,
            ILogger<SimpleResilientHsmsConnection>? logger = null)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _mode = mode;
            _logger = logger;
        }

        /// <summary>
        /// Connect with simple retry logic
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("[SimpleResilient] ConnectAsync called");

            if (_disposed)
                throw new ObjectDisposedException(nameof(SimpleResilientHsmsConnection));

            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                if (IsConnected && IsSelected)
                {
                    _logger?.LogInformation("[SimpleResilient] Already connected and selected");
                    return true;
                }

                // Simple retry loop
                for (int attempt = 0; attempt < MaxRetries; attempt++)
                {
                    if (attempt > 0)
                    {
                        _logger?.LogInformation("[SimpleResilient] Retry attempt {Attempt}/{MaxRetries}", attempt + 1, MaxRetries);
                        await Task.Delay(RetryDelayMs, cancellationToken);
                    }

                    try
                    {
                        _logger?.LogInformation("[SimpleResilient] Attempt {Attempt}: Starting connection", attempt + 1);
                        // Clean up previous connection
                        if (_connection != null)
                        {
                            _logger?.LogInformation("[SimpleResilient] Cleaning up previous connection");
                            _connection.MessageReceived -= OnMessageReceived;
                            _connection.ErrorOccurred -= OnErrorOccurred;
                            _connection.Dispose();
                            _connection = null;
                        }

                        // Create new connection
                        _logger?.LogInformation("[SimpleResilient] Creating HsmsConnection to {Endpoint} in {Mode} mode", _endpoint, _mode);
                        _connection = new HsmsConnection(_endpoint, _mode, null);

                        // Subscribe to events BEFORE connecting
                        _connection.MessageReceived += OnMessageReceived;
                        _connection.ErrorOccurred += OnErrorOccurred;

                        // Connect with timeout
                        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        connectCts.CancelAfter(ConnectTimeoutMs);

                        _logger?.LogInformation("[SimpleResilient] Calling HsmsConnection.ConnectAsync with {Timeout}ms timeout", ConnectTimeoutMs);
                        await _connection.ConnectAsync(connectCts.Token);
                        _logger?.LogInformation("[SimpleResilient] Physical connection established, IsConnected={IsConnected}", _connection.IsConnected);

                        // For Active mode, perform selection
                        if (_mode == HsmsConnection.HsmsConnectionMode.Active)
                        {
                            _logger?.LogInformation("[SimpleResilient] Active mode - performing selection");
                            if (await SelectAsync(cancellationToken))
                            {
                                IsSelected = true;
                                _logger?.LogInformation("[SimpleResilient] HSMS selection successful");
                            }
                            else
                            {
                                _logger?.LogError("[SimpleResilient] HSMS selection failed");
                                throw new InvalidOperationException("HSMS selection failed");
                            }
                        }
                        else
                        {
                            // Passive mode is automatically selected
                            _logger?.LogInformation("[SimpleResilient] Passive mode - automatically selected");
                            IsSelected = true;
                        }

                        _logger?.LogInformation("[SimpleResilient] Connection successful, raising Connected event");
                        Connected?.Invoke(this, EventArgs.Empty);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "[SimpleResilient] Connection attempt {Attempt} failed: {Message}", attempt + 1, ex.Message);

                        if (attempt == MaxRetries - 1)
                        {
                            ErrorOccurred?.Invoke(this, ex);
                            return false;
                        }
                    }
                }

                return false;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        /// <summary>
        /// Perform HSMS selection (simplified)
        /// </summary>
        private async Task<bool> SelectAsync(CancellationToken cancellationToken)
        {
            _logger?.LogInformation("[SimpleResilient] SelectAsync called");

            if (_connection == null || !_connection.IsConnected)
            {
                _logger?.LogWarning("[SimpleResilient] Cannot select - connection is null or not connected");
                return false;
            }

            var selectReq = new HsmsMessage
            {
                MessageType = HsmsMessageType.SelectReq,
                SystemBytes = (uint)Random.Shared.Next(1, 65536)  // Use 16-bit range for SEMI compatibility
            };

            _logger?.LogInformation("[SimpleResilient] Sending SelectReq with SystemBytes={SystemBytes}", selectReq.SystemBytes);

            // Set up response handler
            _pendingSystemBytes = selectReq.SystemBytes;
            _pendingResponse = new TaskCompletionSource<HsmsMessage>();

            try
            {
                // Send select request
                await _connection.SendMessageAsync(selectReq, cancellationToken);
                _logger?.LogInformation("[SimpleResilient] SelectReq sent, waiting for response with {Timeout}ms timeout", SelectTimeoutMs);

                // Wait for response with timeout
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(SelectTimeoutMs);

                var response = await _pendingResponse.Task.WaitAsync(cts.Token);
                _logger?.LogInformation("[SimpleResilient] Received response: MessageType={MessageType}", response.MessageType);

                return response.MessageType == HsmsMessageType.SelectRsp;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogError("[SimpleResilient] Selection timeout after {Timeout}ms", SelectTimeoutMs);
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[SimpleResilient] Selection failed with exception");
                return false;
            }
            finally
            {
                _pendingResponse = null;
            }
        }

        /// <summary>
        /// Send a message
        /// </summary>
        public async Task<bool> SendMessageAsync(HsmsMessage message, CancellationToken cancellationToken = default)
        {
            if (!IsConnected || !IsSelected)
            {
                _logger?.LogWarning("Not connected or selected");
                return false;
            }

            try
            {
                await _connection!.SendMessageAsync(message, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send message");
                ErrorOccurred?.Invoke(this, ex);
                return false;
            }
        }

        /// <summary>
        /// Wait for a message with timeout (using event-based approach)
        /// </summary>
        public async Task<HsmsMessage?> WaitForMessageAsync(
            Func<HsmsMessage, bool> predicate,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (!IsConnected || !IsSelected)
                return null;

            var tcs = new TaskCompletionSource<HsmsMessage>();

            EventHandler<HsmsMessage> handler = (sender, msg) =>
            {
                if (predicate(msg))
                {
                    tcs.TrySetResult(msg);
                }
            };

            try
            {
                MessageReceived += handler;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                return await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                MessageReceived -= handler;
            }
        }

        /// <summary>
        /// Disconnect
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_connection == null)
                return;

            await _connectionLock.WaitAsync();
            try
            {
                IsSelected = false;

                if (_connection != null)
                {
                    await _connection.DisconnectAsync();
                    _connection.MessageReceived -= OnMessageReceived;
                    _connection.ErrorOccurred -= OnErrorOccurred;
                    _connection.Dispose();
                    _connection = null;
                }

                Disconnected?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private void OnMessageReceived(object? sender, HsmsMessage message)
        {
            _logger?.LogDebug("[SimpleResilient] OnMessageReceived: Type={Type}, SystemBytes={SystemBytes}",
                message.MessageType, message.SystemBytes);

            // Handle pending selection response
            if (_pendingResponse != null && message.SystemBytes == _pendingSystemBytes)
            {
                _logger?.LogInformation("[SimpleResilient] Received matching response for pending selection");
                if (message.MessageType == HsmsMessageType.SelectRsp ||
                    message.MessageType == HsmsMessageType.RejectReq)
                {
                    _pendingResponse.TrySetResult(message);
                    return; // Don't forward control messages
                }
            }

            // Forward data messages to subscribers
            if (message.MessageType == HsmsMessageType.DataMessage)
            {
                _logger?.LogDebug("[SimpleResilient] Forwarding data message to subscribers");
                MessageReceived?.Invoke(this, message);
            }
        }

        private void OnErrorOccurred(object? sender, Exception ex)
        {
            _logger?.LogError(ex, "[SimpleResilient] Connection error occurred: {Message}", ex.Message);
            ErrorOccurred?.Invoke(this, ex);

            // Simple approach: disconnect on error, let caller reconnect if needed
            _logger?.LogInformation("[SimpleResilient] Scheduling disconnect due to error");
            Task.Run(async () => await DisconnectAsync());
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                DisconnectAsync().GetAwaiter().GetResult();
            }
            catch { }

            _connectionLock?.Dispose();
        }
    }
}