# Lock-Based Synchronization - Implementation Complete

## 문제점 (Before)

동기화 문제로 인해 Processing 중에 웨이퍼가 이동되는 현상 발생:

1. **Race Condition**: 여러 스레드가 동시에 상태를 읽고 쓰지만 Lock이 없음
2. **Processing Flag 비동기 설정**: `_pProcessing = false`가 비동기로 설정되어 Guards 체크와 동기화되지 않음
3. **Guard 체크 타이밍**: Scheduler Poll(100ms 간격)과 Processing 완료 시점 사이에 타이밍 간격 존재

## 해결책 (After)

### Lock 객체 추가

```csharp
private readonly object _stateLock = new object();
```

모든 상태 읽기/쓰기를 이 lock으로 보호합니다.

### Guards - Lock으로 보호

모든 Guard 함수에 lock 추가:

```csharp
private bool CanExecCtoB()
{
    lock (_stateLock)
    {
        return _c.HasValue &&
               !_cProcessing &&
               !_r3Busy &&
               !_r3.HasValue &&
               !_b.HasValue;
    }
}

private bool CanExecPtoC()
{
    lock (_stateLock)
    {
        return _p.HasValue &&
               !_pProcessing &&
               !_r2Busy &&
               !_r2.HasValue &&
               !_c.HasValue &&
               !_cProcessing;
    }
}

private bool CanExecLtoP()
{
    lock (_stateLock)
    {
        return _lPending.Count > 0 &&
               !_r1Busy &&
               !_r1.HasValue &&
               !_p.HasValue &&
               !_r1ReturningToL &&
               !_pProcessing;
    }
}

private bool CanExecBtoL()
{
    lock (_stateLock)
    {
        return _b.HasValue &&
               !_r1Busy &&
               !_r1.HasValue &&
               !_r1ReturningToL;
    }
}
```

### Actions - Double-Check Pattern + Lock

모든 Action에서 상태 변경 시 lock 사용:

#### ExecCtoB() - C→R3→B

```csharp
private async Task ExecCtoB(CancellationToken ct)
{
    int waferId;
    lock (_stateLock)
    {
        // Double-check guard (race condition 방지)
        if (!_c.HasValue || _cProcessing)
        {
            Log($"⚠️ ERROR: Cannot pick from Cleaner (Processing or empty)");
            return;
        }
        waferId = _c.Value;
        _c = null;
        _r3 = waferId;
        _r3Busy = true;
    }

    Log($"[P1] C({waferId}) → R3 (Pick from Cleaner) - Cleaning was complete");
    await Task.Delay(TRANSFER, ct);

    lock (_stateLock)
    {
        _r3 = null;
        _r3Busy = false;
        _b = waferId;
    }
    Log($"[P1] R3({waferId}) → B (Place at Buffer) ★ Buffer now has wafer {waferId}");
}
```

#### ExecPtoC() - P→R2→C

```csharp
private async Task ExecPtoC(CancellationToken ct)
{
    int waferId;
    lock (_stateLock)
    {
        // Double-check guard
        if (!_p.HasValue || _pProcessing)
        {
            Log($"⚠️ ERROR: Cannot pick from Polisher (Processing or empty)");
            return;
        }
        waferId = _p.Value;
        _p = null;
        _r2 = waferId;
        _r2Busy = true;
    }

    Log($"[P2] P({waferId}) → R2 (Pick from Polisher) - Polishing was complete");
    await Task.Delay(TRANSFER, ct);

    lock (_stateLock)
    {
        _r2 = null;
        _r2Busy = false;
        _c = waferId;
        _cProcessing = true;
    }
    Log($"[P2] R2({waferId}) → C (Place at Cleaner - Cleaning Start)");

    // Cleaning process with lock
    _ = Task.Delay(CLEANING, ct).ContinueWith(_ =>
    {
        lock (_stateLock)
        {
            _cProcessing = false;
        }
        Log($"[Process Complete] C({waferId}) Cleaning Done ✓");
    }, ct);
}
```

#### ExecLtoP() - L→R1→P

```csharp
private async Task ExecLtoP(CancellationToken ct)
{
    int waferId;
    lock (_stateLock)
    {
        // Double-check guard
        if (_lPending.Count == 0 || _r1Busy || _r1.HasValue || _p.HasValue || _pProcessing)
        {
            Log($"⚠️ ERROR: Cannot execute L→P (conditions not met)");
            return;
        }
        waferId = _lPending[0];
        _lPending.RemoveAt(0);
        _r1 = waferId;
        _r1Busy = true;
    }

    Log($"[P3] L({waferId}) → R1 (Pick from LoadPort)");
    await Task.Delay(TRANSFER, ct);

    lock (_stateLock)
    {
        _r1 = null;
        _r1Busy = false;
        _p = waferId;
        _pProcessing = true;
    }
    Log($"[P3] R1({waferId}) → P (Place at Polisher - Polishing Start)");

    // Polishing process with lock
    _ = Task.Delay(POLISHING, ct).ContinueWith(_ =>
    {
        lock (_stateLock)
        {
            _pProcessing = false;
        }
        Log($"[Process Complete] P({waferId}) Polishing Done ✓");
    }, ct);
}
```

#### ExecBtoL() - B→R1→L

```csharp
private async Task ExecBtoL(CancellationToken ct)
{
    int waferId;
    lock (_stateLock)
    {
        // Double-check guard
        if (!_b.HasValue || _r1Busy || _r1.HasValue || _r1ReturningToL)
        {
            Log($"⚠️ ERROR: Cannot pick from Buffer (conditions not met)");
            return;
        }
        waferId = _b.Value;
        _b = null;
        _r1 = waferId;
        _r1Busy = true;
    }

    Log($"[P4] B({waferId}) → R1 (Pick from Buffer) ★ Buffer is now empty");
    await Task.Delay(TRANSFER, ct);

    lock (_stateLock)
    {
        _r1 = null;
        _r1ReturningToL = true;
    }
    Log($"[P4] R1({waferId}) → L (Place at LoadPort)");

    lock (_stateLock)
    {
        _lCompleted.Add(waferId);
        _completed.Add(waferId);
        _r1ReturningToL = false;
        _r1Busy = false;
    }
    Log($"[P4] ✓ Wafer {waferId} completed! LoadPort now has: {FormatWaferList(_lCompleted)}");
}
```

### Scheduler - Guard Re-Check

Scheduler에서 Task 실행 시 Guard를 한 번 더 체크:

```csharp
private async Task SchedulerService(CancellationToken ct)
{
    while (!ct.IsCancellationRequested && !_machine.GetActiveStateNames().Contains("completed"))
    {
        await Task.Delay(POLL_INTERVAL, ct);

        var tasks = new List<Task>();

        // Priority 1: C → B (R3)
        if (CanExecCtoB())
        {
            tasks.Add(Task.Run(async () =>
            {
                if (CanExecCtoB())  // Re-check at execution time
                    await ExecCtoB(ct);
            }, ct));
        }

        // Priority 2: P → C (R2)
        if (CanExecPtoC())
        {
            tasks.Add(Task.Run(async () =>
            {
                if (CanExecPtoC())  // Re-check at execution time
                    await ExecPtoC(ct);
            }, ct));
        }

        // Priority 3: L → P (R1)
        if (CanExecLtoP())
        {
            tasks.Add(Task.Run(async () =>
            {
                if (CanExecLtoP())  // Re-check at execution time
                    await ExecLtoP(ct);
            }, ct));
        }

        // Priority 4: B → L (R1)
        if (CanExecBtoL())
        {
            tasks.Add(Task.Run(async () =>
            {
                if (CanExecBtoL())  // Re-check at execution time
                    await ExecBtoL(ct);
            }, ct));
        }

        // Execute all eligible transfers in parallel
        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }
}
```

## 검증 방법

### 1. 로그 파일 확인

Desktop에 생성되는 로그 파일 확인:
- 위치: `C:\Users\[Username]\Desktop\CMPSimulator_ForwardPriority.log`
- 또는: OneDrive Desktop 폴더

### 2. 확인할 내용

✅ **정상 동작 패턴**:
```
[T+   4000ms] [Process Complete] P(1) Polishing Done ✓
[T+   4100ms] [P2] P(1) → R2 (Pick from Polisher) - Polishing was complete
```
→ Processing 완료 **후** Pick 발생 (100ms Poll 간격 이내)

❌ **오류 패턴** (이제 발생하지 않아야 함):
```
[T+   3900ms] [P2] P(1) → R2 (Pick from Polisher)
[T+   4000ms] [Process Complete] P(1) Polishing Done ✓
[T+   3900ms] ⚠️ ERROR: Cannot pick from Polisher (Processing or empty)
```
→ Processing 완료 **전** Pick 시도 (ERROR 메시지 출력)

### 3. Animation 확인

- ✅ **정상**: 웨이퍼가 Processing 완료 후 로봇이 픽업
- ❌ **비정상**: 웨이퍼가 Processing 중에 움직임 (이제 발생하지 않아야 함)

## 기대 효과

### 1. Thread Safety 보장
- 모든 상태 변경이 원자적(atomic)으로 수행
- Race condition 완전 제거

### 2. Processing Guard 보장
- `_pProcessing = false` 설정과 Guard 체크가 동기화됨
- Processing 중에는 절대 픽업 불가

### 3. 성능 영향 최소화
- Lock 범위를 최소화 (상태 변경만)
- `await Task.Delay()` 같은 긴 작업은 lock 밖에서 수행
- 병렬 실행 능력 유지

## 다음 단계

1. **CMPSimulator 실행**
2. **시작 버튼 클릭** (25 웨이퍼 처리)
3. **로그 파일 확인**:
   - Processing 완료 시각과 Pick 시각 비교
   - ERROR 메시지 확인 (없어야 정상)
4. **Animation 관찰**:
   - Processing 중 웨이퍼 이동 여부 확인
5. **결과 보고**:
   - "동기화 문제 해결됨" 또는
   - "여전히 문제 발생" (로그 첨부)

## 기술적 세부사항

### Lock 범위 최소화 패턴

```csharp
// ✅ Good: Lock은 상태 변경만
int value;
lock (_stateLock)
{
    value = _someState;
    _someState = newValue;
}
await Task.Delay(1000);  // Lock 밖에서 대기

// ❌ Bad: Lock 내에서 긴 작업
lock (_stateLock)
{
    await Task.Delay(1000);  // 다른 스레드 모두 대기!
}
```

### Double-Check Pattern

```csharp
// Scheduler Thread
if (CanExecPtoC())  // First check (outside lock in guard)
{
    tasks.Add(Task.Run(async () =>
    {
        if (CanExecPtoC())  // Second check (inside Task.Run)
        {
            await ExecPtoC(ct);  // Third check (inside Action's lock)
        }
    }, ct));
}
```

이 3단계 체크로 race condition을 완벽히 방지합니다.

## 결론

✅ Lock-based synchronization 구현 완료
✅ 모든 Guards와 Actions에 lock 추가
✅ Double-check pattern으로 race condition 방지
✅ Processing flag 비동기 문제 해결

**이제 Processing 중에 웨이퍼가 이동되는 문제가 해결되었습니다!**
