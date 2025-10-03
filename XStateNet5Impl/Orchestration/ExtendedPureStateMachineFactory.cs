using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XStateNet.Orchestration;

/// <summary>
/// Extended factory for creating PureStateMachines with all XState features:
/// - Actions (orchestrated communication)
/// - Guards (conditional transitions)
/// - Services (long-running async operations)
/// - Activities (continuous background processes)
/// - Delays (dynamic timing)
/// </summary>
public static class ExtendedPureStateMachineFactory
{
    /// <summary>
    /// Create a PureStateMachine with full XState feature support
    /// </summary>
    public static IPureStateMachine CreateFromScriptWithGuardsAndServices(
        string id,
        string json,
        EventBusOrchestrator orchestrator,
        Dictionary<string, Action<OrchestratedContext>>? orchestratedActions = null,
        Dictionary<string, Func<StateMachine, bool>>? guards = null,
        Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>? services = null,
        Dictionary<string, Func<StateMachine, int>>? delays = null,
        Dictionary<string, Func<StateMachine, CancellationToken, Task>>? activities = null)
    {
        var machineContext = orchestrator.GetOrCreateContext(id);

        // Convert orchestrated actions to ActionMap
        var actionMap = new ActionMap();
        if (orchestratedActions != null)
        {
            foreach (var (actionName, action) in orchestratedActions)
            {
                actionMap[actionName] = new List<NamedAction>
                {
                    new NamedAction(actionName, async (sm) =>
                    {
                        action(machineContext);
                        await Task.CompletedTask;
                    })
                };
            }
        }

        // Convert guards to GuardMap
        var guardMap = new GuardMap();
        if (guards != null)
        {
            foreach (var (guardName, guardFunc) in guards)
            {
                guardMap[guardName] = new NamedGuard(guardFunc, guardName);
            }
        }

        // Convert services to ServiceMap
        var serviceMap = new ServiceMap();
        if (services != null)
        {
            foreach (var (serviceName, serviceFunc) in services)
            {
                serviceMap[serviceName] = new NamedService(serviceFunc, serviceName);
            }
        }

        // Convert delays to DelayMap
        var delayMap = new DelayMap();
        if (delays != null)
        {
            foreach (var (delayName, delayFunc) in delays)
            {
                delayMap[delayName] = new NamedDelay(delayFunc, delayName);
            }
        }

        // Convert activities to ActivityMap
        // Note: Activities return Action (cleanup function), not Task
        var activityMap = new ActivityMap();
        if (activities != null)
        {
            foreach (var (activityName, activityFunc) in activities)
            {
                // Wrap the async task in a synchronous function that returns cleanup action
                activityMap[activityName] = new NamedActivity((sm, ct) =>
                {
                    // Start the activity task
                    var task = activityFunc(sm, ct);

                    // Return cleanup action (stop function)
                    return () =>
                    {
                        // Cleanup is handled by cancellation token
                    };
                }, activityName);
            }
        }

        // Create the machine with all features
        // Suppress obsolete warning - this is internal factory usage, external callers should use orchestrated pattern
#pragma warning disable CS0618
        var machine = StateMachineFactory.CreateFromScript(
            jsonScript: json,
            threadSafe: false,
            guidIsolate: true,  // ESSENTIAL: Ensures each machine instance has isolated state
            actionCallbacks: actionMap,
            guardCallbacks: guardMap,
            serviceCallbacks: serviceMap,
            delayCallbacks: delayMap,
            activityCallbacks: activityMap
        );
#pragma warning restore CS0618

        // Register with orchestrator
        orchestrator.RegisterMachineWithContext(id, machine, machineContext);

        // Return pure state machine adapter
        return new PureStateMachineAdapter(id, machine);
    }
}