using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using Xunit;
using static SemiStandard.Tests.StateMachineTestHelpers;

namespace SemiStandard.Tests;

public class E37HSMSSessionMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E37HSMSSessionManager _sessionMgr;

    public E37HSMSSessionMachineTests()
    {
        var config = new OrchestratorConfig { EnableLogging = true, PoolSize = 4, EnableMetrics = false };
        _orchestrator = new EventBusOrchestrator(config);
        _sessionMgr = new E37HSMSSessionManager("EQ001", _orchestrator);
    }

    public void Dispose() => _orchestrator?.Dispose();

    [Fact]
    public async Task E37_Should_Create_HSMSSession()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        Assert.NotNull(session);
        Assert.Equal("SESSION001", session.SessionId);
        Assert.Equal(HSMSMode.Passive, session.Mode);
        Assert.Contains("NotConnected", session.GetCurrentState());
    }

    [Fact]
    public async Task E37_Passive_Should_Connect_And_WaitForSelect()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        // Passive mode: Connect -> Connected
        var result = await session.ConnectAsync();

        AssertState(result, "Connected");
    }

    [Fact]
    public async Task E37_Active_Should_Connect_And_SendSelect()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Active);

        bool selectSent = false;
        session.OnMessageSend += (s, e) =>
        {
            if (e.MessageType == HSMSMessageType.SelectReq)
                selectSent = true;
        };

        // Active mode: Connect -> NotSelected (sends Select.req)
        var result = await session.ConnectAsync();

        AssertState(result, "NotSelected");
        Assert.True(selectSent);
    }

    [Fact]
    public async Task E37_Passive_Should_AcceptSelectRequest()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        bool selectedNotified = false;
        session.OnSelected += (s, e) => selectedNotified = true;

        // Connect
        await session.ConnectAsync();

        // Receive Select.req
        var result = await session.ReceiveSelectRequestAsync();

        AssertState(result, "Selected");
        Assert.True(selectedNotified);
        Assert.NotNull(session.SelectedTime);
    }

    [Fact]
    public async Task E37_Active_Should_HandleSelectResponse_Accepted()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Active);

        bool selectedNotified = false;
        session.OnSelected += (s, e) => selectedNotified = true;

        // Connect (sends Select.req)
        await session.ConnectAsync();

        // Receive Select.rsp with status 0 (accepted)
        var result = await session.ReceiveSelectResponseAsync(0);

        AssertState(result, "Selected");
        Assert.True(selectedNotified);
        Assert.NotNull(session.SelectedTime);
    }

    [Fact]
    public async Task E37_Active_Should_HandleSelectResponse_Rejected()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Active);

        bool disconnected = false;
        session.OnDisconnect += (s, e) => disconnected = true;

        // Connect
        await session.ConnectAsync();

        // Receive Select.rsp with status 1 (rejected)
        var result = await session.ReceiveSelectResponseAsync(1);

        AssertState(result, "NotConnected");
        Assert.True(disconnected);
    }

    [Fact]
    public async Task E37_Should_HandleDeselect()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        // Get to Selected state
        await session.ConnectAsync();
        await session.ReceiveSelectRequestAsync();

        // Deselect
        var result = await session.ReceiveDeselectAsync();

        AssertState(result, "NotSelected");
        Assert.Null(session.SelectedTime);
    }

    [Fact]
    public async Task E37_Should_HandleSeparateRequest()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        // Get to Connected state
        await session.ConnectAsync();

        // Receive Separate.req
        var result = await session.ReceiveSeparateRequestAsync();

        AssertState(result, "NotConnected");
    }

    [Fact]
    public async Task E37_Should_HandleSeparateRequest_From_Selected()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        // Get to Selected state
        await session.ConnectAsync();
        await session.ReceiveSelectRequestAsync();

        // Receive Separate.req
        var result = await session.ReceiveSeparateRequestAsync();

        AssertState(result, "NotConnected");
        Assert.Null(session.SelectedTime);
    }

    [Fact]
    public async Task E37_Should_HandleLinktestRequest()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        bool linktestRspSent = false;
        session.OnMessageSend += (s, e) =>
        {
            if (e.MessageType == HSMSMessageType.LinktestRsp)
                linktestRspSent = true;
        };

        // Get to Selected state
        await session.ConnectAsync();
        await session.ReceiveSelectRequestAsync();

        // Receive Linktest.req
        var result = await session.ReceiveLinktestRequestAsync();

        AssertState(result, "Selected");
        Assert.True(linktestRspSent);
    }

    [Fact]
    public async Task E37_Should_HandleDataMessage()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        bool dataReceived = false;
        session.OnDataMessage += (s, e) => dataReceived = true;

        // Get to Selected state
        await session.ConnectAsync();
        await session.ReceiveSelectRequestAsync();

        // Receive data message
        var result = await session.ReceiveDataMessageAsync();

        AssertState(result, "Selected");
        Assert.True(dataReceived);
    }

    [Fact]
    public async Task E37_Should_HandleTCPDisconnect_From_Connected()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        // Get to Connected state
        await session.ConnectAsync();

        // TCP disconnect
        var result = await session.DisconnectAsync();

        AssertState(result, "NotConnected");
    }

    [Fact]
    public async Task E37_Should_HandleTCPDisconnect_From_Selected()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        // Get to Selected state
        await session.ConnectAsync();
        await session.ReceiveSelectRequestAsync();

        // TCP disconnect
        var result = await session.DisconnectAsync();

        AssertState(result, "NotConnected");
        Assert.Null(session.SelectedTime);
    }

    [Fact]
    public async Task E37_Should_HandleCommunicationErrors()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        // Get to Selected state
        await session.ConnectAsync();
        await session.ReceiveSelectRequestAsync();

        // Report error (should stay selected)
        var result = await session.ReportCommunicationErrorAsync();

        AssertState(result, "Selected");
    }

    [Fact]
    public async Task E37_Should_Disconnect_After_Max_Errors()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        bool disconnected = false;
        session.OnDisconnect += (s, e) => disconnected = true;

        // Get to Selected state
        var result = await session.ConnectAsync();
        AssertState(result, "Connected");

        result = await session.ReceiveSelectRequestAsync();
        AssertState(result, "Selected");

        // Report 3 errors (should trigger disconnect)
        result = await session.ReportCommunicationErrorAsync();
        AssertState(result, "Selected");

        result = await session.ReportCommunicationErrorAsync();
        AssertState(result, "Selected");

        result = await session.ReportCommunicationErrorAsync();
        AssertState(result, "Selected");

        // Wait for async MAX_ERRORS_REACHED to be processed (Task.Run with 10ms delay in action)
        await Task.Delay(50);

        // Verify disconnected
        var currentState = session.GetCurrentState();
        Assert.Contains("NotConnected", currentState);
        Assert.True(disconnected);
    }

    [Fact]
    public async Task E37_Should_Have_Correct_MachineId()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        Assert.StartsWith("E37_HSMS_SESSION001_", session.MachineId);
        Assert.Equal("E37_HSMS_MGR_EQ001", _sessionMgr.MachineId);
    }

    [Fact]
    public async Task E37_Should_Track_Multiple_Sessions()
    {
        await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);
        await _sessionMgr.CreateSessionAsync("SESSION002", HSMSMode.Active);
        await _sessionMgr.CreateSessionAsync("SESSION003", HSMSMode.Passive);

        var sessions = _sessionMgr.GetAllSessions().ToList();
        Assert.Equal(3, sessions.Count);
    }

    [Fact]
    public async Task E37_Should_Get_Session()
    {
        await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        var session = _sessionMgr.GetSession("SESSION001");
        Assert.NotNull(session);
        Assert.Equal("SESSION001", session.SessionId);
    }

    [Fact]
    public async Task E37_Should_Remove_Session()
    {
        await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        var removed = _sessionMgr.RemoveSession("SESSION001");
        Assert.True(removed);

        var session = _sessionMgr.GetSession("SESSION001");
        Assert.Null(session);
    }

    [Fact]
    public async Task E37_Two_Sessions_Should_Not_Interfere()
    {
        // Test for race condition similar to E42
        var session1 = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);
        var session2 = await _sessionMgr.CreateSessionAsync("SESSION002", HSMSMode.Passive);

        // Connect SESSION001 only
        var result = await session1.ConnectAsync();

        // SESSION001 should be Connected, SESSION002 should still be NotConnected
        AssertState(result, "Connected");
        Assert.Contains("NotConnected", session2.GetCurrentState());
    }

    [Fact]
    public async Task E37_Should_Handle_Enable()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        // Enable (initiates connection attempt)
        var result = await session.EnableAsync();

        // Should still be in NotConnected (waiting for actual TCP connection)
        AssertState(result, "NotConnected");
    }

    [Fact]
    public async Task E37_Should_Have_Correct_Timeout_Values()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive,
            t5: 15000, t6: 6000, t7: 12000, t8: 8000);

        Assert.Equal(15000, session.T5Timeout);
        Assert.Equal(6000, session.T6Timeout);
        Assert.Equal(12000, session.T7Timeout);
        Assert.Equal(8000, session.T8Timeout);
    }

    [Fact]
    public async Task E37_Should_Use_Default_Timeout_Values()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        Assert.Equal(10000, session.T5Timeout);
        Assert.Equal(5000, session.T6Timeout);
        Assert.Equal(10000, session.T7Timeout);
        Assert.Equal(5000, session.T8Timeout);
    }

    [Fact]
    public async Task E37_Should_Clear_SelectedTime_On_Disconnect()
    {
        var session = await _sessionMgr.CreateSessionAsync("SESSION001", HSMSMode.Passive);

        // Get to Selected state
        await session.ConnectAsync();
        await session.ReceiveSelectRequestAsync();

        Assert.NotNull(session.SelectedTime);

        // Disconnect
        await session.DisconnectAsync();

        Assert.Null(session.SelectedTime);
    }
}
