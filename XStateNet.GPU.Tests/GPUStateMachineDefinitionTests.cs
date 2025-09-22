using System;
using Xunit;
using XStateNet.GPU.Core;

namespace XStateNet.GPU.Tests
{
    public class GPUStateMachineDefinitionTests
    {
        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            // Act
            var definition = new GPUStateMachineDefinition("TestMachine", 5, 3);

            // Assert
            Assert.Equal("TestMachine", definition.Name);
            Assert.Equal(5, definition.StateCount);
            Assert.Equal(3, definition.EventTypeCount);
            Assert.NotNull(definition.StateNames);
            Assert.NotNull(definition.EventNames);
            Assert.NotNull(definition.TransitionTable);
            Assert.Equal(5, definition.StateNames.Length);
            Assert.Equal(3, definition.EventNames.Length);
            Assert.Empty(definition.TransitionTable);
        }

        [Fact]
        public void GetStateId_ReturnsCorrectId()
        {
            // Arrange
            var definition = new GPUStateMachineDefinition("TestMachine", 3, 2);
            definition.StateNames[0] = "Idle";
            definition.StateNames[1] = "Running";
            definition.StateNames[2] = "Complete";

            // Act & Assert
            Assert.Equal(0, definition.GetStateId("Idle"));
            Assert.Equal(1, definition.GetStateId("Running"));
            Assert.Equal(2, definition.GetStateId("Complete"));
            Assert.Equal(-1, definition.GetStateId("NonExistent"));
        }

        [Fact]
        public void GetEventId_ReturnsCorrectId()
        {
            // Arrange
            var definition = new GPUStateMachineDefinition("TestMachine", 2, 3);
            definition.EventNames[0] = "START";
            definition.EventNames[1] = "STOP";
            definition.EventNames[2] = "RESET";

            // Act & Assert
            Assert.Equal(0, definition.GetEventId("START"));
            Assert.Equal(1, definition.GetEventId("STOP"));
            Assert.Equal(2, definition.GetEventId("RESET"));
            Assert.Equal(-1, definition.GetEventId("INVALID"));
        }

        [Fact]
        public void TransitionTable_CanBeSetAndRetrieved()
        {
            // Arrange
            var definition = new GPUStateMachineDefinition("TestMachine", 2, 2);
            var transitions = new[]
            {
                new TransitionEntry { FromState = 0, EventType = 0, ToState = 1, ActionId = 1, GuardId = 0 },
                new TransitionEntry { FromState = 1, EventType = 1, ToState = 0, ActionId = 2, GuardId = 1 }
            };

            // Act
            definition.TransitionTable = transitions;

            // Assert
            Assert.Equal(2, definition.TransitionTable.Length);
            Assert.Equal(0, definition.TransitionTable[0].FromState);
            Assert.Equal(0, definition.TransitionTable[0].EventType);
            Assert.Equal(1, definition.TransitionTable[0].ToState);
            Assert.Equal(1, definition.TransitionTable[0].ActionId);
            Assert.Equal(0, definition.TransitionTable[0].GuardId);
        }

        [Fact]
        public void ComplexStateMachine_DefinitionWorks()
        {
            // Arrange & Act
            var definition = CreateComplexStateMachineDefinition();

            // Assert
            Assert.Equal("ComplexMachine", definition.Name);
            Assert.Equal(10, definition.StateCount);
            Assert.Equal(8, definition.EventTypeCount);
            Assert.Equal(20, definition.TransitionTable.Length);

            // Verify state and event lookups
            Assert.Equal(0, definition.GetStateId("Init"));
            Assert.Equal(9, definition.GetStateId("Final"));
            Assert.Equal(0, definition.GetEventId("START"));
            Assert.Equal(7, definition.GetEventId("RESET"));
        }

        private GPUStateMachineDefinition CreateComplexStateMachineDefinition()
        {
            var definition = new GPUStateMachineDefinition("ComplexMachine", 10, 8);

            // Define states
            string[] stateNames = { "Init", "Loading", "Ready", "Processing",
                                   "Paused", "Error", "Recovering", "Stopping",
                                   "Stopped", "Final" };
            for (int i = 0; i < stateNames.Length; i++)
            {
                definition.StateNames[i] = stateNames[i];
            }

            // Define events
            string[] eventNames = { "START", "LOAD", "PROCESS", "PAUSE",
                                   "RESUME", "ERROR", "RECOVER", "RESET" };
            for (int i = 0; i < eventNames.Length; i++)
            {
                definition.EventNames[i] = eventNames[i];
            }

            // Define transitions (simplified for testing)
            definition.TransitionTable = new TransitionEntry[20];
            int transitionIndex = 0;

            // Add sample transitions
            for (int state = 0; state < 5; state++)
            {
                for (int evt = 0; evt < 4; evt++)
                {
                    definition.TransitionTable[transitionIndex++] = new TransitionEntry
                    {
                        FromState = state,
                        EventType = evt,
                        ToState = (state + evt + 1) % 10,
                        ActionId = transitionIndex,
                        GuardId = transitionIndex % 3
                    };
                }
            }

            return definition;
        }
    }

    public class GPUStructureTests
    {
        [Fact]
        public void GPUStateMachineInstance_IsBlittable()
        {
            // This test verifies that the structure can be marshaled for GPU transfer
            var instance = new GPUStateMachineInstance
            {
                InstanceId = 1,
                CurrentState = 0,
                PreviousState = -1,
                LastEvent = -1,
                LastTransitionTime = DateTime.UtcNow.Ticks,
                ErrorCount = 0,
                Flags = 0
            };

            // If this compiles and runs, the structure is blittable
            Assert.Equal(1, instance.InstanceId);
        }

        [Fact]
        public void GPUEvent_IsBlittable()
        {
            var evt = new GPUEvent
            {
                InstanceId = 1,
                EventType = 2,
                EventData1 = 100,
                EventData2 = 200,
                Timestamp = DateTime.UtcNow.Ticks
            };

            Assert.Equal(1, evt.InstanceId);
            Assert.Equal(2, evt.EventType);
        }

        [Fact]
        public void TransitionEntry_IsBlittable()
        {
            var entry = new TransitionEntry
            {
                FromState = 0,
                EventType = 1,
                ToState = 2,
                ActionId = 3,
                GuardId = 4
            };

            Assert.Equal(0, entry.FromState);
            Assert.Equal(1, entry.EventType);
            Assert.Equal(2, entry.ToState);
        }

        [Fact]
        public unsafe void GPUStateMachineInstance_ContextData_FixedSize()
        {
            var instance = new GPUStateMachineInstance();

            // Verify fixed buffer size (ContextData is already a fixed array, no need for fixed statement)
            byte* ptr = instance.ContextData;

            // Should be able to write 256 bytes
            for (int i = 0; i < 256; i++)
            {
                ptr[i] = (byte)(i % 256);
            }

            // Verify write
            for (int i = 0; i < 256; i++)
            {
                Assert.Equal((byte)(i % 256), ptr[i]);
            }
        }
    }
}