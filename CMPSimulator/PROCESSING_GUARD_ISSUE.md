# Processing Guard Issue - R2와 R3 동시 실행

## 문제 정의

**사용자 요구사항**: P가 먼저 완료되는 경우, C가 완료될 때 **R2와 R3가 동시에 시작**해야 함

## 시나리오 분석

### Scenario: P 먼저 완료

```
Time 0ms:
- Wafer 1: P에서 Polishing 시작 (4초 소요)
- Wafer 2: C에서 Cleaning 시작 (5초 소요)

Time 4000ms:
- Wafer 1: P 완료 (_pProcessing = false)
- Wafer 2: C 아직 Processing 중 (_cProcessing = true)

Time 5000ms:
- Wafer 2: C 완료 (_cProcessing = false)
```

### 현재 Guards

```csharp
// P→C 전송 조건
CanExecPtoC() =>
    _p.HasValue &&         // P(1)가 있음 ✓
    !_pProcessing &&       // P 처리 완료 ✓
    !_r2Busy &&            // R2 사용 가능 ✓
    !_r2.HasValue &&       // R2 비어있음 ✓
    !_c.HasValue           // C가 비어있어야 함 ✗ (C(2)가 있음!)

// C→B 전송 조건
CanExecCtoB() =>
    _c.HasValue &&         // C(2)가 있음 ✓
    !_cProcessing &&       // C 처리 완료? (Time 4000ms: ✗, Time 5000ms: ✓)
    !_r3Busy &&            // R3 사용 가능 ✓
    !_r3.HasValue &&       // R3 비어있음 ✓
    !_b.HasValue           // B가 비어있음 ✓
```

### 문제점

**Time 4000ms** (P 완료):
- `CanExecPtoC() = false` (C가 비어있지 않음)
- `CanExecCtoB() = false` (C가 아직 Processing 중)
- → **아무것도 실행 안 됨** ✓ 올바름!

**Time 5000ms** (C 완료):
- `CanExecPtoC() = false` (C에 웨이퍼 2가 있음!)
- `CanExecCtoB() = true`
- → **C→B만 실행됨** ✗ 문제!

**Time 5800ms** (C→B 완료):
- C가 비워짐
- `CanExecPtoC() = true`
- → **P→C 실행** (800ms 지연)

### 기대 동작

**Time 5000ms** (C 완료):
- `CanExecCtoB() = true` → C(2) → R3 → B 시작
- `CanExecPtoC() = true` → P(1) → R2 → C 시작 (동시에!)

하지만 **불가능**: C에 웨이퍼가 있으면 P→C를 실행할 수 없음!

## 해결 방법

### Option 1: Swap 동작 추가 (복잡함)

```csharp
// C→B와 P→C를 원자적으로 동시 실행
if (CanExecCtoB() && CanExecSwapPtoC())
{
    // C(2) → R3 → B
    var wafer2 = _c;
    _c = null;
    _r3 = wafer2;
    _r3Busy = true;

    // P(1) → R2 → C (동시에!)
    var wafer1 = _p;
    _p = null;
    _r2 = wafer1;
    _r2Busy = true;
}
```

**문제**: 매우 복잡하고 예외 케이스 많음

### Option 2: 현재 동작 유지 (권장)

**현재 동작**:
```
Time 5000ms: C(2) → R3 → B 시작 (800ms)
Time 5800ms: C 비워짐
             P(1) → R2 → C 시작
```

**지연**: 800ms (Transfer 시간)

**장점**:
- 단순하고 명확한 로직
- 충돌 없음
- 유지보수 쉬움

**단점**:
- R2와 R3가 완전히 동시에 시작하지 않음 (800ms 차이)

### Option 3: Pre-emptive Transfer (중간 복잡도)

C가 Processing을 완료하면 **즉시 두 Transfer를 동시에 트리거**:

```csharp
// Cleaning 완료 시
private void OnCleaningComplete(int waferId)
{
    _cProcessing = false;

    // Trigger simultaneous transfers
    if (_p.HasValue && !_pProcessing)
    {
        // C→B와 P→C를 동시에 실행
        TriggerSimultaneousCBandPC();
    }
}

private async Task TriggerSimultaneousCBandPC()
{
    var waferFromC = _c!.Value;
    var waferFromP = _p!.Value;

    // C→R3 시작
    _c = null;
    _r3 = waferFromC;
    _r3Busy = true;

    // P→R2 시작 (동시에)
    _p = null;
    _r2 = waferFromP;
    _r2Busy = true;

    // 800ms 후 완료
    await Task.Delay(TRANSFER);

    // C→B 완료
    _r3 = null;
    _r3Busy = false;
    _b = waferFromC;

    // P→C 완료
    _r2 = null;
    _r2Busy = false;
    _c = waferFromP;
    _cProcessing = true;  // Cleaning 시작
}
```

## 권장 사항

**Option 2 (현재 동작 유지)를 권장합니다.**

### 이유

1. **단순성**: 코드가 명확하고 이해하기 쉬움
2. **안정성**: 충돌 케이스가 없음
3. **800ms 지연은 미미함**: 전체 사이클 타임(Cleaning 5초)에 비해 16% 추가

### 성능 영향

```
Current:
- C→B: 800ms
- Wait: 0ms (C 비워짐)
- P→C: 800ms
Total: 1600ms

Ideal (Simultaneous):
- C→B + P→C: 800ms (동시)
Total: 800ms

Performance loss: 800ms per cycle
```

**25 wafers 기준**:
- 추가 지연: 800ms × 25 = 20초
- 전체 시간: ~2-2.5분
- 영향: 약 13-16% 증가

### 실제 동작 확인

현재 구현에서는 다음과 같이 동작합니다:

```
Time 5000ms:
[P1] C(2) → R3 (Pick from Cleaner)

Time 5800ms:
[P1] R3(2) → B (Place at Buffer) ★ Buffer now has wafer 2
[P2] P(1) → R2 (Pick from Polisher)

Time 6600ms:
[P2] R2(1) → C (Place at Cleaner - Cleaning Start)
```

**R2와 R3가 100ms 간격으로 시작** (Scheduler Poll 간격)

## 대안: Cleaning 완료 이벤트 기반 트리거

Processing 완료를 **Event 기반**으로 처리:

```csharp
_ = Task.Delay(CLEANING, ct).ContinueWith(_ =>
{
    _cProcessing = false;
    Log($"[Process Complete] C({waferId}) Cleaning Done");

    // Trigger immediate check for simultaneous transfers
    CheckSimultaneousTransfers();
}, ct);

private void CheckSimultaneousTransfers()
{
    if (CanExecCtoB() && CanExecPtoC())
    {
        // Execute both immediately
        _ = ExecCtoB(_cts.Token);
        _ = ExecPtoC(_cts.Token);
    }
}
```

**장점**: 더 빠른 반응 (100ms Poll 대기 없음)

**단점**: 복잡도 증가

## 결론

현재 구현은 **충분히 효율적**입니다:
- ✅ Processing 중에는 전송하지 않음
- ✅ R2와 R3는 거의 동시에 동작 (100ms 차이)
- ✅ 800ms 지연은 전체 성능에 미미한 영향

**추가 최적화는 불필요**합니다. 코드 복잡도가 증가하는 것보다 현재의 단순성을 유지하는 것이 좋습니다.
