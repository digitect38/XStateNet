using FluentAssertions;
using XStateNet.Distributed.Core;
using XStateNet.Distributed.Transports;
using Xunit;

namespace XStateNet.Distributed.Tests
{
    public class InMemoryTransportTests
    {
        [Fact]
        public async Task Connect_Should_EstablishConnection()
        {
            // Arrange
            using var transport = new InMemoryTransport();
            
            // Act
            await transport.ConnectAsync("local://test-node");
            
            // Assert
            transport.IsConnected.Should().BeTrue();
            transport.Type.Should().Be(TransportType.InMemory);
        }

        [Fact]
        public async Task SendAndReceive_Should_TransferMessage()
        {
            // Arrange
            using var sender = new InMemoryTransport();
            using var receiver = new InMemoryTransport();
            
            await sender.ConnectAsync("local://sender");
            await receiver.ConnectAsync("local://receiver");
            
            var message = new StateMachineMessage
            {
                From = "local://sender",
                To = "local://receiver",
                EventName = "TEST_EVENT",
                Payload = MessageSerializer.Serialize("test payload")
            };
            
            // Act
            var sendResult = await sender.SendAsync(message);
            var receivedMessage = await receiver.ReceiveAsync(TimeSpan.FromSeconds(1));
            
            // Assert
            sendResult.Should().BeTrue();
            receivedMessage.Should().NotBeNull();
            receivedMessage!.EventName.Should().Be("TEST_EVENT");
            receivedMessage.From.Should().Be("local://sender");
        }

        [Fact]
        public async Task Subscribe_Should_ReceiveMatchingMessages()
        {
            // Arrange
            using var transport1 = new InMemoryTransport();
            using var transport2 = new InMemoryTransport();
            
            await transport1.ConnectAsync("local://node1");
            await transport2.ConnectAsync("local://node2");
            
            var messages = new List<StateMachineMessage>();
            var subscriptionTask = Task.Run(async () =>
            {
                await foreach (var msg in transport2.SubscribeAsync("TEST_*"))
                {
                    messages.Add(msg);
                    if (messages.Count >= 2) break;
                }
            });
            
            // Act
            await Task.Delay(100); // Give subscription time to start
            
            await transport1.SendAsync(new StateMachineMessage
            {
                From = "local://node1",
                To = "local://node2",
                EventName = "TEST_EVENT1"
            });
            
            await transport1.SendAsync(new StateMachineMessage
            {
                From = "local://node1",
                To = "local://node2",
                EventName = "TEST_EVENT2"
            });
            
            await transport1.SendAsync(new StateMachineMessage
            {
                From = "local://node1",
                To = "local://node2",
                EventName = "OTHER_EVENT"
            });
            
            await Task.WhenAny(subscriptionTask, Task.Delay(2000));
            
            // Assert
            messages.Should().HaveCount(2);
            messages.Should().AllSatisfy(m => m.EventName.Should().StartWith("TEST_"));
        }

        [Fact]
        public async Task RequestResponse_Should_Work()
        {
            // Arrange
            using var client = new InMemoryTransport();
            using var server = new InMemoryTransport();
            
            await client.ConnectAsync("local://client");
            await server.ConnectAsync("local://server");
            
            // Setup server to respond to requests
            _ = Task.Run(async () =>
            {
                await foreach (var msg in server.SubscribeAsync("*"))
                {
                    if (msg.ReplyTo != null && msg.CorrelationId != null)
                    {
                        // Send response
                        await server.SendAsync(new StateMachineMessage
                        {
                            From = "local://server",
                            To = msg.ReplyTo,
                            EventName = "Response",
                            Payload = MessageSerializer.Serialize("response data"),
                            CorrelationId = msg.CorrelationId
                        });
                    }
                }
            });
            
            // Act
            var response = await client.RequestAsync<string, string>(
                "local://server",
                "request data",
                TimeSpan.FromSeconds(2));
            
            // Assert
            response.Should().Be("response data");
        }

        [Fact]
        public async Task Discover_Should_FindRegisteredEndpoints()
        {
            // Arrange
            using var transport1 = new InMemoryTransport();
            using var transport2 = new InMemoryTransport();
            
            await transport1.ConnectAsync("local://node1");
            await transport2.ConnectAsync("local://node2");
            
            await transport1.RegisterAsync(new StateMachineEndpoint
            {
                Id = "node1",
                Address = "local://node1",
                Metadata = new Dictionary<string, string> { ["type"] = "test" }
            });
            
            await transport2.RegisterAsync(new StateMachineEndpoint
            {
                Id = "node2",
                Address = "local://node2",
                Metadata = new Dictionary<string, string> { ["type"] = "test" }
            });
            
            // Act
            var endpoints = await transport1.DiscoverAsync("*");
            
            // Assert
            endpoints.Should().HaveCount(2);
            endpoints.Should().Contain(e => e.Id == "node1");
            endpoints.Should().Contain(e => e.Id == "node2");
            endpoints.Should().AllSatisfy(e => e.Location.Should().Be(MachineLocation.SameProcess));
        }

        [Fact]
        public async Task GetHealth_Should_ReturnHealthStatus()
        {
            // Arrange
            using var transport = new InMemoryTransport();
            await transport.ConnectAsync("local://test");
            
            // Act
            var health = await transport.GetHealthAsync();
            
            // Assert
            health.Should().NotBeNull();
            health.IsHealthy.Should().BeTrue();
            health.Latency.Should().Be(TimeSpan.Zero);
            health.Diagnostics.Should().ContainKey("ActiveChannels");
        }

        [Fact]
        public async Task Disconnect_Should_CloseConnection()
        {
            // Arrange
            using var transport = new InMemoryTransport();
            await transport.ConnectAsync("local://test");
            
            // Act
            await transport.DisconnectAsync();
            
            // Assert
            transport.IsConnected.Should().BeFalse();
        }
    }
}