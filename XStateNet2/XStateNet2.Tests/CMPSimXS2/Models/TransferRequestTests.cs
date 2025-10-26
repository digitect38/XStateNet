using CMPSimXS2.Models;
using FluentAssertions;
using Xunit;

namespace XStateNet2.Tests.CMPSimXS2.Models;

public class TransferRequestTests
{
    [Fact]
    public void Validate_WithValidData_ShouldNotThrow()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher",
            Priority = 1
        };

        // Act
        Action act = () => request.Validate();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithNullFrom_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = null!,
            To = "Polisher"
        };

        // Act
        Action act = () => request.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*'From' cannot be null or empty*");
    }

    [Fact]
    public void Validate_WithEmptyFrom_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "",
            To = "Polisher"
        };

        // Act
        Action act = () => request.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*'From' cannot be null or empty*");
    }

    [Fact]
    public void Validate_WithNullTo_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = null!
        };

        // Act
        Action act = () => request.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*'To' cannot be null or empty*");
    }

    [Fact]
    public void Validate_WithEmptyTo_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = ""
        };

        // Act
        Action act = () => request.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*'To' cannot be null or empty*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_WithInvalidWaferId_ShouldThrowArgumentException(int waferId)
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = waferId,
            From = "Carrier",
            To = "Polisher"
        };

        // Act
        Action act = () => request.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*'WaferId' must be positive*");
    }

    [Fact]
    public void Constructor_ShouldSetDefaultValues()
    {
        // Act
        var request = new TransferRequest();

        // Assert
        request.Priority.Should().Be(0);
        request.RequestedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(1));
        request.PreferredRobotId.Should().BeNull();
        request.OnCompleted.Should().BeNull();
    }

    [Fact]
    public void ToString_ShouldFormatCorrectly()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 5,
            From = "Polisher",
            To = "Cleaner",
            Priority = 2
        };

        // Act
        var result = request.ToString();

        // Assert
        result.Should().Be("TransferRequest(Wafer=5, Polisher→Cleaner, Priority=2)");
    }

    [Fact]
    public void OnCompleted_WhenInvoked_ShouldExecuteCallback()
    {
        // Arrange
        var callbackInvoked = false;
        var receivedWaferId = 0;
        var request = new TransferRequest
        {
            WaferId = 10,
            From = "Carrier",
            To = "Polisher",
            OnCompleted = (id) =>
            {
                callbackInvoked = true;
                receivedWaferId = id;
            }
        };

        // Act
        request.OnCompleted?.Invoke(10);

        // Assert
        callbackInvoked.Should().BeTrue();
        receivedWaferId.Should().Be(10);
    }
}
