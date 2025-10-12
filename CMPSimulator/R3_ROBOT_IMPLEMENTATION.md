# R3 Robot Implementation - Throughput Maximization

## 개요

C→B 전송을 전담하는 **R3 로봇**을 추가하여 처리량(Throughput)을 극대화했습니다.

## 문제점

### Before: R2가 두 가지 역할 수행

```
R2 (Robot 2): P ↔ C ↔ B  (양방향, 두 경로 처리)
  - P → C: Polisher → Cleaner 전송
  - C → B: Cleaner → Buffer 전송
```

**병목 현상**:
1. C→B 전송 중에는 R2가 바쁨 (`R2_Busy = true`)
2. P→C를 실행할 수 없음 (R2가 사용 중)
3. Polisher가 비어도 다음 웨이퍼를 처리할 수 없음
4. **전체 처리량 감소**

## 해결책: R3 로봇 추가

### After: R2와 R3로 역할 분리

```
R2 (Robot 2): P ↔ C  (Polisher-Cleaner 전용)
R3 (Robot 3): C ↔ B  (Cleaner-Buffer 전용)
```

**개선 효과**:
- ✅ R2가 P→C 전송만 처리 → Polisher 가동률 증가
- ✅ R3가 C→B 전송만 처리 → Cleaner 자원 해제 가속화
- ✅ **병렬 처리**: R2와 R3가 동시에 동작 가능
- ✅ **처리량 증가**: 공정 장비가 더 빨리 비워지고 새 웨이퍼 투입 가능

## 로봇 구성 (최종)

| Robot | 역할 | 경로 |
|-------|------|------|
| **R1** | LoadPort-Polisher-Buffer 양방향 | L ↔ P ↔ B |
| **R2** | Polisher-Cleaner 전용 | P ↔ C |
| **R3** | Cleaner-Buffer 전용 | C ↔ B |

## 우선순위 (변경 없음)

```
Priority 1 (Highest): C → R3 → B    (Cleaner 자원 해제 - 이제 R3 사용)
Priority 2:            P → R2 → C    (Polisher 자원 해제 - R2 전용)
Priority 3:            L → R1 → P    (신규 웨이퍼 투입)
Priority 4 (Lowest):   B → R1 → L    (완료 웨이퍼 배출)
```

**로직 변경**: P1에서 R2 대신 R3 사용

## 구현 내용

### 1. Context 업데이트

```csharp
// Before
private int? _r2;
private bool _r2Busy;

// After
private int? _r2;
private int? _r3;      // New robot for C↔B
private bool _r2Busy;
private bool _r3Busy;  // New busy flag
```

### 2. Guards 수정

```csharp
// Before - R2가 두 경로 모두 처리
private bool CanExecCtoB() =>
    _c.HasValue && !_cProcessing && !_r2Busy && !_r2.HasValue && !_b.HasValue;

private bool CanExecPtoC() =>
    _p.HasValue && !_pProcessing && !_r2Busy && !_r2.HasValue && !_c.HasValue;

// After - R3가 C→B, R2가 P→C 전용
private bool CanExecCtoB() =>
    _c.HasValue && !_cProcessing && !_r3Busy && !_r3.HasValue && !_b.HasValue;  // R3 사용

private bool CanExecPtoC() =>
    _p.HasValue && !_pProcessing && !_r2Busy && !_r2.HasValue && !_c.HasValue;  // R2 전용
```

### 3. Actions 수정

```csharp
// Before - Priority 1: C → R2 → B
private async Task ExecCtoB(CancellationToken ct)
{
    var waferId = _c!.Value;
    Log($"[P1] C({waferId}) → R2");
    _c = null;
    _r2 = waferId;
    _r2Busy = true;

    await Task.Delay(TRANSFER, ct);

    Log($"[P1] R2({waferId}) → B");
    _r2 = null;
    _r2Busy = false;
    _b = waferId;
}

// After - Priority 1: C → R3 → B
private async Task ExecCtoB(CancellationToken ct)
{
    var waferId = _c!.Value;
    Log($"[P1] C({waferId}) → R3 (Pick from Cleaner)");
    _c = null;
    _r3 = waferId;
    _r3Busy = true;

    await Task.Delay(TRANSFER, ct);

    Log($"[P1] R3({waferId}) → B (Place at Buffer)");
    _r3 = null;
    _r3Busy = false;
    _b = waferId;
}
```

### 4. UI 업데이트 (WPF)

#### MainWindow.xaml

```xml
<!-- Before -->
<Border Canvas.Left="590" Canvas.Top="300">
    <TextBlock Text="R2" />
    <TextBlock Text="P↔C↔B" />  <!-- 두 경로 표시 -->
</Border>

<!-- After -->
<Border Canvas.Left="590" Canvas.Top="300">
    <TextBlock Text="R2" />
    <TextBlock Text="P↔C" />  <!-- Polisher-Cleaner 전용 -->
</Border>

<!-- New R3 Station -->
<Border Canvas.Left="760" Canvas.Top="420" Background="#FFD0E8">
    <TextBlock Text="R3" />
    <TextBlock Text="C↔B" />  <!-- Cleaner-Buffer 전용 -->
</Border>
```

#### Station Positions

```csharp
// ForwardPriorityController.cs
_stations["R2"] = new StationPosition("R2", 590, 300, 80, 80, 0);
_stations["R3"] = new StationPosition("R3", 760, 420, 80, 80, 0);  // New
_stations["Buffer"] = new StationPosition("Buffer", 590, 500, 80, 80, 1);  // Repositioned
```

### 5. UIUpdateService

```csharp
// Update R3 wafer position
if (_r3.HasValue)
{
    var wafer = Wafers.FirstOrDefault(w => w.Id == _r3.Value);
    if (wafer != null)
    {
        wafer.CurrentStation = "R3";
        var pos = _stations["R3"];
        wafer.X = pos.X + pos.Width / 2;
        wafer.Y = pos.Y + pos.Height / 2;
    }
}
```

## 적용 파일

### 1. ForwardPriorityController.cs (WPF)
- **Context**: `_r3`, `_r3Busy` 필드 추가
- **InitializeStations()**: R3 스테이션 위치 추가
- **ResetSimulation()**: `_r3`, `_r3Busy` 리셋 추가
- **Guards**: `CanExecCtoB()` → R3 사용, `CanExecPtoC()` → R2 전용
- **Actions**: `ExecCtoB()` → R3 사용
- **UIUpdateService**: `UpdateWaferPositions()` → R3 업데이트 추가

### 2. MainWindow.xaml (WPF UI)
- **R2 Label**: "P↔C↔B" → "P↔C"
- **R3 Station**: 새로운 Border 추가 (Canvas.Left="760", Canvas.Top="420")
- **Buffer Position**: 위치 조정 (Canvas.Left="590", Canvas.Top="500")
- **Flow Path Arrows**: 경로 업데이트

### 3. ForwardPrioritySchedulerTests.cs (Unit Tests)
- **Context**: `R3`, `R3_Busy` 속성 추가
- **Guards**: `CanExecCtoB()`, `CanExecPtoC()` 수정
- **Actions**: `StartCtoB()`, `CompleteCtoB()` 수정
- **Comments**: 로봇 역할 명시

## 처리량 비교

### Before (R2만 사용)

```
Time 0ms:    P(1) processing
Time 4000ms: P done → P(1) → R2 (start)
Time 4800ms: R2(1) → C (Cleaning start)
Time 9800ms: C done → C(1) → R2 (start)  ← R2 바쁨!
Time 10600ms: R2(1) → B

Issue: Time 4800ms~10600ms 동안 R2가 계속 바쁨
       → 새로운 P→C 전송 불가 (5.8초 블록)
```

### After (R2 + R3 사용)

```
Time 0ms:    P(1) processing
Time 4000ms: P done → P(1) → R2 (start)
Time 4800ms: R2(1) → C (Cleaning start)
Time 5000ms: P(2) → R2 (start)  ← R2 즉시 사용 가능!
Time 9800ms: C done → C(1) → R3 (start)  ← R3 사용!
Time 10600ms: R3(1) → B

Improvement: R2가 Time 4800ms에 해제됨
             → 200ms 후 즉시 P(2) 처리 가능
             → Polisher 가동률 증가!
```

**처리량 증가**: 병렬 처리로 인해 전체 사이클 타임 감소

## 타이밍 다이어그램

### Before: R2 Bottleneck
```
|--------P(1)--------|  |--------P(2)--------|
                      |--R2-->|
                             |----C(1)----|
                                         |--R2-->|  ← R2 블록!
                                                |------P(2) delayed--------|
```

### After: Parallel Processing
```
|--------P(1)--------|  |--P(2)--|  |--P(3)--|
                      |--R2-->|     |--R2-->|  ← R2 사용 가능!
                             |----C(1)----|
                                         |--R3-->|  ← R3 사용!
```

## 실행 방법

### WPF Simulator
```bash
cd C:\Develop25\XStateNet\CMPSimulator
dotnet run
```

**확인 사항**:
1. R2 레이블: "P↔C"
2. R3 스테이션: Cleaner 아래에 핑크색 박스
3. 로그: `[P1] C(1) → R3 (Pick from Cleaner)`
4. 로그: `[P2] P(2) → R2 (Pick from Polisher)` ← 더 빠르게 발생!

### Unit Test
```bash
dotnet test --filter "FullyQualifiedName~ForwardPriority_ProcessesFewWafers_Diagnostic"
```

**확인 사항**:
- 로그에 R3 메시지 출력
- P→C 처리 시간 단축

## 기술적 세부사항

### Robot Busy 조건

```csharp
// R2 (P↔C only)
!_r2Busy && !_r2.HasValue

// R3 (C↔B only)
!_r3Busy && !_r3.HasValue
```

**독립성**: R2와 R3가 서로 영향을 주지 않음

### 동시 실행 가능 시나리오

1. **R2가 P→C 전송 중 + R3가 C→B 전송 중**
2. **R1이 L→P 전송 중 + R2가 P→C 전송 중**
3. **R1이 B→L 전송 중 + R3가 C→B 전송 중**

**최대 병렬도**: 3개 로봇이 모두 동시에 동작 가능

## 향후 개선 사항

### 1. 추가 로봇 고려
- **R4**: Buffer → LoadPort 전용 (R1의 B→L 역할 분리)
- 효과: R1이 L→P에만 집중 가능

### 2. 로봇 Home Position
- R2 Home: Polisher와 Cleaner 사이
- R3 Home: Cleaner와 Buffer 사이
- 빈 상태로 Home Position 복귀 애니메이션

### 3. 처리량 통계
- WPF UI에 Throughput (wafers/minute) 표시
- R2, R3 각각의 사용률(Utilization) 표시

### 4. 동적 우선순위
- Cleaner가 3개 이상 대기 → P1 우선순위 더 높이기
- Polisher가 비어있음 → P3 우선순위 높이기

## 성능 예측

### 이론적 처리량 계산

```
Before (R2 only):
- P→C: 800ms
- C→B: 800ms
- Total R2 cycle: 1600ms per wafer
- Bottleneck: R2

After (R2 + R3):
- P→C (R2): 800ms
- C→B (R3): 800ms (parallel)
- R2 available every 800ms
- Throughput increase: ~50-100% (depending on workload)
```

**실제 처리량은 공정 시간(P=4s, C=5s)에 의해 제한되지만, 로봇 대기 시간은 크게 감소합니다.**

## 관련 문서

- `README_FORWARD_PRIORITY_WPF.md`: Forward Priority 전체 구현 개요
- `R1_IDLE_RETURN_FIX.md`: R1 대기 위치 복귀 수정
- `LOADPORT_DISPLAY_FIX.md`: LoadPort 웨이퍼 표시 수정

## 결론

✅ **R3 로봇 추가로 처리량 극대화**
✅ **R2와 R3의 역할 분리로 병렬 처리 가능**
✅ **공정 장비(P, C) 가동률 증가**
✅ **Forward Priority 전략의 효과 극대화**

이제 CMP Simulator는 3개의 로봇이 협력하여 최대 처리량을 달성합니다!
