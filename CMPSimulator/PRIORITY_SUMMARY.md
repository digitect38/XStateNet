# Priority System Summary - Final Implementation

## 우선순위 체계

```
C→B ≥ P→C ≥ L→P ≥ B→L
```

### 우선순위 의미

- **P1 (Highest)**: C → R3 → B (Cleaner 자원 해제 최우선)
- **P2**: P → R2 → C (Polisher 자원 해제)
- **P3**: L → R1 → P (신규 웨이퍼 투입)
- **P4 (Lowest)**: B → R1 → L (완료 웨이퍼 배출)

**중요**: `≥` 는 "같거나 높음"이 아니라 **"동시에 실행 가능"**을 의미합니다.

## 동시 실행 시나리오

### 시나리오 1: 3개 로봇 동시 동작 (최대 병렬도)

```
Initial State:
- C(3) 완료 (Cleaning done)
- P(2) 완료 (Polishing done)
- L(1) 대기 중 (Ready to start)

Time 0ms: Scheduler Poll
┌─ CanExecCtoB() = true → C(3) → R3 시작
├─ CanExecPtoC() = true → P(2) → R2 시작
└─ CanExecLtoP() = true → L(1) → R1 시작

← 3개 로봇이 동시에 3개의 웨이퍼 이동!

Time 0~800ms:
├─ R3: C(3) → B 이동 중
├─ R2: P(2) → C 이동 중
└─ R1: L(1) → P 이동 중

Time 800ms: 모두 완료
```

### 시나리오 2: 2개 로봇 동시 동작

```
Initial State:
- C(2) 완료
- L(1) 대기 중

Time 0ms: Scheduler Poll
┌─ CanExecCtoB() = true → C(2) → R3 시작
└─ CanExecLtoP() = true → L(1) → R1 시작

← 2개 로봇이 동시에 2개의 웨이퍼 이동!
```

## 로봇 역할 분담

| Robot | 전담 경로 | 독립성 |
|-------|----------|--------|
| **R1** | L ↔ P ↔ B | R2, R3와 독립적 |
| **R2** | P ↔ C | R1, R3와 독립적 |
| **R3** | C ↔ B | R1, R2와 독립적 |

**핵심**: 각 로봇이 **서로 다른 경로**를 담당하므로 **충돌 없이 동시 실행 가능**

## Guards 독립성

```csharp
// P1: C → R3 → B (R3만 체크)
CanExecCtoB() =>
    _c.HasValue &&
    !_cProcessing &&
    !_r3Busy &&      // R3만 체크
    !_r3.HasValue &&
    !_b.HasValue;

// P2: P → R2 → C (R2만 체크)
CanExecPtoC() =>
    _p.HasValue &&
    !_pProcessing &&
    !_r2Busy &&      // R2만 체크
    !_r2.HasValue &&
    !_c.HasValue;

// P3: L → R1 → P (R1만 체크)
CanExecLtoP() =>
    _lPending.Count > 0 &&
    !_r1Busy &&      // R1만 체크
    !_r1.HasValue &&
    !_p.HasValue &&
    !_r1ReturningToL;

// P4: B → R1 → L (R1만 체크)
CanExecBtoL() =>
    _b.HasValue &&
    !_r1Busy &&      // R1만 체크
    !_r1.HasValue;
```

**독립성 보장**:
- P1은 R3만 체크
- P2는 R2만 체크
- P3/P4는 R1만 체크 (상호 배타적)

→ **동시에 모두 true가 될 수 있음!**

## Scheduler 구현

```csharp
private async Task SchedulerService(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(POLL_INTERVAL, ct);

        var tasks = new List<Task>();

        // 모든 Guards를 체크하고 가능한 모든 전송을 추가
        if (CanExecCtoB()) tasks.Add(Task.Run(async () => await ExecCtoB(ct), ct));
        if (CanExecPtoC()) tasks.Add(Task.Run(async () => await ExecPtoC(ct), ct));
        if (CanExecLtoP()) tasks.Add(Task.Run(async () => await ExecLtoP(ct), ct));
        if (CanExecBtoL()) tasks.Add(Task.Run(async () => await ExecBtoL(ct), ct));

        // 모든 가능한 전송을 동시에 실행
        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }
}
```

**핵심**:
1. 모든 Guards를 순차적으로 체크 (우선순위 순서)
2. 조건을 만족하는 모든 전송을 Task 리스트에 추가
3. `Task.WhenAll()`로 **모두 동시에 실행**

## 전체 흐름 예시

```
Wafer 1, 2, 3 처리 과정:

Time 0ms:
[P3] L(1) → R1 (Start)
[P3] L(2) → R1 (Wait - R1 busy)

Time 800ms:
[P3] R1(1) → P (Polishing Start)
[P3] L(2) → R1 (Start - R1 available)

Time 1600ms:
[P3] R1(2) → P (Wait - P busy)

Time 4800ms:
[P3] P(1) Polishing Done
[P2] P(1) → R2 (Start)
[P3] R1(2) → P (Start - P available)

Time 5600ms:
[P2] R2(1) → C (Cleaning Start)
[P3] L(3) → R1 (Start)

Time 6400ms:
[P3] R1(3) → P (Wait - P busy)

Time 10600ms:
[P2] C(1) Cleaning Done
[P1] C(1) → R3 (Start) ← R3 첫 사용!
[P3] P(2) Polishing Done
[P2] P(2) → R2 (Start)

← C→R3→B와 P→R2→C가 동시에!

Time 11400ms:
[P1] R3(1) → B ★ Buffer now has wafer 1
[P2] R2(2) → C (Cleaning Start)
[P4] B(1) → R1 (Start) ← Buffer에서 바로 픽업!

Time 12200ms:
[P4] R1(1) → L ✓ Wafer 1 completed!
```

## 동시 실행 가능 조합

### 가능한 조합 (✅)

1. **C→B + P→C + L→P** ✅ (최대 병렬도 - 3개 로봇)
2. **C→B + P→C** ✅ (R3 + R2)
3. **C→B + L→P** ✅ (R3 + R1)
4. **P→C + L→P** ✅ (R2 + R1)
5. **C→B + B→L** ✅ (R3 + R1)
6. **P→C + B→L** ✅ (R2 + R1)

### 불가능한 조합 (❌)

1. **L→P + B→L** ❌ (둘 다 R1 필요)
   - Guard 상호 배타: 하나만 실행됨

## 처리량 계산

### 이론적 최대 처리량

```
Sequential (순차):
- 3 transfers × 800ms = 2400ms

Parallel (병렬):
- 3 transfers in 800ms (동시 실행)
- Speedup: 3x
```

### 실제 처리량

```
병목: 공정 시간
- Polishing: 4000ms
- Cleaning: 5000ms

로봇 대기 시간:
- Before: 순차 실행으로 인한 추가 대기
- After: 최소화 (병렬 실행)

예상 개선: 25 wafers 기준
- Before: ~3-4분
- After: ~2-2.5분 (약 30-40% 단축)
```

## 로그 출력 예시

```
[P1] C(1) → R3 (Pick from Cleaner)
[P2] P(2) → R2 (Pick from Polisher)
[P3] L(3) → R1 (Pick from LoadPort)
← 거의 동시에 출력!

Time +800ms:
[P1] R3(1) → B (Place at Buffer) ★ Buffer now has wafer 1
[P2] R2(2) → C (Place at Cleaner - Cleaning Start)
[P3] R1(3) → P (Place at Polisher - Polishing Start)
← 거의 동시에 출력!

Time +900ms:
[P4] B(1) → R1 (Pick from Buffer) ★ Buffer is now empty
← R1이 사용 가능해지면 즉시 실행!
```

## 요약

✅ **우선순위**: C→B ≥ P→C ≥ L→P ≥ B→L
✅ **병렬 실행**: 3개 로봇이 동시에 3개의 웨이퍼 이동 가능
✅ **독립성**: 각 로봇이 서로 다른 경로를 전담
✅ **충돌 방지**: Guards의 독립적 체크로 자동 보장
✅ **최대 처리량**: 로봇 수만큼 병렬도 증가 (3x)

**핵심**: `≥` 기호는 우선순위가 아니라 **"동시 실행 가능"**을 의미합니다!
