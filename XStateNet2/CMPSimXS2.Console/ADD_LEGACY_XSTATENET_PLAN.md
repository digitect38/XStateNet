# 📋 Plan: Add Legacy XStateNet (V1) to Benchmark

## Goal

Add legacy XStateNet (XStateNet V5 implementation) to stress test for comparison with XStateNet2.

---

## New Naming Convention: XS1 vs XS2

### XS1 = Legacy XStateNet (V5 implementation)
- Located in: `/c/Develop25/XStateNet/XStateNet5Impl/`
- Namespace: `XStateNet`
- Uses: Channel-based actor system
- No Akka.NET dependency

### XS2 = XStateNet2 (Current)
- Located in: `/c/Develop25/XStateNet/XStateNet2/XStateNet2.Core/`
- Namespace: `XStateNet2.Core`
- Uses: Akka.NET actor system
- Optimizations: FrozenDictionary, Array-based

---

## Updated Naming Scheme

### Current XStateNet2 Schedulers (Rename to XS2):

| Old Name | New Name | Code |
|----------|----------|------|
| XState-Dict (event) | **XS2-Dict (event)** | `xs2-dict` |
| XState-Frozen (event) | **XS2-Frozen (event)** | `xs2-frozen` |
| XState-Array (event) | **XS2-Array (event)** | `xs2-array` |
| PubSub-Dedicated (multi) | **XS2-PubSub-Dedicated (multi)** | `xs2-pubsub-dedicated` |
| PubSub-Array (one) | **XS2-PubSub-Array (one)** | `xs2-pubsub-array` |

### New Legacy XStateNet Scheduler (Add XS1):

| Name | Code | File |
|------|------|------|
| **XS1-Legacy (event)** | `xs1-legacy` | `RobotSchedulerXS1Legacy.cs` |

---

## Implementation Steps

### Step 1: Add Project Reference ✅ DONE

```xml
<!-- CMPSimXS2.Console.csproj -->
<ItemGroup>
  <ProjectReference Include="..\XStateNet2.Core\XStateNet2.Core.csproj" />
  <ProjectReference Include="..\..\LoggerHelper\LoggerHelper.csproj" />
  <ProjectReference Include="..\..\XStateNet5Impl\XStateNet.csproj" />  ✅ ADDED
</ItemGroup>
```

---

### Step 2: Create Legacy XStateNet Scheduler

**File**: `Schedulers/RobotSchedulerXS1Legacy.cs`

```csharp
using XStateNet;  // Legacy V1 namespace
using CMPSimXS2.Console.Models;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Legacy XStateNet (V1/V5) Robot Scheduler
/// For comparison with XStateNet2 performance
/// </summary>
public class RobotSchedulerXS1Legacy : IRobotScheduler, IDisposable
{
    private readonly XStateNet.ActorSystem _legacySystem;
    private readonly Dictionary<string, XStateNet.IActor> _robots = new();
    private readonly SchedulerContext _context;

    public RobotSchedulerXS1Legacy(string? namePrefix = null)
    {
        _legacySystem = XStateNet.ActorSystem.Instance;
        _context = new SchedulerContext();

        // Create XStateNet V1 machine definition
        var machineJson = @"{
            ""id"": ""scheduler"",
            ""initial"": ""idle"",
            ""states"": {
                ""idle"": {
                    ""on"": {
                        ""REGISTER_ROBOT"": {
                            ""actions"": [""registerRobot""]
                        },
                        ""REQUEST_TRANSFER"": {
                            ""target"": ""processing"",
                            ""actions"": [""queueTransfer""]
                        }
                    }
                },
                ""processing"": {
                    ""on"": {
                        ""ROBOT_STATE_CHANGE"": {
                            ""actions"": [""updateRobotState"", ""tryProcessPending""]
                        },
                        ""REQUEST_TRANSFER"": {
                            ""actions"": [""queueTransfer"", ""tryProcessPending""]
                        }
                    }
                }
            }
        }";

        // Register actions (V1 API is different from V2)
        // TODO: Implement action registration based on XStateNet V1 API
        Logger.Instance.Log("[XS1-Legacy] Initialized with legacy XStateNet V1");
    }

    public void RegisterRobot(string robotId, Akka.Actor.IActorRef robotActor)
    {
        // Convert Akka.NET IActorRef to XStateNet V1 actor
        // This requires a wrapper/adapter
        _context.Robots[robotId] = robotActor;
        Logger.Instance.Log($"[XS1-Legacy] Registered {robotId}");
    }

    public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
    {
        if (_context.RobotStates.TryGetValue(robotId, out var robot))
        {
            robot.State = state;
            robot.HeldWaferId = heldWaferId;
        }
    }

    public void RequestTransfer(TransferRequest request)
    {
        request.Validate();
        _context.PendingRequests.Enqueue(request);
        TryProcessPending();
    }

    public int GetQueueSize() => _context.PendingRequests.Count;

    public string GetRobotState(string robotId)
    {
        return _context.RobotStates.TryGetValue(robotId, out var state) ? state.State : "unknown";
    }

    private void TryProcessPending()
    {
        // Process pending transfers (similar to XS2 logic)
        while (_context.PendingRequests.Count > 0)
        {
            var request = _context.PendingRequests.Peek();
            var robotId = FindAvailableRobot(request);

            if (robotId != null)
            {
                _context.PendingRequests.Dequeue();
                ExecuteTransfer(request, robotId);
            }
            else
            {
                break;
            }
        }
    }

    private string? FindAvailableRobot(TransferRequest request)
    {
        // Same robot routing logic as XS2 schedulers
        return _context.RobotStates
            .Where(kvp => kvp.Value.State == "idle" && CanRobotHandleRoute(kvp.Key, request.From, request.To))
            .Select(kvp => kvp.Key)
            .FirstOrDefault();
    }

    private void ExecuteTransfer(TransferRequest request, string robotId)
    {
        var robot = _context.RobotStates[robotId];
        robot.State = "busy";
        robot.HeldWaferId = request.WaferId;

        _context.ActiveTransfers[robotId] = request;

        // Send pickup command (needs adapter for Akka.NET)
        var pickupData = new Dictionary<string, object>
        {
            ["waferId"] = request.WaferId,
            ["from"] = request.From,
            ["to"] = request.To
        };

        // TODO: Convert to legacy XStateNet message format
        Logger.Instance.Log($"[XS1-Legacy] Executed: {robotId} transferring wafer {request.WaferId}");
    }

    private bool CanRobotHandleRoute(string robotId, string from, string to)
    {
        return robotId switch
        {
            "Robot 1" => (from == "Carrier" && to == "Polisher") ||
                        (from == "Buffer" && to == "Carrier") ||
                        (from == "Polisher" && to == "Carrier"),
            "Robot 2" => (from == "Polisher" && to == "Cleaner"),
            "Robot 3" => (from == "Cleaner" && to == "Buffer"),
            _ => false
        };
    }

    public void Dispose()
    {
        Logger.Instance.Log("[XS1-Legacy] Disposed");
    }

    private class SchedulerContext
    {
        public Dictionary<string, Akka.Actor.IActorRef> Robots { get; } = new();
        public Dictionary<string, RobotState> RobotStates { get; } = new();
        public Queue<TransferRequest> PendingRequests { get; } = new();
        public Dictionary<string, TransferRequest> ActiveTransfers { get; } = new();
    }

    private class RobotState
    {
        public string State { get; set; } = "idle";
        public int? HeldWaferId { get; set; }
    }
}
```

---

### Step 3: Update StressTest.cs

**Add to test list**:
```csharp
// Test each scheduler architecture
// Format: [Version]-[Engine]-[Optimization]-[Communication]

// Lock-based
results.Add(await TestScheduler("Lock (polling)", "lock"));

// Pure Actor
results.Add(await TestScheduler("Actor (event)", "actor"));

// XStateNet V1 (Legacy)
results.Add(await TestScheduler("XS1-Legacy (event)", "xs1-legacy"));  // ✨ NEW

// XStateNet2 Variants: Dict → FrozenDict → Array
results.Add(await TestScheduler("XS2-Dict (event)", "xs2-dict"));
results.Add(await TestScheduler("XS2-Frozen (event)", "xs2-frozen"));
results.Add(await TestScheduler("XS2-Array (event)", "xs2-array"));

// ... (rest of schedulers)
```

**Add to CreateScheduler switch**:
```csharp
private static IRobotScheduler CreateScheduler(ActorSystem actorSystem, string code)
{
    return code switch
    {
        // Lock-based
        "lock" => new RobotScheduler(),

        // Pure Actor
        "actor" => new RobotSchedulerActorProxy(actorSystem, $"stress-{code}"),

        // XStateNet V1 (Legacy)
        "xs1-legacy" => new RobotSchedulerXS1Legacy($"stress-{code}"),  // ✨ NEW

        // XStateNet2 Variants (Dict → FrozenDict → Array)
        "xs2-dict" => new RobotSchedulerXStateDict(actorSystem, $"stress-{code}"),
        "xs2-frozen" => new RobotSchedulerXState(actorSystem, $"stress-{code}"),
        "xs2-array" => new RobotSchedulerXStateArray(actorSystem, $"stress-{code}"),

        // ... (rest)
    };
}
```

---

### Step 4: Rename Existing XState Schedulers

**Update all display names in StressTest.cs**:

```csharp
// OLD:
results.Add(await TestScheduler("XState-Dict (event)", "xstate-dict"));
results.Add(await TestScheduler("XState-Frozen (event)", "xstate-frozen"));
results.Add(await TestScheduler("XState-Array (event)", "xstate-array"));
results.Add(await TestScheduler("PubSub-Dedicated (multi)", "pubsub-dedicated"));
results.Add(await TestScheduler("PubSub-Array (one)", "pubsub-array"));

// NEW:
results.Add(await TestScheduler("XS2-Dict (event)", "xs2-dict"));
results.Add(await TestScheduler("XS2-Frozen (event)", "xs2-frozen"));
results.Add(await TestScheduler("XS2-Array (event)", "xs2-array"));
results.Add(await TestScheduler("XS2-PubSub-Dedicated (multi)", "xs2-pubsub-dedicated"));
results.Add(await TestScheduler("XS2-PubSub-Array (one)", "xs2-pubsub-array"));
```

**Update CreateScheduler switch**:

```csharp
// OLD codes → NEW codes
"xstate-dict" => "xs2-dict"
"xstate-frozen" => "xs2-frozen"
"xstate-array" => "xs2-array"
"pubsub-dedicated" => "xs2-pubsub-dedicated"
"pubsub-array" => "xs2-pubsub-array"
```

---

## Challenges & Solutions

### Challenge 1: Different Actor Systems

**Problem**: XStateNet V1 uses its own actor system, XStateNet2 uses Akka.NET

**Solution**: Create an adapter that wraps Akka.NET IActorRef to work with legacy XStateNet

```csharp
public class AkkaToXS1Adapter : XStateNet.IActor
{
    private readonly Akka.Actor.IActorRef _akkaActor;

    public AkkaToXS1Adapter(Akka.Actor.IActorRef akkaActor, string id)
    {
        _akkaActor = akkaActor;
        Id = id;
    }

    public string Id { get; }
    public ActorStatus Status => ActorStatus.Running;

    public async Task SendAsync(string eventName, object? data = null)
    {
        // Convert XStateNet V1 event to Akka.NET message
        var message = new XStateNet2.Core.Messages.SendEvent(eventName, data as Dictionary<string, object>);
        _akkaActor.Tell(message);
        await Task.CompletedTask;
    }

    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
}
```

### Challenge 2: Different JSON Schema

**Problem**: V1 and V2 have different JSON machine definitions

**Solution**: Use V1's JSON schema for the legacy scheduler

---

## Expected Results

### Comparison Matrix:

| Scheduler | Version | Time | Notes |
|-----------|---------|------|-------|
| **XS1-Legacy (event)** | V1 | ~??.xx s | Baseline legacy performance |
| **XS2-Dict (event)** | V2 | ~18.xx s | V2 without optimizations |
| **XS2-Frozen (event)** | V2 | ~15.xx s | V2 with FrozenDict (+43%) |
| **XS2-Array (event)** | V2 | ~15.xx s | V2 with Array (+63%) |

### Key Comparisons:

1. **V1 vs V2 Baseline**:
   - XS1-Legacy vs XS2-Dict
   - Measures core engine improvement

2. **V2 Optimization Impact**:
   - XS2-Dict → XS2-Frozen → XS2-Array
   - Shows FrozenDict and Array benefits

3. **Architecture Evolution**:
   - XS1 (Channel-based) vs XS2 (Akka.NET-based)
   - Different actor model implementations

---

## Benefits

1. ✅ **Historical Comparison** - See how much XStateNet2 improved
2. ✅ **Clear Versioning** - XS1 vs XS2 naming is unambiguous
3. ✅ **Optimization Impact** - Measure FrozenDict/Array benefits
4. ✅ **Migration Insight** - Understand V1→V2 upgrade value

---

## Next Steps

1. Build adapter for Akka.NET ↔ XStateNet V1 interop
2. Implement `RobotSchedulerXS1Legacy.cs`
3. Rename all existing schedulers (XState → XS2)
4. Update documentation
5. Run stress test and compare results

---

## Quick Command Reference

```bash
# Build with legacy XStateNet reference
cd CMPSimXS2.Console
dotnet build

# Run stress test with all versions
dotnet run --stress-test
```

Expected output:
```
Testing: XS1-Legacy (event)          ← Legacy V1
Testing: XS2-Dict (event)            ← V2 baseline
Testing: XS2-Frozen (event)          ← V2 optimized
Testing: XS2-Array (event)           ← V2 max performance
```

This will clearly show V1 vs V2 performance evolution! 🚀
