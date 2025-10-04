using Xunit;
using System;
using System.Linq;
using XStateNet;
using XStateNet.Orchestration;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedFeatures
{
    public class LeaderFollowerStateMachinesTests : XStateNet.Tests.OrchestratorTestBase
    {
        private string _followerMachineId;
        private string _leaderMachineId;

        private StateMachine? GetUnderlying(IPureStateMachine machine)
        {
            return (machine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;
        }

        private async Task SendToMachineAsync(string machineId, string eventName)
        {
            await _orchestrator.SendEventAsync("test", machineId, eventName);
        }

        public LeaderFollowerStateMachinesTests()
        {
            // Load state machines from JSON
            var followerJson = LeaderFollowerStateMachine.FollowerStateMachineScript;
            var leaderJson = LeaderFollowerStateMachine.LeaderStateMachineScript;

            // Create follower first
            var followerActions = new Dictionary<string, Action<OrchestratedContext>>();
            var followerMachine = CreateMachine("follower", followerJson, followerActions);
            followerMachine.StartAsync().Wait();
            _followerMachineId = followerMachine.Id;

            // Now create leader actions with follower ID captured
            var leaderActions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["sendToFollowerToB"] = ctx => ctx.RequestSend(_followerMachineId, "to_b"),
                ["sendToFollowerToA"] = ctx => ctx.RequestSend(_followerMachineId, "to_a")
            };

            // Create leader
            var leaderMachine = CreateMachine("leader", leaderJson, leaderActions);
            leaderMachine.StartAsync().Wait();
            _leaderMachineId = leaderMachine.Id;
        }

        [Fact]
        public async Task TestLeaderFollowerStateMachines()
        {
            // Get pure machines
            var followerMachine = _machines.First(m => m.Id == _followerMachineId);
            var leaderMachine = _machines.First(m => m.Id == _leaderMachineId);

            // Initially, both state machines should be in state 'a'
            // Note: Don't use machine.Id in state path - it has channel group format (follower#1#guid)
            // but CurrentState uses internal format (#follower_guid.state)
            Assert.Contains(".a", followerMachine.CurrentState);
            Assert.Contains(".a", leaderMachine.CurrentState);

            // Trigger leader to transition to 'b', which should send event to follower
            await SendToMachineAsync(_leaderMachineId, "GO_TO_B");
            await WaitForStateAsync(leaderMachine, "#leader.b", timeoutMs: 1000);
            await WaitForStateAsync(followerMachine, "#follower.b", timeoutMs: 1000);

            Assert.Contains(".b", followerMachine.CurrentState);
            Assert.Contains(".b", leaderMachine.CurrentState);

            // Trigger leader to transition back to 'a', which should send event to follower
            await SendToMachineAsync(_leaderMachineId, "GO_TO_A");
            await WaitForStateAsync(leaderMachine, "#leader.a", timeoutMs: 1000);
            await WaitForStateAsync(followerMachine, "#follower.a", timeoutMs: 1000);

            Assert.Contains(".a", followerMachine.CurrentState);
            Assert.Contains(".a", leaderMachine.CurrentState);
        }
        
        public static class LeaderFollowerStateMachine
        {
            public static string FollowerStateMachineScript => @"
            {
                'id': 'follower',
                'initial': 'a',
                'states': {
                    'a': {
                        'on': {
                            'to_b': {
                                'target': 'b'
                            }
                        }
                    },
                    'b': {
                        'on': {
                            'to_a': {
                                'target': 'a'
                            }
                        }
                    }
                }
            }";

            public static string LeaderStateMachineScript => @"
            {
                'id': 'leader',
                'initial': 'a',
                'states': {
                    'a': {
                        'on': {
                            'GO_TO_B': {
                                'target': 'b',
                                'actions': 'sendToFollowerToB'
                            }
                        }
                    },
                    'b': {
                        'on': {
                            'GO_TO_A': {
                                'target': 'a',
                                'actions': 'sendToFollowerToA'
                            }
                        }
                    }
                }
            }";
        }
    }
}


