using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using HsmsConnection = XStateNet.Semi.Transport.HsmsConnection;
using HsmsMessage = XStateNet.Semi.Transport.HsmsMessage;
using HsmsMessageType = XStateNet.Semi.Transport.HsmsMessageType;
using SecsAscii = XStateNet.Semi.Secs.SecsAscii;
using SecsBinary = XStateNet.Semi.Secs.SecsBinary;
using SecsBoolean = XStateNet.Semi.Secs.SecsBoolean;
using SecsF4 = XStateNet.Semi.Secs.SecsF4;
using SecsF8 = XStateNet.Semi.Secs.SecsF8;
using SecsI1 = XStateNet.Semi.Secs.SecsI1;
using SecsI2 = XStateNet.Semi.Secs.SecsI2;
using SecsI4 = XStateNet.Semi.Secs.SecsI4;
using SecsI8 = XStateNet.Semi.Secs.SecsI8;
using SecsItem = XStateNet.Semi.Secs.SecsItem;
using SecsList = XStateNet.Semi.Secs.SecsList;
using SecsMessage = XStateNet.Semi.Secs.SecsMessage;
using SecsMessageLibrary = XStateNet.Semi.Secs.SecsMessageLibrary;
using SecsU1 = XStateNet.Semi.Secs.SecsU1;
using SecsU2 = XStateNet.Semi.Secs.SecsU2;
using SecsU4 = XStateNet.Semi.Secs.SecsU4;
using SecsU4Array = XStateNet.Semi.Secs.SecsU4Array;
using SecsU8 = XStateNet.Semi.Secs.SecsU8;

namespace XStateNet.Semi.Testing
{
    /// <summary>
    /// Equipment simulator for SEMI compliance testing
    /// </summary>
    public class EquipmentSimulator : IDisposable
    {
        private HsmsConnection? _connection;
        private readonly IPEndPoint _endpoint;
        private readonly ILogger<EquipmentSimulator>? _logger;
        private readonly ConcurrentDictionary<string, Func<SecsMessage, Task<SecsMessage>>> _messageHandlers = new();
        private readonly ConcurrentDictionary<uint, object> _statusVariables = new();
        private readonly ConcurrentDictionary<uint, object> _equipmentConstants = new();
        private readonly ConcurrentDictionary<uint, AlarmInfo> _alarms = new();
        private readonly ConcurrentDictionary<uint, EventInfo> _events = new();
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _messageProcessingTask;
        private bool _disposed;

        // Equipment configuration
        public string ModelName { get; set; } = "XStateNet Simulator";
        public string SoftwareRevision { get; set; } = "1.0.0";
        public EquipmentStateEnum EquipmentState { get; set; } = EquipmentStateEnum.Idle;
        public CommunicationStateEnum CommunicationState { get; set; } = CommunicationStateEnum.NotCommunicating;
        public ControlStateEnum ControlState { get; set; } = ControlStateEnum.EquipmentOffline;

        // Simulation parameters
        public int ResponseDelayMs { get; set; } = 10;
        public double ErrorRate { get; set; } = 0.0; // 0-1, probability of simulated error
        public bool EnableLogging { get; set; } = true;

        public event EventHandler<SecsMessage>? MessageReceived;
        public event EventHandler<SecsMessage>? MessageSent;
        public event EventHandler<string>? StateChanged;

        public enum EquipmentStateEnum
        {
            Idle,
            Setup,
            Ready,
            Executing,
            Pause,
            Error
        }

        public enum CommunicationStateEnum
        {
            Disabled,
            NotCommunicating,
            Communicating
        }

        public enum ControlStateEnum
        {
            EquipmentOffline,
            AttemptOnline,
            HostOffline,
            Local,
            Remote
        }

        public EquipmentSimulator(IPEndPoint endpoint, ILogger<EquipmentSimulator>? logger = null)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _logger = logger;

            InitializeDefaultHandlers();
            InitializeDefaultVariables();
        }

        /// <summary>
        /// Start the equipment simulator
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(EquipmentSimulator));

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Create passive connection (equipment typically acts as server)
            // Don't create a separate logger factory - it causes issues with test runners
            _connection = new HsmsConnection(_endpoint, HsmsConnection.HsmsConnectionMode.Passive, null);
            _connection.MessageReceived += OnMessageReceived;

            await _connection.ConnectAsync(_cancellationTokenSource.Token);

            CommunicationState = CommunicationStateEnum.NotCommunicating;
            _logger?.LogInformation("Equipment simulator started on {Endpoint}", _endpoint);

            // Start message processing
            _messageProcessingTask = ProcessMessagesAsync(_cancellationTokenSource.Token);
        }

        /// <summary>
        /// Stop the equipment simulator
        /// </summary>
        public async Task StopAsync()
        {
            _cancellationTokenSource?.Cancel();

            if (_messageProcessingTask != null)
                await _messageProcessingTask;

            if (_connection != null)
                await _connection.DisconnectAsync();

            CommunicationState = CommunicationStateEnum.Disabled;
            _logger?.LogInformation("Equipment simulator stopped");
        }

        /// <summary>
        /// Register a custom message handler
        /// </summary>
        public void RegisterHandler(string sxfy, Func<SecsMessage, Task<SecsMessage>> handler)
        {
            _messageHandlers[sxfy] = handler;
        }

        /// <summary>
        /// Set a status variable value
        /// </summary>
        public void SetStatusVariable(uint svid, object value)
        {
            _statusVariables[svid] = value;
        }

        /// <summary>
        /// Set an equipment constant value
        /// </summary>
        public void SetEquipmentConstant(uint ecid, object value)
        {
            _equipmentConstants[ecid] = value;
        }

        /// <summary>
        /// Trigger an alarm
        /// </summary>
        public async Task TriggerAlarmAsync(uint alid, string text, bool set = true)
        {
            if (!_alarms.ContainsKey(alid))
            {
                _alarms[alid] = new AlarmInfo { Id = alid, Text = text };
            }

            _alarms[alid].IsSet = set;

            // Send S5F1 alarm report
            var alarmMessage = SecsMessageLibrary.S5F1(
                (byte)(set ? 128 : 0),
                alid,
                text);

            await SendMessageAsync(alarmMessage);
        }

        /// <summary>
        /// Trigger an event
        /// </summary>
        public async Task TriggerEventAsync(uint ceid, List<SecsItem>? reports = null)
        {
            // Send S6F11 event report
            var eventMessage = SecsMessageLibrary.S6F11(
                ceid,
                reports ?? new List<SecsItem>());

            await SendMessageAsync(eventMessage);
        }

        private void InitializeDefaultHandlers()
        {
            // S1F1 - Are You There Request
            RegisterHandler("S1F1", async msg =>
            {
                await SimulateDelay();
                return SecsMessageLibrary.S1F2(ModelName, SoftwareRevision);
            });

            // S1F3 - Selected Equipment Status Request
            RegisterHandler("S1F3", async msg =>
            {
                await SimulateDelay();
                var values = new List<SecsItem>();

                if (msg.Data is SecsU4Array svids)
                {
                    foreach (var svid in svids.Values)
                    {
                        if (_statusVariables.TryGetValue(svid, out var value))
                        {
                            values.Add(ConvertToSecsItem(value));
                        }
                        else
                        {
                            values.Add(new SecsList()); // Empty list for undefined
                        }
                    }
                }

                return SecsMessageLibrary.S1F4(values);
            });

            // S1F13 - Establish Communications Request
            RegisterHandler("S1F13", async msg =>
            {
                await SimulateDelay();
                CommunicationState = CommunicationStateEnum.Communicating;
                return SecsMessageLibrary.S1F14(0, ModelName, SoftwareRevision);
            });

            // S2F13 - Equipment Constant Request
            RegisterHandler("S2F13", async msg =>
            {
                await SimulateDelay();
                var values = new List<SecsItem>();

                if (msg.Data is SecsU4Array ecids)
                {
                    foreach (var ecid in ecids.Values)
                    {
                        if (_equipmentConstants.TryGetValue(ecid, out var value))
                        {
                            values.Add(ConvertToSecsItem(value));
                        }
                        else
                        {
                            values.Add(new SecsList()); // Empty list for undefined
                        }
                    }
                }

                return SecsMessageLibrary.S2F14(values);
            });

            // S2F15 - New Equipment Constant Send
            RegisterHandler("S2F15", async msg =>
            {
                await SimulateDelay();

                if (msg.Data is SecsList list)
                {
                    foreach (var item in list.Items)
                    {
                        if (item is SecsList pair && pair.Items.Count == 2)
                        {
                            if (pair.Items[0] is SecsU4 ecid)
                            {
                                _equipmentConstants[ecid.Value] = pair.Items[1];
                            }
                        }
                    }
                }

                return SecsMessageLibrary.S2F16(0); // EAC_ACCEPTED
            });

            // S2F41 - Host Command Send
            RegisterHandler("S2F41", async msg =>
            {
                await SimulateDelay();

                // Simulate command processing
                if (Random.Shared.NextDouble() < ErrorRate)
                {
                    return SecsMessageLibrary.S2F42(
                        SecsMessageLibrary.ResponseCodes.HCACK_CANNOT_PERFORM_NOW);
                }

                return SecsMessageLibrary.S2F42(
                    SecsMessageLibrary.ResponseCodes.HCACK_OK);
            });
        }

        private void InitializeDefaultVariables()
        {
            // Common status variables
            SetStatusVariable(1, (byte)EquipmentState);
            SetStatusVariable(2, DateTime.Now.ToString("yyyyMMddHHmmss"));
            SetStatusVariable(3, (byte)CommunicationState);
            SetStatusVariable(4, (byte)ControlState);
            SetStatusVariable(5, ModelName);
            SetStatusVariable(6, SoftwareRevision);

            // Common equipment constants
            SetEquipmentConstant(1, 300); // Timeout value
            SetEquipmentConstant(2, 10);  // Max retry count
            SetEquipmentConstant(3, "Equipment001"); // Equipment ID
        }

        private async void OnMessageReceived(object? sender, HsmsMessage hsmsMessage)
        {
            try
            {
                // Debug log the raw HSMS message
                _logger?.LogDebug("Raw HSMS received - Stream: {Stream}, Function: {Function}, Type: {Type}, SystemBytes: {SystemBytes}",
                    hsmsMessage.Stream, hsmsMessage.Function, hsmsMessage.MessageType, hsmsMessage.SystemBytes);

                // Handle HSMS control messages
                if (hsmsMessage.MessageType != HsmsMessageType.DataMessage)
                {
                    _logger?.LogDebug("Starting control message handling...");
                    await HandleControlMessage(hsmsMessage);
                    _logger?.LogDebug("Finished control message handling");
                    return;
                }

                // Convert HSMS data message to SECS message
                var secsMessage = SecsMessage.Decode(
                    hsmsMessage.Stream,
                    hsmsMessage.Function,
                    hsmsMessage.Data ?? Array.Empty<byte>(),
                    true);

                secsMessage.SystemBytes = hsmsMessage.SystemBytes;

                _logger?.LogDebug("Decoded SECS message: {SxFy}", secsMessage.SxFy);

                MessageReceived?.Invoke(this, secsMessage);

                if (EnableLogging)
                {
                    _logger?.LogInformation("Received: {Message}", secsMessage.SxFy);
                }

                // Process message
                if (_messageHandlers.TryGetValue(secsMessage.SxFy, out var handler))
                {
                    var response = await handler(secsMessage);
                    if (response != null && secsMessage.ReplyExpected)
                    {
                        response.SystemBytes = secsMessage.SystemBytes;
                        await SendMessageAsync(response);
                    }
                }
                else
                {
                    _logger?.LogWarning("No handler for {Message}", secsMessage.SxFy);

                    // Send S9F5 (Unrecognized Stream Type) or S9F7 (Illegal Data)
                    if (secsMessage.ReplyExpected)
                    {
                        var errorResponse = new SecsMessage(9, 5, false)
                        {
                            SystemBytes = secsMessage.SystemBytes
                        };
                        await SendMessageAsync(errorResponse);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing message");
            }
        }

        private async Task SendMessageAsync(SecsMessage message)
        {
            if (_connection == null || !_connection.IsConnected)
                return;

            var hsmsMessage = new HsmsMessage
            {
                Stream = (byte)message.Stream,
                Function = (byte)message.Function,
                MessageType = HsmsMessageType.DataMessage,
                SystemBytes = message.SystemBytes,
                Data = message.Encode()
            };

            await _connection.SendMessageAsync(hsmsMessage);
            MessageSent?.Invoke(this, message);

            if (EnableLogging)
            {
                _logger?.LogInformation("Sent: {Message}", message.SxFy);
            }
        }

        private async Task HandleControlMessage(HsmsMessage hsmsMessage)
        {
            _logger?.LogDebug("Handling control message: {Type}", hsmsMessage.MessageType);

            switch (hsmsMessage.MessageType)
            {
                case HsmsMessageType.SelectReq:
                    // Respond with SelectRsp
                    var selectRsp = new HsmsMessage
                    {
                        MessageType = HsmsMessageType.SelectRsp,
                        SystemBytes = hsmsMessage.SystemBytes
                    };
                    await _connection!.SendMessageAsync(selectRsp);
                    _logger?.LogInformation("Sent SelectRsp");
                    break;

                case HsmsMessageType.LinktestReq:
                    // Respond with LinktestRsp
                    var linktestRsp = new HsmsMessage
                    {
                        MessageType = HsmsMessageType.LinktestRsp,
                        SystemBytes = hsmsMessage.SystemBytes
                    };
                    await _connection!.SendMessageAsync(linktestRsp);
                    _logger?.LogDebug("Sent LinktestRsp");
                    break;

                case HsmsMessageType.DeselectReq:
                    // Respond with DeselectRsp
                    var deselectRsp = new HsmsMessage
                    {
                        MessageType = HsmsMessageType.DeselectRsp,
                        SystemBytes = hsmsMessage.SystemBytes
                    };
                    await _connection!.SendMessageAsync(deselectRsp);
                    _logger?.LogInformation("Sent DeselectRsp");
                    break;

                default:
                    _logger?.LogWarning("Unhandled control message type: {Type}", hsmsMessage.MessageType);
                    break;
            }
        }

        private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(100, cancellationToken);
                    // Additional processing if needed
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in message processing loop");
                }
            }
        }

        private async Task SimulateDelay()
        {
            if (ResponseDelayMs > 0)
            {
                await Task.Delay(ResponseDelayMs);
            }
        }

        private SecsItem ConvertToSecsItem(object value)
        {
            return value switch
            {
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
                string str => new SecsAscii(str),
                byte[] bytes => new SecsBinary(bytes),
                bool b => new SecsBoolean(new[] { b }),
                _ => new SecsList()
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
        }

        private class AlarmInfo
        {
            public uint Id { get; set; }
            public string Text { get; set; } = "";
            public bool IsSet { get; set; }
        }

        private class EventInfo
        {
            public uint Id { get; set; }
            public string Name { get; set; } = "";
            public bool Enabled { get; set; }
        }
    }
}