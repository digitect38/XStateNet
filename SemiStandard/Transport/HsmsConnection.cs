using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace XStateNet.Semi.Transport
{
    /// <summary>
    /// SEMI E37 HSMS TCP/IP connection implementation
    /// Handles low-level socket communication for SECS messages
    /// </summary>
    public class HsmsConnection : IDisposable
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private readonly IPEndPoint _endpoint;
        private readonly HsmsConnectionMode _mode;
        private readonly ILogger<HsmsConnection>? _logger;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _receiveTask;
        private readonly ConcurrentQueue<HsmsMessage> _sendQueue = new();
        private readonly ConcurrentQueue<HsmsMessage> _receiveQueue = new();
        private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
        private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
        private bool _disposed;
        
        // HSMS connection parameters (in milliseconds)
        public int T3Timeout { get; set; } = 45000;  // Reply timeout
        public int T5Timeout { get; set; } = 10000;  // Connect separation timeout
        public int T6Timeout { get; set; } = 5000;   // Control transaction timeout
        public int T7Timeout { get; set; } = 10000;  // Not selected timeout
        public int T8Timeout { get; set; } = 5000;   // Network intercharacter timeout
        
        public bool IsConnected => _tcpClient?.Connected ?? false;
        public HsmsConnectionState State { get; private set; } = HsmsConnectionState.NotConnected;
        
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
        
        public HsmsConnection(IPEndPoint endpoint, HsmsConnectionMode mode, ILogger<HsmsConnection>? logger = null)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _mode = mode;
            _logger = logger;
            
            // Add mode to log context for identification
            if (_logger != null)
            {
                using (_logger.BeginScope(new ConcurrentDictionary<string, object> { ["Mode"] = mode.ToString() }))
                {
                    _logger.LogDebug("HsmsConnection created in {Mode} mode", mode);
                }
            }
        }
        
        /// <summary>
        /// Connect to the remote endpoint (Active mode) or start listening (Passive mode)
        /// </summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HsmsConnection));
                
            if (IsConnected)
                return;
                
            try
            {
                SetState(HsmsConnectionState.Connecting);
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                
                if (_mode == HsmsConnectionMode.Active)
                {
                    await ConnectActiveAsync(cancellationToken);
                }
                else
                {
                    await ConnectPassiveAsync(cancellationToken);
                }
                
                SetState(HsmsConnectionState.Connected);
                StartReceiveLoop();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to connect");
                SetState(HsmsConnectionState.Error);
                throw;
            }
        }
        
        private async Task ConnectActiveAsync(CancellationToken cancellationToken)
        {
            _tcpClient = new TcpClient();
            _tcpClient.NoDelay = true; // Disable Nagle's algorithm for low latency
            // Don't set timeouts on TcpClient as they don't work well with async operations
            // We'll handle timeouts with CancellationTokens instead
            // _tcpClient.ReceiveTimeout = T8Timeout;
            // _tcpClient.SendTimeout = T8Timeout;
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(T5Timeout);
            
            await _tcpClient.ConnectAsync(_endpoint.Address, _endpoint.Port, cts.Token);
            _stream = _tcpClient.GetStream();
            _logger?.LogInformation("[ACTIVE] Connected to {Endpoint} in Active mode, Stream: {StreamHash}", _endpoint, _stream?.GetHashCode());
        }
        
        private async Task ConnectPassiveAsync(CancellationToken cancellationToken)
        {
            var listener = new TcpListener(_endpoint.Address, _endpoint.Port);
            listener.Start();
            
            try
            {
                _logger?.LogInformation("Listening on {Endpoint} in Passive mode", _endpoint);
                
                // Don't apply T5 timeout if we're explicitly cancelled
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
                // Don't set timeouts on TcpClient as they don't work well with async operations
                // _tcpClient.ReceiveTimeout = T8Timeout;
                // _tcpClient.SendTimeout = T8Timeout;
                
                _stream = _tcpClient.GetStream();
                
                // Flush any pending data in the stream
                while (_stream.DataAvailable)
                {
                    var dummy = new byte[1024];
                    _stream.Read(dummy, 0, dummy.Length);
                    _logger?.LogWarning("[PASSIVE] Flushed {Bytes} bytes of unexpected data from stream", dummy.Length);
                }
                _logger?.LogInformation("[PASSIVE] Accepted connection from {RemoteEndPoint} in Passive mode, Stream: {StreamHash}", 
                    _tcpClient.Client.RemoteEndPoint, _stream?.GetHashCode());
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
                Array.Clear(buffer, 0, Math.Min(buffer.Length, message.Length + 14)); // Clear buffer before use
                try
                {
                    var length = EncodeMessage(message, buffer);
                    _logger?.LogDebug("[{Mode}] About to send HSMS message: Type={Type}, Stream={Stream}, Function={Function}, SystemBytes={SystemBytes}, DataLength={DataLength}, BufferLength={BufferLength}", 
                        _mode, message.MessageType, message.Stream, message.Function, message.SystemBytes, message.Data?.Length ?? 0, length);
                    
                    // Log hex dump of header
                    var headerHex = BitConverter.ToString(buffer, 0, Math.Min(14, length));
                    _logger?.LogDebug("HSMS Header (hex): {HeaderHex}", headerHex);
                    
                    await _stream.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
                    await _stream.FlushAsync(cancellationToken);
                    
                    _logger?.LogDebug("Successfully sent HSMS message");
                }
                finally
                {
                    _bufferPool.Return(buffer, clearArray: true); // Clear buffer before returning to pool
                }
            }
            finally
            {
                _sendSemaphore.Release();
            }
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
                            _logger?.LogDebug("[{Mode}] Waiting for next HSMS message... DataAvailable: {Available}", _mode, _stream?.DataAvailable ?? false);
                            // Read HSMS header (14 bytes)
                            var bytesRead = 0;
                            while (bytesRead < 14)
                            {
                                var read = await _stream!.ReadAsync(
                                    headerBuffer.AsMemory(bytesRead, 14 - bytesRead), 
                                    _cancellationTokenSource.Token);
                                    
                                if (read == 0)
                                    throw new EndOfStreamException("Connection closed by remote");
                                    
                                bytesRead += read;
                                
                                if (bytesRead < 14)
                                {
                                    _logger?.LogDebug("[{Mode}] Partial header read: {Read} bytes, total: {Total}/14", _mode, read, bytesRead);
                                }
                            }
                            
                            // Parse header to get message length (excluding the 4-byte length field itself)
                            var totalLength = (headerBuffer[0] << 24) | 
                                              (headerBuffer[1] << 16) | 
                                              (headerBuffer[2] << 8) | 
                                              headerBuffer[3];
                            
                            // The total length includes the 10-byte HSMS header after the length field
                            // So actual data length = totalLength - 10
                            var dataLength = totalLength - 10;
                            
                            // Log received header
                            var headerHex = BitConverter.ToString(headerBuffer);
                            _logger?.LogDebug("[{Mode}] Received HSMS Header (hex): {HeaderHex}, TotalLength: {TotalLength}, DataLength: {DataLength}", _mode, headerHex, totalLength, dataLength);
                                              
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
                                        throw new EndOfStreamException("Connection closed by remote");
                                        
                                    bytesRead += read;
                                }
                            }
                            
                            // Decode and process message
                            _logger?.LogDebug("[{Mode}] About to decode message with dataLength={DataLength}", _mode, dataLength);
                            var message = DecodeMessage(headerBuffer, buffer, dataLength);
                            _logger?.LogDebug("[{Mode}] Decoded message: Type={Type}, SystemBytes={SystemBytes}", _mode, message.MessageType, message.SystemBytes);
                            _receiveQueue.Enqueue(message);
                            _logger?.LogDebug("[{Mode}] Enqueued message, queue size={QueueSize}", _mode, _receiveQueue.Count);
                            MessageReceived?.Invoke(this, message);
                            
                            _logger?.LogDebug("Received HSMS message: Type={Type}, Stream={Stream}, Function={Function}, SystemBytes={SystemBytes}, DataLength={DataLength}", 
                                message.MessageType, message.Stream, message.Function, message.SystemBytes, message.Data?.Length ?? 0);
                            _logger?.LogDebug("Continuing receive loop...");
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger?.LogError(ex, "Error receiving message");
                            ErrorOccurred?.Invoke(this, ex);
                            break; // Exit the loop on error
                        }
                    }
                }
                finally
                {
                    _bufferPool.Return(buffer, clearArray: true); // Clear buffer before returning to pool
                }
            });
        }
        
        /// <summary>
        /// Encode HSMS message to byte array
        /// </summary>
        private int EncodeMessage(HsmsMessage message, byte[] buffer)
        {
            var dataLength = message.Data?.Length ?? 0;
            var totalLength = dataLength + 10; // 10 bytes for HSMS header after length field
            
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
            
            // Header Byte 5: Reserved (SType upper bits)
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
                
            try
            {
                SetState(HsmsConnectionState.Disconnecting);
                _cancellationTokenSource?.Cancel();
                
                if (_receiveTask != null)
                {
                    try
                    {
                        await _receiveTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation token is triggered
                    }
                }
                    
                _stream?.Close();
                _tcpClient?.Close();
            }
            finally
            {
                SetState(HsmsConnectionState.NotConnected);
                _logger?.LogInformation("Disconnected from {Endpoint}", _endpoint);
            }
        }
        
        private void SetState(HsmsConnectionState newState)
        {
            if (State != newState)
            {
                State = newState;
                StateChanged?.Invoke(this, newState);
            }
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _stream?.Dispose();
            _tcpClient?.Dispose();
            _sendSemaphore?.Dispose();
        }
    }
    
    /// <summary>
    /// HSMS message structure
    /// </summary>
    public class HsmsMessage
    {
        public ushort SessionId { get; set; }
        public byte Stream { get; set; }
        public byte Function { get; set; }
        public HsmsMessageType MessageType { get; set; }
        public uint SystemBytes { get; set; }
        public byte[]? Data { get; set; }
        public int Length => Data?.Length ?? 0;
    }
    
    /// <summary>
    /// HSMS message types
    /// </summary>
    public enum HsmsMessageType : byte
    {
        DataMessage = 0,
        SelectReq = 1,
        SelectRsp = 2,
        DeselectReq = 3,
        DeselectRsp = 4,
        LinktestReq = 5,
        LinktestRsp = 6,
        RejectReq = 7,
        SeparateReq = 9
    }
}