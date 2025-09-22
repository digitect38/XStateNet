using System;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.GPU;
using Serilog;

class TestComplexityAnalyzer
{
    static async Task Main()
    {
        // Setup Serilog for console output
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Console.WriteLine("=== Testing Complexity Analyzer with GPU/CPU Auto-Selection ===");
        Console.WriteLine();

        // Test 1: Simple state machine (should use GPU if enough instances)
        await TestSimpleMachine();

        Console.WriteLine();

        // Test 2: Complex state machine (should use CPU)
        await TestComplexMachine();

        Console.WriteLine();

        // Test 3: Force GPU on complex machine
        await TestForceGPU();

        Console.WriteLine("=== Test Complete ===");
        Log.CloseAndFlush();
    }

    static async Task TestSimpleMachine()
    {
        Console.WriteLine("--- Testing Simple State Machine ---");

        string simpleJson = @"{
            'id': 'SimpleTrafficLight',
            'initial': 'red',
            'states': {
                'red': {
                    'on': {
                        'TIMER': 'yellow'
                    }
                },
                'yellow': {
                    'on': {
                        'TIMER': 'green'
                    }
                },
                'green': {
                    'on': {
                        'TIMER': 'red'
                    }
                }
            }
        }";

        // Create GPU-accelerated machine with low threshold for testing
        var gpuMachine = new GPUAcceleratedStateMachine(simpleJson, gpuThreshold: 10);

        // Create 20 instances (above threshold, should use GPU)
        Console.WriteLine("Creating 20 instances (above GPU threshold)...");
        var pool = await gpuMachine.CreatePoolAsync(20);

        Console.WriteLine($"Pool created - GPU Accelerated: {pool.IsGPUAccelerated}");

        // Test transitions
        await pool.BroadcastAsync("TIMER");
        Console.WriteLine($"After TIMER: Instance 0 state = {pool.GetState(0)}");

        gpuMachine.Dispose();
    }

    static async Task TestComplexMachine()
    {
        Console.WriteLine("--- Testing Complex State Machine ---");

        string complexJson = @"{
            'id': 'ComplexWorkflow',
            'initial': 'idle',
            'context': {
                'retryCount': 0,
                'data': null
            },
            'states': {
                'idle': {
                    'on': {
                        'START': 'loading'
                    }
                },
                'loading': {
                    'invoke': {
                        'src': 'fetchData',
                        'onDone': {
                            'target': 'processing',
                            'actions': 'assignData'
                        },
                        'onError': 'error'
                    }
                },
                'processing': {
                    'type': 'parallel',
                    'states': {
                        'validation': {
                            'initial': 'validating',
                            'states': {
                                'validating': {
                                    'after': {
                                        '1000': 'validated'
                                    }
                                },
                                'validated': {
                                    'type': 'final'
                                }
                            }
                        },
                        'transformation': {
                            'initial': 'transforming',
                            'states': {
                                'transforming': {
                                    'on': {
                                        'TRANSFORM_COMPLETE': 'transformed'
                                    }
                                },
                                'transformed': {
                                    'type': 'final'
                                }
                            }
                        }
                    },
                    'onDone': 'complete'
                },
                'error': {
                    'entry': 'logError',
                    'on': {
                        'RETRY': [
                            {
                                'target': 'loading',
                                'cond': 'canRetry',
                                'actions': 'incrementRetry'
                            }
                        ],
                        'RESET': 'idle'
                    }
                },
                'complete': {
                    'type': 'final',
                    'entry': 'saveResults'
                }
            }
        }";

        // Create GPU-accelerated machine (should detect complexity and use CPU)
        var gpuMachine = new GPUAcceleratedStateMachine(complexJson, gpuThreshold: 10);

        // Create 100 instances (even above threshold, should use CPU due to complexity)
        Console.WriteLine("Creating 100 instances of complex machine...");
        var pool = await gpuMachine.CreatePoolAsync(100);

        Console.WriteLine($"Pool created - GPU Accelerated: {pool.IsGPUAccelerated}");
        Console.WriteLine("Complex features detected - automatically using CPU execution");

        // Test basic transition
        await pool.SendAsync(0, "START");
        Console.WriteLine($"After START: Instance 0 state = {pool.GetState(0)}");

        gpuMachine.Dispose();
    }

    static async Task TestForceGPU()
    {
        Console.WriteLine("--- Testing Force GPU on Complex Machine ---");

        string moderateJson = @"{
            'id': 'ModerateComplexity',
            'initial': 'idle',
            'states': {
                'idle': {
                    'entry': 'logEntry',
                    'exit': 'logExit',
                    'on': {
                        'START': 'processing'
                    }
                },
                'processing': {
                    'on': {
                        'SUCCESS': 'complete',
                        'FAILURE': [
                            {
                                'target': 'error',
                                'cond': 'isError'
                            }
                        ]
                    }
                },
                'error': {
                    'on': {
                        'RETRY': 'processing',
                        'RESET': 'idle'
                    }
                },
                'complete': {
                    'type': 'final'
                }
            }
        }";

        // Force GPU usage despite moderate complexity
        var gpuMachine = new GPUAcceleratedStateMachine(moderateJson,
            gpuThreshold: 10,
            forceGPU: true);

        Console.WriteLine("Creating 50 instances with forceGPU=true...");
        var pool = await gpuMachine.CreatePoolAsync(50);

        Console.WriteLine($"Pool created - GPU Accelerated: {pool.IsGPUAccelerated}");

        if (pool.IsGPUAccelerated)
        {
            Console.WriteLine("GPU execution forced - some features may not work as expected");
        }

        // Test transitions
        await pool.BroadcastAsync("START");
        Console.WriteLine($"After START: Instance 0 state = {pool.GetState(0)}");

        gpuMachine.Dispose();
    }
}