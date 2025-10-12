# 동기화(Sync) 문제 체크리스트

## 가능한 문제들

### 1. Processing 완료 체크 타이밍

**문제**: `_pProcessing = false`가 비동기로 설정되어 Guards 체크와 동기화되지 않음

```csharp
// ExecLtoP() 내부
_ = Task.Delay(POLISHING, ct).ContinueWith(_ =>
{
    _pProcessing = false;  // 비동기로 설정
}, ct);

// Scheduler (별도 스레드)
await Task.Delay(POLL_INTERVAL, ct);  // 100ms
if (CanExecPtoC())  // _pProcessing 체크
```

**타이밍 이슈**:
```
Time 4000ms: Polishing 완료 Task 시작
Time 4001ms: _pProcessing = false 설정
Time 4100ms: Scheduler Poll → CanExecPtoC() 체크

← 100ms 지연!
```

### 2. Lock이 없는 상태 변경

**문제**: 여러 스레드가 동시에 상태를 읽고 쓰지만 Lock이 없음

```csharp
// Scheduler Thread
if (_p.HasValue && !_pProcessing)  // Read
{
    _p = null;  // Write
    _r2 = waferId;  // Write
}

// Processing Complete Thread
_pProcessing = false;  // Write

← Race Condition 가능!
```

### 3. Task.WhenAll의 실행 순서

**문제**: Task.WhenAll()은 순서를 보장하지 않음

```csharp
var tasks = new List<Task>();
if (CanExecCtoB()) tasks.Add(...);
if (CanExecPtoC()) tasks.Add(...);
await Task.WhenAll(tasks);

// 실제 실행 순서는?
// - Task1이 먼저? Task2가 먼저?
// - 완전히 동시에? (아님!)
```

### 4. UI 업데이트 지연

**문제**: UIUpdateService가 100ms마다 폴링

```csharp
while (!ct.IsCancellationRequested)
{
    await Task.Delay(100, ct);  // 100ms 지연!
    UpdateWaferPositions();
}
```

**결과**: 상태 변경과 UI 표시 사이에 최대 100ms 차이

## 해결 방안

### Option 1: Lock 추가 (권장)

모든 상태 변경을 동기화:

```csharp
private readonly object _stateLock = new object();

private bool CanExecPtoC()
{
    lock (_stateLock)
    {
        return _p.HasValue && !_pProcessing && ...;
    }
}

private async Task ExecPtoC(CancellationToken ct)
{
    int waferId;
    lock (_stateLock)
    {
        if (_pProcessing) return;  // Double-check
        waferId = _p!.Value;
        _p = null;
        _r2 = waferId;
        _r2Busy = true;
    }

    await Task.Delay(TRANSFER, ct);

    lock (_stateLock)
    {
        _r2 = null;
        _r2Busy = false;
        _c = waferId;
        _cProcessing = true;
    }
}
```

### Option 2: 순차 실행 (간단하지만 느림)

병렬 실행 제거:

```csharp
private async Task SchedulerService(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(POLL_INTERVAL, ct);

        // 순차 실행 (continue 사용)
        if (CanExecCtoB()) { await ExecCtoB(ct); continue; }
        if (CanExecPtoC()) { await ExecPtoC(ct); continue; }
        if (CanExecLtoP()) { await ExecLtoP(ct); continue; }
        if (CanExecBtoL()) { await ExecBtoL(ct); continue; }
    }
}
```

### Option 3: Immediate UI Update

상태 변경 시 즉시 UI 업데이트:

```csharp
private async Task ExecPtoC(CancellationToken ct)
{
    // ... state changes ...
    _r2 = waferId;

    // Immediate UI update
    Application.Current?.Dispatcher.Invoke(() =>
    {
        UpdateWaferPositions();
    });

    await Task.Delay(TRANSFER, ct);
}
```

## 진단 필요

Desktop의 로그 파일 확인:
```
C:\Users\[Username]\Desktop\CMPSimulator_ForwardPriority.log
```

확인할 내용:
1. ⚠️ ERROR 메시지가 있는가?
2. Processing 완료와 Pick 사이의 시간 간격은?
3. 동일 시간에 여러 로봇이 움직이는가?
4. 타이밍이 예상과 일치하는가?

## 예상 로그 (정상)

```
[T+      0ms] ✅ Forward Priority Scheduler Started
[T+    100ms] [P3] L(1) → R1
[T+    900ms] [P3] R1(1) → P (Polishing Start)
[T+   4900ms] [Process Complete] P(1) Polishing Done ✓
[T+   5000ms] [P2] P(1) → R2 (Pick from Polisher) - Polishing was complete
[T+   5800ms] [P2] R2(1) → C (Place at Cleaner - Cleaning Start)
[T+  10800ms] [Process Complete] C(1) Cleaning Done ✓
[T+  10900ms] [P1] C(1) → R3 (Pick from Cleaner) - Cleaning was complete
[T+  11700ms] [P1] R3(1) → B (Place at Buffer) ★
```

## 예상 로그 (문제)

```
[T+   4900ms] [Process Complete] P(1) Polishing Done ✓
[T+   4850ms] [P2] P(1) → R2 (Pick from Polisher)  ← 완료 전에 픽업!
[T+   4900ms] ⚠️ ERROR: Trying to pick P(1) while still processing!
```
