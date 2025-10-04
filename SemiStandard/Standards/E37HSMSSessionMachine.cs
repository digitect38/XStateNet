using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// E37 HSMS Session Machine - SEMI E37 Standard
/// Manages High-Speed SECS Message Services (HSMS) TCP/IP connection lifecycle
/// Refactored to use ExtendedPureStateMachineFactory with EventBusOrchestrator
/// </summary>
public class E37HSMSSessionManager
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ConcurrentDictionary<string, HSMSSessionMachine> _sessions = new();

    public string MachineId => $"E37_HSMS_MGR_{_equipmentId}";

    public E37HSMSSessionManager(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Create and register an HSMS session
    /// </summary>
    public async Task<HSMSSessionMachine> CreateSessionAsync(string sessionId, HSMSMode mode = HSMSMode.Passive,
        int t5 = 10000, int t6 = 5000, int t7 = 10000, int t8 = 5000)
    {
        if (_sessions.ContainsKey(sessionId))
        {
            return _sessions[sessionId];
        }

        var session = new HSMSSessionMachine(sessionId, mode, _equipmentId, _orchestrator, t5, t6, t7, t8);
        _sessions[sessionId] = session;

        await session.StartAsync();

        return session;
    }

    /// <summary>
    /// Get session
    /// </summary>
    public HSMSSessionMachine? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    /// <summary>
    /// Get all sessions
    /// </summary>
    public IEnumerable<HSMSSessionMachine> GetAllSessions()
    {
        return _sessions.Values;
    }

    /// <summary>
    /// Remove session
    /// </summary>
    public bool RemoveSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            session.Dispose();
            return true;
        }
        return false;
    }
}

/// <summary>
/// HSMS Mode - Active (Host) or Passive (Equipment)
/// </summary>
public enum HSMSMode
{
    Passive,  // Equipment (accepts connection)
    Active    // Host (initiates connection)
}

/// <summary>
/// HSMS Message Types
/// </summary>
public enum HSMSMessageType
{
    SelectReq,
    SelectRsp,
    DeselectReq,
    DeselectRsp,
    LinktestReq,
    LinktestRsp,
    SeparateReq,
    SeparateRsp,
    DataMessage
}

/// <summary>
/// Individual HSMS session state machine using orchestrator
/// </summary>
public class HSMSSessionMachine : IDisposable
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly HSMSMode _mode;
    private readonly string _instanceId;

    // HSMS Timers
    private readonly Timer? _t5Timer; // Connect Separation Timeout
    private readonly Timer? _t6Timer; // Control Transaction Timeout
    private readonly Timer? _t7Timer; // Not Selected Timeout
    private readonly Timer? _t8Timer; // Network Intercharacter Timeout

    private int _errorCount;
    private bool _disposed;

    public string SessionId { get; }
    public HSMSMode Mode => _mode;
    public int T5Timeout { get; }
    public int T6Timeout { get; }
    public int T7Timeout { get; }
    public int T8Timeout { get; }
    public DateTime? SelectedTime { get; set; }
    public ConcurrentDictionary<string, object> Properties { get; }

    public string MachineId => $"E37_HSMS_{SessionId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public event EventHandler<HSMSMessageEventArgs>? OnMessageSend;
    public event EventHandler<HSMSMessageEventArgs>? OnDataMessage;
    public event EventHandler? OnSelected;
    public event EventHandler? OnDisconnect;

    public HSMSSessionMachine(string sessionId, HSMSMode mode, string equipmentId, EventBusOrchestrator orchestrator,
        int t5 = 10000, int t6 = 5000, int t7 = 10000, int t8 = 5000)
    {
        SessionId = sessionId;
        _mode = mode;
        T5Timeout = t5;
        T6Timeout = t6;
        T7Timeout = t7;
        T8Timeout = t8;
        Properties = new ConcurrentDictionary<string, object>();
        _orchestrator = orchestrator;
        _errorCount = 0;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Inline XState JSON definition with unique ID per session
        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'NotConnected',
            context: {
                sessionId: '{{sessionId}}',
                mode: '{{mode}}',
                selectedEntity: null,
                errorCount: 0
            },
            states: {
                NotConnected: {
                    entry: 'logNotConnected',
                    on: {
                        TCP_CONNECT: [
                            {
                                target: 'Connected',
                                cond: 'isPassiveMode',
                                actions: 'startT7Timer'
                            },
                            {
                                target: 'NotSelected',
                                cond: 'isActiveMode',
                                actions: ['sendSelectReq', 'startT6Timer']
                            }
                        ],
                        ENABLE: {
                            target: 'NotConnected',
                            actions: 'attemptConnection'
                        }
                    }
                },
                Connected: {
                    entry: 'logConnected',
                    on: {
                        SELECT_REQ: {
                            target: 'Selected',
                            actions: ['sendSelectRsp', 'stopT7Timer', 'recordSelectedEntity']
                        },
                        T7_TIMEOUT: {
                            target: 'NotConnected',
                            actions: 'disconnect'
                        },
                        TCP_DISCONNECT: 'NotConnected',
                        SEPARATE_REQ: {
                            target: 'NotConnected',
                            actions: 'sendSeparateRsp'
                        }
                    }
                },
                NotSelected: {
                    entry: 'logNotSelected',
                    on: {
                        SELECT_RSP: [
                            {
                                target: 'Selected',
                                cond: 'isSelectAccepted',
                                actions: ['stopT6Timer', 'recordSelectedEntity']
                            },
                            {
                                target: 'NotConnected',
                                cond: 'isSelectRejected',
                                actions: ['stopT6Timer', 'disconnect']
                            }
                        ],
                        T6_TIMEOUT: {
                            target: 'NotConnected',
                            actions: 'disconnect'
                        },
                        TCP_DISCONNECT: 'NotConnected'
                    }
                },
                Selected: {
                    entry: ['startLinktest', 'notifySelected'],
                    on: {
                        DESELECT: {
                            target: 'NotSelected',
                            actions: ['sendDeselectRsp', 'clearSelectedEntity']
                        },
                        SEPARATE_REQ: {
                            target: 'NotConnected',
                            actions: ['sendSeparateRsp', 'clearSelectedEntity']
                        },
                        LINKTEST_REQ: {
                            target: 'Selected',
                            actions: 'sendLinktestRsp'
                        },
                        DATA_MESSAGE: {
                            target: 'Selected',
                            actions: 'processDataMessage'
                        },
                        TCP_DISCONNECT: {
                            target: 'NotConnected',
                            actions: 'clearSelectedEntity'
                        },
                        COMMUNICATION_ERROR: {
                            target: 'Selected',
                            actions: 'incrementErrorCount'
                        },
                        MAX_ERRORS_REACHED: {
                            target: 'NotConnected',
                            actions: ['disconnect', 'clearSelectedEntity']
                        }
                    }
                }
            }
        }
        """;

        // Orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logNotConnected"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⭕ Not Connected");
                StopAllTimers();
            },

            ["logConnected"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔌 Connected (Passive mode, waiting for Select.req)");
            },

            ["logNotSelected"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📡 Not Selected (Active mode, sent Select.req)");
            },

            ["attemptConnection"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Attempting connection...");

                ctx.RequestSend("TCP_TRANSPORT", "CONNECT_REQUEST", new JObject
                {
                    ["sessionId"] = SessionId
                });
            },

            ["startT6Timer"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏱️ Starting T6 timer ({T6Timeout}ms)");
                _t6Timer?.Change(T6Timeout, Timeout.Infinite);
            },

            ["startT7Timer"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏱️ Starting T7 timer ({T7Timeout}ms)");
                _t7Timer?.Change(T7Timeout, Timeout.Infinite);
            },

            ["stopT6Timer"] = (ctx) =>
            {
                _t6Timer?.Change(Timeout.Infinite, Timeout.Infinite);
            },

            ["stopT7Timer"] = (ctx) =>
            {
                _t7Timer?.Change(Timeout.Infinite, Timeout.Infinite);
            },

            ["sendSelectReq"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📤 Sending Select.req");
                OnMessageSend?.Invoke(this, new HSMSMessageEventArgs(HSMSMessageType.SelectReq, 0));

                ctx.RequestSend("HSMS_TRANSPORT", "SEND_SELECT_REQ", new JObject
                {
                    ["sessionId"] = SessionId
                });
            },

            ["sendSelectRsp"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📤 Sending Select.rsp (Accept)");
                OnMessageSend?.Invoke(this, new HSMSMessageEventArgs(HSMSMessageType.SelectRsp, 0));

                ctx.RequestSend("HSMS_TRANSPORT", "SEND_SELECT_RSP", new JObject
                {
                    ["sessionId"] = SessionId,
                    ["status"] = 0
                });
            },

            ["sendDeselectRsp"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📤 Sending Deselect.rsp");
                OnMessageSend?.Invoke(this, new HSMSMessageEventArgs(HSMSMessageType.DeselectRsp, 0));

                ctx.RequestSend("HSMS_TRANSPORT", "SEND_DESELECT_RSP", new JObject
                {
                    ["sessionId"] = SessionId
                });
            },

            ["sendSeparateRsp"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📤 Sending Separate.rsp");
                OnMessageSend?.Invoke(this, new HSMSMessageEventArgs(HSMSMessageType.SeparateRsp, 0));

                ctx.RequestSend("HSMS_TRANSPORT", "SEND_SEPARATE_RSP", new JObject
                {
                    ["sessionId"] = SessionId
                });
            },

            ["sendLinktestRsp"] = (ctx) =>
            {
                OnMessageSend?.Invoke(this, new HSMSMessageEventArgs(HSMSMessageType.LinktestRsp, 0));
            },

            ["startLinktest"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 💓 Starting periodic linktest");
                // Periodic linktest would be handled by external timer
            },

            ["notifySelected"] = (ctx) =>
            {
                SelectedTime = DateTime.UtcNow;
                Console.WriteLine($"[{MachineId}] ✅ SELECTED - Communication established at {SelectedTime}");
                OnSelected?.Invoke(this, EventArgs.Empty);

                ctx.RequestSend("E30_GEM", "HSMS_SELECTED", new JObject
                {
                    ["sessionId"] = SessionId
                });
            },

            ["disconnect"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔌 Disconnecting");
                OnDisconnect?.Invoke(this, EventArgs.Empty);
                StopAllTimers();

                ctx.RequestSend("TCP_TRANSPORT", "DISCONNECT", new JObject
                {
                    ["sessionId"] = SessionId
                });
            },

            ["processDataMessage"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📨 Processing data message");
                OnDataMessage?.Invoke(this, new HSMSMessageEventArgs(HSMSMessageType.DataMessage, 0));

                ctx.RequestSend("E30_GEM", "DATA_MESSAGE_RECEIVED", new JObject
                {
                    ["sessionId"] = SessionId
                });
            },

            ["recordSelectedEntity"] = (ctx) =>
            {
                Properties["selectedEntity"] = DateTime.UtcNow.ToString();
            },

            ["clearSelectedEntity"] = (ctx) =>
            {
                Properties["selectedEntity"] = null;
                SelectedTime = null;
            },

            ["incrementErrorCount"] = (ctx) =>
            {
                _errorCount++;
                Console.WriteLine($"[{MachineId}] ⚠️ Communication error #{_errorCount}");

                if (_errorCount >= 3)
                {
                    Console.WriteLine($"[{MachineId}] ❌ Max errors reached, disconnecting");
                    Task.Run(async () =>
                    {
                        await Task.Delay(10); // Small delay to ensure action completes
                        await MaxErrorsReachedAsync();
                    });
                }
            }
        };

        // Guards
        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["isPassiveMode"] = (sm) => _mode == HSMSMode.Passive,
            ["isActiveMode"] = (sm) => _mode == HSMSMode.Active,
            ["isSelectAccepted"] = (sm) =>
            {
                // Check if last event data indicates acceptance
                return Properties.TryGetValue("lastSelectStatus", out var status) && (int)status == 0;
            },
            ["isSelectRejected"] = (sm) =>
            {
                // Check if last event data indicates rejection
                return Properties.TryGetValue("lastSelectStatus", out var status) && (int)status != 0;
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: guards,
            enableGuidIsolation: false  // Already has GUID suffix in MachineId
        );

        // Initialize timers
        _t5Timer = new Timer(_ => _ = T5TimeoutAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _t6Timer = new Timer(_ => _ = T6TimeoutAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _t7Timer = new Timer(_ => _ = T7TimeoutAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _t8Timer = new Timer(_ => _ = T8TimeoutAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public string GetCurrentState()
    {
        return _machine.CurrentState;
    }

    private void StopAllTimers()
    {
        _t5Timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _t6Timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _t7Timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _t8Timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    // Timer callbacks
    private async Task T5TimeoutAsync()
    {
        await _orchestrator.SendEventAsync("SYSTEM", MachineId, "T5_TIMEOUT", null);
    }

    private async Task T6TimeoutAsync()
    {
        await _orchestrator.SendEventAsync("SYSTEM", MachineId, "T6_TIMEOUT", null);
    }

    private async Task T7TimeoutAsync()
    {
        await _orchestrator.SendEventAsync("SYSTEM", MachineId, "T7_TIMEOUT", null);
    }

    private async Task T8TimeoutAsync()
    {
        await _orchestrator.SendEventAsync("SYSTEM", MachineId, "T8_TIMEOUT", null);
    }

    // Public API methods
    public async Task<EventResult> ConnectAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "TCP_CONNECT", null);
        return result;
    }

    public async Task<EventResult> DisconnectAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "TCP_DISCONNECT", null);
        return result;
    }

    public async Task<EventResult> EnableAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ENABLE", null);
        return result;
    }

    public async Task<EventResult> ReceiveSelectRequestAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SELECT_REQ", null);
        return result;
    }

    public async Task<EventResult> ReceiveSelectResponseAsync(int status)
    {
        Properties["lastSelectStatus"] = status;
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SELECT_RSP", null);
        return result;
    }

    public async Task<EventResult> ReceiveDeselectAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DESELECT", null);
        return result;
    }

    public async Task<EventResult> ReceiveSeparateRequestAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SEPARATE_REQ", null);
        return result;
    }

    public async Task<EventResult> ReceiveLinktestRequestAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "LINKTEST_REQ", null);
        return result;
    }

    public async Task<EventResult> ReceiveDataMessageAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DATA_MESSAGE", null);
        return result;
    }

    public async Task<EventResult> ReportCommunicationErrorAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "COMMUNICATION_ERROR", null);
        return result;
    }

    private async Task<bool> MaxErrorsReachedAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "MAX_ERRORS_REACHED", null);
        return result.Success;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAllTimers();
        _t5Timer?.Dispose();
        _t6Timer?.Dispose();
        _t7Timer?.Dispose();
        _t8Timer?.Dispose();
    }
}

/// <summary>
/// HSMS Message Event Args
/// </summary>
public class HSMSMessageEventArgs : EventArgs
{
    public HSMSMessageType MessageType { get; }
    public int Status { get; }

    public HSMSMessageEventArgs(HSMSMessageType messageType, int status)
    {
        MessageType = messageType;
        Status = status;
    }
}
