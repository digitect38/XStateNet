using XStateNet.Orchestration;

namespace XStateNet.Tests;

/// <summary>
/// Base class for orchestrator-based tests with channel group isolation
/// Uses global singleton orchestrator with per-test channel groups
/// </summary>
public abstract class OrchestratorTestBase : IDisposable
{
    protected readonly EventBusOrchestrator _orchestrator;
    protected readonly ChannelGroupToken _channelGroup;
    protected readonly List<IPureStateMachine> _machines = new();

    protected OrchestratorTestBase()
    {
        // Use global singleton orchestrator
        _orchestrator = GlobalOrchestratorManager.Instance.Orchestrator;

        // Create isolated channel group for this test
        _channelGroup = GlobalOrchestratorManager.Instance.CreateChannelGroup(
            $"Test_{GetType().Name}");
    }

    /// <summary>
    /// Create a pure state machine from JSON with orchestrated actions and channel group isolation
    /// </summary>
    protected IPureStateMachine CreateMachine(
        string id,
        string json,
        Dictionary<string, Action<OrchestratedContext>>? actions = null,
        Dictionary<string, Func<StateMachine, bool>>? guards = null,
        Dictionary<string, Func<StateMachine, System.Threading.CancellationToken, System.Threading.Tasks.Task<object>>>? services = null)
    {
        // Use CreateWithChannelGroup for isolation
        var machine = ExtendedPureStateMachineFactory.CreateWithChannelGroup(
            id: id,
            json: json,
            orchestrator: _orchestrator,
            channelGroupToken: _channelGroup,
            orchestratedActions: actions ?? new Dictionary<string, Action<OrchestratedContext>>(),
            guards: guards ?? new Dictionary<string, Func<StateMachine, bool>>(),
            services: services ?? new Dictionary<string, Func<StateMachine, System.Threading.CancellationToken, System.Threading.Tasks.Task<object>>>()
        );

        _machines.Add(machine);
        return machine;
    }

    /// <summary>
    /// Send event through orchestrator
    /// </summary>
    protected async System.Threading.Tasks.Task<EventResult> SendEventAsync(
        string fromId,
        string toMachineId,
        string eventName,
        object? data = null)
    {
        return await _orchestrator.SendEventAsync(fromId, toMachineId, eventName, data);
    }

    /// <summary>
    /// Send event through orchestrator
    /// </summary>
    protected async System.Threading.Tasks.Task<EventResult> SendEventAsync(
        string fromId,
        IPureStateMachine toMachine,
        string eventName,
        object? data = null)
    {
        return await _orchestrator.SendEventAsync(fromId, toMachine.Id, eventName, data);
    }

    /// <summary>
    /// Wait for machine to reach expected state (deterministic polling)
    /// </summary>
    protected async System.Threading.Tasks.Task WaitForStateAsync(
        IPureStateMachine machine,
        string expectedState,
        int timeoutMs = 5000)
    {
        var startTime = DateTime.UtcNow;
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            // Handle GUID isolation: "#machine.state" should match "#machine_guid.state"
            if (StateMatches(machine.CurrentState, expectedState))
            {
                return;
            }
            await System.Threading.Tasks.Task.Delay(10);
        }
        throw new TimeoutException($"State '{expectedState}' not reached within {timeoutMs}ms. Current state: {machine.CurrentState}");
    }

    private bool StateMatches(string currentState, string expectedState)
    {
        // Simple contains check first
        if (currentState.Contains(expectedState))
            return true;

        // Handle GUID isolation: "#machine.state.substate" should match "#machine_guid.state.substate"
        // Expected: "#machine.state.substate"
        // Current:  "#machine_guid.state.substate"

        // The GUID suffix is only in the FIRST part (machine ID), not in state names
        // So we need to compare the state path (everything after the machine ID)

        var expectedParts = expectedState.Split('.');
        var currentParts = currentState.Split('.');

        if (expectedParts.Length < 2 || currentParts.Length < 2)
        {
            // If either doesn't have at least machineId.state, can't match
            return false;
        }

        // Extract the machine ID parts (first element)
        string expectedMachineId = expectedParts[0];  // e.g., "#machine"
        string currentMachineId = currentParts[0];     // e.g., "#machine_guid" or "machine#1#guid"

        // Extract the state paths (everything after machine ID)
        var expectedStatePath = string.Join(".", expectedParts.Skip(1));  // e.g., "idle.active"
        var currentStatePath = string.Join(".", currentParts.Skip(1));    // e.g., "idle.active"

        // State paths must match exactly
        if (expectedStatePath != currentStatePath)
            return false;

        // Machine IDs must match (accounting for GUID suffix or channel group)
        // "#machine" should match "#machine_guid" or "#machine_1_guid"

        // Normalize machine IDs (remove leading #)
        string expectedMachineBase = expectedMachineId.TrimStart('#');
        string currentMachineBase = currentMachineId.TrimStart('#');

        // Check if current machine ID starts with expected base
        // Both GUID isolation and channel groups now use underscore separator:
        // - GUID isolation: "machine_guid"
        // - Channel group:  "machine_groupId_guid"
        if (currentMachineBase == expectedMachineBase ||
            currentMachineBase.StartsWith(expectedMachineBase + "_"))
        {
            return true;
        }

        return false;
    }

    public virtual void Dispose()
    {
        // Release channel group (unregisters all machines in group)
        _channelGroup?.Dispose();
    }
}
