# CMPSimXS2.EventDriven - Architecture

## State Machine Definition Approach

### Current Approach (JSON File)

The CMP system now loads the state machine definition directly from `cmp_machine.json`:

```csharp
// Program.cs
var machineJson = await File.ReadAllTextAsync("../../cmp_machine.json");
var cmpActor = CMPMachineFactory.Create(actorSystem, machineJson);
```

### Benefits

1. **XState v5 Specification Compliance**
   - JSON matches XState v5 specification exactly
   - No C#-to-JSON translation layer
   - Easier to validate against XState schemas

2. **Maintainability**
   - Edit JSON directly without recompiling
   - Changes take effect immediately
   - No build step required for state machine updates

3. **Portability**
   - Same JSON works across different XState implementations
   - Can be shared between JavaScript/TypeScript and C# systems
   - Easier to version control and compare changes

4. **SEMI E10 Compliance**
   - Equipment engineers can edit state definitions
   - No C# programming knowledge required
   - Standard JSON tools (editors, validators) can be used

5. **Eliminates Redundancy**
   - Previously: JavaScript → C# → JSON → Parser
   - Now: JSON → Parser
   - Single source of truth

### Previous Approach (Obsolete)

Previously, `CMPMachineBuilder.cs` built the state machine programmatically in C#:

```csharp
// CMPMachineBuilder.cs [OBSOLETE]
public static string BuildMachineJson()
{
    var machine = new
    {
        id = "cmp",
        type = "parallel",
        states = new Dictionary<string, object> { /* ... */ }
    };
    return JsonSerializer.Serialize(machine, options);
}
```

This approach had benefits during early development (type safety, IDE support) but became redundant once the JSON specification stabilized.

### Guard and Action Registration

Guards and actions are still registered in C# code using `CMPMachineFactory`:

```csharp
// CMPMachineFactory.cs
var factory = new XStateMachineFactory(actorSystem);
var machineBuilder = factory.FromJson(machineJson);

// Register guards (pure functions)
machineBuilder
    .WithGuard("canStartCycle", CMPGuards.CanStartCycle)
    .WithGuard("allWafersProcessed", CMPGuards.AllWafersProcessed)
    // ...

// Register actions (with actor reference)
IActorRef? actorRef = null;
CMPActions.RegisterAll(machineBuilder, () => actorRef!);

var actor = machineBuilder.BuildAndStart("cmp-machine");
actorRef = actor;
```

This keeps the business logic in C# while the state machine structure is in JSON.

## Project Structure

```
CMPSimXS2.EventDriven/
├── Program.cs                  # Entry point - loads JSON and starts system
├── CMPMachineFactory.cs        # Factory for building configured machines
├── CMPMachineBuilder.cs        # [OBSOLETE] Kept for reference only
├── Actions/
│   └── CMPActions.cs          # Action implementations
├── Guards/
│   └── CMPGuards.cs           # Guard implementations
└── ../cmp_machine.json         # State machine definition (source of truth)
```

## Migration Guide

If you need to modify the state machine:

1. Edit `cmp_machine.json` directly
2. Validate JSON syntax (use JSON validator)
3. Run the application - no rebuild needed (unless guards/actions changed)
4. If you need new guards/actions, add them to `CMPGuards.cs` or `CMPActions.cs` and register in `CMPMachineFactory.cs`

## XState v5 Features Supported

- ✅ Parallel states
- ✅ Nested states
- ✅ Guarded transitions
- ✅ Actions (entry, exit, transition)
- ✅ Delayed transitions (`after`)
- ✅ Guarded delayed transitions with arrays (first match wins)
- ✅ Always transitions
- ✅ Final states
- ✅ Context management
