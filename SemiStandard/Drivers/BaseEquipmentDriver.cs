using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XStateNet.Semi.Secs;
using XStateNet.Semi.Transport;

namespace XStateNet.Semi.Drivers
{
    /// <summary>
    /// Base implementation of equipment driver with HSMS/SECS communication
    /// </summary>
    public abstract class BaseEquipmentDriver : IEquipmentDriver, IDisposable
    {
        protected HsmsConnection? _connection;
        protected EquipmentConfiguration? _config;
        protected readonly ILogger? _logger;
        // protected E37HSMSSession? _hsmsSession; // TODO: Implement when E37HSMSSession is available
        // protected E30GemController? _gemController; // TODO: Implement when E30GemController is available
        
        private readonly ConcurrentDictionary<uint, TaskCompletionSource<SecsMessage>> _pendingReplies = new();
        private readonly ConcurrentDictionary<uint, object> _statusVariables = new();
        private readonly ConcurrentDictionary<uint, object> _equipmentConstants = new();
        private uint _systemBytesCounter = 1;
        private readonly SemaphoreSlim _commandSemaphore = new(1, 1);
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _disposed;
        
        public abstract string ModelName { get; }
        public abstract string SoftwareRevision { get; }
        public abstract string Manufacturer { get; }
        
        public bool IsConnected => _connection?.IsConnected ?? false;
        public EquipmentState State { get; protected set; } = EquipmentState.Offline;
        
        public event EventHandler<EquipmentState>? StateChanged;
        public event EventHandler<AlarmEventArgs>? AlarmOccurred;
        public event EventHandler<EventReportArgs>? EventReported;
        public event EventHandler<VariableChangedArgs>? VariableChanged;
        
        protected BaseEquipmentDriver(ILogger? logger = null)
        {
            _logger = logger;
        }
        
        public virtual async Task InitializeAsync(EquipmentConfiguration config, CancellationToken cancellationToken = default)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            // Initialize HSMS session
            // TODO: Uncomment when E37HSMSSession is available
            // var mode = config.IsActive ? E37HSMSSession.HSMSMode.Active : E37HSMSSession.HSMSMode.Passive;
            // _hsmsSession = new E37HSMSSession(
            //     config.EquipmentId,
            //     mode,
            //     config.T5Timeout,
            //     config.T6Timeout,
            //     config.T7Timeout,
            //     config.T8Timeout);
            
            // Initialize GEM controller
            // TODO: Uncomment when E30GemController is available
            // _gemController = new E30GemController(config.EquipmentId);
            
            // Load equipment-specific configuration
            await LoadEquipmentConfigurationAsync(cancellationToken);
            
            SetState(EquipmentState.Offline);
        }
        
        public virtual async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsConnected)
                return;
                
            try
            {
                SetState(EquipmentState.Initializing);
                
                // Create HSMS connection
                var endpoint = new IPEndPoint(IPAddress.Parse(_config!.IpAddress), _config.Port);
                var mode = _config.IsActive ? HsmsConnection.HsmsConnectionMode.Active : HsmsConnection.HsmsConnectionMode.Passive;
                
                _connection = new HsmsConnection(endpoint, mode, _logger as ILogger<HsmsConnection>);
                _connection.MessageReceived += OnHsmsMessageReceived;
                _connection.StateChanged += OnConnectionStateChanged;
                _connection.ErrorOccurred += OnConnectionError;
                
                // Set timeouts
                _connection.T5Timeout = _config.T5Timeout;
                _connection.T6Timeout = _config.T6Timeout;
                _connection.T7Timeout = _config.T7Timeout;
                _connection.T8Timeout = _config.T8Timeout;
                
                // Connect
                await _connection.ConnectAsync(cancellationToken);
                
                // Perform HSMS selection
                if (_config.IsActive)
                {
                    await SelectAsync(cancellationToken);
                }
                
                // Establish communication (S1F13/S1F14)
                await EstablishCommunicationAsync(cancellationToken);
                
                SetState(EquipmentState.OnlineLocal);
                _logger?.LogInformation("Equipment driver connected and online");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to connect equipment driver");
                SetState(EquipmentState.Error);
                throw;
            }
        }
        
        public virtual async Task DisconnectAsync()
        {
            if (!IsConnected)
                return;
                
            try
            {
                SetState(EquipmentState.Offline);
                
                // Deselect if needed
                // TODO: Check HSMS session when available
                // if (_hsmsSession?.IsSelected == true)
                if (IsConnected)
                {
                    await DeselectAsync();
                }
                
                // Disconnect HSMS
                if (_connection != null)
                {
                    await _connection.DisconnectAsync();
                    _connection.MessageReceived -= OnHsmsMessageReceived;
                    _connection.StateChanged -= OnConnectionStateChanged;
                    _connection.ErrorOccurred -= OnConnectionError;
                }
                
                _logger?.LogInformation("Equipment driver disconnected");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during disconnect");
            }
        }
        
        public virtual async Task<CommandResult> ExecuteCommandAsync(string command, Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            await _commandSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Convert parameters to SECS items
                var secsParams = new Dictionary<string, SecsItem>();
                foreach (var kvp in parameters)
                {
                    secsParams[kvp.Key] = ConvertToSecsItem(kvp.Value);
                }
                
                // Send S2F41 (Host Command Send)
                var request = SecsMessageLibrary.S2F41(command, secsParams);
                var reply = await SendMessageAsync(request, cancellationToken);
                
                // Parse S2F42 response
                if (reply?.Stream == 2 && reply.Function == 42)
                {
                    var list = reply.Data as SecsList;
                    var hcack = (list?.Items[0] as SecsU1)?.Value ?? 255;
                    
                    return new CommandResult
                    {
                        Success = hcack == SecsMessageLibrary.ResponseCodes.HCACK_OK,
                        ErrorCode = hcack,
                        ErrorMessage = GetHcackMessage(hcack)
                    };
                }
                
                return new CommandResult
                {
                    Success = false,
                    ErrorMessage = "Invalid response"
                };
            }
            finally
            {
                _commandSemaphore.Release();
            }
        }
        
        public virtual async Task<T> ReadConstantAsync<T>(uint ecid, CancellationToken cancellationToken = default)
        {
            // Check cache first
            if (_equipmentConstants.TryGetValue(ecid, out var cached))
                return (T)cached;
                
            // Send S2F13 (Equipment Constant Request)
            var request = SecsMessageLibrary.S2F13(ecid);
            var reply = await SendMessageAsync(request, cancellationToken);
            
            if (reply?.Stream == 2 && reply.Function == 14)
            {
                var list = reply.Data as SecsList;
                if (list?.Items.Count > 0)
                {
                    var value = ConvertFromSecsItem<T>(list.Items[0]);
                    _equipmentConstants[ecid] = value!;
                    return value;
                }
            }
            
            throw new InvalidOperationException($"Failed to read constant {ecid}");
        }
        
        public virtual async Task<bool> WriteConstantAsync(uint ecid, object value, CancellationToken cancellationToken = default)
        {
            var ecidValues = new Dictionary<uint, SecsItem>
            {
                [ecid] = ConvertToSecsItem(value)
            };
            
            // Send S2F15 (New Equipment Constant Send)
            var request = SecsMessageLibrary.S2F15(ecidValues);
            var reply = await SendMessageAsync(request, cancellationToken);
            
            if (reply?.Stream == 2 && reply.Function == 16)
            {
                var eac = (reply.Data as SecsU1)?.Value ?? 255;
                if (eac == SecsMessageLibrary.ResponseCodes.EAC_ACCEPTED)
                {
                    _equipmentConstants[ecid] = value;
                    return true;
                }
            }
            
            return false;
        }
        
        public virtual async Task<T> ReadVariableAsync<T>(uint svid, CancellationToken cancellationToken = default)
        {
            // Check cache first (if variable is tracked)
            if (_statusVariables.TryGetValue(svid, out var cached))
                return (T)cached;
                
            // Send S1F3 (Selected Equipment Status Request)
            var request = SecsMessageLibrary.S1F3(svid);
            var reply = await SendMessageAsync(request, cancellationToken);
            
            if (reply?.Stream == 1 && reply.Function == 4)
            {
                var list = reply.Data as SecsList;
                if (list?.Items.Count > 0)
                {
                    var value = ConvertFromSecsItem<T>(list.Items[0]);
                    _statusVariables[svid] = value!;
                    return value;
                }
            }
            
            throw new InvalidOperationException($"Failed to read variable {svid}");
        }
        
        public virtual async Task<bool> LoadRecipeAsync(string ppid, byte[] ppbody, CancellationToken cancellationToken = default)
        {
            // Send S7F1 (Process Program Load Inquire)
            var inquire = SecsMessageLibrary.S7F1(ppid, (uint)ppbody.Length);
            var grant = await SendMessageAsync(inquire, cancellationToken);
            
            if (grant?.Stream == 7 && grant.Function == 2)
            {
                var ppgnt = (grant.Data as SecsU1)?.Value ?? 255;
                if (ppgnt == SecsMessageLibrary.ResponseCodes.PPGNT_OK)
                {
                    // Send S7F3 (Process Program Send)
                    var send = SecsMessageLibrary.S7F3(ppid, ppbody);
                    var ack = await SendMessageAsync(send, cancellationToken);
                    
                    if (ack?.Stream == 7 && ack.Function == 4)
                    {
                        var ackc7 = (ack.Data as SecsU1)?.Value ?? 255;
                        return ackc7 == SecsMessageLibrary.ResponseCodes.ACKC7_ACCEPTED;
                    }
                }
            }
            
            return false;
        }
        
        public virtual async Task<bool> DeleteRecipeAsync(string ppid, CancellationToken cancellationToken = default)
        {
            // Send S7F17 (Delete Process Program Send)
            var request = SecsMessageLibrary.S7F17(ppid);
            var reply = await SendMessageAsync(request, cancellationToken);
            
            if (reply?.Stream == 7 && reply.Function == 18)
            {
                var ackc7 = (reply.Data as SecsU1)?.Value ?? 255;
                return ackc7 == SecsMessageLibrary.ResponseCodes.ACKC7_ACCEPTED;
            }
            
            return false;
        }
        
        public abstract Task<bool> StartProcessAsync(string ppid, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default);
        public abstract Task<bool> StopProcessAsync(CancellationToken cancellationToken = default);
        public abstract Task<bool> PauseProcessAsync(CancellationToken cancellationToken = default);
        public abstract Task<bool> ResumeProcessAsync(CancellationToken cancellationToken = default);
        
        public virtual async Task ReportAlarmAsync(uint alarmId, AlarmState state, string? text = null)
        {
            var alcd = state == AlarmState.Set ? (byte)128 : (byte)0;
            var message = SecsMessageLibrary.S5F1(alcd, alarmId, text ?? string.Empty);
            
            await SendMessageAsync(message, CancellationToken.None);
            
            AlarmOccurred?.Invoke(this, new AlarmEventArgs
            {
                AlarmId = alarmId,
                State = state,
                Text = text,
                Timestamp = DateTime.UtcNow
            });
        }
        
        public virtual async Task ReportEventAsync(uint eventId, Dictionary<string, object>? data = null)
        {
            var reports = new List<SecsItem>();
            if (data != null)
            {
                foreach (var kvp in data)
                {
                    reports.Add(ConvertToSecsItem(kvp.Value));
                }
            }
            
            var message = SecsMessageLibrary.S6F11(eventId, reports);
            await SendMessageAsync(message, CancellationToken.None);
            
            EventReported?.Invoke(this, new EventReportArgs
            {
                EventId = eventId,
                Data = data,
                Timestamp = DateTime.UtcNow
            });
        }
        
        protected virtual async Task<SecsMessage?> SendMessageAsync(SecsMessage message, CancellationToken cancellationToken)
        {
            if (_connection == null || !IsConnected)
                throw new InvalidOperationException("Not connected");
                
            var systemBytes = GetNextSystemBytes();
            message.SystemBytes = systemBytes;
            
            // Encode SECS message
            var secsData = message.Encode();
            
            // Create HSMS message
            var hsmsMessage = new HsmsMessage
            {
                SessionId = 0,
                Stream = message.Stream,
                Function = message.Function,
                MessageType = HsmsMessageType.DataMessage,
                SystemBytes = systemBytes,
                Data = secsData
            };
            
            // Setup reply handler if needed
            TaskCompletionSource<SecsMessage>? replyTcs = null;
            if (message.ReplyExpected)
            {
                replyTcs = new TaskCompletionSource<SecsMessage>();
                _pendingReplies[systemBytes] = replyTcs;
            }
            
            try
            {
                // Send message
                await _connection.SendMessageAsync(hsmsMessage, cancellationToken);
                
                // Wait for reply if expected
                if (replyTcs != null)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(_config!.T3Timeout);
                    
                    return await replyTcs.Task.WaitAsync(cts.Token);
                }
                
                return null;
            }
            finally
            {
                if (replyTcs != null)
                {
                    _pendingReplies.TryRemove(systemBytes, out _);
                }
            }
        }
        
        protected virtual void OnHsmsMessageReceived(object? sender, HsmsMessage message)
        {
            Task.Run(async () =>
            {
                try
                {
                    // Check if this is a reply to a pending request
                    if (_pendingReplies.TryRemove(message.SystemBytes, out var tcs))
                    {
                        var secsMessage = SecsMessage.Decode(
                            message.Stream,
                            message.Function,
                            message.Data ?? Array.Empty<byte>(),
                            false);
                        
                        tcs.SetResult(secsMessage);
                        return;
                    }
                    
                    // Handle primary message
                    await HandlePrimaryMessageAsync(message);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error handling HSMS message");
                }
            });
        }
        
        protected abstract Task HandlePrimaryMessageAsync(HsmsMessage message);
        protected abstract Task LoadEquipmentConfigurationAsync(CancellationToken cancellationToken);
        
        protected virtual SecsItem ConvertToSecsItem(object value)
        {
            return value switch
            {
                string s => new SecsAscii(s),
                byte b => new SecsU1(b),
                ushort us => new SecsU2(us),
                uint ui => new SecsU4(ui),
                ulong ul => new SecsU8(ul),
                sbyte sb => new SecsI1(sb),
                short s => new SecsI2(s),
                int i => new SecsI4(i),
                long l => new SecsI8(l),
                float f => new SecsF4(f),
                double d => new SecsF8(d),
                bool b => new SecsBoolean(b),
                byte[] ba => new SecsBinary(ba),
                _ => new SecsAscii(value.ToString() ?? string.Empty)
            };
        }
        
        protected virtual T ConvertFromSecsItem<T>(SecsItem item)
        {
            return item switch
            {
                SecsAscii ascii => (T)(object)ascii.Value,
                SecsU1 u1 => (T)(object)u1.Value,
                SecsU2 u2 => (T)(object)u2.Value,
                SecsU4 u4 => (T)(object)u4.Value,
                SecsU8 u8 => (T)(object)u8.Value,
                SecsI1 i1 => (T)(object)i1.Value,
                SecsI2 i2 => (T)(object)i2.Value,
                SecsI4 i4 => (T)(object)i4.Value,
                SecsI8 i8 => (T)(object)i8.Value,
                SecsF4 f4 => (T)(object)f4.Value,
                SecsF8 f8 => (T)(object)f8.Value,
                SecsBoolean b => (T)(object)b.Value[0],
                SecsBinary bin => (T)(object)bin.Value,
                _ => throw new NotSupportedException($"Cannot convert {item.GetType().Name} to {typeof(T).Name}")
            };
        }
        
        private async Task SelectAsync(CancellationToken cancellationToken)
        {
            var selectReq = new HsmsMessage
            {
                MessageType = HsmsMessageType.SelectReq,
                SystemBytes = GetNextSystemBytes()
            };
            
            await _connection!.SendMessageAsync(selectReq, cancellationToken);
            // TODO: Enable HSMS session when available
            // _hsmsSession?.Enable();
        }
        
        private async Task DeselectAsync()
        {
            var deselectReq = new HsmsMessage
            {
                MessageType = HsmsMessageType.DeselectReq,
                SystemBytes = GetNextSystemBytes()
            };
            
            await _connection!.SendMessageAsync(deselectReq, CancellationToken.None);
        }
        
        private async Task EstablishCommunicationAsync(CancellationToken cancellationToken)
        {
            var s1f13 = SecsMessageLibrary.S1F13();
            var s1f14 = await SendMessageAsync(s1f13, cancellationToken);
            
            if (s1f14?.Stream == 1 && s1f14.Function == 14)
            {
                var list = s1f14.Data as SecsList;
                var commack = (list?.Items[0] as SecsU1)?.Value ?? 255;
                
                if (commack != SecsMessageLibrary.ResponseCodes.COMMACK_ACCEPTED)
                {
                    throw new InvalidOperationException("Communication not accepted by equipment");
                }
            }
        }
        
        private void OnConnectionStateChanged(object? sender, HsmsConnection.HsmsConnectionState state)
        {
            _logger?.LogInformation("HSMS connection state changed to {State}", state);
        }
        
        private void OnConnectionError(object? sender, Exception ex)
        {
            _logger?.LogError(ex, "HSMS connection error");
            SetState(EquipmentState.Error);
        }
        
        private void SetState(EquipmentState newState)
        {
            if (State != newState)
            {
                State = newState;
                StateChanged?.Invoke(this, newState);
            }
        }
        
        private uint GetNextSystemBytes()
        {
            return Interlocked.Increment(ref _systemBytesCounter);
        }
        
        private string GetHcackMessage(byte hcack)
        {
            return hcack switch
            {
                SecsMessageLibrary.ResponseCodes.HCACK_OK => "Command accepted",
                SecsMessageLibrary.ResponseCodes.HCACK_INVALID_COMMAND => "Invalid command",
                SecsMessageLibrary.ResponseCodes.HCACK_CANNOT_PERFORM_NOW => "Cannot perform now",
                SecsMessageLibrary.ResponseCodes.HCACK_PARAMETER_ERROR => "Parameter error",
                SecsMessageLibrary.ResponseCodes.HCACK_INITIATED => "Command initiated",
                SecsMessageLibrary.ResponseCodes.HCACK_REJECTED => "Command rejected",
                SecsMessageLibrary.ResponseCodes.HCACK_INVALID_OBJECT => "Invalid object",
                _ => "Unknown error"
            };
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _connection?.Dispose();
            // TODO: Dispose HSMS session when available
            // _hsmsSession?.Dispose();
            _commandSemaphore?.Dispose();
        }
    }
}