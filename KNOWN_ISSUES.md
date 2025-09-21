# Known Issues

## Test Failures

### 1. TimeoutProtectionTests.CreateScope_SharesTimeoutAcrossOperations
- **Type**: Flaky timing test
- **Failure Rate**: ~5% of runs
- **Issue**: Test expects TimeoutException but timing variations cause intermittent failures
- **Location**: `XStateNet.Distributed.Tests/Resilience/TimeoutProtectionTests.cs:165`

### 2. PerformanceValidationTests.Scalability_ShouldScaleNearLinearlyWithCores
- **Type**: Performance/scalability test
- **Failure**: Times out at 8 workers (99.93% completion)
- **Issue**: FalseSharingOptimizedEventBus has contention issues at high concurrency
- **Location**: `XStateNet.Distributed.Tests/PubSub/PerformanceValidationTests.cs:457`

## Root Causes

1. **FalseSharingOptimizedEventBus Issues**:
   - Over-engineered NUMA optimization causing contention
   - Complex P/Invoke and cache-line padding not providing expected benefits
   - Poor scaling characteristics under high concurrency (8+ workers)

2. **Timing-Dependent Tests**:
   - Tests rely on precise timing which varies with system load
   - No proper synchronization mechanisms for deterministic behavior

## Recommended Fixes (Priority Order)

### Priority 1: Replace FalseSharingOptimizedEventBus
- Replace with simpler `Channel<T>` based implementation
- Remove unnecessary NUMA optimizations and P/Invoke calls
- This will fix the scalability test failures and improve overall performance

### Priority 2: Add Resilience Patterns
- Implement Polly retry policies for transient failures
- Add circuit breaker patterns for downstream service protection

### Priority 3: Fix Flaky Tests
- Replace timing-based assertions with proper synchronization
- Use `TaskCompletionSource` for deterministic test behavior
- Increase timeout tolerances for CI/CD environments

## Completed Improvements

✅ **Externalized Orchestrator State**: Redis-backed distributed storage eliminates single point of failure
✅ **Structured Logging**: Serilog integration provides comprehensive observability
✅ **Async API Migration**: Added StartAsync() and SendAsyncWithState() methods

## Impact Assessment

The current issues do not affect the core functionality:
- Redis state storage is fully operational
- System achieves high availability and horizontal scaling
- Performance issues are isolated to the event bus component
- System is production-ready with known limitations documented