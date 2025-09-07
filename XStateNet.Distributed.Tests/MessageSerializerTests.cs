using FluentAssertions;
using XStateNet.Distributed.Core;
using Xunit;

namespace XStateNet.Distributed.Tests
{
    public class MessageSerializerTests
    {
        private class TestData
        {
            public string Name { get; set; } = "";
            public int Value { get; set; }
            public List<string> Items { get; set; } = new();
        }

        [Fact]
        public void SerializeDeserialize_Json_Should_WorkCorrectly()
        {
            // Arrange
            MessageSerializer.SetDefaultSerializationType(MessageSerializer.SerializationType.Json);
            var data = new TestData
            {
                Name = "Test",
                Value = 42,
                Items = new List<string> { "item1", "item2", "item3" }
            };
            
            // Act
            var serialized = MessageSerializer.Serialize(data);
            var deserialized = MessageSerializer.Deserialize<TestData>(serialized);
            
            // Assert
            deserialized.Should().NotBeNull();
            deserialized!.Name.Should().Be("Test");
            deserialized.Value.Should().Be(42);
            deserialized.Items.Should().BeEquivalentTo(new[] { "item1", "item2", "item3" });
        }

        [Fact]
        public void SerializeDeserialize_MessagePack_Should_WorkCorrectly()
        {
            // Arrange
            MessageSerializer.SetDefaultSerializationType(MessageSerializer.SerializationType.MessagePack);
            var data = new TestData
            {
                Name = "Test",
                Value = 42,
                Items = new List<string> { "item1", "item2", "item3" }
            };
            
            // Act
            var serialized = MessageSerializer.Serialize(data);
            var deserialized = MessageSerializer.Deserialize<TestData>(serialized);
            
            // Assert
            deserialized.Should().NotBeNull();
            deserialized!.Name.Should().Be("Test");
            deserialized.Value.Should().Be(42);
            deserialized.Items.Should().BeEquivalentTo(new[] { "item1", "item2", "item3" });
        }

        [Fact]
        public void SerializeMessage_Should_SerializeStateMachineMessage()
        {
            // Arrange
            var message = new StateMachineMessage
            {
                Id = "msg-123",
                From = "node1",
                To = "node2",
                EventName = "TEST_EVENT",
                Payload = MessageSerializer.Serialize("test payload"),
                Priority = 5,
                Headers = new Dictionary<string, string> { ["key"] = "value" }
            };
            
            // Act
            var serialized = MessageSerializer.SerializeMessage(message);
            var deserialized = MessageSerializer.DeserializeMessage(serialized);
            
            // Assert
            deserialized.Should().NotBeNull();
            deserialized!.Id.Should().Be("msg-123");
            deserialized.From.Should().Be("node1");
            deserialized.To.Should().Be("node2");
            deserialized.EventName.Should().Be("TEST_EVENT");
            deserialized.Priority.Should().Be(5);
            deserialized.Headers.Should().ContainKey("key").And.ContainValue("value");
        }

        [Fact]
        public void Serialize_NullObject_Should_ReturnEmptyArray()
        {
            // Act
            var result = MessageSerializer.Serialize<string>(null);
            
            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void Deserialize_NullOrEmptyData_Should_ReturnDefault()
        {
            // Act
            var result1 = MessageSerializer.Deserialize<TestData>(null);
            var result2 = MessageSerializer.Deserialize<TestData>(Array.Empty<byte>());
            
            // Assert
            result1.Should().BeNull();
            result2.Should().BeNull();
        }

        [Fact]
        public void GetTypeName_Should_ReturnFullTypeName()
        {
            // Act
            var typeName = MessageSerializer.GetTypeName(typeof(TestData));
            
            // Assert
            typeName.Should().Contain("XStateNet.Distributed.Tests.MessageSerializerTests+TestData");
            typeName.Should().Contain("XStateNet.Distributed.Tests");
        }

        [Fact]
        public void GetType_Should_ReturnCorrectType()
        {
            // Arrange
            var typeName = MessageSerializer.GetTypeName(typeof(string));
            
            // Act
            var type = MessageSerializer.GetType(typeName);
            
            // Assert
            type.Should().Be(typeof(string));
        }
    }
}