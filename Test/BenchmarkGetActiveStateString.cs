using System;
using System.Diagnostics;
using Xunit;
using XStateNet;

namespace XStateNet.Tests
{
    public class BenchmarkGetActiveStateString
    {
        [Fact]
        public void Benchmark_GetActiveStateString_Performance()
        {
            // Create a complex state machine with parallel states
            string script = @"{
                'id': 'benchmark',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': { 'START': 'working' }
                    },
                    'working': {
                        'type': 'parallel',
                        'states': {
                            'region1': {
                                'initial': 'r1s1',
                                'states': {
                                    'r1s1': { 'on': { 'NEXT1': 'r1s2' } },
                                    'r1s2': { 'on': { 'NEXT1': 'r1s3' } },
                                    'r1s3': { 'on': { 'NEXT1': 'r1s1' } }
                                }
                            },
                            'region2': {
                                'initial': 'r2s1',
                                'states': {
                                    'r2s1': { 'on': { 'NEXT2': 'r2s2' } },
                                    'r2s2': { 'on': { 'NEXT2': 'r2s3' } },
                                    'r2s3': { 'on': { 'NEXT2': 'r2s1' } }
                                }
                            },
                            'region3': {
                                'initial': 'r3s1',
                                'states': {
                                    'r3s1': { 'on': { 'NEXT3': 'r3s2' } },
                                    'r3s2': { 'on': { 'NEXT3': 'r3s3' } },
                                    'r3s3': { 'on': { 'NEXT3': 'r3s1' } }
                                }
                            }
                        }
                    }
                }
            }";

            var stateMachine = StateMachineFactory.CreateFromScript(script, false, true);
            stateMachine.Start();
            stateMachine.Send("START");

            // Warm up
            for (int i = 0; i < 100; i++)
            {
                _ = stateMachine.GetActiveStateNames();
            }

            const int iterations = 10000;

            // Benchmark without state changes (should hit cache)
            var sw1 = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                _ = stateMachine.GetActiveStateNames();
            }
            sw1.Stop();
            var cachedTime = sw1.ElapsedMilliseconds;

            // Benchmark with state changes (cache invalidation)
            var sw2 = Stopwatch.StartNew();
            for (int i = 0; i < iterations / 3; i++)
            {
                stateMachine.Send("NEXT1");
                _ = stateMachine.GetActiveStateNames();
                stateMachine.Send("NEXT2");
                _ = stateMachine.GetActiveStateNames();
                stateMachine.Send("NEXT3");
                _ = stateMachine.GetActiveStateNames();
            }
            sw2.Stop();
            var uncachedTime = sw2.ElapsedMilliseconds;

            // Results
            Console.WriteLine($"GetActiveStateString Performance Benchmark:");
            Console.WriteLine($"  Cached (no state changes): {iterations} calls in {cachedTime}ms = {(double)cachedTime / iterations * 1000:F2} μs/call");
            Console.WriteLine($"  With state changes: {iterations} calls in {uncachedTime}ms = {(double)uncachedTime / iterations * 1000:F2} μs/call");
            Console.WriteLine($"  Cache speedup: {(double)uncachedTime / cachedTime:F1}x faster");

            // Assert that caching provides significant improvement
            Assert.True(cachedTime < uncachedTime / 5, $"Cached calls should be at least 5x faster. Cached: {cachedTime}ms, Uncached: {uncachedTime}ms");
        }

        [Fact]
        public void Verify_Cache_Correctness()
        {
            string script = @"{
                'id': 'cachetest',
                'initial': 'state1',
                'states': {
                    'state1': { 'on': { 'NEXT': 'state2' } },
                    'state2': { 'on': { 'NEXT': 'state3' } },
                    'state3': { 'on': { 'NEXT': 'state1' } }
                }
            }";

            var stateMachine = StateMachineFactory.CreateFromScript(script, false, true);
            stateMachine.Start();

            // Get initial state
            var state1 = stateMachine.GetActiveStateNames();
            var state1Again = stateMachine.GetActiveStateNames();
            Assert.Equal(state1, state1Again); // Should be same (cached)

            // Change state
            stateMachine.Send("NEXT");
            var state2 = stateMachine.GetActiveStateNames();
            Assert.NotEqual(state1, state2); // Should be different

            // Multiple calls should return same value
            var state2Again = stateMachine.GetActiveStateNames();
            Assert.Equal(state2, state2Again); // Should be same (cached)

            // Change state again
            stateMachine.Send("NEXT");
            var state3 = stateMachine.GetActiveStateNames();
            Assert.NotEqual(state2, state3); // Should be different

            // Verify full state string caching
            var fullState = stateMachine.GetActiveStateNames(leafOnly: false);
            var fullStateAgain = stateMachine.GetActiveStateNames(leafOnly: false);
            Assert.Equal(fullState, fullStateAgain); // Should be same (cached)
        }
    }
}