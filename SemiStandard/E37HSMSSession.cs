using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using SemiStandard;
using Timer = System.Timers.Timer;

namespace SemiStandard.E37
{
    /// <summary>
    /// SEMI E37 High Speed SECS Message Services (HSMS)
    /// TCP/IP 기반 SECS 통신의 연결 상태 관리
    /// </summary>
    public class E37HSMSSession
    {
        private readonly StateMachineAdapter _stateMachine;
        private readonly string _sessionId;
        private readonly Timer _t5Timer; // Connect Separation Timeout
        private readonly Timer _t6Timer; // Control Transaction Timeout  
        private readonly Timer _t7Timer; // Not Selected Timeout
        private readonly Timer _t8Timer; // Network Intercharacter Timeout
        
        public string SessionId => _sessionId;
        public HSMSState CurrentState { get; private set; }
        public bool IsSelected => CurrentState == HSMSState.Selected;
        
        public enum HSMSState
        {
            NotConnected,
            Connected,
            NotSelected,
            Selected
        }
        
        public enum HSMSMode
        {
            Passive,  // Equipment (accepts connection)
            Active    // Host (initiates connection)
        }
        
        private readonly HSMSMode _mode;
        private readonly int _t5Timeout;
        private readonly int _t6Timeout;
        private readonly int _t7Timeout;
        private readonly int _t8Timeout;

        public E37HSMSSession(string sessionId, HSMSMode mode = HSMSMode.Passive,
            int t5 = 10000, int t6 = 5000, int t7 = 10000, int t8 = 5000)
        {
            _sessionId = sessionId;
            _mode = mode;
            _t5Timeout = t5;
            _t6Timeout = t6;
            _t7Timeout = t7;
            _t8Timeout = t8;
            
            _t5Timer = new Timer(_t5Timeout);
            _t6Timer = new Timer(_t6Timeout);
            _t7Timer = new Timer(_t7Timeout);
            _t8Timer = new Timer(_t8Timeout);
            
            // Load configuration from JSON file
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "SemiStandard.XStateScripts.E37HSMSSession.json";
            string config;
            
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    // Fallback to file system if embedded resource not found
                    var configPath = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? "", "XStateScripts", "E37HSMSSession.json");
                    config = File.ReadAllText(configPath);
                }
                else
                {
                    using (var reader = new StreamReader(stream))
                    {
                        config = reader.ReadToEnd();
                    }
                }
            }

            _stateMachine = StateMachineFactory.Create(config);
            
            // Setup timers
            _t5Timer.Elapsed += async (s, e) => await _stateMachine.SendAsync("T5_TIMEOUT");
            _t6Timer.Elapsed += async (s, e) => await _stateMachine.SendAsync("T6_TIMEOUT");
            _t7Timer.Elapsed += async (s, e) => await _stateMachine.SendAsync("T7_TIMEOUT");
            _t8Timer.Elapsed += async (s, e) => await _stateMachine.SendAsync("T8_TIMEOUT");
            
            // Register conditions
            _stateMachine.RegisterCondition("isPassiveMode", (ctx, evt) => _mode == HSMSMode.Passive);
            _stateMachine.RegisterCondition("isActiveMode", (ctx, evt) => _mode == HSMSMode.Active);
            _stateMachine.RegisterCondition("isSelectAccepted", (ctx, evt) => 
            {
                return evt.Data is SelectResponse resp && resp.Status == 0;
            });
            _stateMachine.RegisterCondition("isSelectRejected", (ctx, evt) =>
            {
                return evt.Data is SelectResponse resp && resp.Status != 0;
            });
            
            // Register actions
            _stateMachine.RegisterAction("stopAllTimers", (ctx, evt) =>
            {
                _t5Timer.Stop();
                _t6Timer.Stop();
                _t7Timer.Stop();
                _t8Timer.Stop();
            });
            
            _stateMachine.RegisterAction("startT6Timer", (ctx, evt) =>
            {
                _t6Timer.Start();
            });
            
            _stateMachine.RegisterAction("startT7Timer", (ctx, evt) =>
            {
                _t7Timer.Start();
            });
            
            _stateMachine.RegisterAction("stopT6Timer", (ctx, evt) =>
            {
                _t6Timer.Stop();
            });
            
            _stateMachine.RegisterAction("stopT7Timer", (ctx, evt) =>
            {
                _t7Timer.Stop();
            });
            
            _stateMachine.RegisterAction("sendSelectReq", (ctx, evt) =>
            {
                Console.WriteLine($"[E37] Session {_sessionId}: Sending Select.req");
                OnMessageSend?.Invoke(this, new HSMSMessage { Type = MessageType.SelectReq });
            });
            
            _stateMachine.RegisterAction("sendSelectRsp", (ctx, evt) =>
            {
                Console.WriteLine($"[E37] Session {_sessionId}: Sending Select.rsp (Accept)");
                OnMessageSend?.Invoke(this, new HSMSMessage { Type = MessageType.SelectRsp, Status = 0 });
            });
            
            _stateMachine.RegisterAction("sendDeselectRsp", (ctx, evt) =>
            {
                Console.WriteLine($"[E37] Session {_sessionId}: Sending Deselect.rsp");
                OnMessageSend?.Invoke(this, new HSMSMessage { Type = MessageType.DeselectRsp });
            });
            
            _stateMachine.RegisterAction("sendSeparateRsp", (ctx, evt) =>
            {
                Console.WriteLine($"[E37] Session {_sessionId}: Sending Separate.rsp");
                OnMessageSend?.Invoke(this, new HSMSMessage { Type = MessageType.SeparateRsp });
            });
            
            _stateMachine.RegisterAction("sendLinktestRsp", (ctx, evt) =>
            {
                OnMessageSend?.Invoke(this, new HSMSMessage { Type = MessageType.LinktestRsp });
            });
            
            _stateMachine.RegisterAction("startLinktest", (ctx, evt) =>
            {
                Console.WriteLine($"[E37] Session {_sessionId}: Starting periodic linktest");
                // Start periodic linktest timer
            });
            
            _stateMachine.RegisterAction("notifySelected", (ctx, evt) =>
            {
                Console.WriteLine($"[E37] Session {_sessionId}: SELECTED - Communication established");
                OnSelected?.Invoke(this, EventArgs.Empty);
            });
            
            _stateMachine.RegisterAction("disconnect", (ctx, evt) =>
            {
                Console.WriteLine($"[E37] Session {_sessionId}: Disconnecting");
                OnDisconnect?.Invoke(this, EventArgs.Empty);
            });
            
            _stateMachine.RegisterAction("processDataMessage", (ctx, evt) =>
            {
                if (evt.Data is HSMSMessage msg)
                {
                    OnDataMessage?.Invoke(this, msg);
                }
            });
            
            _stateMachine.RegisterAction("recordSelectedEntity", (ctx, evt) =>
            {
                ctx["selectedEntity"] = DateTime.UtcNow.ToString();
            });
            
            _stateMachine.RegisterAction("clearSelectedEntity", (ctx, evt) =>
            {
                ctx["selectedEntity"] = null;
            });
            
            _stateMachine.RegisterAction("incrementErrorCount", async (ctx, evt) =>
            {
                var errorCount = (int)(ctx["errorCount"] ?? 0);
                errorCount++;
                ctx["errorCount"] = errorCount;

                if (errorCount >= 3)
                {
                    await _stateMachine.SendAsync("MAX_ERRORS_REACHED");
                }
            });
            
            _stateMachine.Start();
            UpdateState();
        }
        
        private void UpdateState()
        {
            var state = _stateMachine.CurrentStates.FirstOrDefault()?.Name;
            if (string.IsNullOrEmpty(state) || state == "Unknown")
            {
                CurrentState = HSMSState.NotConnected;
            }
            else
            {
                // Handle state names that include machine ID prefix (e.g., "#E37_HSMS_Session.NotConnected")
                if (state.Contains('.'))
                {
                    state = state.Split('.').Last();
                }
                // Also handle state names that start with # and contain the actual state after underscore
                if (state.Contains('_'))
                {
                    var parts = state.Split('_');
                    var lastPart = parts.Last();
                    if (Enum.TryParse<HSMSState>(lastPart, out var parsedState))
                    {
                        CurrentState = parsedState;
                        return;
                    }
                }

                if (Enum.TryParse<HSMSState>(state, out var hsmsState))
                {
                    CurrentState = hsmsState;
                }
                else
                {
                    CurrentState = HSMSState.NotConnected;
                }
            }
        }
        
        // Connection Management
        public async Task ConnectAsync()
        {
            await _stateMachine.SendAsync("TCP_CONNECT");
            UpdateState();
        }

        public void Connect()
        {
            ConnectAsync().GetAwaiter().GetResult();
        }

        public async Task DisconnectAsync()
        {
            await _stateMachine.SendAsync("TCP_DISCONNECT");
            UpdateState();
        }

        public void Disconnect()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }

        public async Task EnableAsync()
        {
            await _stateMachine.SendAsync("ENABLE");
            UpdateState();
        }

        public void Enable()
        {
            EnableAsync().GetAwaiter().GetResult();
        }
        
        // Message Handling
        public async Task ReceiveSelectRequestAsync()
        {
            await _stateMachine.SendAsync("SELECT_REQ");
            UpdateState();
        }

        public void ReceiveSelectRequest()
        {
            ReceiveSelectRequestAsync().GetAwaiter().GetResult();
        }

        public async Task ReceiveSelectResponseAsync(int status)
        {
            await _stateMachine.SendAsync(new StateMachineEvent
            {
                Name = "SELECT_RSP",
                Data = new SelectResponse { Status = status }
            });
            UpdateState();
        }

        public void ReceiveSelectResponse(int status)
        {
            ReceiveSelectResponseAsync(status).GetAwaiter().GetResult();
        }

        public async Task ReceiveDeselectRequestAsync()
        {
            await _stateMachine.SendAsync("DESELECT");
            UpdateState();
        }

        public void ReceiveDeselectRequest()
        {
            ReceiveDeselectRequestAsync().GetAwaiter().GetResult();
        }

        public async Task ReceiveSeparateRequestAsync()
        {
            await _stateMachine.SendAsync("SEPARATE_REQ");
            UpdateState();
        }

        public void ReceiveSeparateRequest()
        {
            ReceiveSeparateRequestAsync().GetAwaiter().GetResult();
        }

        public async Task ReceiveLinktestRequestAsync()
        {
            await _stateMachine.SendAsync("LINKTEST_REQ");
            UpdateState();
        }

        public void ReceiveLinktestRequest()
        {
            ReceiveLinktestRequestAsync().GetAwaiter().GetResult();
        }

        public async Task ReceiveDataMessageAsync(HSMSMessage message)
        {
            await _stateMachine.SendAsync(new StateMachineEvent
            {
                Name = "DATA_MESSAGE",
                Data = message
            });
            UpdateState();
        }

        public void ReceiveDataMessage(HSMSMessage message)
        {
            ReceiveDataMessageAsync(message).GetAwaiter().GetResult();
        }

        public async Task ReportCommunicationErrorAsync()
        {
            await _stateMachine.SendAsync("COMMUNICATION_ERROR");
            UpdateState();
        }

        public void ReportCommunicationError()
        {
            ReportCommunicationErrorAsync().GetAwaiter().GetResult();
        }
        
        // Events
        public event EventHandler<HSMSMessage>? OnMessageSend;
        public event EventHandler? OnSelected;
        public event EventHandler? OnDisconnect;
        public event EventHandler<HSMSMessage>? OnDataMessage;
        
        public class SelectResponse
        {
            public int Status { get; set; } // 0 = Accept, 1 = Already Active, 2 = Not Ready, etc.
        }
        
        public class HSMSMessage
        {
            public MessageType Type { get; set; }
            public int Stream { get; set; }
            public int Function { get; set; }
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public int Status { get; set; }
        }
        
        public enum MessageType
        {
            DataMessage,
            SelectReq,
            SelectRsp,
            DeselectReq,
            DeselectRsp,
            LinktestReq,
            LinktestRsp,
            RejectReq,
            SeparateReq,
            SeparateRsp
        }
        
        public void Dispose()
        {
            _t5Timer?.Dispose();
            _t6Timer?.Dispose();
            _t7Timer?.Dispose();
            _t8Timer?.Dispose();
        }
    }
}
