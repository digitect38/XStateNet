# Timing Debug Analysis

## 현재 설정 확인

### Timing Constants
```csharp
POLISHING = 4000ms   // 4초
CLEANING = 5000ms    // 5초
TRANSFER = 800ms     // 0.8초
POLL_INTERVAL = 100ms // 0.1초
```

### 예상 타이밍

**Wafer 1 전체 사이클**:
```
Time 0ms:     L(1) → R1 시작
Time 800ms:   R1 → P (Polishing 시작)
Time 4800ms:  P 완료
Time 4900ms:  P(1) → R2 시작 (100ms poll 후)
Time 5700ms:  R2 → C (Cleaning 시작)
Time 10700ms: C 완료
Time 10800ms: C(1) → R3 시작 (100ms poll 후)
Time 11600ms: R3 → B
Time 11700ms: B(1) → R1 시작 (100ms poll 후)
Time 12500ms: R1 → L (완료!)
Time 12900ms: R1 idle return 완료

Total: 약 12.9초
```

## 문제 가능성

### 1. Processing 플래그 타이밍

**문제**: Processing 완료와 Guard 체크 사이에 동기화 이슈

```csharp
// Polishing 완료 (비동기)
_ = Task.Delay(POLISHING, ct).ContinueWith(_ =>
{
    _pProcessing = false;  // ← 이 시점과
}, ct);

// Scheduler (별도 스레드)
if (CanExecPtoC())  // ← 이 체크 사이에 타이밍 이슈
```

### 2. 병렬 실행 Race Condition

**문제**: 여러 Task가 동시에 같은 상태를 변경

```
Time 0ms:
- Task1: CanExecCtoB() = true → ExecCtoB() 추가
- Task2: CanExecPtoC() = true → ExecPtoC() 추가

Time 1ms: (거의 동시에 실행)
- Task1: _c = null, _r3 = 1
- Task2: _c.HasValue 체크? (이미 null?)
```

### 3. UI 업데이트 지연

**문제**: UIUpdateService가 100ms마다만 업데이트

```
Time 800ms:  R1 → P 완료 (실제 상태 변경)
Time 900ms:  UI 업데이트 (100ms 지연)
```

→ 사용자가 보기에는 웨이퍼가 "점프"하는 것처럼 보임

## 진단 방법

### 로그 타임스탬프 추가

상세한 타이밍 로그를 위해 수정이 필요합니다.
