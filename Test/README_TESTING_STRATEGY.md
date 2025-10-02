# XStateNet Testing Strategy

## Dual Test Approach

This project maintains **two parallel test suites** to serve different purposes:

### 1. Legacy Tests (Framework Validation)
**Location:** `Test/UnitTest_*.cs` (original files)
**Pattern:** Uses `StateMachineFactory` and direct `StateMachine` API
**Purpose:**
- Validate core XStateNet framework functionality
- Ensure `StateMachine`, `StateMachineFactory`, and XState features work correctly
- Test fundamental state machine behaviors (transitions, guards, actions, invokes, etc.)
- Regression testing for framework changes

**Examples:**
- `UnitTest_TrafficLight.cs` - Tests parallel states, guards, nested states
- `UnitTest_InvokeHeavy.cs` - Tests invoke services, retries, cancellation
- `UnitTest_AfterProp.cs` - Tests delayed transitions
- `UnitTest_ErrorHandling.cs` - Tests error propagation
- And 40+ other framework validation tests

### 2. Orchestrated Tests (Best Practices)
**Location:** `Test/*_Orchestrated.cs` (new files)
**Pattern:** Uses `EventBusOrchestrator` and `IPureStateMachine` API
**Purpose:**
- Demonstrate recommended patterns for production code
- Show how to use orchestrator for deadlock-free inter-machine communication
- Provide examples of modern event-driven architecture
- Test SEMI standards and application-level features

**Examples:**
- `UnitTest_TrafficLight_Orchestrated.cs` - 8 tests showing orchestrated parallel states
- `UnitTest_InvokeHeavy_Orchestrated.cs` - 8 tests showing orchestrated invoke patterns
- `UnitTest_AfterProp_Orchestrated.cs` - 2 tests showing orchestrated delayed transitions
- `SemiStandard.Tests/*MachineTests.cs` - All SEMI standard tests use orchestrator

## Key Differences

| Aspect | Legacy Tests | Orchestrated Tests |
|--------|-------------|-------------------|
| **Machine Type** | `StateMachine` | `IPureStateMachine` |
| **Creation** | `StateMachineFactory.CreateFromScript()` | `ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices()` |
| **Events** | `machine.Send("EVENT")` (synchronous) | `orchestrator.SendEventAsync("from", "to", "EVENT")` (async) |
| **Actions** | `ActionMap` with `NamedAction` | `Dictionary<string, Action<OrchestratedContext>>` |
| **Communication** | Direct `machine.Send()` (can deadlock) | Through orchestrator (deadlock-free) |
| **Base Class** | `IDisposable` | `OrchestratorTestBase` |

## When to Use Each Pattern

### Use Legacy Pattern (StateMachineFactory) When:
- ✅ Testing core XStateNet framework features
- ✅ Validating state machine fundamentals
- ✅ Writing unit tests for framework changes
- ✅ Single machine scenarios without inter-machine communication

### Use Orchestrated Pattern (EventBusOrchestrator) When:
- ✅ Building production applications
- ✅ Implementing SEMI standards
- ✅ Coordinating multiple state machines
- ✅ Requiring deadlock-free communication
- ✅ Building distributed or event-driven systems
- ✅ Writing new application code

## Test Base Classes

### OrchestratorTestBase
Provides helpers for orchestrated tests:
```csharp
public class MyTest : OrchestratorTestBase
{
    private IPureStateMachine? _currentMachine;

    [Fact]
    public async Task MyTest()
    {
        _currentMachine = CreateMachine("id", json, actions, guards, services);
        await _currentMachine.StartAsync();
        await SendEventAsync("FROM", "id", "EVENT");
    }
}
```

## Migration Notes

If you're migrating from legacy to orchestrated pattern:

1. **Change inheritance:** `IDisposable` → `OrchestratorTestBase`
2. **Change machine type:** `StateMachine` → `IPureStateMachine`
3. **Change actions:** `ActionMap` → `Dictionary<string, Action<OrchestratedContext>>`
4. **Change event sending:** `machine.Send()` → `await SendEventAsync()`
5. **Add async:** Make test methods `async Task`

## Test Statistics

- **Legacy Tests:** 44 files, 100+ tests validating core framework
- **Orchestrated Tests:** 3 files, 18 tests demonstrating best practices
- **SEMI Standards:** 12 machines, 250+ tests using orchestrator pattern

## Conclusion

Both test suites are **valuable and maintained**:
- Legacy tests ensure the framework works correctly
- Orchestrated tests show how to use it in production

When writing **new application code**, follow the **orchestrated pattern** demonstrated in `*_Orchestrated.cs` files and SEMI standards.
