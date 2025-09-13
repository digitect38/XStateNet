using Microsoft.Extensions.Logging;
using System.Net;
using Xunit.Abstractions;
using XStateNet.Semi.Transport;

namespace SemiStandard.Integration.Tests;

/// <summary>
/// Unit tests for HSMS transport layer functionality
/// </summary>
public class HsmsTransportTests : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HsmsTransportTests> _logger;
    private readonly IPEndPoint _testEndpoint;
    
    private HsmsConnection? _passiveConnection;
    private HsmsConnection? _activeConnection;

    public HsmsTransportTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XunitLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = _loggerFactory.CreateLogger<HsmsTransportTests>();
        _testEndpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
    }

    [Fact]
    public async Task Should_EstablishConnection_BetweenActiveAndPassive()
    {
        // Arrange
        await StartPassiveConnectionAsync();
        
        // Act
        await ConnectActiveConnectionAsync();
        
        // Assert
        Assert.True(_passiveConnection!.IsConnected);
        Assert.True(_activeConnection!.IsConnected);
        Assert.Equal(HsmsConnection.HsmsConnectionState.Connected, _passiveConnection.State);
        Assert.Equal(HsmsConnection.HsmsConnectionState.Connected, _activeConnection.State);
        
        _logger.LogInformation("✓ HSMS connection established successfully");
    }

    [Fact]
    public async Task Should_SendControlMessage_SelectReqRsp()
    {
        // Arrange
        await StartPassiveConnectionAsync();
        await ConnectActiveConnectionAsync();
        
        var selectRspReceived = new TaskCompletionSource<HsmsMessage>();
        var selectReqReceived = new TaskCompletionSource<HsmsMessage>();
        
        _activeConnection!.MessageReceived += (sender, msg) =>
        {
            if (msg.MessageType == HsmsMessageType.SelectRsp)
                selectRspReceived.TrySetResult(msg);
        };
        
        _passiveConnection!.MessageReceived += (sender, msg) =>
        {
            if (msg.MessageType == HsmsMessageType.SelectReq)
                selectReqReceived.TrySetResult(msg);
        };

        // Act
        var systemBytes = (uint)Random.Shared.Next(1, int.MaxValue);
        var selectReq = new HsmsMessage
        {
            MessageType = HsmsMessageType.SelectReq,
            SystemBytes = systemBytes
        };
        
        await _activeConnection.SendMessageAsync(selectReq);
        
        // Assert
        var receivedReq = await selectReqReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HsmsMessageType.SelectReq, receivedReq.MessageType);
        Assert.Equal(systemBytes, receivedReq.SystemBytes);
        
        // Send response
        var selectRsp = new HsmsMessage
        {
            MessageType = HsmsMessageType.SelectRsp,
            SystemBytes = systemBytes
        };
        
        await _passiveConnection.SendMessageAsync(selectRsp);
        
        var receivedRsp = await selectRspReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HsmsMessageType.SelectRsp, receivedRsp.MessageType);
        Assert.Equal(systemBytes, receivedRsp.SystemBytes);
        
        _logger.LogInformation("✓ SelectReq/Rsp exchange successful");
    }

    [Fact]
    public async Task Should_SendDataMessage_WithCorrectEncoding()
    {
        // Arrange
        await StartPassiveConnectionAsync();
        await ConnectActiveConnectionAsync();
        
        var messageReceived = new TaskCompletionSource<HsmsMessage>();
        
        _passiveConnection!.MessageReceived += (sender, msg) =>
        {
            if (msg.MessageType == HsmsMessageType.DataMessage)
                messageReceived.TrySetResult(msg);
        };

        // Act
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var systemBytes = (uint)Random.Shared.Next(1, int.MaxValue);
        var dataMessage = new HsmsMessage
        {
            Stream = 1,
            Function = 1,
            MessageType = HsmsMessageType.DataMessage,
            SystemBytes = systemBytes,
            Data = testData
        };
        
        await _activeConnection!.SendMessageAsync(dataMessage);
        
        // Assert
        var received = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HsmsMessageType.DataMessage, received.MessageType);
        Assert.Equal(1, received.Stream);
        Assert.Equal(1, received.Function);
        Assert.Equal(systemBytes, received.SystemBytes);
        Assert.Equal(testData, received.Data);
        
        _logger.LogInformation("✓ Data message with correct encoding sent and received");
    }

    [Fact]
    public async Task Should_HandleLinktestMessage()
    {
        // Arrange
        await StartPassiveConnectionAsync();
        await ConnectActiveConnectionAsync();
        
        var linktestRspReceived = new TaskCompletionSource<HsmsMessage>();
        
        _activeConnection!.MessageReceived += (sender, msg) =>
        {
            if (msg.MessageType == HsmsMessageType.LinktestRsp)
                linktestRspReceived.TrySetResult(msg);
        };

        // Act
        var systemBytes = (uint)Random.Shared.Next(1, int.MaxValue);
        var linktestReq = new HsmsMessage
        {
            MessageType = HsmsMessageType.LinktestReq,
            SystemBytes = systemBytes
        };
        
        await _activeConnection.SendMessageAsync(linktestReq);
        
        // Passive connection should automatically respond with LinktestRsp
        // (This would be handled by the equipment simulator in real scenarios)
        
        // For this test, manually send the response
        var linktestRsp = new HsmsMessage
        {
            MessageType = HsmsMessageType.LinktestRsp,
            SystemBytes = systemBytes
        };
        
        await _passiveConnection!.SendMessageAsync(linktestRsp);
        
        // Assert
        var received = await linktestRspReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HsmsMessageType.LinktestRsp, received.MessageType);
        Assert.Equal(systemBytes, received.SystemBytes);
        
        _logger.LogInformation("✓ Linktest request/response successful");
    }

    [Fact]
    public async Task Should_HandleMultipleSimultaneousMessages()
    {
        // Arrange
        await StartPassiveConnectionAsync();
        await ConnectActiveConnectionAsync();
        
        var receivedMessages = new List<HsmsMessage>();
        var messagesReceived = new TaskCompletionSource<bool>();
        
        _passiveConnection!.MessageReceived += (sender, msg) =>
        {
            lock (receivedMessages)
            {
                receivedMessages.Add(msg);
                if (receivedMessages.Count >= 5)
                    messagesReceived.TrySetResult(true);
            }
        };

        // Act - Send 5 messages rapidly
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            var systemBytes = (uint)(1000 + i);
            var message = new HsmsMessage
            {
                Stream = 1,
                Function = (byte)(i + 1),
                MessageType = HsmsMessageType.DataMessage,
                SystemBytes = systemBytes,
                Data = new byte[] { (byte)i }
            };
            
            tasks.Add(_activeConnection!.SendMessageAsync(message));
        }
        
        await Task.WhenAll(tasks);
        
        // Assert
        await messagesReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        
        Assert.Equal(5, receivedMessages.Count);
        
        // Verify all messages were received correctly
        for (int i = 0; i < 5; i++)
        {
            var expectedSystemBytes = (uint)(1000 + i);
            var message = receivedMessages.FirstOrDefault(m => m.SystemBytes == expectedSystemBytes);
            Assert.NotNull(message);
            Assert.Equal(1, message.Stream);
            Assert.Equal(i + 1, message.Function);
            Assert.Equal(new byte[] { (byte)i }, message.Data);
        }
        
        _logger.LogInformation("✓ Multiple simultaneous messages handled correctly");
    }

    [Fact]
    public async Task Should_HandleConnectionDisconnection()
    {
        // Arrange
        await StartPassiveConnectionAsync();
        await ConnectActiveConnectionAsync();
        
        Assert.True(_activeConnection!.IsConnected);
        Assert.True(_passiveConnection!.IsConnected);

        // Act
        await _activeConnection.DisconnectAsync();
        
        // Wait a moment for the disconnection to propagate
        await Task.Delay(500);
        
        // Assert
        Assert.False(_activeConnection.IsConnected);
        Assert.Equal(HsmsConnection.HsmsConnectionState.NotConnected, _activeConnection.State);
        
        _logger.LogInformation("✓ Connection disconnection handled correctly");
    }

    private async Task StartPassiveConnectionAsync()
    {
        _passiveConnection = new HsmsConnection(
            _testEndpoint,
            HsmsConnection.HsmsConnectionMode.Passive,
            _loggerFactory.CreateLogger<HsmsConnection>());

        var connectTask = _passiveConnection.ConnectAsync();
        
        // Give it a moment to start listening
        await Task.Delay(200);
        
        _logger.LogInformation("Passive connection started listening on {Endpoint}", _testEndpoint);
    }

    private async Task ConnectActiveConnectionAsync()
    {
        _activeConnection = new HsmsConnection(
            _testEndpoint,
            HsmsConnection.HsmsConnectionMode.Active,
            _loggerFactory.CreateLogger<HsmsConnection>());

        await _activeConnection.ConnectAsync();
        
        // Give the passive connection time to accept
        await Task.Delay(200);
        
        _logger.LogInformation("Active connection established to {Endpoint}", _testEndpoint);
    }

    private static int GetAvailablePort()
    {
        var random = new Random();
        return random.Next(5500, 6500);
    }

    public async ValueTask DisposeAsync()
    {
        if (_activeConnection != null)
        {
            await _activeConnection.DisconnectAsync();
            _activeConnection.Dispose();
        }

        if (_passiveConnection != null)
        {
            await _passiveConnection.DisconnectAsync();
            _passiveConnection.Dispose();
        }

        _loggerFactory?.Dispose();
    }
}