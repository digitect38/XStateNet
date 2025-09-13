using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XStateNet.Semi.Secs;
using XStateNet.Semi.Transport;

namespace SemiStandard.Integration.Tests
{
    /// <summary>
    /// Non-blocking equipment simulator for testing
    /// </summary>
    public class NonBlockingEquipmentSimulator : IDisposable
    {
        private readonly IPEndPoint _endpoint;
        private readonly ILogger<NonBlockingEquipmentSimulator>? _logger;
        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _acceptTask;
        private Task? _receiveTask;
        private readonly ConcurrentDictionary<uint, SecsMessage> _statusVariables = new();
        private bool _disposed;
        private bool _isSelected;

        public string ModelName { get; set; } = "TestEquipment";
        public string SoftwareRevision { get; set; } = "1.0.0";
        public bool IsConnected => _client?.Connected ?? false;
        
        private readonly Queue<HsmsMessage> _messagesToSend = new();

        public NonBlockingEquipmentSimulator(IPEndPoint endpoint, ILogger<NonBlockingEquipmentSimulator>? logger = null)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _logger = logger;
            InitializeDefaultResponses();
        }

        /// <summary>
        /// Start listening for connections (non-blocking)
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NonBlockingEquipmentSimulator));

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(_endpoint.Address, _endpoint.Port);
            _listener.Start();
            
            _logger?.LogInformation("Simulator listening on {Endpoint}", _endpoint);
            
            // Start accepting connections in background (non-blocking)
            _acceptTask = Task.Run(async () => await AcceptConnectionAsync(_cancellationTokenSource.Token));
            
            return Task.CompletedTask; // Return immediately, don't block
        }

        private async Task AcceptConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger?.LogDebug("Waiting for client connection...");
                _client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                _client.NoDelay = true;
                _stream = _client.GetStream();
                
                _logger?.LogInformation("Client connected from {RemoteEndPoint}", _client.Client.RemoteEndPoint);
                
                // Start receiving messages
                _receiveTask = Task.Run(async () => await ReceiveMessagesAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Error accepting connection");
            }
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[65536];
            var headerBuffer = new byte[14];
            
            try
            {
                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    // Read HSMS header
                    var bytesRead = 0;
                    while (bytesRead < 14)
                    {
                        var read = await _stream!.ReadAsync(headerBuffer.AsMemory(bytesRead, 14 - bytesRead), cancellationToken);
                        if (read == 0) return; // Connection closed
                        bytesRead += read;
                    }
                    
                    // Parse header
                    var totalLength = (headerBuffer[0] << 24) | (headerBuffer[1] << 16) | (headerBuffer[2] << 8) | headerBuffer[3];
                    var dataLength = totalLength - 10;
                    var messageType = (HsmsMessageType)headerBuffer[8];
                    var systemBytes = (uint)((headerBuffer[10] << 24) | (headerBuffer[11] << 16) | (headerBuffer[12] << 8) | headerBuffer[13]);
                    
                    // Read data if present
                    byte[]? data = null;
                    if (dataLength > 0)
                    {
                        data = new byte[dataLength];
                        bytesRead = 0;
                        while (bytesRead < dataLength)
                        {
                            var read = await _stream!.ReadAsync(data.AsMemory(bytesRead, dataLength - bytesRead), cancellationToken);
                            if (read == 0) return;
                            bytesRead += read;
                        }
                    }
                    
                    _logger?.LogDebug("Received HSMS message: Type={Type}, SystemBytes={SystemBytes}", messageType, systemBytes);
                    
                    // Handle message
                    await HandleMessageAsync(new HsmsMessage
                    {
                        Stream = headerBuffer[6],
                        Function = headerBuffer[7],
                        MessageType = messageType,
                        SystemBytes = systemBytes,
                        Data = data
                    }, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Error receiving messages");
            }
        }

        private async Task HandleMessageAsync(HsmsMessage message, CancellationToken cancellationToken)
        {
            try
            {
                HsmsMessage? response = null;
                
                switch (message.MessageType)
                {
                    case HsmsMessageType.SelectReq:
                        _isSelected = true;
                        response = new HsmsMessage
                        {
                            MessageType = HsmsMessageType.SelectRsp,
                            SystemBytes = message.SystemBytes
                        };
                        _logger?.LogInformation("Accepted selection request");
                        break;
                        
                    case HsmsMessageType.LinktestReq:
                        response = new HsmsMessage
                        {
                            MessageType = HsmsMessageType.LinktestRsp,
                            SystemBytes = message.SystemBytes
                        };
                        break;
                        
                    case HsmsMessageType.DataMessage:
                        if (_isSelected)
                        {
                            response = await HandleSecsMessageAsync(message);
                            if (response != null)
                            {
                                _logger?.LogDebug("HandleSecsMessageAsync returned response for S{Stream}F{Function}", response.Stream, response.Function);
                            }
                            else
                            {
                                _logger?.LogWarning("HandleSecsMessageAsync returned null for S{Stream}F{Function}", message.Stream, message.Function);
                            }
                        }
                        break;
                }
                
                if (response != null)
                {
                    _logger?.LogDebug("Sending response: Type={Type}, S{Stream}F{Function}", response.MessageType, response.Stream, response.Function);
                    await SendMessageAsync(response, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling message");
            }
        }

        private Task<HsmsMessage?> HandleSecsMessageAsync(HsmsMessage hsmsMessage)
        {
            var sxfy = $"S{hsmsMessage.Stream}F{hsmsMessage.Function}";
            _logger?.LogDebug("Handling SECS message: {SxFy}, Stream={Stream}, Function={Function}", sxfy, hsmsMessage.Stream, hsmsMessage.Function);
            
            SecsMessage? responseMessage = null;
            
            // Handle specific messages
            _logger?.LogDebug("Checking switch case for: '{SxFy}'", sxfy);
            switch (sxfy)
            {
                case "S1F1": // Are You There
                    responseMessage = new SecsMessage(1, 2, false)
                    {
                        Data = new SecsList(
                            new SecsAscii(ModelName),
                            new SecsAscii(SoftwareRevision)
                        )
                    };
                    break;
                    
                case "S1F3": // Status Variables Request
                    var requestData = hsmsMessage.Data != null ? 
                        SecsMessage.Decode(hsmsMessage.Stream, hsmsMessage.Function, hsmsMessage.Data, true).Data : null;
                    
                    if (requestData is SecsList list)
                    {
                        var items = new SecsItem[list.Items.Count];
                        for (int i = 0; i < list.Items.Count; i++)
                        {
                            items[i] = new SecsU4(1000 + (uint)i); // Return dummy values
                        }
                        responseMessage = new SecsMessage(1, 4, false)
                        {
                            Data = new SecsList(items)
                        };
                    }
                    break;
                    
                case "S1F13": // Establish Communications
                    responseMessage = new SecsMessage(1, 14, false)
                    {
                        Data = new SecsList(
                            new SecsU1(0), // COMMACK = 0 (accepted)
                            new SecsList() // Empty MDLN list
                        )
                    };
                    break;
                    
                case "S2F13": // Equipment Constants Request
                    _logger?.LogDebug("Handling S2F13 request");
                    var ecRequest = hsmsMessage.Data != null ? 
                        SecsMessage.Decode(hsmsMessage.Stream, hsmsMessage.Function, hsmsMessage.Data, true).Data : null;
                    
                    _logger?.LogDebug("S2F13 request data type: {Type}", ecRequest?.GetType().Name ?? "null");
                    
                    if (ecRequest is SecsList ecList)
                    {
                        _logger?.LogDebug("S2F13 request is SecsList with {Count} items", ecList.Items.Count);
                        var ecItems = new SecsItem[ecList.Items.Count];
                        for (int i = 0; i < ecList.Items.Count; i++)
                        {
                            // Return dummy equipment constant values
                            ecItems[i] = new SecsU4(5000 + (uint)i);
                        }
                        responseMessage = new SecsMessage(2, 14, false)
                        {
                            Data = new SecsList(ecItems)
                        };
                        _logger?.LogDebug("Created S2F14 response with {Count} equipment constants", ecItems.Length);
                    }
                    else
                    {
                        _logger?.LogWarning("S2F13 request data is not a SecsList");
                    }
                    break;
                    
                default:
                    _logger?.LogWarning("No handler for message: {SxFy}", sxfy);
                    break;
            }
            
            if (responseMessage != null)
            {
                responseMessage.SystemBytes = hsmsMessage.SystemBytes;
                var encodedData = responseMessage.Encode();
                
                return Task.FromResult<HsmsMessage?>(new HsmsMessage
                {
                    Stream = (byte)responseMessage.Stream,
                    Function = (byte)responseMessage.Function,
                    MessageType = HsmsMessageType.DataMessage,
                    SystemBytes = responseMessage.SystemBytes,
                    Data = encodedData
                });
            }
            
            return Task.FromResult<HsmsMessage?>(null);
        }

        private async Task SendMessageAsync(HsmsMessage message, CancellationToken cancellationToken)
        {
            if (_stream == null || !IsConnected) return;
            
            var dataLength = message.Data?.Length ?? 0;
            var totalLength = dataLength + 10;
            var buffer = new byte[14 + dataLength];
            
            // Length
            buffer[0] = (byte)(totalLength >> 24);
            buffer[1] = (byte)(totalLength >> 16);
            buffer[2] = (byte)(totalLength >> 8);
            buffer[3] = (byte)totalLength;
            
            // Session ID (0 for now)
            buffer[4] = 0;
            buffer[5] = 0;
            
            // Stream & Function
            buffer[6] = message.Stream;
            buffer[7] = message.Function;
            
            // Message Type
            buffer[8] = (byte)message.MessageType;
            buffer[9] = 0;
            
            // System Bytes
            buffer[10] = (byte)(message.SystemBytes >> 24);
            buffer[11] = (byte)(message.SystemBytes >> 16);
            buffer[12] = (byte)(message.SystemBytes >> 8);
            buffer[13] = (byte)message.SystemBytes;
            
            // Data
            if (message.Data != null && dataLength > 0)
            {
                Array.Copy(message.Data, 0, buffer, 14, dataLength);
            }
            
            await _stream.WriteAsync(buffer.AsMemory(0, 14 + dataLength), cancellationToken);
            await _stream.FlushAsync(cancellationToken);
            
            _logger?.LogDebug("Sent HSMS response: Type={Type}, SystemBytes={SystemBytes}", message.MessageType, message.SystemBytes);
        }

        private void InitializeDefaultResponses()
        {
            // Initialize default status variables
            _statusVariables[1] = new SecsMessage(1, 4, false) { Data = new SecsU4(100) };
            _statusVariables[2] = new SecsMessage(1, 4, false) { Data = new SecsU4(200) };
            _statusVariables[3] = new SecsMessage(1, 4, false) { Data = new SecsU4(300) };
        }
        
        /// <summary>
        /// Trigger an alarm to be sent to the host
        /// </summary>
        public async Task TriggerAlarmAsync(uint alarmId, string alarmText, bool set)
        {
            if (!IsConnected || !_isSelected || _stream == null) return;
            
            // Create S5F1 alarm message
            var alarmMessage = new SecsMessage(5, 1, true)
            {
                Data = new SecsList(
                    new SecsU1((byte)(set ? 128 : 0)), // ALCD - bit 7 set for alarm set
                    new SecsU4(alarmId), // ALID
                    new SecsAscii(alarmText) // ALTX
                ),
                SystemBytes = (uint)Random.Shared.Next(1, 65536)  // Use 16-bit range for SEMI compatibility
            };
            
            var encodedData = alarmMessage.Encode();
            var hsmsMessage = new HsmsMessage
            {
                Stream = 5,
                Function = 1,
                MessageType = HsmsMessageType.DataMessage,
                SystemBytes = alarmMessage.SystemBytes,
                Data = encodedData
            };
            
            await SendMessageAsync(hsmsMessage, CancellationToken.None);
            _logger?.LogInformation("Sent S5F1 alarm: ID={AlarmId}, Text={AlarmText}, Set={Set}", alarmId, alarmText, set);
        }
        
        /// <summary>
        /// Trigger an event to be sent to the host
        /// </summary>
        public async Task TriggerEventAsync(uint ceid, List<SecsItem> data)
        {
            if (!IsConnected || !_isSelected || _stream == null) return;
            
            // Create S6F11 event message
            var eventMessage = new SecsMessage(6, 11, true)
            {
                Data = new SecsList(
                    new SecsU4(0), // DATAID
                    new SecsU4(ceid), // CEID
                    new SecsList(data.ToArray()) // Event data
                ),
                SystemBytes = (uint)Random.Shared.Next(1, 65536)  // Use 16-bit range for SEMI compatibility
            };
            
            var encodedData = eventMessage.Encode();
            var hsmsMessage = new HsmsMessage
            {
                Stream = 6,
                Function = 11,
                MessageType = HsmsMessageType.DataMessage,
                SystemBytes = eventMessage.SystemBytes,
                Data = encodedData
            };
            
            await SendMessageAsync(hsmsMessage, CancellationToken.None);
            _logger?.LogInformation("Sent S6F11 event: CEID={Ceid}", ceid);
        }

        public async Task StopAsync()
        {
            _cancellationTokenSource?.Cancel();
            
            if (_acceptTask != null)
            {
                try { await _acceptTask; } catch { }
            }
            
            if (_receiveTask != null)
            {
                try { await _receiveTask; } catch { }
            }
            
            _stream?.Close();
            _client?.Close();
            _listener?.Stop();
            
            _logger?.LogInformation("Simulator stopped");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            StopAsync().GetAwaiter().GetResult();
            
            _stream?.Dispose();
            _client?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}