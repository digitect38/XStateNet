using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using XStateNet;
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
            if (machine.CurrentState.Contains(expectedState))
            {
                return;
            }
            await System.Threading.Tasks.Task.Delay(10);
        }
        throw new TimeoutException($"State '{expectedState}' not reached within {timeoutMs}ms. Current state: {machine.CurrentState}");
    }

    public virtual void Dispose()
    {
        // Release channel group (unregisters all machines in group)
        _channelGroup?.Dispose();
    }
}
