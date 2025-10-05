using System.Text.RegularExpressions;

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
    /// <param name="enableGuidIsolation">If true, appends a GUID to the machine ID to ensure uniqueness. Default is true.</param>
    public static IPureStateMachine CreateFromScriptWithGuardsAndServices(
        string id,
        string json,
        EventBusOrchestrator orchestrator,
        Dictionary<string, Action<OrchestratedContext>>? orchestratedActions = null,
        Dictionary<string, Func<StateMachine, bool>>? guards = null,
        Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>? services = null,
        Dictionary<string, Func<StateMachine, int>>? delays = null,
        Dictionary<string, Func<StateMachine, CancellationToken, Task>>? activities = null,
        bool enableGuidIsolation = true)
    {
        return CreateFromScriptWithGuardsAndServicesInternal(
            id, json, orchestrator,
            orchestratedActions, guards, services, delays, activities,
            enableGuidIsolation);
    }

    /// <summary>
    /// Create a PureStateMachine with full XState feature support and channel group isolation
    /// </summary>
    public static IPureStateMachine CreateWithChannelGroup(
        string id,
        string json,
        EventBusOrchestrator orchestrator,
        ChannelGroupToken channelGroupToken,
        Dictionary<string, Action<OrchestratedContext>>? orchestratedActions = null,
        Dictionary<string, Func<StateMachine, bool>>? guards = null,
        Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>? services = null,
        Dictionary<string, Func<StateMachine, int>>? delays = null,
        Dictionary<string, Func<StateMachine, CancellationToken, Task>>? activities = null)
    {
        // Generate scoped machine ID with channel group
        // Channel group already provides isolation, so disable additional GUID isolation
        var machineId = GlobalOrchestratorManager.Instance.CreateScopedMachineId(channelGroupToken, id);

        return CreateFromScriptWithGuardsAndServicesInternal(
            machineId, json, orchestrator,
            orchestratedActions, guards, services, delays, activities, enableGuidIsolation: false);
    }

    /// <summary>
    /// Internal implementation for creating PureStateMachine
    /// </summary>
    private static IPureStateMachine CreateFromScriptWithGuardsAndServicesInternal(
        string id,
        string json,
        EventBusOrchestrator orchestrator,
        Dictionary<string, Action<OrchestratedContext>>? orchestratedActions = null,
        Dictionary<string, Func<StateMachine, bool>>? guards = null,
        Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>? services = null,
        Dictionary<string, Func<StateMachine, int>>? delays = null,
        Dictionary<string, Func<StateMachine, CancellationToken, Task>>? activities = null,
        bool enableGuidIsolation = true)
    {
        // Extract the ID from the JSON to check if it matches the parameter ID
        var jsonIdPattern = @"(['""]?)id\1\s*:\s*(['""])(.+?)\2";
        var jsonIdMatch = Regex.Match(json, jsonIdPattern);

        string jsonId = "";
        if (jsonIdMatch.Success)
        {
            jsonId = jsonIdMatch.Groups[3].Value.TrimStart('#');
        }

        var paramId = id.TrimStart('#');

        // If JSON ID differs from parameter ID, replace it
        // This ensures the parameter ID is used as the base for GUID isolation
        if (!string.IsNullOrEmpty(jsonId) && jsonId != paramId)
        {
            var idForReplacement = id.StartsWith("#") ? id.Substring(1) : id;
            // Replace ALL occurrences including references (preserveReferences: false)
            // This ensures targets like '#machineId.state' become '#test.state'
            json = StateMachineFactory.ReplaceMachineId(json, idForReplacement, preserveReferences: false);

            // Also replace machine ID references in target strings (e.g., '#machineId.stateName')
            // This handles targets in arrays that aren't caught by the id: pattern
            var refPattern = $@"(['""])#{jsonId}\.";
            json = Regex.Replace(json, refPattern, match =>
            {
                var quote = match.Groups[1].Value;
                return $"{quote}#{idForReplacement}.";
            });
        }

        // Predict what the final machine ID will be based on GUID isolation setting
        string predictedMachineId;
        if (enableGuidIsolation)
        {
            // GUID isolation enabled - generate GUID now to create the correct context
            var normalizedId = id.TrimStart('#');
            predictedMachineId = $"{normalizedId}_{Guid.NewGuid():N}";

            // Replace the machine ID in JSON with the GUID-isolated version
            // This ensures the machine is created with the correct ID from the start
            json = StateMachineFactory.ReplaceMachineId(json, predictedMachineId, preserveReferences: false);
        }
        else
        {
            // GUID isolation disabled - use ID as-is
            predictedMachineId = id.TrimStart('#');
        }

        // Create machine context with the predicted final ID
        var machineContext = orchestrator.GetOrCreateContext(predictedMachineId);

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
            // Disable GUID isolation - we've already applied it manually above
            guidIsolate: false,
            actionCallbacks: actionMap,
            guardCallbacks: guardMap,
            serviceCallbacks: serviceMap,
            delayCallbacks: delayMap,
            activityCallbacks: activityMap
        );
#pragma warning restore CS0618

        // Get the machine's actual ID (which should match our predicted ID)
        // Machine IDs internally use # prefix, but we need to normalize for external API
        var actualMachineId = machine.machineId;
        var normalizedMachineId = actualMachineId.StartsWith("#") ? actualMachineId.Substring(1) : actualMachineId;

        // Verify our prediction was correct
        if (normalizedMachineId != predictedMachineId)
        {
            throw new InvalidOperationException(
                $"Machine ID mismatch: predicted '{predictedMachineId}' but got '{normalizedMachineId}'");
        }

        // Register with orchestrator using the normalized machine ID (without # prefix)
        // This ensures that events sent to the machine ID will be routed correctly
        orchestrator.RegisterMachineWithContext(normalizedMachineId, machine, machineContext);

        // Return pure state machine adapter with the normalized machine ID (without # prefix)
        // This ensures IPureStateMachine.Id returns the same format as the input ID
        //
        // NOTE: For timeout protection, applications should use:
        // - TimeoutProtectedPureStateMachineFactory in XStateNet.Distributed (wraps with TimeoutProtectedStateMachine)
        // - This keeps the core XStateNet assembly free of resilience dependencies
        return new PureStateMachineAdapter(normalizedMachineId, machine);
    }
}
