# Processing 동작 검증 - P와 C의 Processing 규칙

## 요구사항

> "P와 C는 Wafer 도착하자마자 Processing 하고 끝나면 이동한다"

## 현재 구현 확인

### ✅ P (Polisher) 동작

**ExecLtoP (L → R1 → P)**:
```csharp
// 1. R1이 P에 웨이퍼 배치
lock (_stateLock)
{
    _r1 = null;
    _r1Busy = false;
    _p = waferId;
    _pProcessing = true;  // ← 즉시 Processing 시작!
}
Log($"[P3] R1({waferId}) → P (Place at Polisher)");
Log($"🔨 [Processing] P({waferId}) Polishing START (will take {POLISHING}ms)");

// 2. POLISHING(4000ms) 후 완료
_ = Task.Delay(POLISHING, ct).ContinueWith(_ =>
{
    lock (_stateLock)
    {
        _pProcessing = false;  // ← Processing 완료
    }
    Log($"✅ [Processing] P({waferId}) Polishing DONE (after {POLISHING}ms)");
}, ct);
```

**CanExecPtoC (P → R2 → C)**:
```csharp
private bool CanExecPtoC()
{
    lock (_stateLock)
    {
        // _pProcessing이 false일 때만 Pick 가능
        return _p.HasValue && !_pProcessing && !_r2Busy && ...;
    }
}
```

**타이밍**:
```
Time 0ms: L(1) → R1
Time 800ms: R1(1) → P ← P에 도착
         _pProcessing = true ← 즉시 Processing 시작
         🔨 Polishing START

Time 800~4800ms: Processing 중
         CanExecPtoC() = false (_pProcessing = true)
         🚫 Cannot Pick P(1): Still Processing

Time 4800ms: Polishing 완료
         _pProcessing = false
         ✅ Polishing DONE

Time 4900ms: Scheduler Poll (100ms 후)
         CanExecPtoC() = true
         P(1) → R2 ← Pick 시작
```

---

### ✅ C (Cleaner) 동작

**ExecPtoC (P → R2 → C)**:
```csharp
// 1. R2가 C에 웨이퍼 배치
lock (_stateLock)
{
    _r2 = null;
    _r2Busy = false;
    _c = waferId;
    _cProcessing = true;  // ← 즉시 Processing 시작!
}
Log($"[P2] R2({waferId}) → C (Place at Cleaner)");
Log($"🧼 [Processing] C({waferId}) Cleaning START (will take {CLEANING}ms)");

// 2. CLEANING(5000ms) 후 완료
_ = Task.Delay(CLEANING, ct).ContinueWith(_ =>
{
    lock (_stateLock)
    {
        _cProcessing = false;  // ← Processing 완료
    }
    Log($"✅ [Processing] C({waferId}) Cleaning DONE (after {CLEANING}ms)");
}, ct);
```

**CanExecCtoB (C → R3 → B)**:
```csharp
private bool CanExecCtoB()
{
    lock (_stateLock)
    {
        // _cProcessing이 false일 때만 Pick 가능
        return _c.HasValue && !_cProcessing && !_r3Busy && ...;
    }
}
```

**타이밍**:
```
Time 0ms: P(1) → R2
Time 800ms: R2(1) → C ← C에 도착
         _cProcessing = true ← 즉시 Processing 시작
         🧼 Cleaning START

Time 800~5800ms: Processing 중
         CanExecCtoB() = false (_cProcessing = true)
         🚫 Cannot Pick C(1): Still Cleaning

Time 5800ms: Cleaning 완료
         _cProcessing = false
         ✅ Cleaning DONE

Time 5900ms: Scheduler Poll (100ms 후)
         CanExecCtoB() = true
         C(1) → R3 ← Pick 시작
```

---

## 예상 로그 (정상 동작)

### Wafer 1 전체 흐름

```
[T+      0ms] [P3] L(1) → R1
[T+    800ms] [P3] R1(1) → P (Place at Polisher)
[T+    800ms] 🔨 [Processing] P(1) Polishing START (will take 4000ms)

← Processing 중 (800~4800ms)
[T+    900ms] 🚫 Cannot Pick P(1): Still Processing (pProcessing=True)
[T+   1000ms] 🚫 Cannot Pick P(1): Still Processing (pProcessing=True)
[T+   1100ms] 🚫 Cannot Pick P(1): Still Processing (pProcessing=True)
... (4000ms 동안 계속)

[T+   4800ms] ✅ [Processing] P(1) Polishing DONE (after 4000ms)
[T+   4900ms] [P2] P(1) → R2 (Pick from Polisher) - Polishing was complete
[T+   5700ms] [P2] R2(1) → C (Place at Cleaner)
[T+   5700ms] 🧼 [Processing] C(1) Cleaning START (will take 5000ms)

← Processing 중 (5700~10700ms)
[T+   5800ms] 🚫 Cannot Pick C(1): Still Cleaning (cProcessing=True)
[T+   5900ms] 🚫 Cannot Pick C(1): Still Cleaning (cProcessing=True)
[T+   6000ms] 🚫 Cannot Pick C(1): Still Cleaning (cProcessing=True)
... (5000ms 동안 계속)

[T+  10700ms] ✅ [Processing] C(1) Cleaning DONE (after 5000ms)
[T+  10800ms] [P1] C(1) → R3 (Pick from Cleaner) - Cleaning was complete
[T+  11600ms] [P1] R3(1) → B (Place at Buffer) ★
```

---

## 디버깅 로그 추가

### Guards에 디버깅 로그

```csharp
private bool CanExecPtoC()
{
    lock (_stateLock)
    {
        bool canExec = _p.HasValue && !_pProcessing && ...;

        if (_p.HasValue && _pProcessing)
        {
            // Processing 중일 때 로그 출력
            DebugLog($"🚫 Cannot Pick P({_p.Value}): Still Processing (pProcessing={_pProcessing})");
        }

        return canExec;
    }
}
```

이 로그를 통해:
- Processing 중에 Pick 시도가 있는지 확인
- `_pProcessing` 플래그가 올바르게 설정되는지 확인
- Pick이 언제 가능해지는지 정확히 파악

---

## 확인 방법

### 1. 애플리케이션 실행

```bash
cd C:\Develop25\XStateNet\CMPSimulator\bin\Debug\net8.0-windows
.\CMPSimulator.exe
```

### 2. 로그 파일 확인

Desktop의 로그 파일:
```
C:\Users\[Username]\Desktop\CMPSimulator_ForwardPriority.log
```

### 3. 정상 동작 패턴

✅ **Processing 시작 즉시**:
```
[T+    800ms] [P3] R1(1) → P (Place at Polisher)
[T+    800ms] 🔨 [Processing] P(1) Polishing START (will take 4000ms)
```
→ 웨이퍼가 P에 도착하자마자 Processing 시작

✅ **Processing 중 Pick 차단**:
```
[T+    900ms] 🚫 Cannot Pick P(1): Still Processing (pProcessing=True)
[T+   1000ms] 🚫 Cannot Pick P(1): Still Processing (pProcessing=True)
...
```
→ Processing 중에는 Pick 불가 (여러 번 체크됨)

✅ **Processing 완료 후 Pick**:
```
[T+   4800ms] ✅ [Processing] P(1) Polishing DONE (after 4000ms)
[T+   4900ms] [P2] P(1) → R2 (Pick from Polisher)
```
→ Processing 완료 후 100ms 이내에 Pick

### 4. 문제 있는 패턴 (이제 발생하지 않아야 함)

❌ **Processing 중 Pick 시도**:
```
[T+   2000ms] [P2] P(1) → R2 (Pick from Polisher)
[T+   4800ms] ✅ [Processing] P(1) Polishing DONE (after 4000ms)
```
→ Processing 완료 전에 Pick (문제!)

❌ **ERROR 메시지**:
```
⚠️ ERROR: Cannot pick from Polisher (Processing or empty)
```
→ Double-check에서 걸림 (Guard가 제대로 작동 안 함)

---

## 코드 검증

### Processing Flag 설정

**Polisher**:
```csharp
// ✅ 도착 즉시 true
_p = waferId;
_pProcessing = true;  // Line 589

// ✅ 4000ms 후 false
_pProcessing = false;  // Line 598
```

**Cleaner**:
```csharp
// ✅ 도착 즉시 true
_c = waferId;
_cProcessing = true;  // Line 550

// ✅ 5000ms 후 false
_cProcessing = false;  // Line 559
```

### Guards 검증

**CanExecPtoC**:
```csharp
return _p.HasValue && !_pProcessing && ...;  // Line 476
```
→ `!_pProcessing` 체크 있음 ✅

**CanExecCtoB**:
```csharp
return _c.HasValue && !_cProcessing && ...;  // Line 468
```
→ `!_cProcessing` 체크 있음 ✅

### Lock 검증

모든 Processing flag 접근이 Lock으로 보호됨:
```csharp
// 설정 시
lock (_stateLock)
{
    _pProcessing = true;
}

// 읽기 시 (Guard)
lock (_stateLock)
{
    return ... && !_pProcessing && ...;
}
```
→ Thread-safe ✅

---

## 결론

### 현재 구현

✅ **P와 C는 웨이퍼 도착 즉시 Processing 시작**
- `_pProcessing = true` 즉시 설정 (Line 589)
- `_cProcessing = true` 즉시 설정 (Line 550)

✅ **Processing 중에는 Pick 불가**
- `CanExecPtoC()`: `!_pProcessing` 체크 (Line 476)
- `CanExecCtoB()`: `!_cProcessing` 체크 (Line 468)

✅ **Processing 완료 후 이동**
- POLISHING 완료 → `_pProcessing = false` (Line 598)
- CLEANING 완료 → `_cProcessing = false` (Line 559)
- 다음 Poll(100ms 후) → Pick 가능

### 기대 효과

이제 로그에서 다음을 확인할 수 있습니다:

1. **🔨 Processing START** - 웨이퍼 도착 즉시
2. **🚫 Cannot Pick** - Processing 중 Pick 차단
3. **✅ Processing DONE** - Processing 완료
4. **[P2] Pick from Polisher** - 완료 후 Pick

**규칙이 정확히 지켜지고 있는지 로그로 검증 가능합니다!**
