using System;
using System.Threading.Tasks;
using Xunit;

using XStateNet;
using XStateNet.Tests.TestHelpers;

namespace InterMachineTests;

/// <summary>
/// Tests demonstrating how to check state machine states from outside
/// </summary>
public class StateCheckingTests
{
    [Fact]
    public async Task IsInState_Should_CheckCurrentState()
    {
        // Arrange
        var json = @"{
            'id': 'trafficLight',
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

        var machine = StateMachine.CreateFromScript(json, guidIsolate: true);
        machine.Start();

        // Assert - Initial state (use full path with machine ID)
        Assert.True(machine.IsInState(machine,  $"{machine.machineId}.red"));
        Assert.False(machine.IsInState(machine, $"{machine.machineId}.yellow"));
        Assert.False(machine.IsInState(machine, $"{machine.machineId}.green"));
        
        // Act - Transition to yellow
        machine.Send("TIMER");
        await DeterministicWait.WaitForStateAsync(machine, "yellow");

        // Assert - After first transition
        Assert.False(machine.IsInState(machine, $"{machine.machineId}.red"));
        Assert.True(machine.IsInState(machine,  $"{machine.machineId}.yellow"));
        Assert.False(machine.IsInState(machine, $"{machine.machineId}.green"));

        // Act - Transition to green
        machine.Send("TIMER");
        await DeterministicWait.WaitForStateAsync(machine, "green");

        // Assert - After second transition
        Assert.False(machine.IsInState(machine, $"{machine.machineId}.red"));
        Assert.False(machine.IsInState(machine, $"{machine.machineId}.yellow"));
        Assert.True(machine.IsInState(machine,  $"{machine.machineId}.green"));
    }
    
    [Fact]
    public async Task GetActiveStateString_Should_ReturnCurrentStates()
    {
        // Arrange
        var json = @"{
            'id': 'hierarchicalMachine',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'START': 'working'
                    }
                },
                'working': {
                    'initial': 'processing',
                    'states': {
                        'processing': {
                            'on': {
                                'PAUSE': 'paused'
                            }
                        },
                        'paused': {
                            'on': {
                                'RESUME': 'processing'
                            }
                        }
                    },
                    'on': {
                        'STOP': 'idle'
                    }
                }
            }
        }";

        var machine = StateMachine.CreateFromScript(json, guidIsolate: true);
        machine.Start();

        // Assert - Initial state
        var initialState = machine.GetActiveStateString(leafOnly: true);
        Assert.Contains("idle", initialState);
        
        // Act - Start working and get new state
        var workingState = await machine.SendAsyncWithState("START");

        // Assert - Working state (SendAsyncWithState returns full state by default)
        Assert.Contains("processing", workingState);

        // Get full path (already have it from SendAsyncWithState)
        var fullPath = workingState;
        Assert.Contains("working", fullPath);
        Assert.Contains("processing", fullPath);
    }
    
    [Fact]
    public async Task GetSourceSubStateCollection_Should_ReturnStateHierarchy()
    {
        // Arrange
        var json = @"{
            'id': 'nestedMachine',
            'initial': 'level1',
            'states': {
                'level1': {
                    'initial': 'level2',
                    'states': {
                        'level2': {
                            'initial': 'level3',
                            'states': {
                                'level3': {
                                    'on': {
                                        'NEXT': 'level3b'
                                    }
                                },
                                'level3b': {}
                            }
                        }
                    }
                }
            }
        }";

        var machine = StateMachine.CreateFromScript(json, guidIsolate: true);
        machine.Start();
        // Machine starts in pending.fetch state immediately, no need to wait

        // Act - Get current state hierarchy
        var states = machine.GetSourceSubStateCollection(null);
        var stateString = states.ToCsvString(machine, true);
        
        // Assert
        Assert.Contains("level1", stateString);
        Assert.Contains("level2", stateString);
        Assert.Contains("level3", stateString);
        
        // Check nested state (use full paths)
        Assert.True(machine.IsInState(machine, $"{machine.machineId}.level1"));
        Assert.True(machine.IsInState(machine, $"{machine.machineId}.level1.level2"));
        Assert.True(machine.IsInState(machine, $"{machine.machineId}.level1.level2.level3"));
    }
    
    [Fact]
    public void ExternalStateChecking_Should_WorkWithMultipleMachines()
    {
        // Arrange - Create two independent machines
        var machine1Json = @"{
            'id': 'machine1',
            'initial': 'on',
            'states': {
                'on': {
                    'on': { 'TOGGLE': 'off' }
                },
                'off': {
                    'on': { 'TOGGLE': 'on' }
                }
            }
        }";
        
        var machine2Json = @"{
            'id': 'machine2',
            'initial': 'inactive',
            'states': {
                'inactive': {
                    'on': { 'ACTIVATE': 'active' }
                },
                'active': {
                    'on': { 'DEACTIVATE': 'inactive' }
                }
            }
        }";
        
        var machine1 = StateMachine.CreateFromScript(machine1Json, guidIsolate: true);
        var machine2 = StateMachine.CreateFromScript(machine2Json, guidIsolate: true);
        
        machine1.Start();
        machine2.Start();
        
        // Act & Assert - Check states independently
        Assert.True(machine1.IsInState(machine1, $"{machine1.machineId}.on"));
        Assert.True(machine2.IsInState(machine2, $"{machine2.machineId}.inactive"));
        
        // Toggle machine1
        machine1.Send("TOGGLE");
        Assert.True(machine1.IsInState(machine1, $"{machine1.machineId}.off"));
        Assert.False(machine1.IsInState(machine1, $"{machine1.machineId}.on"));
        
        // Machine2 should be unaffected
        Assert.True(machine2.IsInState(machine2, $"{machine2.machineId}.inactive"));
        
        // Activate machine2
        machine2.Send("ACTIVATE");
        Assert.True(machine2.IsInState(machine2, $"{machine2.machineId}.active"));
        Assert.False(machine2.IsInState(machine2, $"{machine2.machineId}.inactive"));
        
        // Machine1 should still be off
        Assert.True(machine1.IsInState(machine1, $"{machine1.machineId}.off"));
    }
    
    [Fact]
    public async Task ConditionalLogic_Should_UseStateChecking()
    {
        // Arrange
        var json = @"{
            'id': 'door',
            'initial': 'closed',
            'states': {
                'closed': {
                    'on': {
                        'OPEN': 'open'
                    }
                },
                'open': {
                    'on': {
                        'CLOSE': 'closed'
                    }
                }
            }
        }";

        var door = StateMachine.CreateFromScript(json, guidIsolate: true);
        door.Start();

        // Act - Conditional logic based on state
        var result = "";
        var doorId = door.machineId;

        if (door.IsInState(door, $"{doorId}.closed"))
        {
            result = "Door is closed, opening...";
            door.Send("OPEN");
        }
        await DeterministicWait.WaitForStateAsync(door, "open");

        if (door.IsInState(door, $"{doorId}.open"))
        {
            result += " Door is now open!";
        }

        // Assert
        Assert.Equal("Door is closed, opening... Door is now open!", result);
        Assert.True(door.IsInState(door, $"{doorId}.open"));
    }
    
    [Fact]
    public async Task ParallelStates_Should_CheckMultipleActiveStates()
    {
        // Arrange - Machine with parallel states
        var json = @"{
            'id': 'parallelMachine',
            'type': 'parallel',
            'states': {
                'lights': {
                    'initial': 'off',
                    'states': {
                        'off': {
                            'on': { 'TURN_ON': 'on' }
                        },
                        'on': {
                            'on': { 'TURN_OFF': 'off' }
                        }
                    }
                },
                'heating': {
                    'initial': 'idle',
                    'states': {
                        'idle': {
                            'on': { 'HEAT': 'heating' }
                        },
                        'heating': {
                            'on': { 'COOL': 'idle' }
                        }
                    }
                }
            }
        }";

        var machine = StateMachine.CreateFromScript(json, guidIsolate: true);
        machine.Start();
        var machineId = machine.machineId;
        // Machine starts immediately in parallel states, no need to wait

        // Assert - Both parallel regions should be active
        var activeStates = machine.GetActiveStateString(leafOnly: false);
        Assert.Contains("lights", activeStates);
        Assert.Contains("heating", activeStates);
        Assert.Contains("off", activeStates);
        Assert.Contains("idle", activeStates);
        
        // Act - Turn on lights
        machine.Send("TURN_ON");
        await DeterministicWait.WaitForStateAsync(machine, "on");

        // Assert - Lights should be on, heating still idle
        Assert.True(machine.IsInState(machine, $"{machineId}.lights.on"));
        Assert.True(machine.IsInState(machine, $"{machineId}.heating.idle"));
        
        // Act - Start heating
        machine.Send("HEAT");
        await DeterministicWait.WaitForStateAsync(machine, "heating");

        // Assert - Both should be in their active states
        Assert.True(machine.IsInState(machine, $"{machineId}.lights.on"));
        Assert.True(machine.IsInState(machine, $"{machineId}.heating.heating"));
    }
}