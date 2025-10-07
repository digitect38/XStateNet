using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Runs harsh tests 1000 times continuously to detect rare race conditions and instabilities
    /// </summary>
    public class Continuous1000TestRunner : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly ILoggerFactory _loggerFactory;

        public Continuous1000TestRunner(ITestOutputHelper output)
        {
            _output = output;
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddDebug().SetMinimumLevel(LogLevel.Error);
            });
        }

        [Fact]
        public async Task RunNetworkChaosTests_1000Times()
        {
            await RunTestContinuously(
                "NetworkChaosTests",
                1000,
                async (iteration) =>
                {
                    using var test = new NetworkChaosTests(_output);

                    // Rotate through different network chaos tests
                    switch (iteration % 6)
                    {
                        case 0:
                            await test.NetworkChaos_RandomDisconnects_SystemRecovers();
                            break;
                        case 1:
                            await test.NetworkChaos_LatencyInjection_MaintainsThroughput();
                            break;
                        case 2:
                            await test.NetworkChaos_PacketLoss_RetriesSucceed();
                            break;
                        case 3:
                            await test.NetworkChaos_PartitionedNetwork_IsolatesNodes();
                            break;
                        case 4:
                            await test.NetworkChaos_ConcurrentConnectionFailures_GracefulDegradation();
                            break;
                        case 5:
                            // Run all tests in sequence
                            await test.NetworkChaos_RandomDisconnects_SystemRecovers();
                            await test.NetworkChaos_LatencyInjection_MaintainsThroughput();
                            break;
                    }
                });
        }

        [Fact]
        public async Task RunSoakStabilityTests_1000Times()
        {
            await RunTestContinuously(
                "SoakStabilityTests",
                1000,
                async (iteration) =>
                {
                    using var test = new SoakStabilityTests(_output);

                    // Note: Running full 30-second soak tests 1000x would take 8+ hours
                    // So we'll run the faster stability tests more frequently
                    switch (iteration % 4)
                    {
                        case 0:
                            await test.SoakTest_VaryingLoad_BurstTraffic_StaysStable();
                            break;
                        case 1:
                            await test.StabilityTest_MultipleMachines_ContinuousInteraction_15Seconds();
                            break;
                        case 2:
                            await test.StabilityTest_GradualLoadIncrease_FindsBreakingPoint();
                            break;
                        case 3:
                            await test.StabilityTest_LongRunning_WithRecovery_20Seconds();
                            break;
                    }
                });
        }

        [Fact]
        public async Task RunCascadingFailureTests_1000Times()
        {
            await RunTestContinuously(
                "CascadingFailureTests",
                1000,
                async (iteration) =>
                {
                    using var test = new CascadingFailureTests(_output);

                    switch (iteration % 6)
                    {
                        case 0:
                            await test.CascadingFailure_ThreeLayerService_PropagatesUpward();
                            break;
                        case 1:
                            await test.CascadingFailure_BulkheadPattern_IsolatesFailures();
                            break;
                        case 2:
                            await test.CascadingFailure_MultipleCircuitBreakers_PreventOverload();
                            break;
                        case 3:
                            await test.CascadingFailure_DownstreamTimeout_TriggersUpstreamFailure();
                            break;
                        case 4:
                            await test.CascadingFailure_FanOut_PartialFailureHandling();
                            break;
                        case 5:
                            // Mix of tests
                            await test.CascadingFailure_ThreeLayerService_PropagatesUpward();
                            await test.CascadingFailure_BulkheadPattern_IsolatesFailures();
                            break;
                    }
                });
        }

        [Fact]
        public async Task RunLargePayloadTests_1000Times()
        {
            await RunTestContinuously(
                "LargePayloadTests",
                1000,
                async (iteration) =>
                {
                    using var test = new LargePayloadTests(_output);

                    switch (iteration % 7)
                    {
                        case 0:
                            await test.LargePayload_VariousSizes_ProcessedSuccessfully(1024);
                            break;
                        case 1:
                            await test.LargePayload_VariousSizes_ProcessedSuccessfully(100 * 1024);
                            break;
                        case 2:
                            await test.LargePayload_VariousSizes_ProcessedSuccessfully(1024 * 1024);
                            break;
                        case 3:
                            await test.LargePayload_ConcurrentLargeMessages_NoMemoryLeak();
                            break;
                        case 4:
                            await test.LargePayload_StringMessages_UnicodeAndSpecialChars();
                            break;
                        case 5:
                            await test.LargePayload_ComplexNestedStructures_SerializeDeserialize();
                            break;
                        case 6:
                            await test.LargePayload_MultiMB_Batching_OptimizesPerformance();
                            break;
                    }
                });
        }

        [Fact]
        public async Task RunResourceLimitTests_1000Times()
        {
            await RunTestContinuously(
                "ResourceLimitTests",
                1000,
                async (iteration) =>
                {
                    using var test = new ResourceLimitTests(_output);

                    switch (iteration % 7)
                    {
                        case 0:
                            await test.ResourceLimit_ThreadPoolExhaustion_GracefulDegradation();
                            break;
                        case 1:
                            await test.ResourceLimit_ConnectionPoolExhaustion_ReusesConnections();
                            break;
                        case 2:
                            await test.ResourceLimit_MemoryPressure_TriggersGC();
                            break;
                        case 3:
                            await test.ResourceLimit_BoundedChannel_Backpressure();
                            break;
                        case 4:
                            await test.ResourceLimit_FileHandleExhaustion_GracefulHandling();
                            break;
                        case 5:
                            await test.ResourceLimit_SemaphoreSlim_MaxConcurrency();
                            break;
                        case 6:
                            await test.ResourceLimit_ConcurrentDictionary_HighContention();
                            break;
                    }
                });
        }

        [Fact]
        public async Task RunAllHarshTests_1000Times_Mixed()
        {
            await RunTestContinuously(
                "AllHarshTests (Mixed)",
                1000,
                async (iteration) =>
                {
                    // Randomly select test suite to run
                    var testType = iteration % 5;

                    switch (testType)
                    {
                        case 0:
                            using (var test = new NetworkChaosTests(_output))
                            {
                                await test.NetworkChaos_ConcurrentConnectionFailures_GracefulDegradation();
                            }
                            break;
                        case 1:
                            using (var test = new SoakStabilityTests(_output))
                            {
                                await test.StabilityTest_GradualLoadIncrease_FindsBreakingPoint();
                            }
                            break;
                        case 2:
                            using (var test = new CascadingFailureTests(_output))
                            {
                                await test.CascadingFailure_MultipleCircuitBreakers_PreventOverload();
                            }
                            break;
                        case 3:
                            using (var test = new LargePayloadTests(_output))
                            {
                                await test.LargePayload_ConcurrentLargeMessages_NoMemoryLeak();
                            }
                            break;
                        case 4:
                            using (var test = new ResourceLimitTests(_output))
                            {
                                await test.ResourceLimit_ThreadPoolExhaustion_GracefulDegradation();
                            }
                            break;
                    }
                });
        }

        private async Task RunTestContinuously(string testName, int iterations, Func<int, Task> testAction)
        {
            var results = new ConcurrentBag<TestResult>();
            var totalStopwatch = Stopwatch.StartNew();

            _output.WriteLine($"\n{'═',80}");
            _output.WriteLine($"  CONTINUOUS TEST: {testName}");
            _output.WriteLine($"  Running {iterations} iterations...");
            _output.WriteLine($"{'═',80}\n");

            var passCount = 0;
            var failCount = 0;
            var errorsByType = new ConcurrentDictionary<string, int>();
            var iterationTimes = new ConcurrentBag<long>();

            // Track memory over time
            var initialMemory = GC.GetTotalMemory(false);
            var memorySnapshots = new ConcurrentBag<(int iteration, long memory)>();

            for (int i = 0; i < iterations; i++)
            {
                var iteration = i + 1;
                var sw = Stopwatch.StartNew();

                try
                {
                    await testAction(i);
                    sw.Stop();

                    Interlocked.Increment(ref passCount);
                    iterationTimes.Add(sw.ElapsedMilliseconds);

                    results.Add(new TestResult
                    {
                        Iteration = iteration,
                        Success = true,
                        DurationMs = sw.ElapsedMilliseconds
                    });
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Interlocked.Increment(ref failCount);

                    var errorType = ex.GetType().Name;
                    errorsByType.AddOrUpdate(errorType, 1, (k, v) => v + 1);

                    results.Add(new TestResult
                    {
                        Iteration = iteration,
                        Success = false,
                        DurationMs = sw.ElapsedMilliseconds,
                        ErrorType = errorType,
                        ErrorMessage = ex.Message
                    });

                    _output.WriteLine($"[{iteration:D4}] ❌ FAILED: {errorType} - {ex.Message}");
                }

                // Progress reporting every 50 iterations
                if (iteration % 50 == 0)
                {
                    var currentMemory = GC.GetTotalMemory(false);
                    memorySnapshots.Add((iteration, currentMemory));

                    var progress = iteration / (double)iterations;
                    var avgTime = iterationTimes.Any() ? iterationTimes.Average() : 0;
                    var memoryMB = currentMemory / (1024.0 * 1024.0);

                    _output.WriteLine($"[{iteration:D4}/{iterations}] " +
                        $"Progress: {progress:P0} | " +
                        $"Pass: {passCount} | " +
                        $"Fail: {failCount} | " +
                        $"Avg: {avgTime:F0}ms | " +
                        $"Mem: {memoryMB:F1}MB");
                }

                // Periodic GC to prevent memory buildup
                if (iteration % 100 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }

            totalStopwatch.Stop();

            // Final memory check
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var finalMemory = GC.GetTotalMemory(false);
            var memoryLeakMB = (finalMemory - initialMemory) / (1024.0 * 1024.0);

            // Generate comprehensive report
            GenerateReport(testName, iterations, totalStopwatch, results, errorsByType,
                iterationTimes, memorySnapshots, initialMemory, finalMemory, memoryLeakMB);

            // Assert overall success
            var successRate = passCount / (double)iterations;
            Assert.True(successRate >= 0.95,
                $"Success rate {successRate:P2} is below 95% threshold. " +
                $"Passed: {passCount}, Failed: {failCount}");

            Assert.True(memoryLeakMB < 500,
                $"Memory leak detected: {memoryLeakMB:F2} MB growth over {iterations} iterations");
        }

        private void GenerateReport(
            string testName,
            int iterations,
            Stopwatch totalStopwatch,
            ConcurrentBag<TestResult> results,
            ConcurrentDictionary<string, int> errorsByType,
            ConcurrentBag<long> iterationTimes,
            ConcurrentBag<(int iteration, long memory)> memorySnapshots,
            long initialMemory,
            long finalMemory,
            double memoryLeakMB)
        {
            var passCount = results.Count(r => r.Success);
            var failCount = results.Count(r => !r.Success);
            var successRate = passCount / (double)iterations;

            var times = iterationTimes.OrderBy(t => t).ToList();
            var avgTime = times.Any() ? times.Average() : 0;
            var minTime = times.Any() ? times.Min() : 0;
            var maxTime = times.Any() ? times.Max() : 0;
            var p50Time = times.Any() ? times[times.Count / 2] : 0;
            var p95Time = times.Any() ? times[(int)(times.Count * 0.95)] : 0;
            var p99Time = times.Any() ? times[(int)(times.Count * 0.99)] : 0;

            _output.WriteLine($"\n{'═',80}");
            _output.WriteLine($"  CONTINUOUS TEST REPORT: {testName}");
            _output.WriteLine($"{'═',80}");
            _output.WriteLine($"\n📊 OVERALL RESULTS:");
            _output.WriteLine($"  Total Iterations: {iterations:N0}");
            _output.WriteLine($"  Total Duration: {totalStopwatch.Elapsed.TotalMinutes:F1} minutes");
            _output.WriteLine($"  Passed: {passCount:N0} ✅");
            _output.WriteLine($"  Failed: {failCount:N0} ❌");
            _output.WriteLine($"  Success Rate: {successRate:P2}");
            _output.WriteLine($"  Throughput: {iterations / totalStopwatch.Elapsed.TotalSeconds:F2} iterations/sec");

            _output.WriteLine($"\n⏱️  TIMING STATISTICS:");
            _output.WriteLine($"  Average: {avgTime:F2} ms");
            _output.WriteLine($"  Minimum: {minTime:F2} ms");
            _output.WriteLine($"  Maximum: {maxTime:F2} ms");
            _output.WriteLine($"  P50 (Median): {p50Time:F2} ms");
            _output.WriteLine($"  P95: {p95Time:F2} ms");
            _output.WriteLine($"  P99: {p99Time:F2} ms");

            _output.WriteLine($"\n💾 MEMORY ANALYSIS:");
            _output.WriteLine($"  Initial Memory: {initialMemory / (1024.0 * 1024.0):F2} MB");
            _output.WriteLine($"  Final Memory: {finalMemory / (1024.0 * 1024.0):F2} MB");
            _output.WriteLine($"  Memory Growth: {memoryLeakMB:F2} MB");
            _output.WriteLine($"  Memory per Iteration: {memoryLeakMB / iterations * 1024:F2} KB");

            if (memorySnapshots.Any())
            {
                _output.WriteLine($"\n  Memory Progression:");
                foreach (var snapshot in memorySnapshots.OrderBy(s => s.iteration))
                {
                    var memMB = snapshot.memory / (1024.0 * 1024.0);
                    _output.WriteLine($"    Iteration {snapshot.iteration:D4}: {memMB:F2} MB");
                }
            }

            if (errorsByType.Any())
            {
                _output.WriteLine($"\n❌ ERROR BREAKDOWN:");
                foreach (var error in errorsByType.OrderByDescending(e => e.Value))
                {
                    var errorRate = error.Value / (double)failCount;
                    _output.WriteLine($"  {error.Key}: {error.Value} ({errorRate:P1} of failures)");
                }

                // Show sample error messages
                _output.WriteLine($"\n  Sample Error Messages:");
                var sampleErrors = results.Where(r => !r.Success).Take(5);
                foreach (var error in sampleErrors)
                {
                    _output.WriteLine($"    [{error.Iteration:D4}] {error.ErrorType}: {error.ErrorMessage}");
                }
            }

            // Stability analysis
            _output.WriteLine($"\n🔍 STABILITY ANALYSIS:");
            var failuresByIteration = results
                .Where(r => !r.Success)
                .GroupBy(r => r.Iteration / 100)
                .OrderBy(g => g.Key)
                .ToList();

            if (failuresByIteration.Any())
            {
                _output.WriteLine($"  Failures by 100-iteration blocks:");
                foreach (var block in failuresByIteration)
                {
                    var blockStart = block.Key * 100;
                    var blockEnd = Math.Min((block.Key + 1) * 100, iterations);
                    _output.WriteLine($"    Iterations {blockStart:D4}-{blockEnd:D4}: {block.Count()} failures");
                }
            }
            else
            {
                _output.WriteLine($"  ✅ No failures detected - system is extremely stable!");
            }

            // Consecutive failures (sign of instability)
            var consecutiveFailures = 0;
            var maxConsecutiveFailures = 0;
            var orderedResults = results.OrderBy(r => r.Iteration).ToList();

            foreach (var result in orderedResults)
            {
                if (!result.Success)
                {
                    consecutiveFailures++;
                    maxConsecutiveFailures = Math.Max(maxConsecutiveFailures, consecutiveFailures);
                }
                else
                {
                    consecutiveFailures = 0;
                }
            }

            _output.WriteLine($"  Max Consecutive Failures: {maxConsecutiveFailures}");
            if (maxConsecutiveFailures > 5)
            {
                _output.WriteLine($"  ⚠️  WARNING: High consecutive failures may indicate systemic issue");
            }

            _output.WriteLine($"\n{'═',80}\n");
        }

        public void Dispose()
        {
            _loggerFactory?.Dispose();
        }

        private class TestResult
        {
            public int Iteration { get; set; }
            public bool Success { get; set; }
            public long DurationMs { get; set; }
            public string ErrorType { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
        }
    }
}
