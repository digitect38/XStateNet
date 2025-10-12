# SEMI E157 기반 CMP 시뮬레이터 아키텍처

## 문서 개요

본 문서는 SEMI E157 (Equipment State Model) 표준을 기반으로 구현된 CMP (Chemical Mechanical Polishing) 시뮬레이터의 아키텍처를 설명합니다.

---

## 1. SEMI 표준 개요

### 1.1 SEMI E157 - Equipment State Model

SEMI E157은 반도체 제조 장비의 표준 상태 모델을 정의합니다.

**주요 상태 (E157 States):**
- **IDLE**: 장비가 전원이 켜져 있고 준비된 상태
- **SETUP**: 장비가 프로세스를 위해 설정/구성되는 상태
- **READY**: 프로세스를 시작할 준비가 완료된 상태
- **EXECUTING**: 실제 프로세스가 진행 중인 상태
- **PAUSED**: 프로세스가 일시 정지된 상태
- **COMPLETE**: 프로세스가 완료되어 자재 제거 대기 중인 상태

### 1.2 SEMI E30 - GEM (Generic Equipment Model)

GEM 표준은 장비 간 통신 및 제어를 위한 표준 인터페이스를 정의합니다.

**핵심 개념:**
- **State-driven architecture**: 상태 기반 제어
- **Event-driven communication**: 이벤트 기반 통신
- **Centralized control**: 중앙 집중식 제어 (Host/Scheduler)

---

## 2. 시스템 컴포넌트 정의

### 2.1 LoadPort (L) - 자재 적재/하역 포트

**역할:**
- 웨이퍼의 초기 저장소 및 최종 반환 위치
- 25개 웨이퍼 슬롯 관리
- 프로세스 완료된 웨이퍼 수령

**상태:**
- `Pending`: 처리 대기 중인 웨이퍼 목록
- `Completed`: 처리 완료된 웨이퍼 목록

**주요 기능:**
- 웨이퍼 ID 추적
- 슬롯 위치 관리
- 완료 웨이퍼 표시 (시각적 피드백)

**SEMI 준수:**
- 단순 저장소 역할로 E157 상태 모델 미적용
- Passive 컴포넌트 (명령 수신만, 자체 결정 없음)

---

### 2.2 Polisher (P) - CMP 연마 장비

**역할:**
- 웨이퍼의 화학적-기계적 연마 수행
- E157 표준 상태 모델 준수
- 처리 시간: 3000ms (시뮬레이션)

**E157 상태 전이:**
```
IDLE → SETUP → READY → EXECUTING → COMPLETE → IDLE
         ↑                ↓
         └────── PAUSED ──┘
```

**XState 스크립트:**
```json
{
  "id": "polisher",
  "initial": "IDLE",
  "states": {
    "IDLE": {
      "entry": ["reportIdle"],
      "on": {
        "LOAD_WAFER": {
          "target": "SETUP",
          "actions": ["onLoadWafer"]
        }
      }
    },
    "SETUP": {
      "entry": ["reportSetup", "performSetup"],
      "invoke": {
        "src": "setupEquipment",
        "onDone": {
          "target": "READY",
          "actions": ["onSetupComplete"]
        },
        "onError": {
          "target": "IDLE",
          "actions": ["onSetupError"]
        }
      }
    },
    "READY": {
      "entry": ["reportReady", "autoStartProcessing"],
      "on": {
        "START_PROCESS": {
          "target": "EXECUTING",
          "actions": ["onStartProcess"]
        },
        "CANCEL": {
          "target": "IDLE",
          "actions": ["onCancel"]
        }
      }
    },
    "EXECUTING": {
      "entry": ["reportExecuting", "startProcessing"],
      "on": {
        "PAUSE": {
          "target": "PAUSED",
          "actions": ["onPause"]
        },
        "ABORT": {
          "target": "IDLE",
          "actions": ["onAbort"]
        }
      },
      "invoke": {
        "src": "processWafer",
        "onDone": {
          "target": "COMPLETE",
          "actions": ["onProcessComplete"]
        },
        "onError": {
          "target": "IDLE",
          "actions": ["onProcessError"]
        }
      }
    },
    "PAUSED": {
      "entry": ["reportPaused"],
      "on": {
        "RESUME": {
          "target": "EXECUTING",
          "actions": ["onResume"]
        },
        "ABORT": {
          "target": "IDLE",
          "actions": ["onAbort"]
        }
      }
    },
    "COMPLETE": {
      "entry": ["reportComplete"],
      "on": {
        "UNLOAD_WAFER": {
          "target": "IDLE",
          "actions": ["onUnloadWafer"]
        }
      }
    }
  }
}
```

**상태별 동작:**
1. **IDLE**
   - 전원 ON, 준비 상태
   - `LOAD_WAFER` 이벤트 대기

2. **SETUP**
   - 레시피 로딩, 파라미터 구성
   - 장비 캘리브레이션
   - 시간: ~200ms

3. **READY**
   - 프로세스 시작 준비 완료
   - Auto-start: `START_PROCESS` 자동 발송

4. **EXECUTING**
   - 실제 CMP 연마 진행
   - `PAUSE`, `ABORT` 이벤트 수신 가능
   - 시간: 3000ms

5. **PAUSED**
   - 일시 정지 상태
   - `RESUME` 또는 `ABORT` 대기

6. **COMPLETE**
   - 연마 완료
   - `UNLOAD_WAFER` 이벤트 대기

**Status Reporting:**
```json
{
  "station": "polisher",
  "state": "EXECUTING",
  "e157State": "EXECUTING",
  "wafer": 1,
  "timestamp": "2025-10-12T13:00:00Z",
  "processingStartTime": "2025-10-12T12:59:57Z"
}
```

**SEMI 준수:**
- ✅ SEMI E157 완전 준수
- ✅ 모든 표준 상태 구현
- ✅ Setup/Ready 단계 포함
- ✅ Pause/Resume 기능

---

### 2.3 Cleaner (C) - 세정 장비

**역할:**
- 연마 후 웨이퍼 세정
- 잔류 슬러리 및 파티클 제거
- 처리 시간: 3000ms

**상태 모델:**
- 단순화된 상태 모델 사용 (비-E157)
- 상태: `empty` → `idle` → `processing` → `done`

**상태 전이:**
```
empty → idle → processing → done → empty
```

**XState 스크립트:**
```json
{
  "id": "cleaner",
  "initial": "empty",
  "states": {
    "empty": {
      "entry": ["reportEmpty"],
      "on": {
        "PLACE": {
          "target": "idle",
          "actions": ["onPlace"]
        }
      }
    },
    "idle": {
      "entry": ["reportIdle", "autoStart"],
      "on": {
        "START": {
          "target": "processing",
          "actions": ["onStart"]
        }
      }
    },
    "processing": {
      "entry": ["reportProcessing"],
      "invoke": {
        "src": "processWafer",
        "onDone": {
          "target": "done",
          "actions": ["onProcessComplete"]
        }
      }
    },
    "done": {
      "entry": ["reportDone"],
      "on": {
        "PICK": {
          "target": "empty",
          "actions": ["onPick"]
        }
      }
    }
  }
}
```

**Status Reporting:**
```json
{
  "station": "cleaner",
  "state": "processing",
  "wafer": 1
}
```

**향후 개선:**
- E157 표준 적용 가능
- Setup/Ready 단계 추가
- Pause/Resume 기능 추가

---

### 2.4 Buffer (B) - 임시 저장소

**역할:**
- 세정 완료 웨이퍼 임시 저장
- LoadPort 복귀 대기
- 단순 저장소 (처리 없음)

**상태:**
- `empty`: 빈 상태
- `occupied`: 웨이퍼 보관 중

**상태 전이:**
```
empty ⇄ occupied
```

**XState 스크립트:**
```json
{
  "id": "buffer",
  "initial": "empty",
  "states": {
    "empty": {
      "entry": ["reportEmpty"],
      "on": {
        "PLACE": {
          "target": "occupied",
          "actions": ["onPlace"]
        }
      }
    },
    "occupied": {
      "entry": ["reportOccupied"],
      "on": {
        "PICK": {
          "target": "empty",
          "actions": ["onPick"]
        }
      }
    }
  }
}
```

**SEMI 준수:**
- 단순 저장소로 E157 미적용
- Passive 컴포넌트

---

### 2.5 Robot R1 - Transfer Robot (LoadPort ↔ Polisher)

**역할:**
- LoadPort → Polisher (P3 우선순위)
- Buffer → LoadPort (P4 우선순위)
- 양방향 전송 담당

**상태 모델:**
```
idle → pickingUp → holding → placingDown → returning → idle
                      ↓
                waitingDestination (조건부)
```

**XState 스크립트:**
```json
{
  "id": "R1",
  "initial": "idle",
  "states": {
    "idle": {
      "entry": ["reportIdle"],
      "on": {
        "TRANSFER": {
          "target": "pickingUp",
          "actions": ["storeTransferInfo"]
        }
      }
    },
    "pickingUp": {
      "entry": ["logPickingUp"],
      "invoke": {
        "src": "moveToPickup",
        "onDone": {
          "target": "holding",
          "actions": ["pickWafer"]
        }
      }
    },
    "holding": {
      "entry": ["reportHolding", "logHolding"],
      "on": {
        "DESTINATION_READY": {
          "target": "placingDown"
        }
      }
    },
    "waitingDestination": {
      "entry": ["logWaitingDestination"],
      "on": {
        "DESTINATION_READY": {
          "target": "placingDown"
        }
      }
    },
    "placingDown": {
      "entry": ["logPlacingDown"],
      "invoke": {
        "src": "moveToPlace",
        "onDone": {
          "target": "returning",
          "actions": ["placeWafer"]
        }
      }
    },
    "returning": {
      "entry": ["logReturning"],
      "invoke": {
        "src": "returnToIdle",
        "onDone": {
          "target": "idle",
          "actions": ["completeTransfer"]
        }
      }
    }
  }
}
```

**상태별 동작:**

1. **idle**
   - 대기 상태
   - Scheduler로부터 `TRANSFER` 명령 대기

2. **pickingUp**
   - 소스 위치로 이동
   - 웨이퍼 픽업 수행
   - 시간: 800ms
   - 완료 시 소스에 `PICK` 또는 `UNLOAD_WAFER` 이벤트 전송

3. **holding**
   - 웨이퍼 보유 상태
   - Scheduler에 상태 + destination 정보 보고
   - Scheduler의 `DESTINATION_READY` 이벤트 대기
   - **중요**: Robot은 destination 상태를 직접 체크하지 않음

4. **waitingDestination**
   - Destination이 준비되지 않은 경우
   - Scheduler가 destination 상태 모니터링
   - 준비 완료 시 `DESTINATION_READY` 수신

5. **placingDown**
   - 목적지로 이동
   - 웨이퍼 배치
   - 시간: 800ms
   - 완료 시 목적지에 `PLACE` 또는 `LOAD_WAFER` 이벤트 전송

6. **returning**
   - 홈 포지션으로 복귀
   - 시간: 400ms

**Event Routing Intelligence:**
```csharp
// E157 장비는 LOAD_WAFER/UNLOAD_WAFER 사용
// 기타 장비는 PLACE/PICK 사용
var placeEvent = (_placeTo == "polisher") ? "LOAD_WAFER" : "PLACE";
var pickEvent = (_pickFrom == "polisher") ? "UNLOAD_WAFER" : "PICK";
```

**Status Reporting:**
```json
{
  "robot": "R1",
  "state": "holding",
  "wafer": 1,
  "waitingFor": "polisher"  // Scheduler에게 의도 전달
}
```

**SEMI 준수:**
- ✅ State-driven operation
- ✅ Event-driven communication
- ✅ No autonomous decision making
- ✅ Centralized control by Scheduler

---

### 2.6 Robot R2 - Transfer Robot (Polisher ↔ Cleaner)

**역할:**
- Polisher → Cleaner 전송 전담 (P2 우선순위)
- 단방향 전송

**상태 모델:**
- R1과 동일한 상태 모델
- 더 단순한 전송 경로 (단방향)

**XState 스크립트:**
```json
{
  "id": "R2",
  "initial": "idle",
  "states": {
    "idle": {
      "entry": ["reportIdle"],
      "on": {
        "TRANSFER": {
          "target": "pickingUp",
          "actions": ["storeTransferInfo"]
        }
      }
    },
    "pickingUp": {
      "entry": ["logPickingUp"],
      "invoke": {
        "src": "moveToPickup",
        "onDone": {
          "target": "holding",
          "actions": ["pickWafer"]
        }
      }
    },
    "holding": {
      "entry": ["reportHolding", "logHolding"],
      "on": {
        "DESTINATION_READY": {
          "target": "placingDown"
        }
      }
    },
    "waitingDestination": {
      "entry": ["logWaitingDestination"],
      "on": {
        "DESTINATION_READY": {
          "target": "placingDown"
        }
      }
    },
    "placingDown": {
      "entry": ["logPlacingDown"],
      "invoke": {
        "src": "moveToPlace",
        "onDone": {
          "target": "returning",
          "actions": ["placeWafer"]
        }
      }
    },
    "returning": {
      "entry": ["logReturning"],
      "invoke": {
        "src": "returnToIdle",
        "onDone": {
          "target": "idle",
          "actions": ["completeTransfer"]
        }
      }
    }
  }
}
```

**특이사항:**
- Polisher COMPLETE 상태 감지
- Cleaner 가용성 확인 (Scheduler가 수행)

---

### 2.7 Robot R3 - Transfer Robot (Cleaner → Buffer)

**역할:**
- Cleaner → Buffer 전송 전담 (P1 우선순위)
- 단방향 전송
- 최고 우선순위 작업

**상태 모델:**
- R1, R2와 동일
- 가장 빈번하게 동작 (P1 우선순위)

**XState 스크립트:**
```json
{
  "id": "R3",
  "initial": "idle",
  "states": {
    "idle": {
      "entry": ["reportIdle"],
      "on": {
        "TRANSFER": {
          "target": "pickingUp",
          "actions": ["storeTransferInfo"]
        }
      }
    },
    "pickingUp": {
      "entry": ["logPickingUp"],
      "invoke": {
        "src": "moveToPickup",
        "onDone": {
          "target": "holding",
          "actions": ["pickWafer"]
        }
      }
    },
    "holding": {
      "entry": ["reportHolding", "logHolding"],
      "on": {
        "DESTINATION_READY": {
          "target": "placingDown"
        }
      }
    },
    "waitingDestination": {
      "entry": ["logWaitingDestination"],
      "on": {
        "DESTINATION_READY": {
          "target": "placingDown"
        }
      }
    },
    "placingDown": {
      "entry": ["logPlacingDown"],
      "invoke": {
        "src": "moveToPlace",
        "onDone": {
          "target": "returning",
          "actions": ["placeWafer"]
        }
      }
    },
    "returning": {
      "entry": ["logReturning"],
      "invoke": {
        "src": "returnToIdle",
        "onDone": {
          "target": "idle",
          "actions": ["completeTransfer"]
        }
      }
    }
  }
}
```

---

### 2.8 Scheduler (S) - 중앙 제어 시스템

**역할:**
- **모든 의사결정의 중심**
- 모든 장비 및 로봇 상태 추적
- Forward Priority 스케줄링 알고리즘 실행
- Robot 전송 명령 발행
- Destination readiness 판단

**XState 스크립트:**
```json
{
  "id": "scheduler",
  "initial": "running",
  "states": {
    "running": {
      "entry": ["reportRunning"],
      "on": {
        "STATION_STATUS": {
          "actions": ["onStationStatus"]
        },
        "ROBOT_STATUS": {
          "actions": ["onRobotStatus"]
        }
      }
    }
  }
}
```

**상태 추적:**
```csharp
// Station states
Dictionary<string, string> _stationStates;
Dictionary<string, int?> _stationWafers;

// Robot states
Dictionary<string, string> _robotStates;
Dictionary<string, int?> _robotWafers;
Dictionary<string, string> _robotWaitingFor;

// LoadPort queue
List<int> _lPending;    // 처리 대기 웨이퍼
List<int> _lCompleted;  // 처리 완료 웨이퍼
```

**이벤트 처리:**

1. **STATION_STATUS 수신**
   ```csharp
   {
     "station": "polisher",
     "state": "IDLE",
     "wafer": null
   }
   ```
   - Station 상태 업데이트
   - 대기 중인 Robot이 있는지 확인 (`CheckWaitingRobots`)
   - 우선순위 체크 (`CheckPriorities`)

2. **ROBOT_STATUS 수신**
   ```csharp
   {
     "robot": "R1",
     "state": "holding",
     "wafer": 1,
     "waitingFor": "polisher"
   }
   ```
   - Robot 상태 업데이트
   - `holding` 상태인 경우:
     - Destination 상태 즉시 확인
     - 준비되었으면 `DESTINATION_READY` 전송
     - 아니면 대기 목록에 추가
   - `idle` 상태인 경우:
     - 우선순위 체크하여 다음 작업 할당

**Forward Priority 알고리즘:**

우선순위 순서 (높음 → 낮음):
```
P1: Cleaner → Buffer (R3)
P2: Polisher → Cleaner (R2)
P3: LoadPort → Polisher (R1)
P4: Buffer → LoadPort (R1)
```

**Priority 1: C→B (Cleaner to Buffer)**
```csharp
Condition:
- Cleaner state == "done"
- R3 state == "idle"
- Buffer state == "empty"

Action:
- Send TRANSFER(R3, cleaner → buffer)
```

**Priority 2: P→C (Polisher to Cleaner)**
```csharp
Condition:
- Polisher state == "COMPLETE"  // E157
- R2 state == "idle"
- Cleaner available (empty or will be empty when R3 moves)

Action:
- Send TRANSFER(R2, polisher → cleaner)
```

**Priority 3: L→P (LoadPort to Polisher)**
```csharp
Condition:
- Pending wafers > 0
- R1 state == "idle"
- Polisher available ("IDLE" or will be "IDLE" when R2 moves)

Action:
- wafer = _lPending.RemoveFirst()
- Send TRANSFER(R1, LoadPort → polisher)
```

**Priority 4: B→L (Buffer to LoadPort)**
```csharp
Condition:
- Buffer state == "occupied"
- R1 state == "idle"
- No L→P work available

Action:
- Send TRANSFER(R1, buffer → LoadPort)
- Mark wafer as completed
- Check if all 25 wafers completed
```

**Destination Readiness 판단:**

```csharp
// Robot이 holding 상태가 되면
if (robotState == "holding" && waitingFor != null)
{
    var destState = GetStationState(waitingFor);

    // E157 및 표준 상태 모두 지원
    bool ready = (destState == "empty" || destState == "IDLE" ||
                  destState == "done" || destState == "COMPLETE");

    if (ready)
        SendEvent(robot, "DESTINATION_READY");
    else
        AddToWaitingList(robot, waitingFor);
}

// Station 상태 변경 시
if (stationState == "empty" || stationState == "IDLE")
{
    // 이 station을 기다리는 robot 확인
    foreach (var waitingRobot in GetWaitingRobots(station))
    {
        SendEvent(waitingRobot, "DESTINATION_READY");
    }
}
```

**SEMI 준수:**
- ✅ SEMI E30 GEM - Centralized control
- ✅ Host 역할 수행 (Equipment Controller)
- ✅ 모든 상태 추적 및 의사결정
- ✅ Event-driven architecture
- ✅ E157 및 비-E157 장비 모두 지원

---

## 3. 컴포넌트 간 협업 시나리오

### 3.1 정상 프로세스 플로우 (Single Wafer)

```
시간    컴포넌트        이벤트/상태                                      Scheduler 동작
────────────────────────────────────────────────────────────────────────────────────
T0      LoadPort        웨이퍼 1 대기 중                                 -
        Polisher        IDLE (E157)                                     -
        Cleaner         empty                                           -
        Buffer          empty                                           -
        R1,R2,R3        idle                                           -

T1      Scheduler       모든 상태 수신 완료                              CheckPriorities()
                                                                        → P3 조건 충족

T2      R1              TRANSFER 명령 수신                              Send TRANSFER(R1, L→P)
                        → pickingUp 전이

T3      R1              소스 도착, 픽업 완료                             -
                        → holding (waitingFor: polisher)

T4      Scheduler       ROBOT_STATUS 수신                               Polisher IDLE 확인
                        (R1 holding, wafer:1, waitingFor:polisher)     → Send DESTINATION_READY(R1)

T5      R1              DESTINATION_READY 수신                          -
                        → placingDown 전이

T6      Polisher        LOAD_WAFER 수신                                -
                        IDLE → SETUP (200ms)

T7      Polisher        SETUP → READY                                  -
                        Auto-start: START_PROCESS

T8      Polisher        READY → EXECUTING                              -
                        Processing 시작 (3000ms)

T9      Scheduler       STATION_STATUS 수신                            State updated
                        (polisher: EXECUTING, wafer:1)

T10     R1              웨이퍼 배치 완료                                 -
                        → returning → idle

T11     Scheduler       ROBOT_STATUS 수신 (R1: idle)                   CheckPriorities()
                                                                        → P3 조건 충족 (wafer 2)

...     (3000ms 경과)

T12     Polisher        Processing 완료                                 -
                        EXECUTING → COMPLETE

T13     Scheduler       STATION_STATUS 수신                            CheckPriorities()
                        (polisher: COMPLETE, wafer:1)                  → P2 조건 충족
                                                                        → Send TRANSFER(R2, P→C)

T14     R2              TRANSFER 명령 수신                              -
                        → pickingUp

T15     R2              Polisher 도착, 픽업                             -
                        → holding (waitingFor: cleaner)

T16     Polisher        UNLOAD_WAFER 수신                              -
                        COMPLETE → IDLE

T17     Scheduler       ROBOT_STATUS (R2: holding)                     Cleaner empty 확인
                        STATION_STATUS (polisher: IDLE)                → Send DESTINATION_READY(R2)
                                                                        → P3 조건 충족 (next wafer)

T18     R2              placingDown → Cleaner                          -
        R1              TRANSFER (LoadPort → Polisher)                 병렬 동작

T19     Cleaner         PLACE 수신                                      -
                        empty → idle → processing (3000ms)

...     (반복)
```

### 3.2 Waiting Scenario (Destination Not Ready)

```
상황: R2가 Polisher에서 픽업 완료했지만 Cleaner가 아직 processing 중

T1      R2              Polisher 픽업 완료                              -
                        → holding (waitingFor: cleaner)

T2      Scheduler       ROBOT_STATUS 수신                               Cleaner 상태 확인
                        (R2 holding, waitingFor: cleaner)              → Cleaner: processing (busy)
                                                                        → AddToWaitingList(R2, cleaner)
                                                                        → Log: "Destination not ready"

T3      R2              DESTINATION_READY 미수신                        -
                        holding 상태 유지

...     (Cleaner processing 계속)

T4      Cleaner         Processing 완료                                 -
                        processing → done

T5      Scheduler       STATION_STATUS 수신                            CheckWaitingRobots(cleaner)
                        (cleaner: done)                                → R2가 대기 중임을 확인
                                                                        → Send DESTINATION_READY(R2)

T6      R2              DESTINATION_READY 수신                         -
                        holding → placingDown
```

**핵심 포인트:**
- ❌ Robot이 destination 상태를 직접 체크하지 않음
- ✅ Scheduler가 모든 상태를 추적하고 판단
- ✅ Scheduler가 적절한 타이밍에 DESTINATION_READY 전송

### 3.3 Priority-based Scheduling

```
상황: 여러 작업이 동시에 가능한 경우

현재 상태:
- Cleaner: done (wafer 1)
- Polisher: COMPLETE (wafer 2)
- LoadPort: wafers 3-25 pending
- Buffer: empty
- R1, R2, R3: all idle

Scheduler 우선순위 체크:

1. P1: C→B 체크
   ✅ Cleaner done
   ✅ R3 idle
   ✅ Buffer empty
   → P1 조건 충족!
   → Send TRANSFER(R3, cleaner → buffer)
   → 다른 우선순위 체크 안함 (P1 실행)

다음 사이클 (R3가 이동 후):

현재 상태:
- Cleaner: empty (R3가 픽업함)
- Polisher: COMPLETE (wafer 2)
- R3: pickingUp
- R1, R2: idle

Scheduler 우선순위 체크:

1. P1: C→B 체크
   ❌ Cleaner not done

2. P2: P→C 체크
   ✅ Polisher COMPLETE
   ✅ R2 idle
   ✅ Cleaner empty
   → P2 조건 충족!
   → Send TRANSFER(R2, polisher → cleaner)

다음 사이클 (R2가 이동 후):

현재 상태:
- Polisher: IDLE (R2가 픽업함)
- R2: pickingUp
- R1: idle

Scheduler 우선순위 체크:

1. P1: C→B 체크 ❌
2. P2: P→C 체크 ❌
3. P3: L→P 체크
   ✅ Pending > 0 (wafers 3-25)
   ✅ R1 idle
   ✅ Polisher IDLE
   → P3 조건 충족!
   → Send TRANSFER(R1, LoadPort → polisher, wafer 3)
```

**핵심 포인트:**
- Scheduler가 우선순위 순서대로 조건 체크
- 높은 우선순위 조건 충족 시 즉시 실행, 하위 우선순위 체크 안함
- 각 상태 변화마다 `CheckPriorities()` 재실행
- 최적의 throughput 보장

---

## 4. 이벤트 통신 프로토콜

### 4.1 Destination Readiness Protocol

**현재 구현 (2-way protocol):**
```
┌────────┐           ┌───────────┐           ┌──────────┐
│   R1   │           │ Scheduler │           │ Polisher │
└───┬────┘           └─────┬─────┘           └────┬─────┘
    │                      │                      │
    │ 1. ROBOT_STATUS      │                      │
    │    (holding,         │                      │
    │     waitingFor:P)    │                      │
    ├─────────────────────>│                      │
    │                      │                      │
    │                      │ 2. Check P state     │
    │                      │    (internal)        │
    │                      │──┐                   │
    │                      │  │ Is P IDLE?        │
    │                      │<─┘                   │
    │                      │                      │
    │ 3. DESTINATION_READY │                      │
    │    (if P is IDLE)    │                      │
    │<─────────────────────┤                      │
    │                      │                      │
    │ 4. LOAD_WAFER        │                      │
    │──────────────────────┼─────────────────────>│
    │                      │                      │
```

**특징:**
- ✅ 단순하고 빠름
- ✅ Scheduler가 모든 상태를 추적하므로 신뢰 가능
- ✅ E157 상태 모델과 호환 (IDLE = ready)
- ⚠️ Station의 명시적 acknowledgement 없음

**향후 개선 (3-way handshake - 선택적):**
```
┌────────┐           ┌───────────┐           ┌──────────┐
│   R1   │           │ Scheduler │           │ Polisher │
└───┬────┘           └─────┬─────┘           └────┬─────┘
    │                      │                      │
    │ 1. ROBOT_STATUS      │                      │
    │    (holding,         │                      │
    │     waitingFor:P)    │                      │
    ├─────────────────────>│                      │
    │                      │                      │
    │                      │ 2. REQUEST_LOAD_ACK  │
    │                      │      (optional)      │
    │                      ├─────────────────────>│
    │                      │                      │
    │                      │ 3. LOAD_ACK          │
    │                      │    (ready/busy)      │
    │                      │<─────────────────────┤
    │                      │                      │
    │ 4. DESTINATION_READY │                      │
    │    (if ACK = ready)  │                      │
    │<─────────────────────┤                      │
    │                      │                      │
    │ 5. LOAD_WAFER        │                      │
    │──────────────────────┼─────────────────────>│
    │                      │                      │
```

**언제 3-way handshake가 필요한가:**
- 🔧 Equipment가 maintenance 모드일 때
- 🔧 Equipment가 error 상태일 때
- 🔧 Equipment가 다른 internal process 진행 중일 때
- 🔧 복잡한 multi-chamber equipment
- 🔧 실제 SECS/GEM 구현 시

**현재 시뮬레이터의 판단:**
- ✅ **2-way protocol이 충분함**
- E157 상태 모델이 명확하게 정의되어 있음 (IDLE = ready to receive)
- Scheduler가 모든 상태를 실시간으로 추적
- Polisher는 IDLE 상태에서만 LOAD_WAFER를 받을 수 있음
- 단순하고 효율적

### 4.2 Event Types

**Station → Scheduler (Status Reports)**

```typescript
STATION_STATUS {
  station: string,          // "polisher", "cleaner", "buffer"
  state: string,            // "IDLE", "EXECUTING", "COMPLETE" (E157)
                           // "empty", "idle", "processing", "done" (non-E157)
  e157State?: string,       // E157 장비만, 명시적 E157 상태
  wafer: number | null,     // 현재 처리 중인 웨이퍼 ID
  timestamp?: DateTime,     // 상태 변경 시각
  processingStartTime?: DateTime,  // 처리 시작 시각 (E157)
  processingDuration?: number      // 처리 소요 시간 (E157)
}
```

**Robot → Scheduler (Status Reports)**

```typescript
ROBOT_STATUS {
  robot: string,            // "R1", "R2", "R3"
  state: string,            // "idle", "pickingUp", "holding",
                           // "waitingDestination", "placingDown", "returning"
  wafer: number | null,     // 현재 보유 웨이퍼 ID
  waitingFor?: string       // 대기 중인 destination station
}
```

**Scheduler → Robot (Commands)**

```typescript
TRANSFER {
  waferId: number,          // 전송할 웨이퍼 ID
  from: string,             // 소스 위치
  to: string                // 목적지 위치
}

DESTINATION_READY {
  // No payload, signal only
}
```

**Robot → Station (Control)**

```typescript
// E157 Equipment
LOAD_WAFER {
  wafer: number
}

UNLOAD_WAFER {
  wafer: number
}

// Non-E157 Equipment
PLACE {
  wafer: number
}

PICK {
  wafer: number
}
```

**E157 Equipment Internal Events**

```typescript
START_PROCESS    // READY → EXECUTING (auto-triggered)
PAUSE            // EXECUTING → PAUSED
RESUME           // PAUSED → EXECUTING
ABORT            // Any → IDLE (error handling)
CANCEL           // READY → IDLE
```

### 4.2 Event Flow Diagram

```
┌─────────────┐
│  LoadPort   │
│   (L)       │
└──────┬──────┘
       │
       │ P3: TRANSFER(L→P)
       ↓
┌─────────────┐         ┌──────────────┐
│   Robot R1  │────────→│  Scheduler   │
│             │  STATUS │    (S)       │
│             │←────────│              │
└──────┬──────┘ TRANSFER └──────┬───────┘
       │                        │
       │ LOAD_WAFER             │ P3,P2,P1 Priority
       ↓                        │ Checking
┌─────────────┐                 │
│  Polisher   │                 │
│   (P-E157)  │                 │
│  SETUP→     │                 │
│  EXECUTING  │                 │
│  COMPLETE   │─────STATUS─────→│
└──────┬──────┘                 │
       │                        │
       │ P2: TRANSFER(P→C)      │
       ↓                        ↓
┌─────────────┐         ┌──────────────┐
│  Robot R2   │←────────│              │
└──────┬──────┘         │              │
       │                │              │
       │ PLACE          │              │
       ↓                │              │
┌─────────────┐         │              │
│   Cleaner   │────────→│              │
│    (C)      │  STATUS │              │
└──────┬──────┘         │              │
       │                │              │
       │ P1: TRANSFER   │              │
       ↓                │              │
┌─────────────┐         │              │
│  Robot R3   │←────────┘              │
└──────┬──────┘                        │
       │                               │
       │ PLACE                         │
       ↓                               │
┌─────────────┐                        │
│   Buffer    │───────STATUS──────────→│
│    (B)      │                        │
└──────┬──────┘                        │
       │                               │
       │ P4: TRANSFER(B→L)             │
       ↓                               │
┌─────────────┐                        │
│  Robot R1   │←───────────────────────┘
│  (재사용)    │
└──────┬──────┘
       │
       │ PLACE
       ↓
┌─────────────┐
│  LoadPort   │
│  (Complete) │
└─────────────┘
```

---

## 5. SEMI 표준 준수사항

### 5.1 SEMI E157 준수 (Polisher)

✅ **모든 E157 상태 구현**
- IDLE, SETUP, READY, EXECUTING, PAUSED, COMPLETE

✅ **Setup Phase 포함**
- 장비 구성 및 레시피 로딩 시뮬레이션

✅ **Pause/Resume 지원**
- 프로세스 일시 정지/재개 기능

✅ **상태별 Entry Actions**
- 각 상태 진입 시 적절한 로깅 및 보고

✅ **Timestamp 추적**
- processingStartTime, processingDuration

### 5.2 SEMI E30 GEM 준수

✅ **Centralized Control**
- Scheduler가 모든 의사결정 담당
- Equipment는 상태 보고만 수행

✅ **Event-driven Communication**
- 폴링 없음, 순수 이벤트 기반
- Pub/Sub 패턴 사용

✅ **State-based Operation**
- 모든 동작이 상태 전이로 표현
- 명확한 상태 머신 정의

✅ **Host Control**
- Scheduler = Host 역할
- Equipment = Controlled Entity

### 5.3 추가 SEMI 권장사항

✅ **Equipment State Tracking**
- 모든 장비 상태의 중앙 집중식 추적

✅ **Material Tracking**
- 웨이퍼 ID 기반 위치 추적
- 완료 상태 추적

✅ **Event Logging**
- 모든 상태 전이 및 이벤트 로깅
- 타임스탬프 포함

✅ **Error Handling**
- ABORT, CANCEL 이벤트 지원
- Setup 실패 처리

---

## 6. 아키텍처 원칙

### 6.1 책임 분리 (Separation of Concerns)

**Equipment (Station/Robot)**
- ❌ 의사결정 금지
- ✅ 상태 보고만 수행
- ✅ 명령 수행만 수행
- ✅ 자체 상태 관리

**Scheduler**
- ✅ 모든 의사결정 담당
- ✅ 전체 시스템 상태 추적
- ✅ 우선순위 기반 스케줄링
- ✅ Destination readiness 판단

### 6.2 통신 방향

```
단방향 상태 보고:
Station → Scheduler
Robot → Scheduler

단방향 명령:
Scheduler → Robot
Robot → Station (placement/pickup)

금지:
Station ↔ Station (직접 통신 금지)
Robot ↔ Robot (직접 통신 금지)
Station → Robot (명령 금지)
Robot → Scheduler (명령 금지, 상태만 보고)
```

### 6.3 Event-driven Architecture

✅ **No Polling**
- 타이머 기반 폴링 없음
- 모든 동작이 이벤트로 촉발

✅ **Reactive**
- 상태 변화에 즉시 반응
- `CheckPriorities()` 자동 실행

✅ **Asynchronous**
- EventBusOrchestrator를 통한 비동기 통신
- Deferred sends로 데드락 방지

### 6.4 Scalability

**확장 가능한 설계:**
- 새로운 Station 추가 용이
- 새로운 Robot 추가 용이
- 새로운 우선순위 규칙 추가 가능
- E157 및 비-E157 장비 혼재 지원

---

## 7. 향후 개선 방향

### 7.1 Full E157 Compliance

**목표:** 모든 장비에 E157 적용
- Cleaner → E157 상태 모델로 전환
- Buffer → E157 적용 검토
- LoadPort → E157 적용 검토

### 7.2 SEMI E40 (PJM - Process Job Management)

**목표:** 프로세스 작업 관리 표준화
- ProcessJob 개념 도입
- 웨이퍼 → Job → Recipe 매핑
- Job 상태 추적

### 7.3 SEMI E87 (CMS - Carrier Management System)

**목표:** 캐리어 관리 표준화
- FOUP (Front Opening Unified Pod) 모델링
- LoadPort ↔ FOUP 인터페이스
- Carrier ID 추적

### 7.4 SEMI E90 (Substrate Tracking)

**목표:** 웨이퍼 추적 강화
- 웨이퍼 이력 추적
- 위치 이력 로깅
- 프로세스 이력 기록

### 7.5 SECS/GEM Communication

**목표:** 실제 SECS/GEM 프로토콜 구현
- HSMS (High-Speed SECS Message Services)
- SECS-II 메시지 포맷
- GEM300 (Equipment Self Description)

---

## 8. 코드 구조

### 8.1 Directory Structure

```
CMPSimulator/
├── StateMachines/
│   ├── SchedulerMachine.cs           # 중앙 제어
│   ├── E157PolisherMachine.cs        # E157 준수 연마기
│   ├── ProcessingStationMachine.cs   # 일반 처리 장비 (Cleaner)
│   ├── BufferMachine.cs              # 버퍼
│   └── RobotMachine.cs               # 로봇 (R1, R2, R3)
│
├── Controllers/
│   └── OrchestratedForwardPriorityController.cs  # UI 컨트롤러
│
├── Models/
│   ├── Wafer.cs                      # 웨이퍼 모델
│   └── StationPosition.cs            # 위치 정보
│
└── SEMI_E157_ARCHITECTURE.md         # 본 문서
```

### 8.2 Key Classes

**SchedulerMachine**
- Forward Priority 알고리즘 구현
- 모든 상태 추적
- 이벤트 핸들러 (`onStationStatus`, `onRobotStatus`)
- 우선순위 체크 (`CheckPriorities`)
- 대기 로봇 관리 (`CheckWaitingRobots`)

**E157PolisherMachine**
- SEMI E157 완전 구현
- 6개 상태 전이
- Setup/Ready 단계
- Pause/Resume 기능
- 상세한 상태 보고

**RobotMachine**
- 6개 상태 (idle, pickingUp, holding, waitingDestination, placingDown, returning)
- Scheduler 중심 제어
- Event routing intelligence (E157 vs non-E157)
- No autonomous decision making

---

## 9. 성능 및 메트릭

### 9.1 처리 시간

| 작업 | 시간 (ms) | 비고 |
|-----|----------|------|
| Polisher Setup | 200 | E157 구성 단계 |
| Polishing | 3000 | 실제 CMP 프로세스 |
| Cleaning | 3000 | 세정 |
| Robot Transfer | 800 | 픽업/배치 각각 |
| Robot Return | 400 | 홈 복귀 |

### 9.2 예상 Throughput

**25개 웨이퍼 처리 시간 (이론값):**
- 병렬 처리 고려
- Polisher가 병목 (3000ms + 200ms setup)
- 약 25 × 3.2초 = 80초 (최적 조건)

**실제 throughput:**
- Buffer 대기 시간
- Robot 이동 시간
- Destination waiting
- 약 90-100초 예상

---

## 10. 요약

### 10.1 핵심 설계 원칙

1. **SEMI E157 준수**: Polisher는 완전한 E157 구현
2. **중앙 집중식 제어**: Scheduler가 모든 의사결정
3. **Event-driven**: 폴링 없는 순수 이벤트 기반
4. **상태 기반**: 모든 동작이 명확한 상태 전이
5. **책임 분리**: Equipment는 보고만, Scheduler는 결정만

### 10.2 컴포넌트 역할 요약

| 컴포넌트 | 역할 | 상태 모델 | SEMI 준수 |
|---------|------|-----------|----------|
| LoadPort (L) | 웨이퍼 저장소 | Passive | N/A |
| Polisher (P) | CMP 연마 | E157 (6 states) | ✅ E157 |
| Cleaner (C) | 세정 | Simple (4 states) | Partial |
| Buffer (B) | 임시 저장 | Simple (2 states) | N/A |
| Robot R1 | L↔P, B↔L 전송 | 6 states | ✅ Event-driven |
| Robot R2 | P→C 전송 | 6 states | ✅ Event-driven |
| Robot R3 | C→B 전송 | 6 states | ✅ Event-driven |
| Scheduler (S) | 중앙 제어 | State tracker | ✅ GEM Host |

### 10.3 주요 혁신점

1. **E157 + Non-E157 혼재 지원**
   - Polisher: Full E157
   - Cleaner: Simple model
   - 동일 시스템에서 공존

2. **Scheduler 중심 아키텍처**
   - Robot이 destination 체크 안함
   - 모든 판단은 Scheduler
   - 진정한 중앙 집중식 제어

3. **Event Routing Intelligence**
   - E157 장비: LOAD_WAFER/UNLOAD_WAFER
   - 일반 장비: PLACE/PICK
   - Robot이 자동으로 올바른 이벤트 선택

4. **Priority-based Scheduling**
   - Forward Priority 알고리즘
   - 동적 우선순위 평가
   - 최적 throughput

---

## 11. 참고 문헌

- SEMI E157: Specification for Equipment State Machine Behavior
- SEMI E30: Generic Model for Communications and Control of Manufacturing Equipment (GEM)
- SEMI E40: Standard for Processing Management
- SEMI E87: Specification for Carrier Management (CMS)
- SEMI E90: Specification for Substrate Tracking

---

**문서 버전:** 1.0
**작성일:** 2025-10-12
**작성자:** XStateNet CMP Simulator Team
