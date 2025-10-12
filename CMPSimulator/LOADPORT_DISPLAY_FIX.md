# LoadPort 웨이퍼 표시 문제 수정

## 문제점

**"LoadPort에서 웨이퍼가 줄기만 하고 다시 채워지지 않는 문제"**

B→L 경로를 통해 완료된 웨이퍼가 LoadPort로 돌아왔지만, UI에 표시되지 않았습니다.

### 원인

#### 1. CurrentStation 업데이트 누락

```csharp
// Before - UpdateWaferPositions()
foreach (var waferId in _lPending.Concat(_lCompleted))
{
    var wafer = Wafers.FirstOrDefault(w => w.Id == waferId);
    if (wafer != null && wafer.CurrentStation == "LoadPort")  // ← 문제!
    {
        // LoadPort 위치로 이동
    }
}
```

**문제 시나리오**:
1. Wafer(1)이 전체 경로를 완료: L → P → C → B
2. `ExecBtoL()`에서 `_lCompleted.Add(waferId)` 실행
3. 하지만 `wafer.CurrentStation`은 여전히 "Buffer"
4. `UpdateWaferPositions()`의 조건 `wafer.CurrentStation == "LoadPort"`가 false
5. **웨이퍼가 LoadPort로 이동하지 않음**

#### 2. Station 기반 카운트 조회

```csharp
// Before - MainWindow.xaml.cs
private void UpdateStationDisplays()
{
    var loadPortCount = _controller.Stations["LoadPort"].WaferSlots.Count;  // ← 문제!
    // ForwardPriorityController는 Station 기반이 아님!
}
```

**문제**:
- `ForwardPriorityController`는 `_lPending`, `_lCompleted` 필드로 직접 관리
- `Stations["LoadPort"].WaferSlots`는 사용되지 않음
- UI 카운트가 실제 상태를 반영하지 못함

## 해결책

### 1. CurrentStation 강제 업데이트

```csharp
// After - ForwardPriorityController.cs
private void UpdateWaferPositions()
{
    // Update LoadPort wafers (both pending and completed)
    foreach (var waferId in _lPending.Concat(_lCompleted))
    {
        var wafer = Wafers.FirstOrDefault(w => w.Id == waferId);
        if (wafer != null)
        {
            // Force CurrentStation to LoadPort for all wafers in LoadPort lists
            wafer.CurrentStation = "LoadPort";  // ← 명시적으로 설정!
            var slot = _waferOriginalSlots[waferId];
            var (x, y) = _stations["LoadPort"].GetWaferPosition(slot);
            wafer.X = x;
            wafer.Y = y;
        }
    }
}
```

**개선 효과**:
- ✅ `_lPending` 또는 `_lCompleted`에 있는 모든 웨이퍼의 `CurrentStation`을 "LoadPort"로 강제 설정
- ✅ 웨이퍼가 어디에서 돌아왔든 관계없이 LoadPort 위치로 이동
- ✅ 조건 체크 제거로 로직 단순화

### 2. Wafer 기반 카운트 조회

```csharp
// After - MainWindow.xaml.cs
private void UpdateStationDisplays()
{
    // Update LoadPort count (count wafers whose CurrentStation is LoadPort)
    var loadPortCount = _controller.Wafers.Count(w => w.CurrentStation == "LoadPort");
    LoadPortCountText.Text = $"{loadPortCount}/25";

    // Update Polisher status (check if any wafer is at Polisher)
    var polisherBusy = _controller.Wafers.Any(w => w.CurrentStation == "Polisher");
    PolisherStatusText.Text = polisherBusy ? "Processing" : "Idle";

    // Update Cleaner status (check if any wafer is at Cleaner)
    var cleanerBusy = _controller.Wafers.Any(w => w.CurrentStation == "Cleaner");
    CleanerStatusText.Text = cleanerBusy ? "Processing" : "Idle";
}
```

**개선 효과**:
- ✅ `Wafers` 컬렉션의 `CurrentStation` 속성을 직접 조회
- ✅ 실제 상태를 정확하게 반영
- ✅ ForwardPriorityController의 구조에 맞는 구현

## 시뮬레이션 흐름

### Before (문제)
```
Time 0ms:     L(1,2,3,...,25)  [25개]
Time 1000ms:  L(2,3,...,25) → P(1)  [24개]
...
Time 20000ms: L() → B(1) → ... → L(?)  [웨이퍼가 사라짐!]
```

### After (수정)
```
Time 0ms:     L(1,2,3,...,25)  [25개]
Time 1000ms:  L(2,3,...,25) → P(1)  [24개]
...
Time 20000ms: L() → B(1) → L(1)  [완료 웨이퍼가 다시 나타남!]
Time 25000ms: L(1,2,3,...,10)  [완료된 웨이퍼들이 LoadPort에 쌓임]
```

## 적용 파일

### 1. `Controllers/ForwardPriorityController.cs`
**수정 위치**: `UpdateWaferPositions()` 메서드 (line 302-317)
**변경 내용**:
- `wafer.CurrentStation == "LoadPort"` 조건 제거
- `wafer.CurrentStation = "LoadPort"` 명시적 설정 추가

### 2. `MainWindow.xaml.cs`
**수정 위치**: `UpdateStationDisplays()` 메서드 (line 143-156)
**변경 내용**:
- `_controller.Stations["LoadPort"].WaferSlots.Count` 제거
- `_controller.Wafers.Count(w => w.CurrentStation == "LoadPort")` 사용
- Polisher, Cleaner도 동일하게 수정

## 검증 방법

### 1. Visual 검증 (WPF)
```bash
cd C:\Develop25\XStateNet\CMPSimulator
dotnet run
```

**확인 사항**:
1. 초기 LoadPort에 25개 웨이퍼 표시
2. 웨이퍼가 L → P → C → B 경로로 이동
3. **B → L 경로로 돌아온 웨이퍼가 LoadPort에 다시 나타남**
4. LoadPort 카운트가 정확하게 업데이트됨

### 2. 로그 확인
```
[P4] B(1) → R1 (Pick from Buffer)
[P4] R1(1) → L (Place at LoadPort)
[P4] R1 returned to idle position (DONE)
```

Desktop의 `CMPSimulator_ForwardPriority.log` 파일에서 B→L 완료 확인

## 기술적 세부사항

### Context 구조
```csharp
_lPending     // [1,2,3,...,25] → 아직 처리되지 않은 웨이퍼
_lCompleted   // [] → [1] → [1,2] → ... → 완료되어 돌아온 웨이퍼
```

### UI 업데이트 주기
- `UIUpdateService`: 100ms 간격으로 폴링
- `UpdateWaferPositions()`: 모든 웨이퍼의 위치를 Context 상태와 동기화
- WPF `Dispatcher.Invoke`: UI 스레드에서 안전하게 업데이트

### 애니메이션
- 800ms 부드러운 이동 애니메이션 (Cubic Ease)
- B→L: 800ms (Transfer) + 400ms (Idle Return) = 1200ms 총 시간
- 웨이퍼가 LoadPort에 도착하면 원래 슬롯 위치로 애니메이션

## 관련 문서

- `R1_IDLE_RETURN_FIX.md`: R1 대기 위치 복귀 수정
- `README_FORWARD_PRIORITY_WPF.md`: Forward Priority 구현 개요
- `FORWARD_PRIORITY_IMPLEMENTATION.md`: 상세 구현 설명

## 결론

✅ **LoadPort 웨이퍼 표시 문제 해결**
✅ **완료된 웨이퍼가 다시 LoadPort에 나타남**
✅ **UI 카운트가 정확하게 표시됨**
✅ **ForwardPriorityController 구조에 맞는 구현**

이제 WPF Simulator가 완전한 순환 경로를 보여줍니다: L → P → C → B → L
