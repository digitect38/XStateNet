using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using Xunit;

namespace SemiStandard.Tests;

/// <summary>
/// Helper methods for testing state machines with deterministic state transitions
/// Uses EventResult.NewState directly - no polling needed!
/// </summary>
public static class StateMachineTestHelpers
{
    /// <summary>
    /// Assert that an EventResult contains the expected state
    /// This is fully deterministic - the state is already in the result
    /// </summary>
    /// <param name="result">The EventResult from SendEventAsync</param>
    /// <param name="expectedState">The expected state substring</param>
    public static void AssertState(EventResult result, string expectedState)
    {
        Assert.True(result.Success, $"Event failed: {result.ErrorMessage}");
        Assert.NotNull(result.NewState);
        Assert.Contains(expectedState, result.NewState, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Legacy helper for checking recipe current state directly
    /// </summary>
    public static Task AssertStateAsync(RecipeMachine recipe, string expectedState)
    {
        var currentState = recipe.GetCurrentState();
        Assert.Contains(expectedState, currentState, StringComparison.OrdinalIgnoreCase);
        return Task.CompletedTask;
    }
}
