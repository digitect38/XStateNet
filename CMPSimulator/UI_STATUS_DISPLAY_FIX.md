# UI 상태 문자 표시 수정

## 문제

"UI 상태 문자 표시에 문제가 있어"

Polisher와 Cleaner의 상태 텍스트("Processing" / "Idle")가 실제 Processing 상태와 동기화되지 않음.

## 근본 원인

### 1. 잘못된 상태 체크

**Before** (MainWindow.xaml.cs:114-119):
```csharp
// Update Polisher status (check if any wafer is at Polisher)
var polisherBusy = _controller.Wafers.Any(w => w.CurrentStation == "Polisher");
PolisherStatusText.Text = polisherBusy ? "Processing" : "Idle";

// Update Cleaner status (check if any wafer is at Cleaner)
var cleanerBusy = _controller.Wafers.Any(w => w.CurrentStation == "Cleaner");
CleanerStatusText.Text = cleanerBusy ? "Processing" : "Idle";
```

**문제점**:
- `CurrentStation == "Polisher"` 체크만 함
- 하지만 **Processing flag (`_pProcessing`)는 체크하지 않음**
- 웨이퍼가 P에 있어도 Processing이 끝났을 수 있음

**시나리오**:
```
Time 800ms: R1 → P (Place)
         _p = 1, _pProcessing = true
         Wafer.CurrentStation = "Polisher"
         UI: "Processing" ✓

Time 4800ms: Polishing 완료
         _pProcessing = false
         Wafer.CurrentStation = "Polisher" (아직!)
         UI: "Processing" ✗ (잘못됨! 실제로는 완료)

Time 4900ms: P → R2 (Pick)
         _p = null
         Wafer.CurrentStation = "R2"
         UI: "Idle" ✓
```

**결과**: Processing이 끝나도 Pick되기 전까지 "Processing" 표시 유지 (부정확!)

### 2. 업데이트 타이밍 문제

**Before**:
```csharp
private void Log(string message)
{
    LogTextBlock.Text += message + Environment.NewLine;

    // Update station displays when relevant events occur
    if (message.Contains("LoadPort") || message.Contains("Polisher") || message.Contains("Cleaner"))
    {
        UpdateStationDisplays();  // ← 로그 메시지에 키워드가 있을 때만!
    }
}
```

**문제점**:
- Processing 완료는 `Task.Delay().ContinueWith()`에서 발생
- 로그 메시지는 나중에 출력
- 하지만 `_pProcessing = false`는 **먼저** 설정됨
- UI 업데이트는 로그 메시지를 기다림

**타이밍**:
```
Time 4800ms: _pProcessing = false 설정
Time 4801ms: Log("Polishing Done ✓") 출력
Time 4801ms: UpdateStationDisplays() 호출
         하지만 이미 1ms 지연!
```

더 큰 문제: **UIUpdateService(50ms 주기)는 `UpdateStationDisplays()`를 호출하지 않음!**

---

## 해결 방법

### 1. Processing Flag를 Public Property로 노출

**ForwardPriorityController.cs**:
```csharp
// Public properties for UI status display
public bool IsPolisherProcessing
{
    get
    {
        lock (_stateLock)
        {
            return _pProcessing;
        }
    }
}

public bool IsCleanerProcessing
{
    get
    {
        lock (_stateLock)
        {
            return _cProcessing;
        }
    }
}
```

**장점**:
- Thread-safe (lock 사용)
- 실제 Processing flag 직접 반환
- UI가 정확한 상태 확인 가능

### 2. UI 업데이트 이벤트 추가

**ForwardPriorityController.cs**:
```csharp
public event EventHandler? StationStatusChanged;  // New event for UI status updates
```

**UIUpdateService에서 이벤트 발생**:
```csharp
private async Task UIUpdateService(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(50, ct);  // 50ms for smoother UI updates

        Application.Current?.Dispatcher.Invoke(() =>
        {
            UpdateWaferPositions();

            // Notify UI to update station status displays
            StationStatusChanged?.Invoke(this, EventArgs.Empty);
        });
    }
}
```

**장점**:
- 50ms마다 정기적으로 UI 업데이트 트리거
- 로그 메시지와 무관하게 동작
- Processing 상태 변경 즉시 반영 (최대 50ms 지연)

### 3. MainWindow에서 정확한 상태 표시

**MainWindow.xaml.cs**:
```csharp
private void UpdateStationDisplays()
{
    // Update LoadPort count
    var loadPortCount = _controller.Wafers.Count(w => w.CurrentStation == "LoadPort");
    LoadPortCountText.Text = $"{loadPortCount}/25";

    // Update Polisher status - use Processing flag from controller
    PolisherStatusText.Text = _controller.IsPolisherProcessing ? "Processing" : "Idle";

    // Update Cleaner status - use Processing flag from controller
    CleanerStatusText.Text = _controller.IsCleanerProcessing ? "Processing" : "Idle";
}

private void Controller_StationStatusChanged(object? sender, EventArgs e)
{
    // Already on UI thread (called from Dispatcher.Invoke in UIUpdateService)
    UpdateStationDisplays();
}
```

**장점**:
- `IsPolisherProcessing` 직접 체크 (정확!)
- 50ms마다 자동 업데이트
- Thread-safe (property가 lock 사용)

---

## 동작 비교

### Before (부정확한 상태 표시)

```
Time 800ms: R1 → P
         _p = 1, _pProcessing = true
         Wafer.CurrentStation = "Polisher"
         UpdateStationDisplays()
         → polisherBusy = true (w.CurrentStation == "Polisher")
         → UI: "Processing" ✓

Time 4800ms: Polishing 완료
         _pProcessing = false
         (UpdateStationDisplays() 호출 안 됨!)
         → UI: "Processing" ✗ (여전히!)

Time 4900ms: P → R2
         _p = null
         Wafer.CurrentStation = "R2"
         UpdateStationDisplays()
         → polisherBusy = false (웨이퍼 없음)
         → UI: "Idle" ✓

100ms 동안 잘못된 상태 표시!
```

### After (정확한 상태 표시)

```
Time 800ms: R1 → P
         _p = 1, _pProcessing = true
         Wafer.CurrentStation = "Polisher"
Time 850ms: UIUpdateService
         → StationStatusChanged 이벤트
         → IsPolisherProcessing = true
         → UI: "Processing" ✓

Time 4800ms: Polishing 완료
         _pProcessing = false

Time 4850ms: UIUpdateService (50ms 후)
         → StationStatusChanged 이벤트
         → IsPolisherProcessing = false
         → UI: "Idle" ✓ (즉시 반영!)

Time 4900ms: P → R2
         _p = null
         UI: 이미 "Idle" 표시 중

최대 50ms 지연, 정확한 상태 표시!
```

---

## 타이밍 분석

### Processing 상태 변경 → UI 반영

**Before**:
```
Time 4800ms: _pProcessing = false
Time 4900ms: Pick 시작 (로그 출력)
         → UpdateStationDisplays()
         → UI 업데이트

지연: 100ms
```

**After**:
```
Time 4800ms: _pProcessing = false
Time 4850ms: UIUpdateService
         → StationStatusChanged 이벤트
         → IsPolisherProcessing 체크
         → UI 업데이트

지연: 50ms (UIUpdateService 주기)
```

**개선**: 100ms → 50ms (2배 빠름!)

---

## 검증 방법

### 1. 실행 및 관찰

```bash
cd C:\Develop25\XStateNet\CMPSimulator\bin\Debug\net8.0-windows
.\CMPSimulator.exe
```

### 2. 로그와 UI 상태 비교

**로그**:
```
[T+    800ms] 🔨 [Processing] P(1) Polishing START (will take 4000ms)
```

**UI (최대 50ms 후)**:
```
Polisher: Processing ← 800~850ms 사이에 표시
```

**로그**:
```
[T+   4800ms] ✅ [Processing] P(1) Polishing DONE (after 4000ms)
```

**UI (최대 50ms 후)**:
```
Polisher: Idle ← 4800~4850ms 사이에 표시
```

**로그**:
```
[T+   4900ms] [P2] P(1) → R2 (Pick from Polisher)
```

**UI**:
```
Polisher: Idle ← 이미 Idle 표시 중 (정확!)
```

### 3. Processing 중 확인

```
Processing 중:
- Polisher에 웨이퍼 있음
- UI: "Processing" ✓

Processing 완료:
- Polisher에 웨이퍼 여전히 있음 (아직 Pick 안 됨)
- UI: "Idle" ✓ (Processing flag 기준!)

Pick 시작:
- Polisher에서 웨이퍼 제거
- UI: "Idle" ✓ (유지)
```

---

## 추가 개선 사항

### 향후 가능한 확장

**Processing 진행률 표시**:
```csharp
public int PolisherProgressPercent
{
    get
    {
        lock (_stateLock)
        {
            if (!_pProcessing) return 0;
            // Calculate based on elapsed time
            return (int)(_polishingElapsed / POLISHING * 100);
        }
    }
}
```

**Processing 중인 웨이퍼 ID 표시**:
```csharp
public int? PolisherWaferId
{
    get
    {
        lock (_stateLock)
        {
            return _p;
        }
    }
}
```

**UI**:
```xml
<TextBlock Text="{Binding PolisherWaferId, StringFormat='Wafer {0}'}" />
<ProgressBar Value="{Binding PolisherProgressPercent}" Maximum="100" />
```

---

## 결론

### 문제

✅ **잘못된 체크**
- `CurrentStation == "Polisher"` 체크 (부정확)
- Processing flag 무시

✅ **업데이트 타이밍**
- 로그 메시지 의존
- 50ms UI 주기 미활용

✅ **지연**
- Processing 완료 → UI 반영: 100ms

### 해결

✅ **정확한 상태 체크**
- `IsPolisherProcessing` property (Processing flag 직접 체크)
- Thread-safe (lock 사용)

✅ **자동 업데이트**
- `StationStatusChanged` 이벤트 (50ms마다)
- 로그와 무관하게 동작

✅ **빠른 반응**
- Processing 완료 → UI 반영: 50ms

### 기대 효과

- ✅ Polisher/Cleaner 상태가 정확히 표시
- ✅ Processing 완료 즉시 "Idle"로 변경 (50ms 이내)
- ✅ 로그와 UI 상태가 일치
- ✅ 실시간 상태 모니터링 가능

**이제 UI 상태 문자 표시가 정확합니다!**
