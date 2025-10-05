using Microsoft.Extensions.Logging;
using System.Net;
using XStateNet.Semi.Secs;
using XStateNet.Semi.Testing;
using XStateNet.Semi.Transport;
using Xunit.Abstractions;

namespace SemiStandard.Tests;

/// <summary>
/// Integration tests using direct HsmsConnection without resilient wrapper
/// </summary>
public class DirectHsmsIntegrationTests : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DirectHsmsIntegrationTests> _logger;
    private readonly IPEndPoint _testEndpoint;

    private EquipmentSimulator? _simulator;
    private HsmsConnection? _hostConnection;
    private TaskCompletionSource<HsmsMessage>? _selectResponse;
    private uint _selectSystemBytes;
    // TaskCompletionSource for deterministic synchronization

    public DirectHsmsIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XunitLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = _loggerFactory.CreateLogger<DirectHsmsIntegrationTests>();
        _testEndpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
    }

    //[Fact]
    public async Task Should_EstablishConnection_WithDirectHsmsConnection()
    {
        // Arrange
        _logger.LogInformation("Starting direct HSMS connection test");

        // Start simulator
        _simulator = new EquipmentSimulator(_testEndpoint, _loggerFactory.CreateLogger<EquipmentSimulator>())
        {
            ModelName = "TestEquipment001",
            SoftwareRevision = "2.0.0",
            ResponseDelayMs = 50
        };

        // Start the simulator - it will begin listening immediately

        await _simulator.StartAsync();
        _logger.LogInformation("Equipment simulator started on {Endpoint}", _testEndpoint);

        // Wait deterministically for simulator to be ready to accept connections
        await WaitForSimulatorReady();

        // Create direct connection
        _hostConnection = new HsmsConnection(_testEndpoint, HsmsConnection.HsmsConnectionMode.Active, null);

        // Subscribe to events before connecting
        _hostConnection.MessageReceived += OnMessageReceived;
        _hostConnection.ErrorOccurred += (sender, ex) => _logger.LogError(ex, "Connection error");

        // Connect
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _hostConnection.ConnectAsync(cts.Token);
        _logger.LogInformation("Physical connection established");

        // Perform selection
        var selectReq = new HsmsMessage
        {
            MessageType = HsmsMessageType.SelectReq,
            SystemBytes = (uint)Random.Shared.Next(1, 65536)  // Use 16-bit range for SEMI compatibility
        };

        _selectSystemBytes = selectReq.SystemBytes;
        _selectResponse = new TaskCompletionSource<HsmsMessage>();

        await _hostConnection.SendMessageAsync(selectReq, cts.Token);
        _logger.LogInformation("SelectReq sent with SystemBytes={SystemBytes}", selectReq.SystemBytes);

        // Wait for SelectRsp
        using var selectCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var response = await _selectResponse.Task.WaitAsync(selectCts.Token);

        // Assert
        Assert.Equal(HsmsMessageType.SelectRsp, response.MessageType);
        Assert.True(_hostConnection.IsConnected);
        _logger.LogInformation("✓ Direct connection and selection successful");
    }
    // Copilot: Disable this test if you don't have a simulator running that can respond to S1F1 messages
    //[Fact]
    public async Task Should_SendAndReceive_S1F1_WithDirectConnection()
    {
        // Arrange
        await EstablishDirectConnection();

        // Act - Send S1F1
        var s1f1 = SecsMessageLibrary.S1F1();
        var systemBytes = (uint)Random.Shared.Next();
        s1f1.SystemBytes = systemBytes;

        var responseReceived = new TaskCompletionSource<SecsMessage>();

        void OnDataMessage(object? sender, HsmsMessage hsms)
        {
            if (hsms.SystemBytes == systemBytes && hsms.MessageType == HsmsMessageType.DataMessage)
            {
                var secsMsg = SecsMessage.Decode(hsms.Stream, hsms.Function, hsms.Data ?? Array.Empty<byte>(), false);
                secsMsg.SystemBytes = hsms.SystemBytes;
                responseReceived.TrySetResult(secsMsg);
            }
        }

        _hostConnection!.MessageReceived += OnDataMessage;

        try
        {
            // Send message
            var hsmsMessage = new HsmsMessage
            {
                Stream = (byte)s1f1.Stream,
                Function = (byte)s1f1.Function,
                MessageType = HsmsMessageType.DataMessage,
                SystemBytes = systemBytes,
                Data = s1f1.Encode()
            };

            await _hostConnection.SendMessageAsync(hsmsMessage);
            _logger.LogInformation("Sent S1F1 with SystemBytes={SystemBytes}", systemBytes);

            // Wait for response
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await responseReceived.Task.WaitAsync(cts.Token);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(1, response.Stream);
            Assert.Equal(2, response.Function);

            var responseData = response.Data as SecsList;
            Assert.NotNull(responseData);
            Assert.True(responseData.Items.Count >= 2);

            _logger.LogInformation("✓ S1F1/F2 successful with direct connection");
        }
        finally
        {
            _hostConnection.MessageReceived -= OnDataMessage;
        }
    }

    private async Task EstablishDirectConnection()
    {
        // Start simulator
        _simulator = new EquipmentSimulator(_testEndpoint, _loggerFactory.CreateLogger<EquipmentSimulator>())
        {
            ModelName = "TestEquipment001",
            SoftwareRevision = "2.0.0",
            ResponseDelayMs = 50
        };

        // Start the simulator - it will begin listening immediately

        await _simulator.StartAsync();

        // Wait deterministically for simulator to be ready
        await WaitForSimulatorReady();

        // Create and connect
        _hostConnection = new HsmsConnection(_testEndpoint, HsmsConnection.HsmsConnectionMode.Active, null);
        _hostConnection.MessageReceived += OnMessageReceived;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _hostConnection.ConnectAsync(cts.Token);

        // Select
        var selectReq = new HsmsMessage
        {
            MessageType = HsmsMessageType.SelectReq,
            SystemBytes = (uint)Random.Shared.Next(1, 65536)  // Use 16-bit range for SEMI compatibility
        };

        _selectSystemBytes = selectReq.SystemBytes;
        _selectResponse = new TaskCompletionSource<HsmsMessage>();

        await _hostConnection.SendMessageAsync(selectReq, cts.Token);

        using var selectCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var response = await _selectResponse.Task.WaitAsync(selectCts.Token);

        if (response.MessageType != HsmsMessageType.SelectRsp)
        {
            throw new InvalidOperationException($"Selection failed: {response.MessageType}");
        }

        _logger.LogInformation("Direct connection established and selected");
    }

    private void OnMessageReceived(object? sender, HsmsMessage message)
    {
        _logger.LogDebug("Received message: Type={Type}, SystemBytes={SystemBytes}",
            message.MessageType, message.SystemBytes);

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

    private static int GetAvailablePort()
    {
        // Find an actually available port by letting the OS assign one
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Waits deterministically for simulator to be ready to accept connections
    /// </summary>
    private async Task WaitForSimulatorReady()
    {
        // Poll until simulator is listening
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                using var testClient = new System.Net.Sockets.TcpClient();
                await testClient.ConnectAsync(_testEndpoint.Address, _testEndpoint.Port, cts.Token);
                testClient.Close();
                _logger.LogDebug("Simulator is accepting connections");
                return;
            }
            catch
            {
                await Task.Delay(50, cts.Token);
            }
        }

        throw new TimeoutException("Simulator did not become ready within timeout");
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