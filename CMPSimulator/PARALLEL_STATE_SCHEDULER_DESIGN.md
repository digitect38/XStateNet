# Parallel State Machine Scheduler 설계

## 현재 문제점

### SchedulerMachine.cs의 제어문 기반 로직

현재 SchedulerMachine은 **state machine의 형태를 하고 있지만**, 실제 스케줄링 로직은 **제어문(if-else)으로 구현**되어 있습니다.

```csharp
private void CheckPriorities(OrchestratedContext ctx)
{
    _logger($"[Scheduler] 🔍 CheckPriorities called (Pending wafers: {_lPending.Count})");

    // Priority 1: C → B (R3)
    if (CanExecuteCtoB())
    {
        _logger("✓ P1 condition met: C→B available");
        ExecuteCtoB(ctx);
        return; // Execute highest priority only
    }

    // Priority 2: P → C (R2)
    if (CanExecutePtoC())
    {
        _logger("✓ P2 condition met: P→C available");
        ExecutePtoC(ctx);
        return;
    }

    // Priority 3: L → P (R1)
    if (CanExecuteLtoP())
    {
        _logger("✓ P3 condition met: L→P available");
        ExecuteLtoP(ctx);
        return;
    }

    // Priority 4: B → L (R1)
    if (CanExecuteBtoL())
    {
        _logger("✓ P4 condition met: B→L available");
        ExecuteBtoL(ctx);
        return;
    }

    _logger("No priority conditions met");
}
```

### 문제점

1. **State Machine의 장점을 활용하지 못함**
   - 조건 판단이 코드(actions)에 숨어있음
   - 상태 전환 로직이 명시적이지 않음
   - Guards가 활용되지 않음

2. **단일 스레드 실행**
   - 한 번에 하나의 우선순위만 체크
   - 동시 실행 가능성 검토 어려움

3. **디버깅/시각화 어려움**
   - 어떤 조건이 만족/불만족인지 추적 어려움
   - 상태 전환 히스토리 부재

4. **확장성 제약**
   - 새로운 우선순위 추가 시 코드 수정 필요
   - Sync Mode 같은 다른 스케줄링 전략 추가 시 복잡도 증가

---

## 개선 방향: Parallel State Machine

### XStateNet Parallel States 활용

Parallel states를 사용하면 각 로봇(R1, R2, R3)의 상태를 **동시에 추적**하고, **조건부 전환(guarded transitions)**을 통해 스케줄링 로직을 선언적으로 표현할 수 있습니다.

```
┌─────────────────────────────────────────────────────────────┐
│                    Scheduler (parallel)                      │
├──────────────────┬──────────────────┬──────────────────────┤
│   R1 Control     │   R2 Control     │   R3 Control         │
│                  │                  │                      │
│  - monitoring    │  - monitoring    │  - monitoring        │
│  - readyToSend   │  - readyToSend   │  - readyToSend       │
│  - commandSent   │  - commandSent   │  - commandSent       │
│  - holding       │  - holding       │  - holding           │
└──────────────────┴──────────────────┴──────────────────────┘
```

각 로봇별 region이 독립적으로 상태를 유지하면서, guards를 통해 조건을 체크하고, 이벤트를 통해 명령을 전송합니다.

---

## 설계 상세

### 1. Parallel State 구조

```json
{
  "id": "scheduler",
  "type": "parallel",
  "states": {
    "r1Control": {
      "initial": "monitoring",
      "states": {
        "monitoring": {
          "on": {
            "STATION_STATUS": [
              {
                "cond": "canExecuteLtoP",
                "target": "readyToSendLtoP",
                "actions": ["logR1LtoPReady"]
              },
              {
                "cond": "canExecuteBtoL",
                "target": "readyToSendBtoL",
                "actions": ["logR1BtoLReady"]
              }
            ],
            "ROBOT_STATUS": {
              "actions": ["updateR1State"]
            }
          }
        },
        "readyToSendLtoP": {
          "entry": ["sendLtoPCommand"],
          "on": {
            "ROBOT_STATUS": {
              "cond": "r1CommandSent",
              "target": "commandSent",
              "actions": ["logR1CommandSent"]
            }
          }
        },
        "readyToSendBtoL": {
          "entry": ["sendBtoLCommand"],
          "on": {
            "ROBOT_STATUS": {
              "cond": "r1CommandSent",
              "target": "commandSent",
              "actions": ["logR1CommandSent"]
            }
          }
        },
        "commandSent": {
          "on": {
            "ROBOT_STATUS": [
              {
                "cond": "r1Holding",
                "target": "holding",
                "actions": ["logR1Holding"]
              },
              {
                "cond": "r1Idle",
                "target": "monitoring",
                "actions": ["logR1Idle"]
              }
            ]
          }
        },
        "holding": {
          "on": {
            "STATION_STATUS": [
              {
                "cond": "r1DestinationReady",
                "actions": ["sendR1DestinationReady"]
              }
            ],
            "ROBOT_STATUS": {
              "cond": "r1Idle",
              "target": "monitoring",
              "actions": ["logR1Completed"]
            }
          }
        }
      }
    },
    "r2Control": {
      "initial": "monitoring",
      "states": {
        "monitoring": {
          "on": {
            "STATION_STATUS": {
              "cond": "canExecutePtoC",
              "target": "readyToSend",
              "actions": ["logR2PtoCReady"]
            },
            "ROBOT_STATUS": {
              "actions": ["updateR2State"]
            }
          }
        },
        "readyToSend": {
          "entry": ["sendPtoCCommand"],
          "on": {
            "ROBOT_STATUS": {
              "cond": "r2CommandSent",
              "target": "commandSent",
              "actions": ["logR2CommandSent"]
            }
          }
        },
        "commandSent": {
          "on": {
            "ROBOT_STATUS": [
              {
                "cond": "r2Holding",
                "target": "holding",
                "actions": ["logR2Holding"]
              },
              {
                "cond": "r2Idle",
                "target": "monitoring",
                "actions": ["logR2Idle"]
              }
            ]
          }
        },
        "holding": {
          "on": {
            "STATION_STATUS": {
              "cond": "r2DestinationReady",
              "actions": ["sendR2DestinationReady"]
            },
            "ROBOT_STATUS": {
              "cond": "r2Idle",
              "target": "monitoring",
              "actions": ["logR2Completed"]
            }
          }
        }
      }
    },
    "r3Control": {
      "initial": "monitoring",
      "states": {
        "monitoring": {
          "on": {
            "STATION_STATUS": {
              "cond": "canExecuteCtoB",
              "target": "readyToSend",
              "actions": ["logR3CtoBReady"]
            },
            "ROBOT_STATUS": {
              "actions": ["updateR3State"]
            }
          }
        },
        "readyToSend": {
          "entry": ["sendCtoBCommand"],
          "on": {
            "ROBOT_STATUS": {
              "cond": "r3CommandSent",
              "target": "commandSent",
              "actions": ["logR3CommandSent"]
            }
          }
        },
        "commandSent": {
          "on": {
            "ROBOT_STATUS": [
              {
                "cond": "r3Holding",
                "target": "holding",
                "actions": ["logR3Holding"]
              },
              {
                "cond": "r3Idle",
                "target": "monitoring",
                "actions": ["logR3Idle"]
              }
            ]
          }
        },
        "holding": {
          "on": {
            "STATION_STATUS": {
              "cond": "r3DestinationReady",
              "actions": ["sendR3DestinationReady"]
            },
            "ROBOT_STATUS": {
              "cond": "r3Idle",
              "target": "monitoring",
              "actions": ["logR3Completed"]
            }
          }
        }
      }
    }
  }
}
```

### 2. Guards 정의

각 로봇별 조건을 guards로 분리하여 재사용성과 가독성을 높입니다.

```csharp
// R1 Guards
private bool CanExecuteLtoP(StateMachine sm)
{
    var polisherState = GetStationState("polisher");
    bool polisherAvailable = (polisherState == "empty" || polisherState == "IDLE") ||
                           ((polisherState == "done" || polisherState == "COMPLETE") &&
                            GetRobotState("R2") == "idle");

    return _lPending.Count > 0 &&
           GetRobotState("R1") == "idle" &&
           polisherAvailable;
}

private bool CanExecuteBtoL(StateMachine sm)
{
    bool canActuallyDoLtoP = CanExecuteLtoP(sm);

    return GetStationState("buffer") == "occupied" &&
           GetRobotState("R1") == "idle" &&
           !canActuallyDoLtoP;
}

private bool R1Holding(StateMachine sm)
{
    return GetRobotState("R1") == "holding";
}

private bool R1Idle(StateMachine sm)
{
    return GetRobotState("R1") == "idle";
}

private bool R1DestinationReady(StateMachine sm)
{
    var destination = _robotWaitingFor.GetValueOrDefault("R1");
    if (destination == "LoadPort") return true;

    var destState = GetStationState(destination);
    return destState == "empty" || destState == "idle";
}

private bool R1CommandSent(StateMachine sm)
{
    // 로봇 상태가 idle에서 다른 상태로 변경되었는지 체크
    return GetRobotState("R1") != "idle";
}

// R2 Guards
private bool CanExecutePtoC(StateMachine sm)
{
    var cleanerState = GetStationState("cleaner");
    bool cleanerAvailable = cleanerState == "empty" ||
                          (cleanerState == "done" && GetRobotState("R3") == "idle");

    var polisherState = GetStationState("polisher");
    bool polisherDone = (polisherState == "done" || polisherState == "COMPLETE");

    return polisherDone &&
           GetRobotState("R2") == "idle" &&
           cleanerAvailable;
}

private bool R2Holding(StateMachine sm)
{
    return GetRobotState("R2") == "holding";
}

private bool R2Idle(StateMachine sm)
{
    return GetRobotState("R2") == "idle";
}

private bool R2DestinationReady(StateMachine sm)
{
    var destination = _robotWaitingFor.GetValueOrDefault("R2");
    var destState = GetStationState(destination);
    return destState == "empty" || destState == "idle";
}

private bool R2CommandSent(StateMachine sm)
{
    return GetRobotState("R2") != "idle";
}

// R3 Guards
private bool CanExecuteCtoB(StateMachine sm)
{
    return GetStationState("cleaner") == "done" &&
           GetRobotState("R3") == "idle";
}

private bool R3Holding(StateMachine sm)
{
    return GetRobotState("R3") == "holding";
}

private bool R3Idle(StateMachine sm)
{
    return GetRobotState("R3") == "idle";
}

private bool R3DestinationReady(StateMachine sm)
{
    var destination = _robotWaitingFor.GetValueOrDefault("R3");
    var destState = GetStationState(destination);
    return destState == "empty" || destState == "idle";
}

private bool R3CommandSent(StateMachine sm)
{
    return GetRobotState("R3") != "idle";
}
```

### 3. Actions 정의

```csharp
// R1 Actions
["sendLtoPCommand"] = (ctx) =>
{
    if (_lPending.Count == 0) return;

    int waferId = _lPending[0];
    _lPending.RemoveAt(0);

    _logger($"[Scheduler] [R1] L→P: Commanding R1 to transfer wafer {waferId}");
    _robotsWithPendingCommands.Add("R1");

    ctx.RequestSend("R1", "TRANSFER", new JObject
    {
        ["waferId"] = waferId,
        ["from"] = "LoadPort",
        ["to"] = "polisher"
    });
},

["sendBtoLCommand"] = (ctx) =>
{
    int? waferId = _stationWafers.GetValueOrDefault("buffer");
    if (waferId == null || waferId == 0) return;

    _logger($"[Scheduler] [R1] B→L: Commanding R1 to return wafer {waferId}");
    _robotsWithPendingCommands.Add("R1");

    ctx.RequestSend("R1", "TRANSFER", new JObject
    {
        ["waferId"] = waferId.Value,
        ["from"] = "buffer",
        ["to"] = "LoadPort"
    });

    _lCompleted.Add(waferId.Value);
    _logger($"[Scheduler] ✓ Wafer {waferId} completed ({_lCompleted.Count}/25)");

    if (_lCompleted.Count >= 25)
    {
        _logger("[Scheduler] ✅ All 25 wafers completed!");
        AllWafersCompleted?.Invoke(this, EventArgs.Empty);
    }
},

["sendR1DestinationReady"] = (ctx) =>
{
    var destination = _robotWaitingFor.GetValueOrDefault("R1");
    _logger($"[Scheduler] [R1] ✓ Destination {destination} ready! Sending DESTINATION_READY");
    ctx.RequestSend("R1", "DESTINATION_READY", new JObject());
    _robotWaitingFor.Remove("R1");
},

["updateR1State"] = (ctx) =>
{
    // Extract and update R1 state from ROBOT_STATUS event
    if (_underlyingMachine?.ContextMap != null)
    {
        var data = _underlyingMachine.ContextMap["_event"] as JObject;
        if (data != null && data["robot"]?.ToString() == "R1")
        {
            var state = data["state"]?.ToString();
            var wafer = data["wafer"]?.ToObject<int?>();
            var waitingFor = data["waitingFor"]?.ToString();

            _robotStates["R1"] = state;
            _robotWafers["R1"] = wafer;
            _robotsWithPendingCommands.Remove("R1");

            if (waitingFor != null)
            {
                _robotWaitingFor["R1"] = waitingFor;
            }

            _logger($"[Scheduler] [R1] State updated: {state} (wafer: {wafer})");
        }
    }
},

// R2 Actions
["sendPtoCCommand"] = (ctx) =>
{
    int? waferId = _stationWafers.GetValueOrDefault("polisher");
    if (waferId == null || waferId == 0) return;

    _logger($"[Scheduler] [R2] P→C: Commanding R2 to transfer wafer {waferId}");
    _robotsWithPendingCommands.Add("R2");

    ctx.RequestSend("R2", "TRANSFER", new JObject
    {
        ["waferId"] = waferId,
        ["from"] = "polisher",
        ["to"] = "cleaner"
    });
},

["sendR2DestinationReady"] = (ctx) =>
{
    var destination = _robotWaitingFor.GetValueOrDefault("R2");
    _logger($"[Scheduler] [R2] ✓ Destination {destination} ready! Sending DESTINATION_READY");
    ctx.RequestSend("R2", "DESTINATION_READY", new JObject());
    _robotWaitingFor.Remove("R2");
},

["updateR2State"] = (ctx) =>
{
    // Similar to updateR1State but for R2
},

// R3 Actions
["sendCtoBCommand"] = (ctx) =>
{
    int? waferId = _stationWafers.GetValueOrDefault("cleaner");
    if (waferId == null || waferId == 0) return;

    _logger($"[Scheduler] [R3] C→B: Commanding R3 to transfer wafer {waferId}");
    _robotsWithPendingCommands.Add("R3");

    ctx.RequestSend("R3", "TRANSFER", new JObject
    {
        ["waferId"] = waferId,
        ["from"] = "cleaner",
        ["to"] = "buffer"
    });
},

["sendR3DestinationReady"] = (ctx) =>
{
    var destination = _robotWaitingFor.GetValueOrDefault("R3");
    _logger($"[Scheduler] [R3] ✓ Destination {destination} ready! Sending DESTINATION_READY");
    ctx.RequestSend("R3", "DESTINATION_READY", new JObject());
    _robotWaitingFor.Remove("R3");
},

["updateR3State"] = (ctx) =>
{
    // Similar to updateR1State but for R3
}
```

---

## 장점

### 1. 선언적 설계 (Declarative Design)
- 조건(guards)과 상태 전환이 JSON 정의에 명시
- 코드 가독성 향상
- 디버깅 시 상태 흐름 추적 용이

### 2. 병렬 실행 (Parallel Execution)
- 각 로봇의 상태를 동시에 추적
- 우선순위 없이 모든 로봇이 준비되면 즉시 실행
- Sync Mode 구현 용이

### 3. 확장성 (Extensibility)
- 새로운 로봇 추가 시 region만 추가
- 새로운 스케줄링 전략 추가 시 guards/actions만 수정
- Forward Priority vs Sync Mode를 guards로 제어 가능

### 4. 테스트 용이성
- 각 guard를 독립적으로 테스트 가능
- 상태 전환 시뮬레이션 가능
- 조건 만족/불만족 케이스 검증 용이

### 5. 시각화 (Visualization)
- XState Visualizer 사용 가능
- 실시간 상태 모니터링
- 병렬 상태 흐름 확인

---

## 단점 및 고려사항

### 1. 학습 곡선
- Parallel states 개념 이해 필요
- Guards와 Actions 분리 설계 필요

### 2. 복잡도 증가
- JSON 정의가 길어짐
- Guards 수가 많아짐 (로봇당 5-6개)

### 3. 디버깅 복잡도
- 여러 region이 동시에 상태 전환
- 이벤트 순서에 따른 경쟁 조건 가능

### 4. Forward Priority 구현
- 우선순위 기반 스케줄링은 여전히 guards에서 구현 필요
- 예: R1의 L→P와 B→L 우선순위를 guards로 제어

---

## 구현 우선순위

### Phase 1: 기본 구조 변경
- [x] 문서 작성 (현재 문서)
- [ ] Parallel state JSON 정의 작성
- [ ] Guards 함수 구현
- [ ] Actions 함수 구현

### Phase 2: Forward Priority 구현
- [ ] 기존 CheckPriorities() 로직을 guards로 이전
- [ ] 우선순위 기반 조건 guards로 표현
- [ ] 테스트 및 검증

### Phase 3: Sync Mode 구현
- [ ] Sync Mode용 guards 추가
- [ ] 동기화 조건 guards로 표현
- [ ] 모드 전환 메커니즘 구현

### Phase 4: 최적화 및 확장
- [ ] Guards 성능 최적화
- [ ] 로깅 개선
- [ ] 상태 시각화 도구 연동

---

## 비교: Before vs After

### Before (제어문 기반)

```csharp
private void CheckPriorities(OrchestratedContext ctx)
{
    // Priority 1: C → B (R3)
    if (CanExecuteCtoB())
    {
        ExecuteCtoB(ctx);
        return;
    }

    // Priority 2: P → C (R2)
    if (CanExecutePtoC())
    {
        ExecutePtoC(ctx);
        return;
    }

    // ... more priorities
}
```

**특징:**
- 순차적 실행 (한 번에 하나만)
- 상태 추적 어려움
- 확장 시 코드 수정 필요

### After (Parallel State Machine 기반)

```json
{
  "type": "parallel",
  "states": {
    "r1Control": {
      "states": {
        "monitoring": {
          "on": {
            "STATION_STATUS": {
              "cond": "canExecuteLtoP",
              "target": "readyToSend"
            }
          }
        }
      }
    },
    "r2Control": { "..." },
    "r3Control": { "..." }
  }
}
```

**특징:**
- 병렬 실행 (모든 로봇 동시 체크)
- 상태 전환 명시적
- 확장 시 region 추가만

---

## 결론

Parallel State Machine 방식은 **복잡한 제어 로직을 선언적으로 표현**하고, **병렬 실행**을 가능하게 하며, **확장성과 테스트 용이성**을 크게 향상시킵니다.

다만, **학습 곡선**과 **초기 구현 복잡도**가 있으므로, 단계적으로 구현하는 것이 좋습니다:
1. 먼저 R1만 parallel state로 변경 (PoC)
2. R2, R3 추가
3. Forward Priority vs Sync Mode 분기
4. 최적화 및 확장

이 방식은 특히 **Sync Mode 구현에 매우 유리**하며, 각 로봇의 상태를 동시에 모니터링하고, 동기화 조건을 guards로 명확하게 표현할 수 있습니다.
