using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace XStateNet.Semi.Transport
{
    /// <summary>
    /// SEMI E37 HSMS TCP/IP connection implementation using XStateNet state machine
    /// Handles low-level socket communication for SECS messages with elegant state management
    /// </summary>
    public class XStateNetHsmsConnection : IDisposable
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private readonly IPEndPoint _endpoint;
        private readonly HsmsConnectionMode _mode;
        private readonly ILogger<XStateNetHsmsConnection>? _logger;
        private readonly StateMachine _stateMachine;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _receiveTask;
        private readonly ConcurrentQueue<HsmsMessage> _sendQueue = new();
        private readonly ConcurrentQueue<HsmsMessage> _receiveQueue = new();
        private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
        private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
        private bool _disposed;
        private TaskCompletionSource<bool>? _connectTcs;
        private TaskCompletionSource<bool>? _disconnectTcs;
        private Exception? _lastError;

        // HSMS connection parameters (in milliseconds)
        public int T3Timeout { get; set; } = 45000;  // Reply timeout
        public int T5Timeout { get; set; } = 10000;  // Connect separation timeout
        public int T6Timeout { get; set; } = 5000;   // Control transaction timeout
        public int T7Timeout { get; set; } = 10000;  // Not selected timeout
        public int T8Timeout { get; set; } = 5000;   // Network intercharacter timeout

        public bool IsConnected => _tcpClient?.Connected ?? false;
        public HsmsConnectionState State => GetCurrentState();

        public event EventHandler<HsmsMessage>? MessageReceived;
        public event EventHandler<HsmsConnectionState>? StateChanged;
        public event EventHandler<Exception>? ErrorOccurred;

        public enum HsmsConnectionMode
        {
            Active,  // Initiates connection (typically Host)
            Passive  // Accepts connection (typically Equipment)
        }

        public enum HsmsConnectionState
        {
            NotConnected,
            Connecting,
            Connected,
            Selected,
            Disconnecting,
            Error
        }

        public XStateNetHsmsConnection(IPEndPoint endpoint, HsmsConnectionMode mode, ILogger<XStateNetHsmsConnection>? logger = null)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _mode = mode;
            _logger = logger;

            // Create state machine
            _stateMachine = CreateStateMachine();
            _stateMachine.Start();

            // Add mode to log context for identification
            if (_logger != null)
            {
                using (_logger.BeginScope(new ConcurrentDictionary<string, object> { ["Mode"] = mode.ToString() }))
                {
                    _logger.LogDebug("XStateNetHsmsConnection created in {Mode} mode", mode);
                }
            }
        }

        private StateMachine CreateStateMachine()
        {
            var config = @"{
                'id': 'HsmsConnection',
                'initial': 'notConnected',
                'context': {
                    'mode': '" + _mode.ToString() + @"',
                    'retryCount': 0
                },
                'states': {
                    'notConnected': {
                        'on': {
                            'CONNECT': 'connecting'
                        }
                    },
                    'connecting': {
                        'entry': 'doConnect',
                        'on': {
                            'CONNECTED': 'connected',
                            'CONNECT_FAILED': [
                                {
                                    'target': 'waitingRetry',
                                    'cond': 'shouldRetry',
                                    'actions': 'incrementRetry'
                                },
                                {
                                    'target': 'error',
                                    'actions': 'reportError'
                                }
                            ],
                            'CANCEL': 'notConnected'
                        }
                    },
                    'waitingRetry': {
                        'after': {
                            '2000': 'connecting'
                        },
                        'on': {
                            'CANCEL': 'notConnected'
                        }
                    },
                    'connected': {
                        'entry': ['startReceiving', 'resetRetry'],
                        'on': {
                            'SELECT': 'selected',
                            'DISCONNECT': 'disconnecting',
                            'CONNECTION_LOST': 'error',
                            'ERROR': 'error'
                        },
                        'after': {
                            '" + T7Timeout + @"': {
                                'target': 'error',
                                'actions': 'timeoutNotSelected'
                            }
                        }
                    },
                    'selected': {
                        'on': {
                            'DESELECT': 'connected',
                            'DISCONNECT': 'disconnecting',
                            'CONNECTION_LOST': 'error',
                            'ERROR': 'error'
                        }
                    },
                    'disconnecting': {
                        'entry': 'doDisconnect',
                        'on': {
                            'DISCONNECTED': 'notConnected'
                        }
                    },
                    'error': {
                        'entry': 'handleError',
                        'on': {
                            'RECONNECT': 'connecting',
                            'RESET': 'notConnected'
                        }
                    }
                }
            }";

            var actionMap = new ActionMap();

            actionMap["doConnect"] = new List<NamedAction>
            {
                new NamedAction("doConnect", async (sm) =>
                {
                    try
                    {
                        _cancellationTokenSource = new CancellationTokenSource();

                        if (_mode == HsmsConnectionMode.Active)
                        {
                            await ConnectActiveAsync(_cancellationTokenSource.Token);
                        }
                        else
                        {
                            await ConnectPassiveAsync(_cancellationTokenSource.Token);
                        }

                        _ = Task.Run(async () => await sm.SendAsync("CONNECTED"));
                        _connectTcs?.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex;
                        _logger?.LogError(ex, "Connection failed");
                        _ = Task.Run(async () => await sm.SendAsync("CONNECT_FAILED"));
                        _connectTcs?.TrySetException(ex);
                    }
                })
            };

            actionMap["startReceiving"] = new List<NamedAction>
            {
                new NamedAction("startReceiving", (sm) =>
                {
                    StartReceiveLoop();
                    _logger?.LogInformation("Started receive loop");
                })
            };

            actionMap["doDisconnect"] = new List<NamedAction>
            {
                new NamedAction("doDisconnect", async (sm) =>
                {
                    try
                    {
                        _cancellationTokenSource?.Cancel();

                        if (_receiveTask != null)
                        {
                            try
                            {
                                await _receiveTask;
                            }
                            catch { }
                        }

                        _stream?.Close();
                        _tcpClient?.Close();

                        _ = Task.Run(async () => await sm.SendAsync("DISCONNECTED"));
                        _disconnectTcs?.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error during disconnect");
                        _ = Task.Run(async () => await sm.SendAsync("DISCONNECTED"));
                    }
                })
            };

            actionMap["incrementRetry"] = new List<NamedAction>
            {
                new NamedAction("incrementRetry", (sm) =>
                {
                    var retryCount = (int)(sm.ContextMap["retryCount"] ?? 0);
                    sm.ContextMap["retryCount"] = retryCount + 1;
                    _logger?.LogWarning("Connection retry #{RetryCount}", retryCount + 1);
                })
            };

            actionMap["resetRetry"] = new List<NamedAction>
            {
                new NamedAction("resetRetry", (sm) =>
                {
                    sm.ContextMap["retryCount"] = 0;
                })
            };

            actionMap["reportError"] = new List<NamedAction>
            {
                new NamedAction("reportError", (sm) =>
                {
                    ErrorOccurred?.Invoke(this, _lastError ?? new Exception("Connection failed"));
                })
            };

            actionMap["handleError"] = new List<NamedAction>
            {
                new NamedAction("handleError", (sm) =>
                {
                    _logger?.LogError(_lastError, "Connection entered error state");
                    ErrorOccurred?.Invoke(this, _lastError ?? new Exception("Connection error"));
                })
            };

            actionMap["timeoutNotSelected"] = new List<NamedAction>
            {
                new NamedAction("timeoutNotSelected", (sm) =>
                {
                    _lastError = new TimeoutException($"T7 timeout: Not selected within {T7Timeout}ms");
                    _logger?.LogError(_lastError.Message);
                })
            };

            var guardMap = new GuardMap();

            guardMap["shouldRetry"] = new NamedGuard("shouldRetry", (sm) =>
            {
                var retryCount = (int)(sm.ContextMap["retryCount"] ?? 0);
                return retryCount < 3; // Max 3 retries
            });

            // Suppress obsolete warning - this is a low-level transport connection handler
            // It doesn't interact with other state machines, so orchestrator is not needed
#pragma warning disable CS0618
            return StateMachineFactory.CreateFromScript(config, threadSafe: false, true, actionMap, guardMap);
#pragma warning restore CS0618
        }

        private HsmsConnectionState GetCurrentState()
        {
            var stateString = _stateMachine.GetActiveStateNames();
            return stateString?.ToLowerInvariant() switch
            {
                var s when s?.Contains("notconnected") == true => HsmsConnectionState.NotConnected,
                var s when s?.Contains("connecting") == true || s?.Contains("waitingretry") == true => HsmsConnectionState.Connecting,
                var s when s?.Contains("connected") == true && !s.Contains("dis") && !s.Contains("not") => HsmsConnectionState.Connected,
                var s when s?.Contains("selected") == true => HsmsConnectionState.Selected,
                var s when s?.Contains("disconnecting") == true => HsmsConnectionState.Disconnecting,
                var s when s?.Contains("error") == true => HsmsConnectionState.Error,
                _ => HsmsConnectionState.NotConnected
            };
        }

        /// <summary>
        /// Connect to the remote endpoint (Active mode) or start listening (Passive mode)
        /// </summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(XStateNetHsmsConnection));

            if (IsConnected)
                return;

            _connectTcs = new TaskCompletionSource<bool>();

            using (cancellationToken.Register(() =>
            {
                _ = Task.Run(async () => await _stateMachine.SendAsync("CANCEL"));
                _connectTcs.TrySetCanceled();
            }))
            {
                await _stateMachine.SendAsync("CONNECT");
                await _connectTcs.Task;
            }
        }

        private async Task ConnectActiveAsync(CancellationToken cancellationToken)
        {
            _tcpClient = new TcpClient();
            _tcpClient.NoDelay = true; // Disable Nagle's algorithm for low latency

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(T5Timeout);

            await _tcpClient.ConnectAsync(_endpoint.Address, _endpoint.Port, cts.Token);
            _stream = _tcpClient.GetStream();
            _logger?.LogInformation("[ACTIVE] Connected to {Endpoint} in Active mode", _endpoint);
        }

        private async Task ConnectPassiveAsync(CancellationToken cancellationToken)
        {
            var listener = new TcpListener(_endpoint.Address, _endpoint.Port);
            listener.Start();

            try
            {
                _logger?.LogInformation("Listening on {Endpoint} in Passive mode", _endpoint);

                Task<TcpClient> acceptTask;
                if (cancellationToken.CanBeCanceled)
                {
                    acceptTask = listener.AcceptTcpClientAsync(cancellationToken).AsTask();
                }
                else
                {
                    using var cts = new CancellationTokenSource(T5Timeout);
                    acceptTask = listener.AcceptTcpClientAsync(cts.Token).AsTask();
                }

                _tcpClient = await acceptTask;
                _tcpClient.NoDelay = true;
                _stream = _tcpClient.GetStream();

                // Flush any pending data in the stream
                while (_stream.DataAvailable)
                {
                    var dummy = new byte[1024];
                    _stream.Read(dummy, 0, dummy.Length);
                }

                _logger?.LogInformation("[PASSIVE] Accepted connection from {RemoteEndPoint} in Passive mode",
                    _tcpClient.Client.RemoteEndPoint);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning("Connection timeout after {Timeout}ms waiting for client", T5Timeout);
                throw new TimeoutException($"No connection received within {T5Timeout}ms");
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>
        /// Send an HSMS message
        /// </summary>
        public async Task SendMessageAsync(HsmsMessage message, CancellationToken cancellationToken = default)
        {
            if (!IsConnected || _stream == null)
                throw new InvalidOperationException("Not connected");

            await _sendSemaphore.WaitAsync(cancellationToken);
            try
            {
                var buffer = _bufferPool.Rent(message.Length + 14); // 14 bytes for HSMS header
                Array.Clear(buffer, 0, Math.Min(buffer.Length, message.Length + 14));
                try
                {
                    var length = EncodeMessage(message, buffer);
                    await _stream.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
                    await _stream.FlushAsync(cancellationToken);

                    _logger?.LogDebug("Successfully sent HSMS message: Type={Type}", message.MessageType);
                }
                finally
                {
                    _bufferPool.Return(buffer, clearArray: true);
                }
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        /// <summary>
        /// Send a Select.req message to transition to Selected state
        /// </summary>
        public async Task SelectAsync(CancellationToken cancellationToken = default)
        {
            if (State != HsmsConnectionState.Connected)
                throw new InvalidOperationException("Can only select when in Connected state");

            // Send Select.req message
            var selectReq = new HsmsMessage
            {
                MessageType = HsmsMessageType.SelectReq,
                SessionId = 0xFFFF,
                SystemBytes = (uint)Random.Shared.Next()
            };

            await SendMessageAsync(selectReq, cancellationToken);
            await _stateMachine.SendAsync("SELECT");
            _logger?.LogInformation("Sent Select.req, transitioning to Selected state");
        }

        /// <summary>
        /// Send a Deselect.req message to transition back to Connected state
        /// </summary>
        public async Task DeselectAsync(CancellationToken cancellationToken = default)
        {
            if (State != HsmsConnectionState.Selected)
                throw new InvalidOperationException("Can only deselect when in Selected state");

            // Send Deselect.req message
            var deselectReq = new HsmsMessage
            {
                MessageType = HsmsMessageType.DeselectReq,
                SessionId = 0xFFFF,
                SystemBytes = (uint)Random.Shared.Next()
            };

            await SendMessageAsync(deselectReq, cancellationToken);
            await _stateMachine.SendAsync("DESELECT");
            _logger?.LogInformation("Sent Deselect.req, transitioning to Connected state");
        }

        /// <summary>
        /// Receive messages continuously
        /// </summary>
        private void StartReceiveLoop()
        {
            _receiveTask = Task.Run(async () =>
            {
                var buffer = _bufferPool.Rent(65536); // 64KB buffer
                var headerBuffer = new byte[14];

                try
                {
                    while (!_cancellationTokenSource!.Token.IsCancellationRequested && IsConnected)
                    {
                        try
                        {
                            // Read HSMS header (14 bytes)
                            var bytesRead = 0;
                            while (bytesRead < 14)
                            {
                                var read = await _stream!.ReadAsync(
                                    headerBuffer.AsMemory(bytesRead, 14 - bytesRead),
                                    _cancellationTokenSource.Token);

                                if (read == 0)
                                {
                                    _lastError = new EndOfStreamException("Connection closed by remote");
                                    _ = Task.Run(async () => await _stateMachine.SendAsync("CONNECTION_LOST"));
                                    return;
                                }

                                bytesRead += read;
                            }

                            // Parse header to get message length
                            var totalLength = (headerBuffer[0] << 24) |
                                              (headerBuffer[1] << 16) |
                                              (headerBuffer[2] << 8) |
                                              headerBuffer[3];

                            var dataLength = totalLength - 10;

                            // Read message body if there is data
                            if (dataLength > 0)
                            {
                                if (dataLength > buffer.Length)
                                {
                                    _bufferPool.Return(buffer);
                                    buffer = _bufferPool.Rent(dataLength);
                                }

                                bytesRead = 0;
                                while (bytesRead < dataLength)
                                {
                                    var read = await _stream!.ReadAsync(
                                        buffer.AsMemory(bytesRead, dataLength - bytesRead),
                                        _cancellationTokenSource.Token);

                                    if (read == 0)
                                    {
                                        _lastError = new EndOfStreamException("Connection closed by remote");
                                        _ = Task.Run(async () => await _stateMachine.SendAsync("CONNECTION_LOST"));
                                        return;
                                    }

                                    bytesRead += read;
                                }
                            }

                            // Decode and process message
                            var message = DecodeMessage(headerBuffer, buffer, dataLength);
                            _receiveQueue.Enqueue(message);
                            MessageReceived?.Invoke(this, message);

                            _logger?.LogDebug("Received HSMS message: Type={Type}", message.MessageType);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger?.LogError(ex, "Error receiving message");
                            _lastError = ex;
                            _ = Task.Run(async () => await _stateMachine.SendAsync("ERROR"));
                            break;
                        }
                    }
                }
                finally
                {
                    _bufferPool.Return(buffer, clearArray: true);
                }
            });
        }

        /// <summary>
        /// Encode HSMS message to byte array
        /// </summary>
        private int EncodeMessage(HsmsMessage message, byte[] buffer)
        {
            var dataLength = message.Data?.Length ?? 0;
            var totalLength = dataLength + 10;

            // Message length (4 bytes)
            buffer[0] = (byte)(totalLength >> 24);
            buffer[1] = (byte)(totalLength >> 16);
            buffer[2] = (byte)(totalLength >> 8);
            buffer[3] = (byte)totalLength;

            // Session ID (2 bytes)
            buffer[4] = (byte)(message.SessionId >> 8);
            buffer[5] = (byte)message.SessionId;

            // Header Byte 2: Stream
            buffer[6] = message.Stream;

            // Header Byte 3: Function
            buffer[7] = message.Function;

            // Header Byte 4: P-Type and S-Type
            buffer[8] = (byte)message.MessageType;

            // Header Byte 5: Reserved
            buffer[9] = 0;

            // System Bytes (4 bytes)
            buffer[10] = (byte)(message.SystemBytes >> 24);
            buffer[11] = (byte)(message.SystemBytes >> 16);
            buffer[12] = (byte)(message.SystemBytes >> 8);
            buffer[13] = (byte)message.SystemBytes;

            // Copy data if present
            if (message.Data != null && dataLength > 0)
            {
                Array.Copy(message.Data, 0, buffer, 14, dataLength);
            }

            return 14 + dataLength;
        }

        /// <summary>
        /// Decode HSMS message from byte arrays
        /// </summary>
        private HsmsMessage DecodeMessage(byte[] header, byte[] data, int dataLength)
        {
            return new HsmsMessage
            {
                SessionId = (ushort)((header[4] << 8) | header[5]),
                Stream = header[6],
                Function = header[7],
                MessageType = (HsmsMessageType)header[8],
                SystemBytes = (uint)((header[10] << 24) | (header[11] << 16) | (header[12] << 8) | header[13]),
                Data = dataLength > 0 ? data[..dataLength] : null
            };
        }

        /// <summary>
        /// Disconnect from remote endpoint
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (!IsConnected)
                return;

            _disconnectTcs = new TaskCompletionSource<bool>();
            await _stateMachine.SendAsync("DISCONNECT");
            await _disconnectTcs.Task;
        }

        /// <summary>
        /// Attempt to reconnect after an error
        /// </summary>
        public async Task ReconnectAsync(CancellationToken cancellationToken = default)
        {
            if (State != HsmsConnectionState.Error)
                throw new InvalidOperationException("Can only reconnect from Error state");

            _connectTcs = new TaskCompletionSource<bool>();

            using (cancellationToken.Register(() =>
            {
                _ = Task.Run(async () => await _stateMachine.SendAsync("CANCEL"));
                _connectTcs.TrySetCanceled();
            }))
            {
                await _stateMachine.SendAsync("RECONNECT");
                await _connectTcs.Task;
            }
        }

        /// <summary>
        /// Reset the connection to initial state
        /// </summary>
        public void Reset()
        {
            _stateMachine.SendAsync("RESET").GetAwaiter().GetResult();
            _lastError = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancellationTokenSource?.Cancel();
            _receiveTask?.Wait(TimeSpan.FromSeconds(1));
            _stream?.Dispose();
            _tcpClient?.Dispose();
            _stateMachine?.Stop();
            _sendSemaphore?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}