# Sync Mode Design Document

## 개요

Sync Mode는 R1, R2, R3 로봇이 동시에 픽업하고 배치하는 동기화 스케줄링 모드입니다.
Forward Priority 모드의 최적 상태를 시각적으로 보여주는 디버깅/데모 모드로 활용할 수 있습니다.

**주요 특징:**
- 3개 로봇의 동기화된 동작 (Pick → Hold → Place)
- 처리 시간 조정: P, C 모두 5초 (관찰 용이)
- 배속 조절: 0.1x ~ 5.0x (슬로우 모션 ~ 고속 재생)

---

## 동작 원리

### Phase 1: Synchronized Pickup (동시 픽업)
모든 로봇이 idle 상태이고, 각 source station이 준비되었을 때 동시에 픽업 명령 전송

```
조건:
- R1 = idle && LoadPort에 pending wafer 존재
- R2 = idle && Polisher = done
- R3 = idle && Cleaner = done

명령:
- R1: TRANSFER(LoadPort → Polisher, wafer=next)
- R2: TRANSFER(Polisher → Cleaner, wafer=current)
- R3: TRANSFER(Cleaner → Buffer, wafer=current)
```

### Phase 2: Synchronized Holding (동시 대기)
모든 로봇이 holding 상태에 도달할 때까지 대기

```
대기 조건:
- R1.state = holding
- R2.state = holding
- R3.state = holding
```

### Phase 3: Destination Check (목적지 확인)
모든 destination이 준비될 때까지 대기

```
Destination Ready 조건:
- Polisher = empty (R1의 destination)
- Cleaner = empty (R2의 destination)
- Buffer = empty (R3의 destination)
```

### Phase 4: Synchronized Placement (동시 배치)
모든 destination이 ready가 되면 동시에 DESTINATION_READY 전송

```
명령:
- R1: DESTINATION_READY
- R2: DESTINATION_READY
- R3: DESTINATION_READY
```

### Phase 5: Synchronized Return (동시 복귀)
모든 로봇이 idle로 복귀하면 Phase 1로 돌아감

---

## 시간 설정 및 배속 조절

### 기본 시간 설정 (BaseTime)

```csharp
// 기본 처리 시간 (ms)
private const int BASE_POLISHING_TIME = 5000;  // 5초
private const int BASE_CLEANING_TIME = 5000;   // 5초
private const int BASE_TRANSFER_TIME = 800;    // 0.8초 (로봇 이동)
```

### 배속 옵션 (SimulationSpeed)

```csharp
public enum SimulationSpeed
{
    Slow_0_1x,      // 0.1배속 (10배 느리게) - 50초 processing
    Slow_0_2x,      // 0.2배속 (5배 느리게)  - 25초 processing
    Slow_0_5x,      // 0.5배속 (2배 느리게)  - 10초 processing
    Normal_1_0x,    // 1.0배속 (정상)        - 5초 processing
    Fast_2_0x,      // 2.0배속 (2배 빠르게)  - 2.5초 processing
    Fast_5_0x       // 5.0배속 (5배 빠르게)  - 1초 processing
}
```

### 실제 시간 계산

```csharp
private double _speedMultiplier = 1.0;

public void SetSimulationSpeed(SimulationSpeed speed)
{
    _speedMultiplier = speed switch
    {
        SimulationSpeed.Slow_0_1x => 0.1,
        SimulationSpeed.Slow_0_2x => 0.2,
        SimulationSpeed.Slow_0_5x => 0.5,
        SimulationSpeed.Normal_1_0x => 1.0,
        SimulationSpeed.Fast_2_0x => 2.0,
        SimulationSpeed.Fast_5_0x => 5.0,
        _ => 1.0
    };

    // 스테이션 처리 시간 업데이트
    UpdateProcessingTimes();
}

private int GetAdjustedTime(int baseTime)
{
    return (int)(baseTime / _speedMultiplier);
}
```

### 동적 시간 업데이트

State Machine 생성 시 시간을 동적으로 전달하므로, 시뮬레이션 시작 전에 배속을 설정해야 합니다.

**해결 방안:**
1. **재시작 방식**: 배속 변경 시 모든 state machine을 재생성
2. **서비스 시간 동적 변경**: State machine의 service 실행 시간을 동적으로 조정

**권장: 재시작 방식**
- 구현 간단
- 상태 일관성 보장
- UI에 "배속 변경 시 시뮬레이션이 재시작됩니다" 메시지 표시

---

## 구현 계획

### 1. SimulationSpeed Enum 추가

```csharp
namespace CMPSimulator.Models;

/// <summary>
/// Simulation speed multiplier
/// Controls how fast the simulation runs
/// </summary>
public enum SimulationSpeed
{
    Slow_0_1x,      // 10x slower (for detailed observation)
    Slow_0_2x,      // 5x slower
    Slow_0_5x,      // 2x slower
    Normal_1_0x,    // Normal speed
    Fast_2_0x,      // 2x faster
    Fast_5_0x       // 5x faster (for quick testing)
}

public static class SimulationSpeedExtensions
{
    public static double GetMultiplier(this SimulationSpeed speed)
    {
        return speed switch
        {
            SimulationSpeed.Slow_0_1x => 0.1,
            SimulationSpeed.Slow_0_2x => 0.2,
            SimulationSpeed.Slow_0_5x => 0.5,
            SimulationSpeed.Normal_1_0x => 1.0,
            SimulationSpeed.Fast_2_0x => 2.0,
            SimulationSpeed.Fast_5_0x => 5.0,
            _ => 1.0
        };
    }

    public static string GetDisplayName(this SimulationSpeed speed)
    {
        return speed switch
        {
            SimulationSpeed.Slow_0_1x => "0.1x (Slow Motion)",
            SimulationSpeed.Slow_0_2x => "0.2x (Very Slow)",
            SimulationSpeed.Slow_0_5x => "0.5x (Slow)",
            SimulationSpeed.Normal_1_0x => "1.0x (Normal)",
            SimulationSpeed.Fast_2_0x => "2.0x (Fast)",
            SimulationSpeed.Fast_5_0x => "5.0x (Very Fast)",
            _ => "1.0x (Normal)"
        };
    }
}
```

**파일:** `CMPSimulator/Models/SimulationSpeed.cs` (신규)

---

### 2. SchedulingMode Enum 추가

```csharp
public enum SchedulingMode
{
    ForwardPriority,  // 기존 우선순위 기반 스케줄링
    Synchronized      // 동기화 스케줄링 (신규)
}
```

**파일:** `CMPSimulator/Models/SchedulingMode.cs` (신규)

---

### 2. SchedulerMachine 수정

#### 필드 추가
```csharp
private SchedulingMode _schedulingMode = SchedulingMode.ForwardPriority;
private readonly HashSet<string> _robotsInHolding = new();
```

#### CheckPriorities 분기
```csharp
private void CheckPriorities(OrchestratedContext ctx)
{
    if (_schedulingMode == SchedulingMode.ForwardPriority)
    {
        CheckPrioritiesForwardPriority(ctx);
    }
    else if (_schedulingMode == SchedulingMode.Synchronized)
    {
        CheckPrioritiesSync(ctx);
    }
}
```

#### Sync 모드 로직
```csharp
private void CheckPrioritiesSync(OrchestratedContext ctx)
{
    // Phase 1: Check if all robots are idle
    if (GetRobotState("R1") != "idle" ||
        GetRobotState("R2") != "idle" ||
        GetRobotState("R3") != "idle")
    {
        return; // Wait for all robots to be idle
    }

    // Phase 2: Check if all sources are ready
    bool r1SourceReady = _lPending.Count > 0; // LoadPort has wafer
    bool r2SourceReady = GetStationState("polisher") == "done";
    bool r3SourceReady = GetStationState("cleaner") == "done";

    if (!r1SourceReady || !r2SourceReady || !r3SourceReady)
    {
        _logger("[Scheduler] [SYNC] Waiting for all sources to be ready...");
        return;
    }

    // Phase 3: Send synchronized TRANSFER commands
    _logger("[Scheduler] [SYNC] ✓ All robots idle and sources ready! Sending synchronized TRANSFER commands...");

    // R1: L → P
    int r1Wafer = _lPending[0];
    _lPending.RemoveAt(0);
    ctx.RequestSend("R1", "TRANSFER", new JObject
    {
        ["waferId"] = r1Wafer,
        ["from"] = "LoadPort",
        ["to"] = "polisher"
    });

    // R2: P → C
    int? r2Wafer = _stationWafers.GetValueOrDefault("polisher");
    if (r2Wafer != null)
    {
        ctx.RequestSend("R2", "TRANSFER", new JObject
        {
            ["waferId"] = r2Wafer,
            ["from"] = "polisher",
            ["to"] = "cleaner"
        });
    }

    // R3: C → B
    int? r3Wafer = _stationWafers.GetValueOrDefault("cleaner");
    if (r3Wafer != null)
    {
        ctx.RequestSend("R3", "TRANSFER", new JObject
        {
            ["waferId"] = r3Wafer,
            ["from"] = "cleaner",
            ["to"] = "buffer"
        });
    }

    _logger("[Scheduler] [SYNC] Synchronized transfers initiated!");
}
```

#### Holding 상태 추적
```csharp
// onRobotStatus 액션 내부에 추가
if (state == "holding")
{
    _robotsInHolding.Add(robot);

    if (_schedulingMode == SchedulingMode.Synchronized)
    {
        CheckSynchronizedHolding(ctx);
    }
    else
    {
        // 기존 ForwardPriority 로직
    }
}
else
{
    _robotsInHolding.Remove(robot);
}
```

#### 동기화된 Holding 체크
```csharp
private void CheckSynchronizedHolding(OrchestratedContext ctx)
{
    // Wait for all robots to reach holding state
    if (!_robotsInHolding.Contains("R1") ||
        !_robotsInHolding.Contains("R2") ||
        !_robotsInHolding.Contains("R3"))
    {
        _logger("[Scheduler] [SYNC] Waiting for all robots to reach holding state...");
        return;
    }

    // Check if all destinations are ready
    bool r1DestReady = GetStationState("polisher") == "empty" || GetStationState("polisher") == "idle";
    bool r2DestReady = GetStationState("cleaner") == "empty" || GetStationState("cleaner") == "idle";
    bool r3DestReady = GetStationState("buffer") == "empty";

    if (!r1DestReady || !r2DestReady || !r3DestReady)
    {
        _logger($"[Scheduler] [SYNC] Waiting for destinations: P={GetStationState("polisher")}, C={GetStationState("cleaner")}, B={GetStationState("buffer")}");
        return;
    }

    // All destinations ready - send synchronized DESTINATION_READY
    _logger("[Scheduler] [SYNC] ✓ All destinations ready! Synchronized placement...");

    ctx.RequestSend("R1", "DESTINATION_READY", new JObject());
    ctx.RequestSend("R2", "DESTINATION_READY", new JObject());
    ctx.RequestSend("R3", "DESTINATION_READY", new JObject());

    _robotsInHolding.Clear();
}
```

---

### 3. OrchestratedForwardPriorityController 수정

#### 필드 추가
```csharp
private SchedulingMode _schedulingMode = SchedulingMode.ForwardPriority;
private SimulationSpeed _simulationSpeed = SimulationSpeed.Normal_1_0x;

// 기본 시간 (5초 처리 시간)
private const int BASE_POLISHING = 5000;
private const int BASE_CLEANING = 5000;
private const int BASE_TRANSFER = 800;
```

#### 메서드 추가
```csharp
public void SetSchedulingMode(SchedulingMode mode)
{
    if (_schedulingMode != mode)
    {
        _schedulingMode = mode;
        _scheduler?.SetSchedulingMode(mode);
        Log($"Scheduling mode changed to: {mode}");
    }
}

public void SetSimulationSpeed(SimulationSpeed speed)
{
    if (_simulationSpeed != speed)
    {
        _simulationSpeed = speed;
        Log($"⚠ Simulation speed changed to: {speed.GetDisplayName()}");
        Log("⚠ Please restart simulation for speed change to take effect");
    }
}

private int GetAdjustedTime(int baseTime)
{
    return (int)(baseTime / _simulationSpeed.GetMultiplier());
}
```

#### InitializeStateMachines 수정
```csharp
private void InitializeStateMachines()
{
    // 배속 적용된 시간 계산
    int polishingTime = GetAdjustedTime(BASE_POLISHING);
    int cleaningTime = GetAdjustedTime(BASE_CLEANING);
    int transferTime = GetAdjustedTime(BASE_TRANSFER);

    // Create state machines with adjusted times
    _scheduler = new SchedulerMachine(_orchestrator, Log);
    _scheduler.SetSchedulingMode(_schedulingMode);

    _polisher = new PolisherMachine("polisher", _orchestrator, polishingTime, Log);
    _cleaner = new CleanerMachine("cleaner", _orchestrator, cleaningTime, Log);
    _buffer = new BufferMachine(_orchestrator, Log);
    _r1 = new RobotMachine("R1", _orchestrator, transferTime, Log);
    _r2 = new RobotMachine("R2", _orchestrator, transferTime, Log);
    _r3 = new RobotMachine("R3", _orchestrator, transferTime, Log);

    // ... rest of initialization

    Log($"✓ State machines created (Speed: {_simulationSpeed.GetDisplayName()})");
    Log($"  - Polishing: {polishingTime}ms, Cleaning: {cleaningTime}ms, Transfer: {transferTime}ms");
}
```

---

### 4. UI 변경

#### MainWindow.xaml
```xml
<StackPanel Orientation="Horizontal" Margin="10,10,10,5">
    <!-- 스케줄링 모드 선택 -->
    <Label Content="Scheduling Mode:" VerticalAlignment="Center"/>
    <ComboBox x:Name="SchedulingModeComboBox"
              SelectedIndex="0"
              Width="150"
              Margin="5,0,20,0"
              SelectionChanged="SchedulingModeComboBox_SelectionChanged">
        <ComboBoxItem Content="Forward Priority"/>
        <ComboBoxItem Content="Synchronized"/>
    </ComboBox>

    <!-- 시뮬레이션 속도 선택 -->
    <Label Content="Speed:" VerticalAlignment="Center"/>
    <ComboBox x:Name="SimulationSpeedComboBox"
              SelectedIndex="3"
              Width="150"
              Margin="5,0,0,0"
              SelectionChanged="SimulationSpeedComboBox_SelectionChanged">
        <ComboBoxItem Content="0.1x (Slow Motion)" Tag="Slow_0_1x"/>
        <ComboBoxItem Content="0.2x (Very Slow)" Tag="Slow_0_2x"/>
        <ComboBoxItem Content="0.5x (Slow)" Tag="Slow_0_5x"/>
        <ComboBoxItem Content="1.0x (Normal)" Tag="Normal_1_0x"/>
        <ComboBoxItem Content="2.0x (Fast)" Tag="Fast_2_0x"/>
        <ComboBoxItem Content="5.0x (Very Fast)" Tag="Fast_5_0x"/>
    </ComboBox>
</StackPanel>

<!-- 경고 메시지 -->
<TextBlock x:Name="SpeedChangeWarning"
           Text="⚠ Speed change requires simulation restart"
           Foreground="Orange"
           Margin="15,0,0,5"
           Visibility="Collapsed"/>
```

#### MainWindow.xaml.cs
```csharp
private void SchedulingModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (_controller == null) return;

    var selectedIndex = SchedulingModeComboBox.SelectedIndex;
    var mode = selectedIndex == 0
        ? SchedulingMode.ForwardPriority
        : SchedulingMode.Synchronized;

    _controller.SetSchedulingMode(mode);
}

private void SimulationSpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (_controller == null) return;

    var selectedItem = SimulationSpeedComboBox.SelectedItem as ComboBoxItem;
    if (selectedItem?.Tag is string tagValue)
    {
        if (Enum.TryParse<SimulationSpeed>(tagValue, out var speed))
        {
            _controller.SetSimulationSpeed(speed);

            // Show warning if simulation is running
            if (!StartButton.IsEnabled)
            {
                SpeedChangeWarning.Visibility = Visibility.Visible;
            }
        }
    }
}

private void StartButton_Click(object sender, RoutedEventArgs e)
{
    // Hide warning when starting simulation
    SpeedChangeWarning.Visibility = Visibility.Collapsed;

    // ... rest of start logic
}

private void ResetButton_Click(object sender, RoutedEventArgs e)
{
    // Hide warning on reset
    SpeedChangeWarning.Visibility = Visibility.Collapsed;

    // ... rest of reset logic
}
```

#### OrchestratedForwardPriorityController
```csharp
public void SetSchedulingMode(SchedulingMode mode)
{
    _scheduler?.SetSchedulingMode(mode);
}
```

---

## 사용 시나리오

### 1. 정상 동작 (Ideal Case)
```
Cycle 1:
- R1 picks W1 from L → holds → places at P
- R2 picks W0 from P → holds → places at C (처음엔 없음)
- R3 picks W-1 from C → holds → places at B (처음엔 없음)

Cycle 2:
- R1 picks W2 from L → holds → places at P
- R2 picks W1 from P → holds → places at C
- R3 picks W0 from C → holds → places at B

...계속 동기화 유지
```

### 2. Buffer 처리 (Wafer Return)
Buffer에 wafer가 쌓이면 R1이 B→L 작업을 해야 하므로 동기화가 일시적으로 깨질 수 있음.

**해결 방안:**
- Sync 모드에서는 B→L을 별도 Phase로 처리
- 또는 R1이 항상 L→P만 하고, 별도 로봇(R4?)이 B→L 담당

---

## 장점

1. **최대 Throughput 시각화**: 3개 로봇이 동시에 움직이는 이상적인 상태
2. **디버깅 용이**: 동기화 문제를 쉽게 발견
3. **데모 효과**: 시각적으로 인상적인 동작

## 단점

1. **현실성 낮음**: 실제 반도체 장비는 비동기 동작
2. **Buffer 처리 복잡**: B→L 작업이 동기화를 깨뜨림
3. **초기 상태 제약**: 모든 스테이션이 특정 상태여야 시작 가능

---

## 구현 우선순위

### Phase 1: 기본 구현
- [ ] SchedulingMode enum 추가
- [ ] SchedulerMachine에 mode 필드 및 분기 로직
- [ ] CheckPrioritiesSync() 기본 구현
- [ ] CheckSynchronizedHolding() 구현

### Phase 2: UI 연동
- [ ] MainWindow에 모드 선택 ComboBox 추가
- [ ] Controller에 SetSchedulingMode() 메서드 추가
- [ ] 런타임 모드 전환 지원

### Phase 3: 고급 기능
- [ ] Buffer 처리 로직 개선
- [ ] 동기화 실패 시 자동 복구
- [ ] 통계 정보 수집 (사이클 시간, throughput)

---

## 참고사항

- Forward Priority 모드는 기존 기능 유지
- Sync 모드는 선택적 기능으로 추가
- 두 모드는 런타임에 전환 가능하도록 설계
