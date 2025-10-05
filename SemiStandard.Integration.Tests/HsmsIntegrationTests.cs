using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.Channels;
using XStateNet.Semi.Secs;
using XStateNet.Semi.Transport;
using Xunit.Abstractions;

namespace SemiStandard.Tests;

/// <summary>
/// Integration tests for HSMS protocol communication between equipment simulator and host
/// </summary>
public class HsmsIntegrationTests : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HsmsIntegrationTests> _logger;
    private readonly IPEndPoint _testEndpoint;

    private NonBlockingEquipmentSimulator? _simulator;
    private HsmsConnection? _hostConnection;
    private TaskCompletionSource<HsmsMessage>? _selectResponse;
    private uint _selectSystemBytes;
    private readonly Channel<bool> _simulatorReady = Channel.CreateUnbounded<bool>();
    private readonly Channel<bool> _connectionReady = Channel.CreateUnbounded<bool>();

    public HsmsIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XunitLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = _loggerFactory.CreateLogger<HsmsIntegrationTests>();
        _testEndpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
    }

    [Fact]
    public async Task Should_EstablishConnection_AndHandleSelection()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await StartSimulatorAsync();
        await _simulatorReady.Reader.ReadAsync(cts.Token);
        await ConnectHostDirectAsync(cts.Token);

        // Act & Assert
        Assert.True(_hostConnection!.IsConnected);

        _logger.LogInformation("✓ Connection and selection successful");
    }

    [Fact]
    public async Task Should_SendAndReceive_S1F1_AreYouThere()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StartSimulatorAsync();
        await _simulatorReady.Reader.ReadAsync(cts.Token);
        await ConnectHostDirectAsync(cts.Token);
        await _connectionReady.Reader.ReadAsync(cts.Token);

        // Act
        var s1f1 = SecsMessageLibrary.S1F1();
        var response = await SendAndReceiveAsync(s1f1, TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(response);
        Assert.Equal(1, response.Stream);
        Assert.Equal(2, response.Function);

        var responseData = response.Data as SecsList;
        Assert.NotNull(responseData);
        Assert.True(responseData.Items.Count >= 2);

        var modelName = (responseData.Items[0] as SecsAscii)?.Value;
        var softwareRev = (responseData.Items[1] as SecsAscii)?.Value;

        Assert.NotNull(modelName);
        Assert.NotNull(softwareRev);

        _logger.LogInformation("✓ S1F1/F2 successful - Model: {Model}, Software: {Software}", modelName, softwareRev);
    }

    [Fact]
    public async Task Should_SendAndReceive_S1F13_EstablishCommunications()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StartSimulatorAsync();
        await ConnectHostDirectAsync(cts.Token);
        await _connectionReady.Reader.ReadAsync(cts.Token);

        // Act
        var s1f13 = SecsMessageLibrary.S1F13();
        var response = await SendAndReceiveAsync(s1f13, TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(response);
        Assert.Equal(1, response.Stream);
        Assert.Equal(14, response.Function);

        var responseData = response.Data as SecsList;
        Assert.NotNull(responseData);
        Assert.True(responseData.Items.Count > 0);

        var commack = (responseData.Items[0] as SecsU1)?.Value;
        Assert.NotNull(commack);
        Assert.Equal(0, commack.Value); // 0 = Accepted

        _logger.LogInformation("✓ S1F13/F14 successful - COMMACK: {COMMACK}", commack);
    }

    [Fact]
    public async Task Should_SendAndReceive_S1F3_StatusVariables()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StartSimulatorAsync();
        await _simulatorReady.Reader.ReadAsync(cts.Token);
        await ConnectHostDirectAsync(cts.Token);
        await _connectionReady.Reader.ReadAsync(cts.Token);

        // Act
        var s1f3 = SecsMessageLibrary.S1F3(1, 2, 3, 4, 5, 6);
        var response = await SendAndReceiveAsync(s1f3, TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(response);
        Assert.Equal(1, response.Stream);
        Assert.Equal(4, response.Function);

        var responseData = response.Data as SecsList;
        Assert.NotNull(responseData);
        Assert.Equal(6, responseData.Items.Count); // Should return 6 status variables

        _logger.LogInformation("✓ S1F3/F4 successful - Received {Count} status variables", responseData.Items.Count);
    }

    [Fact]
    public async Task Should_SendAndReceive_S2F13_EquipmentConstants()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StartSimulatorAsync();
        await _simulatorReady.Reader.ReadAsync(cts.Token);
        await ConnectHostDirectAsync(cts.Token);
        await _connectionReady.Reader.ReadAsync(cts.Token);

        // Act
        var s2f13 = SecsMessageLibrary.S2F13(1, 2, 3);
        var response = await SendAndReceiveAsync(s2f13, TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(response);
        Assert.Equal(2, response.Stream);
        Assert.Equal(14, response.Function);

        var responseData = response.Data as SecsList;
        Assert.NotNull(responseData);
        Assert.Equal(3, responseData.Items.Count); // Should return 3 equipment constants

        _logger.LogInformation("✓ S2F13/F14 successful - Received {Count} equipment constants", responseData.Items.Count);
    }

    [Fact]
    public async Task Should_ReceiveAlarms_S5F1()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StartSimulatorAsync();
        await _simulatorReady.Reader.ReadAsync(cts.Token);
        await ConnectHostDirectAsync(cts.Token);
        await _connectionReady.Reader.ReadAsync(cts.Token);

        var alarmReceived = new TaskCompletionSource<SecsMessage>();

        // Subscribe to received messages
        _hostConnection!.MessageReceived += (sender, hsmsMsg) =>
        {
            if (hsmsMsg.Stream == 5 && hsmsMsg.Function == 1)
            {
                var secsMsg = SecsMessage.Decode(hsmsMsg.Stream, hsmsMsg.Function, hsmsMsg.Data ?? Array.Empty<byte>(), false);
                secsMsg.SystemBytes = hsmsMsg.SystemBytes;
                alarmReceived.TrySetResult(secsMsg);
            }
        };

        // Act
        await _simulator!.TriggerAlarmAsync(1001, "Test Alarm", true);

        // Assert
        var alarm = await alarmReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(alarm);
        Assert.Equal(5, alarm.Stream);
        Assert.Equal(1, alarm.Function);

        _logger.LogInformation("✓ S5F1 alarm received successfully");
    }

    [Fact]
    public async Task Should_ReceiveEvents_S6F11()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StartSimulatorAsync();
        await _simulatorReady.Reader.ReadAsync(cts.Token);
        await ConnectHostDirectAsync(cts.Token);
        await _connectionReady.Reader.ReadAsync(cts.Token);

        var eventReceived = new TaskCompletionSource<SecsMessage>();

        // Subscribe to received messages
        _hostConnection!.MessageReceived += (sender, hsmsMsg) =>
        {
            if (hsmsMsg.Stream == 6 && hsmsMsg.Function == 11)
            {
                var secsMsg = SecsMessage.Decode(hsmsMsg.Stream, hsmsMsg.Function, hsmsMsg.Data ?? Array.Empty<byte>(), false);
                secsMsg.SystemBytes = hsmsMsg.SystemBytes;
                eventReceived.TrySetResult(secsMsg);
            }
        };

        // Act
        var eventData = new List<SecsItem>
        {
            new SecsU4(12345),
            new SecsAscii("Test Event Data")
        };
        await _simulator!.TriggerEventAsync(2001, eventData);

        // Assert
        var eventMsg = await eventReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(eventMsg);
        Assert.Equal(6, eventMsg.Stream);
        Assert.Equal(11, eventMsg.Function);

        _logger.LogInformation("✓ S6F11 event received successfully");
    }

    [Fact]
    public async Task Should_HandleMultipleSequentialMessages()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StartSimulatorAsync();
        await _simulatorReady.Reader.ReadAsync(cts.Token);
        await ConnectHostDirectAsync(cts.Token);
        await _connectionReady.Reader.ReadAsync(cts.Token);

        // Act & Assert - Send multiple messages in sequence
        var s1f1Response = await SendAndReceiveAsync(SecsMessageLibrary.S1F1(), TimeSpan.FromSeconds(5));
        Assert.NotNull(s1f1Response);
        Assert.Equal(2, s1f1Response.Function);

        // No delay needed - messages are sequenced properly through system bytes

        var s1f13Response = await SendAndReceiveAsync(SecsMessageLibrary.S1F13(), TimeSpan.FromSeconds(5));
        Assert.NotNull(s1f13Response);
        Assert.Equal(14, s1f13Response.Function);

        // No delay needed

        var s1f3Response = await SendAndReceiveAsync(SecsMessageLibrary.S1F3(1, 2, 3), TimeSpan.FromSeconds(5));
        Assert.NotNull(s1f3Response);
        Assert.Equal(4, s1f3Response.Function);

        _logger.LogInformation("✓ Multiple sequential messages handled successfully");
    }

    private async Task StartSimulatorAsync()
    {
        _simulator = new NonBlockingEquipmentSimulator(_testEndpoint, _loggerFactory.CreateLogger<NonBlockingEquipmentSimulator>())
        {
            ModelName = "TestEquipment001",
            SoftwareRevision = "2.0.0"
        };

        await _simulator.StartAsync();
        _simulatorReady.Writer.TryWrite(true);
        _logger.LogInformation("Non-blocking simulator started on {Endpoint}", _testEndpoint);
    }

    private async Task ConnectHostDirectAsync(CancellationToken cancellationToken)
    {
        // Use direct HsmsConnection without resilient wrapper
        _hostConnection = new HsmsConnection(_testEndpoint, HsmsConnection.HsmsConnectionMode.Active, null);

        // Subscribe to handle SelectRsp
        _hostConnection.MessageReceived += OnHostMessageReceived;

        await _hostConnection.ConnectAsync(cancellationToken);
        _logger.LogInformation("Host physical connection established");

        // Perform selection
        var selectReq = new HsmsMessage
        {
            MessageType = HsmsMessageType.SelectReq,
            SystemBytes = (uint)Random.Shared.Next(1, 65536)  // Use 16-bit range for SEMI compatibility
        };

        _selectSystemBytes = selectReq.SystemBytes;
        _selectResponse = new TaskCompletionSource<HsmsMessage>();

        await _hostConnection.SendMessageAsync(selectReq, cancellationToken);
        _logger.LogInformation("SelectReq sent with SystemBytes={SystemBytes}", selectReq.SystemBytes);

        // Wait for SelectRsp
        using var selectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        selectCts.CancelAfter(3000);
        var response = await _selectResponse.Task.WaitAsync(selectCts.Token);

        if (response.MessageType != HsmsMessageType.SelectRsp)
        {
            throw new InvalidOperationException($"Selection failed: {response.MessageType}");
        }

        _logger.LogInformation("Host connection selected");
        _connectionReady.Writer.TryWrite(true);
    }

    private void OnHostMessageReceived(object? sender, HsmsMessage message)
    {
        // Handle SelectRsp
        if (_selectResponse != null && message.SystemBytes == _selectSystemBytes)
        {
            if (message.MessageType == HsmsMessageType.SelectRsp ||
                message.MessageType == HsmsMessageType.RejectReq)
            {
                _selectResponse.TrySetResult(message);
            }
        }
    }

    private async Task<SecsMessage?> SendAndReceiveAsync(SecsMessage message, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<SecsMessage>();
        var systemBytes = (uint)Random.Shared.Next(1, int.MaxValue);
        message.SystemBytes = systemBytes;

        void OnDataMessage(object? sender, HsmsMessage hsms)
        {
            if (hsms.SystemBytes == systemBytes && hsms.MessageType == HsmsMessageType.DataMessage)
            {
                var response = SecsMessage.Decode(
                    hsms.Stream,
                    hsms.Function,
                    hsms.Data ?? Array.Empty<byte>(),
                    false);
                response.SystemBytes = hsms.SystemBytes;
                tcs.TrySetResult(response);
            }
        }

        _hostConnection!.MessageReceived += OnDataMessage;

        try
        {
            var hsmsMessage = new HsmsMessage
            {
                Stream = (byte)message.Stream,
                Function = (byte)message.Function,
                MessageType = HsmsMessageType.DataMessage,
                SystemBytes = message.SystemBytes,
                Data = message.Encode()
            };

            await _hostConnection.SendMessageAsync(hsmsMessage);
            _logger.LogDebug("Sent {SxFy} with SystemBytes {SystemBytes}", message.SxFy, systemBytes);

            return await tcs.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout waiting for response to {SxFy}", message.SxFy);
            return null;
        }
        finally
        {
            _hostConnection.MessageReceived -= OnDataMessage;
        }
    }

    private static int GetAvailablePort()
    {
        // Find an actually available port by letting the OS assign one
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_hostConnection != null)
        {
            await _hostConnection.DisconnectAsync();
            _hostConnection.Dispose();
        }

        if (_simulator != null)
        {
            await _simulator.StopAsync();
            _simulator.Dispose();
        }

        _loggerFactory?.Dispose();
    }
}
