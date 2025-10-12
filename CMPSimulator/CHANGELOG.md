# CMP Simulator - 변경 사항

## 2025-01-XX - State Machine 단순화 및 로봇 로직 최적화

### 1. 클래스 이름 변경 (직관적인 네이밍)

#### 변경 전:
- `E157PolisherMachine` - E157 표준 기반의 복잡한 구현
- `ProcessingStationMachine` - 일반적인 프로세싱 스테이션 (Cleaner용으로 사용)

#### 변경 후:
- `PolisherMachine` - Polisher 전용 상태 머신
- `CleanerMachine` - Cleaner 전용 상태 머신

**파일:**
- `CMPSimulator/StateMachines/PolisherMachine.cs` (신규)
- `CMPSimulator/StateMachines/CleanerMachine.cs` (신규)
- `CMPSimulator/Controllers/OrchestratedForwardPriorityController.cs` (참조 업데이트)

---

### 2. 상태 머신 구조 단순화 및 통일

#### 통일된 상태 사이클:
```
empty → processing → done → idle → empty
```

#### 상태 설명:
- **empty**: 스테이션이 비어있음 (wafer 없음)
- **processing**: wafer 처리 중
- **done**: 처리 완료, wafer가 픽업 대기 중
- **idle**: 전환 상태 (1ms 후 empty로 복귀)

#### 이벤트:
- `PLACE`: 로봇이 wafer를 스테이션에 배치
- `PICK`: 로봇이 스테이션에서 wafer를 픽업

**적용 클래스:**
- `PolisherMachine.cs`
- `CleanerMachine.cs`

---

### 3. RobotMachine 이벤트 통일

#### 변경 전:
- Polisher: `LOAD_WAFER` / `UNLOAD_WAFER` (E157 이벤트)
- 기타 스테이션: `PLACE` / `PICK`

#### 변경 후:
- **모든 스테이션**: `PLACE` / `PICK` 통일

**파일:** `CMPSimulator/StateMachines/RobotMachine.cs`

**수정 위치:**
- `pickWafer` 액션 (line 149)
- `placeWafer` 액션 (line 191)

---

### 4. R2 Holding 로직 수정

#### 문제:
R2가 Cleaner에 wafer가 있는데도 ("done" 상태) 계속 Polisher에서 wafer를 가져와 충돌 발생

#### 해결:
R2는 Cleaner가 **진짜 empty 또는 idle 상태**가 될 때까지 holding 상태에서 대기

**파일:** `CMPSimulator/StateMachines/SchedulerMachine.cs`

**수정 위치:**
- `onRobotStatus` 액션 내 holding 로직 (lines 156-169)
- `waitingDestination` 로직 (lines 207-210)

```csharp
// R2는 destination이 empty/idle일 때만 진행
if (robot == "R3" || robot == "R2" || robot == "R1")
{
    destReady = (destState == "empty" || destState == "idle");
}
```

---

### 5. R3 전담 로봇 최적화

#### 문제:
R3는 C→B 전담 로봇인데, Buffer가 occupied이면 Cleaner에서 픽업조차 하지 않음

#### 해결:
- R3는 Cleaner가 "done" 상태면 **Buffer 상태와 무관하게 즉시 픽업**
- Buffer가 occupied이면 holding 상태에서 대기
- Buffer가 empty가 되면 배치

**파일:** `CMPSimulator/StateMachines/SchedulerMachine.cs`

**수정 위치:**
- `CanExecuteCtoB()` 메서드 (lines 307-314)

```csharp
// 변경 전
return GetStationState("cleaner") == "done" &&
       GetRobotState("R3") == "idle" &&
       GetStationState("buffer") == "empty";  // ← Buffer 체크 제거

// 변경 후
return GetStationState("cleaner") == "done" &&
       GetRobotState("R3") == "idle";
```

---

### 6. R1 B→L 우선순위 로직 수정

#### 문제:
Buffer가 occupied이고 R1이 idle인데도 R1이 B→L을 실행하지 않음

#### 원인:
`CanExecuteBtoL()`에서 pending wafer가 있으면 무조건 B→L을 스킵하는 잘못된 로직

#### 해결:
**실제로 L→P가 실행 가능한지** 체크하도록 수정 (Polisher가 available하지 않으면 B→L 실행)

**파일:** `CMPSimulator/StateMachines/SchedulerMachine.cs`

**수정 위치:**
- `CanExecuteBtoL()` 메서드 (lines 414-427)

```csharp
// 변경 전
bool canDoLtoP = _lPending.Count > 0 && GetRobotState("R1") == "idle";

// 변경 후
bool canActuallyDoLtoP = CanExecuteLtoP();  // 실제 실행 가능 여부 체크
```

---

### 7. R1 Destination-Specific 로직

#### L→P (Non-processed wafer):
- Polisher가 **empty/idle일 때만** 배치
- Polisher가 "done" 상태면 holding에서 대기

#### B→L (Processed wafer):
- LoadPort는 **항상 ready**
- 즉시 배치 (대기 없음)

**파일:** `CMPSimulator/StateMachines/SchedulerMachine.cs`

**수정 위치:**
- `onRobotStatus` 액션 내 holding 로직 (lines 151-175)
- `waitingDestination` 로직 (lines 196-216)

```csharp
// LoadPort는 항상 ready
if (waitingFor == "LoadPort")
{
    destReady = true;
}
else
{
    var destState = GetStationState(waitingFor);
    // R1은 Polisher가 empty/idle일 때만 진행
    if (robot == "R3" || robot == "R2" || robot == "R1")
    {
        destReady = (destState == "empty" || destState == "idle");
    }
}
```

---

## 요약

### 주요 개선 사항:

1. **직관적인 클래스 이름**: `PolisherMachine`, `CleanerMachine`
2. **통일된 상태 사이클**: empty → processing → done → idle → empty
3. **통일된 이벤트**: 모든 스테이션에 `PLACE` / `PICK` 사용
4. **R2 wafer 충돌 방지**: Cleaner가 empty/idle까지 대기
5. **R3 전담 로봇 최적화**: Buffer 상태와 무관하게 Cleaner 즉시 픽업
6. **R1 B→L 로직 수정**: Polisher가 available하지 않을 때 Buffer 비우기
7. **R1 destination별 로직**: L→P는 대기, B→L은 즉시

### 빌드 방법:

1. CMPSimulator.exe 종료
2. `dotnet build "C:\Develop25\XStateNet\CMPSimulator\CMPSimulator.csproj"`
3. CMPSimulator.exe 실행

### 테스트 확인 사항:

- [ ] R2가 Cleaner가 empty가 될 때까지 holding에서 대기
- [ ] R3가 Cleaner done이면 Buffer 상태와 무관하게 즉시 픽업
- [ ] R1이 Buffer occupied이고 Polisher 사용 중일 때 B→L 실행
- [ ] R1이 LoadPort로 갈 때 즉시 배치 (대기 없음)
- [ ] 모든 25개 wafer가 정상적으로 처리 완료
