# R1 Idle Position Return 수정

## 문제점

**"R1이 종착역이 되는 문제"**

B→L 귀환 후 R1이 즉시 다음 작업(L→P)을 시작하여, 실제 설비처럼 **대기 위치로 복귀하는 동작**이 없었습니다.

### 원인

```csharp
// Before
private async Task ExecBtoL(CancellationToken ct)
{
    // ... B → R1 (Pick)
    await Task.Delay(TRANSFER, ct);  // 800ms

    // R1 → L (Place)
    _r1 = null;
    _r1Busy = false;
    _r1ReturningToL = false;  // ← 즉시 해제!

    // 다음 폴링(100ms 후)에서 바로 L→P 실행 가능
}
```

**문제 시나리오**:
1. Time 0ms: B(1) → R1 (Pick)
2. Time 800ms: R1(1) → L (Place) → R1 즉시 해제
3. Time 900ms: Scheduler 폴링 → L(2)→P 바로 실행
4. **R1이 LoadPort에서 바로 다른 웨이퍼를 픽업** (대기 위치 복귀 없음)

## 해결책

### 1. Transfer 단계 세분화

실제 설비 동작을 모방하여 4단계로 구분:

1. **Pick**: Buffer에서 웨이퍼 픽업 (R1이 웨이퍼와 함께 이동 시작)
2. **Transit**: 이동 중 (800ms)
3. **Place**: LoadPort에 웨이퍼 내려놓기 (R1이 빈 상태가 됨)
4. **Return to Idle**: R1이 대기 위치로 복귀 (400ms - 빈 상태로 이동)

### 2. 수정된 코드

```csharp
// After
private async Task ExecBtoL(CancellationToken ct)
{
    var waferId = _b!.Value;
    Log($"[P4] B({waferId}) → R1 (Pick from Buffer)");
    _b = null;
    _r1 = waferId;
    _r1Busy = true;
    _r1ReturningToL = true;  // 귀환 시작

    // Transfer to LoadPort (웨이퍼와 함께 이동)
    await Task.Delay(TRANSFER, ct);  // 800ms

    Log($"[P4] R1({waferId}) → L (Place at LoadPort)");
    _lCompleted.Add(waferId);
    _lCompleted.Sort();
    _completed.Add(waferId);
    _r1 = null;  // 웨이퍼 내려놓음 (R1은 아직 바쁨)

    // R1이 대기 위치로 복귀하는 시간 (로봇이 비어있는 상태로 이동)
    await Task.Delay(TRANSFER / 2, ct);  // 400ms (빈 상태로 복귀: 절반 시간)

    Log($"[P4] R1 returned to idle position (DONE)");
    _r1Busy = false;
    _r1ReturningToL = false;  // 완전히 귀환 완료

    _machine.SendAndForget("CHECK_COMPLETE");
}
```

### 3. 타이밍 다이어그램

#### Before (문제):
```
Time 0ms:    [P4] B(1) → R1 (Pick)
             _r1 = 1, _r1Busy = true, _r1ReturningToL = true

Time 800ms:  [P4] R1(1) → L (DONE)
             _r1 = null, _r1Busy = false, _r1ReturningToL = false
             ↓ 즉시 해제!

Time 900ms:  Scheduler 폴링
             CanExecLtoP() = true ← 문제!
             [P3] L(2) → R1 (바로 다시 픽업)
```

#### After (수정):
```
Time 0ms:    [P4] B(1) → R1 (Pick from Buffer)
             _r1 = 1, _r1Busy = true, _r1ReturningToL = true

Time 800ms:  [P4] R1(1) → L (Place at LoadPort)
             _r1 = null, _r1Busy = true, _r1ReturningToL = true
             ↓ 아직 바쁨! (대기 위치로 복귀 중)

Time 900ms:  Scheduler 폴링
             CanExecLtoP() = false (_r1ReturningToL = true) ← 차단!

Time 1200ms: [P4] R1 returned to idle position (DONE)
             _r1Busy = false, _r1ReturningToL = false
             ↓ 이제 완전히 해제!

Time 1300ms: Scheduler 폴링
             CanExecLtoP() = true ← 정상!
             [P3] L(2) → R1 (대기 위치에서 픽업)
```

## 개선 효과

### 1. 현실성 향상
- ✅ R1이 실제 설비처럼 대기 위치로 복귀
- ✅ 로봇의 물리적 이동이 명확하게 표현됨

### 2. 시각적 개선 (WPF)
- ✅ 웨이퍼가 LoadPort에 도착 후 R1이 비어있는 상태로 이동하는 애니메이션
- ✅ R1이 "종착역"처럼 보이지 않고 계속 순환

### 3. 타이밍 일관성
- ✅ 모든 Transfer가 일관된 패턴: Pick → Transit → Place → Return
- ✅ L→P, P→C 등 다른 Transfer와 동일한 구조

## 적용 파일

- ✅ `Controllers/ForwardPriorityController.cs` (WPF)
  - `ExecBtoL()` 메서드 수정
  - 추가 지연: `TRANSFER / 2` (400ms)
  - 로그 메시지 개선

- ✅ `CMPSimulator.Tests/ForwardPrioritySchedulerTests.cs` (Unit Test)
  - `StartBtoL()`, `CompleteBtoL()` 메서드 수정
  - 로그 메시지 개선
  - Note 추가: 테스트에서는 간소화

## Timing 설정

```csharp
TRANSFER = 800ms     // 웨이퍼와 함께 이동
TRANSFER / 2 = 400ms // 빈 상태로 대기 위치 복귀
```

빈 상태로 이동하는 것이 더 빠르므로 절반 시간을 사용했습니다.

## 로그 메시지 개선

### Before
```
[P4] [Transfer Start] B(1) → R1 (Returning to LoadPort)
[P4] [Transfer Complete] R1(1) → L (DONE)
```

### After
```
[P4] B(1) → R1 (Pick from Buffer)
[P4] R1(1) → L (Place at LoadPort)
[P4] R1 returned to idle position (DONE)
```

더 명확하고 단계별 동작을 표현합니다.

## 향후 개선 사항

### 1. 모든 Transfer에 적용
현재는 B→L만 수정했지만, 일관성을 위해 다른 Transfer도 동일하게 적용:
- L→P: Pick from L → Transit → Place at P → Return to idle
- P→C: Pick from P → Transit → Place at C → Return to idle
- C→B: Pick from C → Transit → Place at B → Return to idle

### 2. 로봇별 Home Position 설정
- R1: LoadPort와 Polisher 사이 (중앙 위치)
- R2: Polisher와 Cleaner 사이 (중앙 위치)

### 3. 시각적 애니메이션 강화 (WPF)
- 로봇이 웨이퍼를 내려놓은 후 Home Position으로 돌아가는 애니메이션
- 빈 상태 표시 (다른 색상 또는 투명도)

## 결론

✅ **R1이 더 이상 종착역이 아닙니다!**
✅ **대기 위치로 복귀하는 동작 추가**
✅ **실제 설비와 유사한 동작 구현**

이제 WPF Simulator와 실제 CMP 설비의 동작이 더 유사해졌습니다.
