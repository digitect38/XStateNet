using NUnit.Framework;
using XStateNet;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedFeatures
{
    [TestFixture]
    public class LeaderFollowerStateMachinesTests
    {
        private StateMachine _followerStateMachine;
        private StateMachine _leaderStateMachine;

        private ActionMap _followerActions;
        private GuardMap _followerGuards;

        private ActionMap _leaderActions;
        private GuardMap _leaderGuards;

        [SetUp]
        public void Setup()
        {
            // Follower actions
            _followerActions = new ();
            _followerGuards = new ();

            // Leader actions
            _leaderActions = new ActionMap()
            {
                ["sendToFollowerToB"] = new List<NamedAction> { new NamedAction("sendToFollowerToB", (sm) => _followerStateMachine.Send("to_b")) },
                ["sendToFollowerToA"] = new List<NamedAction> { new NamedAction("sendToFollowerToA", (sm) => _followerStateMachine.Send("to_a")) }
            };
            _leaderGuards = new ();

            // Load state machines from JSON
            var followerJson = LeaderFollowerStateMachine.FollowerStateMachineScript;
            var leaderJson = LeaderFollowerStateMachine.LeaderStateMachineScript;

            _followerStateMachine = StateMachine.CreateFromScript(followerJson, _followerActions, _followerGuards).Start();
            _leaderStateMachine = StateMachine.CreateFromScript(leaderJson, _leaderActions, _leaderGuards).Start();
        }

        [Test]
        public async Task TestLeaderFollowerStateMachines()
        {
            // Initially, both state machines should be in state 'a'
            Assert.That(_followerStateMachine.GetActiveStateString() == "#follower.a");
            Assert.That(_leaderStateMachine.GetActiveStateString() == "#leader.a");

            // Wait for the leader to send the 'to_b' event to the follower
            await Task.Delay(1100);
            Assert.That(_followerStateMachine.GetActiveStateString() == "#follower.b");
            Assert.That(_leaderStateMachine.GetActiveStateString() == "#leader.b");

            // Wait for the leader to send the 'to_a' event to the follower
            await Task.Delay(1100);
            Assert.That(_followerStateMachine.GetActiveStateString() == "#follower.a");
            Assert.That(_leaderStateMachine.GetActiveStateString() == "#leader.a");
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
                        'after': {
                            '1000': {
                                'target': 'b',
                                'actions': ['sendToFollowerToB']
                            }
                        }
                    },
                    'b': {
                        'after': {
                            '1000': {
                                'target': 'a',
                                'actions': ['sendToFollowerToA']
                            }
                        }
                    }
                }
            }";
        }
    }
}
