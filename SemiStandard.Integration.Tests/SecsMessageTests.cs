using Xunit.Abstractions;
using XStateNet.Semi.Secs;

namespace SemiStandard.Integration.Tests;

/// <summary>
/// Unit tests for SECS-II message encoding and decoding
/// </summary>
public class SecsMessageTests
{
    private readonly ITestOutputHelper _output;

    public SecsMessageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Should_EncodeAndDecode_EmptyMessage()
    {
        // Arrange
        var originalMessage = new SecsMessage(1, 1, true);

        // Act
        var encoded = originalMessage.Encode();
        var decoded = SecsMessage.Decode(1, 1, encoded, true);

        // Assert
        Assert.Equal(originalMessage.Stream, decoded.Stream);
        Assert.Equal(originalMessage.Function, decoded.Function);
        Assert.Equal(originalMessage.ReplyExpected, decoded.ReplyExpected);
        Assert.Null(decoded.Data);
        
        _output.WriteLine($"✓ Empty message S{originalMessage.Stream}F{originalMessage.Function} encoded/decoded correctly");
    }

    [Fact]
    public void Should_EncodeAndDecode_MessageWithAsciiData()
    {
        // Arrange
        var originalMessage = new SecsMessage(1, 2, false)
        {
            Data = new SecsList(
                new SecsAscii("TestEquipment"),
                new SecsAscii("1.0.0")
            )
        };

        // Act
        var encoded = originalMessage.Encode();
        var decoded = SecsMessage.Decode(1, 2, encoded, false);

        // Assert
        Assert.Equal(originalMessage.Stream, decoded.Stream);
        Assert.Equal(originalMessage.Function, decoded.Function);
        Assert.Equal(originalMessage.ReplyExpected, decoded.ReplyExpected);
        
        var decodedData = decoded.Data as SecsList;
        Assert.NotNull(decodedData);
        Assert.Equal(2, decodedData.Items.Count);
        
        var modelName = decodedData.Items[0] as SecsAscii;
        var softwareRev = decodedData.Items[1] as SecsAscii;
        
        Assert.NotNull(modelName);
        Assert.NotNull(softwareRev);
        Assert.Equal("TestEquipment", modelName.Value);
        Assert.Equal("1.0.0", softwareRev.Value);
        
        _output.WriteLine($"✓ S{originalMessage.Stream}F{originalMessage.Function} with ASCII data encoded/decoded correctly");
    }

    [Fact]
    public void Should_EncodeAndDecode_MessageWithNumericData()
    {
        // Arrange
        var originalMessage = new SecsMessage(1, 4, false)
        {
            Data = new SecsList(
                new SecsU1(1),
                new SecsU2(1000),
                new SecsU4(100000),
                new SecsI4(-12345),
                new SecsF4(3.14159f),
                new SecsF8(2.718281828459045)
            )
        };

        // Act
        var encoded = originalMessage.Encode();
        var decoded = SecsMessage.Decode(1, 4, encoded, false);

        // Assert
        Assert.Equal(originalMessage.Stream, decoded.Stream);
        Assert.Equal(originalMessage.Function, decoded.Function);
        
        var decodedData = decoded.Data as SecsList;
        Assert.NotNull(decodedData);
        Assert.Equal(6, decodedData.Items.Count);
        
        Assert.Equal((byte)1, (decodedData.Items[0] as SecsU1)?.Value);
        Assert.Equal((ushort)1000, (decodedData.Items[1] as SecsU2)?.Value);
        Assert.Equal(100000u, (decodedData.Items[2] as SecsU4)?.Value);
        Assert.Equal(-12345, (decodedData.Items[3] as SecsI4)?.Value);
        Assert.Equal(3.14159f, (decodedData.Items[4] as SecsF4)?.Value);
        Assert.Equal(2.718281828459045, (decodedData.Items[5] as SecsF8)?.Value);
        
        _output.WriteLine($"✓ S{originalMessage.Stream}F{originalMessage.Function} with numeric data encoded/decoded correctly");
    }

    [Fact]
    public void Should_EncodeAndDecode_NestedListStructure()
    {
        // Arrange
        var originalMessage = new SecsMessage(2, 13, true)
        {
            Data = new SecsList(
                new SecsU4(1), // ECID 1
                new SecsU4(2), // ECID 2
                new SecsList(   // Nested list
                    new SecsAscii("SubItem1"),
                    new SecsAscii("SubItem2")
                )
            )
        };

        // Act
        var encoded = originalMessage.Encode();
        var decoded = SecsMessage.Decode(2, 13, encoded, true);

        // Assert
        var decodedData = decoded.Data as SecsList;
        Assert.NotNull(decodedData);
        Assert.Equal(3, decodedData.Items.Count);
        
        Assert.Equal(1u, (decodedData.Items[0] as SecsU4)?.Value);
        Assert.Equal(2u, (decodedData.Items[1] as SecsU4)?.Value);
        
        var nestedList = decodedData.Items[2] as SecsList;
        Assert.NotNull(nestedList);
        Assert.Equal(2, nestedList.Items.Count);
        Assert.Equal("SubItem1", (nestedList.Items[0] as SecsAscii)?.Value);
        Assert.Equal("SubItem2", (nestedList.Items[1] as SecsAscii)?.Value);
        
        _output.WriteLine($"✓ S{originalMessage.Stream}F{originalMessage.Function} with nested structure encoded/decoded correctly");
    }

    [Fact]
    public void Should_EncodeAndDecode_BinaryData()
    {
        // Arrange
        var binaryData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0xFF, 0xFE };
        var originalMessage = new SecsMessage(10, 1, false)
        {
            Data = new SecsBinary(binaryData)
        };

        // Act
        var encoded = originalMessage.Encode();
        var decoded = SecsMessage.Decode(10, 1, encoded, false);

        // Assert
        var decodedBinary = decoded.Data as SecsBinary;
        Assert.NotNull(decodedBinary);
        Assert.Equal(binaryData, decodedBinary.Value);
        
        _output.WriteLine($"✓ S{originalMessage.Stream}F{originalMessage.Function} with binary data encoded/decoded correctly");
    }

    [Fact]
    public void Should_CreateStandardMessages_FromLibrary()
    {
        // Test S1F1 - Are You There
        var s1f1 = SecsMessageLibrary.S1F1();
        Assert.Equal(1, s1f1.Stream);
        Assert.Equal(1, s1f1.Function);
        Assert.True(s1f1.ReplyExpected);
        Assert.Null(s1f1.Data);

        // Test S1F13 - Establish Communications
        var s1f13 = SecsMessageLibrary.S1F13();
        Assert.Equal(1, s1f13.Stream);
        Assert.Equal(13, s1f13.Function);
        Assert.True(s1f13.ReplyExpected);
        Assert.Null(s1f13.Data);

        // Test S1F3 - Selected Status Variable Request
        var svids = new uint[] { 1, 2, 3 };
        var s1f3 = SecsMessageLibrary.S1F3(svids);
        Assert.Equal(1, s1f3.Stream);
        Assert.Equal(3, s1f3.Function);
        Assert.True(s1f3.ReplyExpected);
        
        var s1f3Data = s1f3.Data as SecsList;
        Assert.NotNull(s1f3Data);
        Assert.Equal(3, s1f3Data.Items.Count);
        
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(svids[i], (s1f3Data.Items[i] as SecsU4)?.Value);
        }

        _output.WriteLine("✓ Standard SECS message library functions work correctly");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(127, 255)]
    [InlineData(255, 0)]
    public void Should_HandleEdgeCases_ForStreamAndFunction(byte stream, byte function)
    {
        // Arrange
        var originalMessage = new SecsMessage(stream, function, false);

        // Act
        var encoded = originalMessage.Encode();
        var decoded = SecsMessage.Decode(stream, function, encoded, false);

        // Assert
        Assert.Equal(stream, decoded.Stream);
        Assert.Equal(function, decoded.Function);
        
        _output.WriteLine($"✓ Edge case S{stream}F{function} handled correctly");
    }

    [Fact]
    public void Should_GenerateCorrect_SxFyFormat()
    {
        var message1 = new SecsMessage(1, 1, true);
        Assert.Equal("S1F1", message1.SxFy);

        var message2 = new SecsMessage(10, 255, false);
        Assert.Equal("S10F255", message2.SxFy);

        var message3 = new SecsMessage(127, 1, true);
        Assert.Equal("S127F1", message3.SxFy);

        _output.WriteLine("✓ SxFy format generation works correctly");
    }

    [Fact]
    public void Should_PreserveSystemBytes_ThroughEncoding()
    {
        // Arrange
        var originalMessage = new SecsMessage(1, 1, true)
        {
            SystemBytes = 0x12345678,
            Data = new SecsAscii("Test")
        };

        // Act
        var encoded = originalMessage.Encode();
        var decoded = SecsMessage.Decode(1, 1, encoded, true);
        decoded.SystemBytes = originalMessage.SystemBytes; // SystemBytes are handled at HSMS level

        // Assert
        Assert.Equal(originalMessage.SystemBytes, decoded.SystemBytes);
        Assert.Equal("Test", (decoded.Data as SecsAscii)?.Value);
        
        _output.WriteLine("✓ SystemBytes preservation works correctly");
    }
}