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
            _tcpClient.ReceiveTimeout = T8Timeout;
            _tcpClient.SendTimeout = T8Timeout;
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(T5Timeout);
            
            await _tcpClient.ConnectAsync(_endpoint.Address, _endpoint.Port, cts.Token);
            _stream = _tcpClient.GetStream();
            _logger?.LogInformation("Connected to {Endpoint} in Active mode", _endpoint);
        }
        
        private async Task ConnectPassiveAsync(CancellationToken cancellationToken)
        {
            var listener = new TcpListener(_endpoint.Address, _endpoint.Port);
            listener.Start();
            
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(T5Timeout);
                
                _tcpClient = await listener.AcceptTcpClientAsync(cts.Token);
                _tcpClient.NoDelay = true;
                _tcpClient.ReceiveTimeout = T8Timeout;
                _tcpClient.SendTimeout = T8Timeout;
                
                _stream = _tcpClient.GetStream();
                _logger?.LogInformation("Accepted connection from {RemoteEndPoint} in Passive mode", 
                    _tcpClient.Client.RemoteEndPoint);
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
                try
                {
                    var length = EncodeMessage(message, buffer);
                    await _stream.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
                    await _stream.FlushAsync(cancellationToken);
                    
                    _logger?.LogDebug("Sent HSMS message: Type={Type}, Stream={Stream}, Function={Function}", 
                        message.MessageType, message.Stream, message.Function);
                }
                finally
                {
                    _bufferPool.Return(buffer);
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
                            }
                            
                            // Parse header to get message length
                            var messageLength = (headerBuffer[0] << 24) | 
                                              (headerBuffer[1] << 16) | 
                                              (headerBuffer[2] << 8) | 
                                              headerBuffer[3];
                                              
                            // Read message body
                            if (messageLength > 0)
                            {
                                if (messageLength > buffer.Length)
                                {
                                    _bufferPool.Return(buffer);
                                    buffer = _bufferPool.Rent(messageLength);
                                }
                                
                                bytesRead = 0;
                                while (bytesRead < messageLength)
                                {
                                    var read = await _stream.ReadAsync(
                                        buffer.AsMemory(bytesRead, messageLength - bytesRead),
                                        _cancellationTokenSource.Token);
                                        
                                    if (read == 0)
                                        throw new EndOfStreamException("Connection closed by remote");
                                        
                                    bytesRead += read;
                                }
                            }
                            
                            // Decode and process message
                            var message = DecodeMessage(headerBuffer, buffer, messageLength);
                            _receiveQueue.Enqueue(message);
                            MessageReceived?.Invoke(this, message);
                            
                            _logger?.LogDebug("Received HSMS message: Type={Type}, Stream={Stream}, Function={Function}", 
                                message.MessageType, message.Stream, message.Function);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger?.LogError(ex, "Error receiving message");
                            ErrorOccurred?.Invoke(this, ex);
                        }
                    }
                }
                finally
                {
                    _bufferPool.Return(buffer);
                }
            }, _cancellationTokenSource!.Token);
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
            
            // Header Byte 5: System Bytes
            buffer[9] = (byte)(message.SystemBytes >> 24);
            buffer[10] = (byte)(message.SystemBytes >> 16);
            buffer[11] = (byte)(message.SystemBytes >> 8);
            buffer[12] = (byte)message.SystemBytes;
            
            // Header Byte 6-10: System bytes continued (already set above)
            buffer[13] = 0; // Reserved
            
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
                SystemBytes = (uint)((header[9] << 24) | (header[10] << 16) | (header[11] << 8) | header[12]),
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
                    await _receiveTask;
                    
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