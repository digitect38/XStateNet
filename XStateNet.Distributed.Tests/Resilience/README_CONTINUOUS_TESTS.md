# Continuous 1000x Test Suite

## Overview

This directory contains **comprehensive continuous testing infrastructure** that runs harsh real-world scenarios **1000 times each** to detect:
- Race conditions
- Memory leaks
- Deadlocks
- Resource exhaustion
- Network failures
- Cascading failures
- Performance degradation

## Test Files

### 1. **QuickContinuousTests.cs** ⚡
Fast validation tests (5-10 minutes total)

| Test | Description | Iterations |
|------|-------------|------------|
| `QuickContinuous_ConcurrentEvents_1000Times` | 50 concurrent events per iteration | 1000 |
| `QuickContinuous_CircuitBreaker_1000Times` | Circuit breaker state transitions | 1000 |
| `QuickContinuous_RaceConditions_1000Times` | Race condition detection | 1000 |
| `QuickContinuous_MemoryStress_1000Times` | Memory allocation/deallocation | 1000 |
| `QuickContinuous_AllTests_1000Times` | All quick tests sequentially | 4000 |

**Run time:** ~5-10 minutes
**Best for:** Quick validation before commits

### 2. **ExtremeContinuousTests.cs** 💪
Extreme stress scenarios (30-60 minutes total)

| Test | Description | Operations/Iteration |
|------|-------------|---------------------|
| `Extreme_ConcurrentMachines_1000Iterations` | 100 concurrent state machines | 1000 events |
| `Extreme_ChainedCircuitBreakers_1000Iterations` | 10 circuit breakers in chain | 50 calls |
| `Extreme_RandomChaos_1000Iterations` | Random failures (30% error rate) | 100 ops |
| `Extreme_MemoryChurn_1000Iterations` | 20MB allocations per iteration | 20 allocs |
| `Extreme_DeadlockPrevention_1000Iterations` | Bidirectional communication | 100 msgs |
| `Extreme_BurstTraffic_1000Iterations` | 1000 events at once | 1000 events |
| `Extreme_StateTransitionStorm_1000Iterations` | Rapid state changes | 500 transitions |

**Run time:** ~30-60 minutes
**Best for:** Nightly builds

### 3. **ParallelContinuousTests.cs** 🔥
Maximum stress with parallel execution (20-40 minutes)

| Test | Description | Parallel Tasks |
|------|-------------|----------------|
| `Parallel_AllScenarios_1000Iterations` | 5 scenarios simultaneously | 5 per iteration |
| `Parallel_MixedLoad_1000Iterations` | Varying load (60 tasks) | 10+20+30 tasks |
| `Parallel_ThunderingHerd_1000Iterations` | 1000 simultaneous requests | 1000 concurrent |
| `Parallel_RapidCreateDestroy_1000Iterations` | 100 orchestrators created/destroyed | 100 per iteration |

**Run time:** ~20-40 minutes
**Best for:** Pre-release validation

### 4. **Continuous1000TestRunner.cs** 📊
Full harsh test suites with detailed analytics

| Test | Description | Duration |
|------|-------------|----------|
| `RunNetworkChaosTests_1000Times` | All 6 network chaos scenarios | ~60 min |
| `RunSoakStabilityTests_1000Times` | All 6 stability tests | ~120 min |
| `RunCascadingFailureTests_1000Times` | All 6 cascading failures | ~60 min |
| `RunLargePayloadTests_1000Times` | All 7 payload tests | ~90 min |
| `RunResourceLimitTests_1000Times` | All 7 resource limit tests | ~60 min |
| `RunAllHarshTests_1000Times_Mixed` | Random mix of all tests | ~90 min |

**Run time:** ~1-2 hours per test
**Best for:** Weekend stress testing

### 5. **MasterContinuousTestSuite.cs** 🎯
Orchestrates all continuous tests

| Test | Description | Total Operations |
|------|-------------|------------------|
| `RunAllContinuousTests_Sequential` | All suites sequentially | ~18,000 |
| `RunAllContinuousTests_Parallel_Maximum_Stress` | All suites in parallel | ~6,000 |
| `RunStressMatrix_1000x1000` | 1 million test operations | 1,000,000 |

**Run time:** 2-8 hours
**Best for:** Full system validation

## How to Run

### Visual Studio Test Explorer

1. **Quick Tests (Recommended for development):**
   - Navigate to `QuickContinuousTests.cs`
   - Right-click class → Run All Tests
   - ⏱️ ~5-10 minutes

2. **Extreme Tests (Pre-commit validation):**
   - Navigate to `ExtremeContinuousTests.cs`
   - Right-click specific test → Run Test
   - ⏱️ ~2-5 minutes per test

3. **Parallel Tests (CI/CD pipeline):**
   - Navigate to `ParallelContinuousTests.cs`
   - Right-click class → Run All Tests
   - ⏱️ ~20-40 minutes

4. **Master Suite (Full validation):**
   - Navigate to `MasterContinuousTestSuite.cs`
   - Right-click `RunAllContinuousTests_Sequential`
   - ⚠️ WARNING: Takes 2-4 hours!

### Command Line

```bash
# Quick validation (5-10 minutes)
dotnet test --filter "FullyQualifiedName~QuickContinuousTests"

# Specific extreme test (2-5 minutes)
dotnet test --filter "FullyQualifiedName~Extreme_ConcurrentMachines_1000Iterations"

# All extreme tests (30-60 minutes)
dotnet test --filter "FullyQualifiedName~ExtremeContinuousTests"

# Parallel tests (20-40 minutes)
dotnet test --filter "FullyQualifiedName~ParallelContinuousTests"

# Full network chaos 1000x (60 minutes)
dotnet test --filter "FullyQualifiedName~RunNetworkChaosTests_1000Times"

# Master suite sequential (2-4 hours)
dotnet test --filter "FullyQualifiedName~RunAllContinuousTests_Sequential"

# Master suite parallel - MAXIMUM STRESS (1-2 hours)
dotnet test --filter "FullyQualifiedName~RunAllContinuousTests_Parallel_Maximum_Stress"

# Stress matrix - 1 MILLION tests (4-8 hours!)
dotnet test --filter "FullyQualifiedName~RunStressMatrix_1000x1000"
```

### Parallel Execution for Maximum Stress

To run multiple test suites simultaneously (push system to limits):

```bash
# Open multiple terminals and run different suites in parallel:

# Terminal 1
dotnet test --filter "FullyQualifiedName~QuickContinuous_ConcurrentEvents_1000Times"

# Terminal 2
dotnet test --filter "FullyQualifiedName~Extreme_BurstTraffic_1000Iterations"

# Terminal 3
dotnet test --filter "FullyQualifiedName~Parallel_ThunderingHerd_1000Iterations"
```

## Output Reports

Each test generates comprehensive reports:

```
════════════════════════════════════════════════════════════════════════════════
  CONTINUOUS TEST REPORT: ConcurrentMachines
════════════════════════════════════════════════════════════════════════════════

📊 OVERALL RESULTS:
  Total Iterations: 1,000
  Total Duration: 2.5 minutes
  Passed: 997 ✅
  Failed: 3 ❌
  Success Rate: 99.70%
  Throughput: 6.67 iterations/sec

⏱️  TIMING STATISTICS:
  Average: 150.25 ms
  Minimum: 98.10 ms
  Maximum: 423.55 ms
  P50 (Median): 145.20 ms
  P95: 201.34 ms
  P99: 298.67 ms

💾 MEMORY ANALYSIS:
  Initial Memory: 45.23 MB
  Final Memory: 52.18 MB
  Memory Growth: 6.95 MB
  Memory per Iteration: 7.12 KB

🔍 STABILITY ANALYSIS:
  Max Consecutive Failures: 2
  ✅ System is stable across all iterations!

❌ ERROR BREAKDOWN:
  TimeoutException: 2 (66.7% of failures)
  InvalidOperationException: 1 (33.3% of failures)
```

## What Gets Tested 1000 Times

### Concurrency
- ✅ Race conditions with forced context switches
- ✅ Concurrent state machine operations
- ✅ Thread-safe event handling
- ✅ Deadlock prevention

### Memory
- ✅ Memory leak detection
- ✅ GC pressure handling
- ✅ Large allocation/deallocation cycles
- ✅ Memory churn (20MB+ per iteration)

### Network & I/O
- ✅ Random disconnects
- ✅ Latency injection (0-100ms)
- ✅ Packet loss (30%)
- ✅ Network partitions

### Resilience
- ✅ Circuit breaker state transitions
- ✅ Cascading failures (10 layers deep)
- ✅ Bulkhead isolation
- ✅ Timeout handling

### Load & Stress
- ✅ Burst traffic (1000 events at once)
- ✅ Thundering herd (1000 simultaneous)
- ✅ Sustained load (30+ seconds)
- ✅ Gradual load increase

### Resource Limits
- ✅ Thread pool exhaustion
- ✅ Connection pool limits
- ✅ File handle limits
- ✅ Bounded channel backpressure

## Success Criteria

| Metric | Threshold |
|--------|-----------|
| Success Rate | ≥ 95% |
| Memory Growth | < 500 MB |
| Max Consecutive Failures | ≤ 5 |
| P95 Latency | No more than 3x average |

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Continuous Tests

on: [push, pull_request]

jobs:
  quick-tests:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - name: Run Quick Continuous Tests
        run: dotnet test --filter "FullyQualifiedName~QuickContinuousTests"
        timeout-minutes: 15

  nightly-tests:
    runs-on: windows-latest
    if: github.event_name == 'schedule'
    steps:
      - uses: actions/checkout@v3
      - name: Run Extreme Continuous Tests
        run: dotnet test --filter "FullyQualifiedName~ExtremeContinuousTests"
        timeout-minutes: 120

  weekly-tests:
    runs-on: windows-latest
    if: github.event_name == 'schedule'
    steps:
      - uses: actions/checkout@v3
      - name: Run Master Test Suite
        run: dotnet test --filter "FullyQualifiedName~RunAllContinuousTests_Sequential"
        timeout-minutes: 300
```

## Troubleshooting

### Tests Timing Out
- Increase timeout in test runner
- Check system resources (CPU, memory)
- Reduce iterations for local testing

### High Failure Rate
- Check for resource limits (file handles, connections)
- Review error breakdown in report
- Run individual failing tests in isolation

### Memory Issues
- Force GC between iterations: `GC.Collect(); GC.WaitForPendingFinalizers();`
- Reduce concurrent operations
- Monitor memory growth trend

### Deadlocks
- Check for circular dependencies
- Review timeout configurations
- Enable detailed logging

## Performance Baseline

### Expected Throughput (on typical dev machine)

| Test Type | Iterations/Second |
|-----------|-------------------|
| Quick Concurrent | ~100-150 |
| Extreme Machines | ~5-10 |
| Parallel Mixed | ~20-40 |
| Burst Traffic | ~2-5 |

### Expected Memory Usage

| Test Type | Peak Memory |
|-----------|-------------|
| Quick Tests | ~100-200 MB |
| Extreme Tests | ~200-500 MB |
| Parallel Tests | ~500-1000 MB |
| Master Suite | ~1-2 GB |

## Best Practices

1. **Run Quick Tests Frequently**
   - Before every commit
   - Part of pre-commit hooks

2. **Run Extreme Tests Daily**
   - Automated nightly builds
   - Before merging PRs

3. **Run Parallel Tests Weekly**
   - Weekend automated runs
   - Before releases

4. **Run Master Suite Pre-Release**
   - Major version releases
   - Critical bug fixes

5. **Monitor Trends**
   - Track success rates over time
   - Watch for degradation
   - Monitor memory growth

## Contributing

When adding new continuous tests:

1. Follow naming convention: `[Type]_[Scenario]_1000Iterations`
2. Include progress reporting every 50-100 iterations
3. Track memory usage
4. Provide detailed failure messages
5. Set appropriate success thresholds (≥95%)
6. Document expected run time

## Questions?

- Check test output for detailed error reports
- Review stability analysis section
- Compare with performance baselines
- Check resource monitor during execution

---

**Total Test Coverage:** 1,000,000+ operations
**Estimated Total Run Time:** 8-12 hours (all tests)
**Confidence Level:** Production-ready ✅
