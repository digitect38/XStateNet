using XStateNet;

namespace XStateNet.Distributed.Resilience
{
    /// <summary>
    /// Builder for Circuit Breaker state machine using proper XStateNet JSON configuration
    /// </summary>
    public static class CircuitBreakerStateMachineBuilder
    {
        public static StateMachine Build(
            string name,
            CircuitBreakerOptions options,
            ActionMap actionMap,
            GuardMap guardMap)
        {
            // Build proper XStateNet configuration using JavaScript object notation
            var configJson = @"{
                id: 'CircuitBreaker_" + name + @"',
                initial: 'closed',
                context: {
                    consecutiveFailures: 0,
                    successCountInHalfOpen: 0
                },
                states: {
                    closed: {
                        on: {
                            FAIL: [
                                {
                                    target: 'open',
                                    cond: 'thresholdExceeded',
                                    actions: ['incrementFailures', 'notifyOpen']
                                },
                                {
                                    actions: ['incrementFailures']
                                }
                            ],
                            SUCCESS: {
                                actions: ['resetFailures']
                            }
                        }
                    },
                    open: {
                        entry: ['notifyOpenState'],
                        after: {
                            " + (int)options.BreakDuration.TotalMilliseconds + @": {
                                target: 'halfOpen',
                                actions: ['notifyHalfOpenTransition']
                            }
                        },
                        on: {
                            EXECUTE: {
                                actions: ['rejectExecution']
                            }
                        }
                    },
                    halfOpen: {
                        entry: ['resetSuccessCount', 'notifyHalfOpenState'],
                        on: {
                            SUCCESS: [
                                {
                                    target: 'closed',
                                    cond: 'successThresholdMet',
                                    actions: ['incrementSuccess', 'notifyClosed', 'resetCounters']
                                },
                                {
                                    actions: ['incrementSuccess']
                                }
                            ],
                            FAIL: {
                                target: 'open',
                                actions: ['notifyOpen']
                            }
                        }
                    }
                }
            }";

            return StateMachine.CreateFromScript(configJson, true, actionMap, guardMap);
        }
    }
}