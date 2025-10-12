# Event-Driven Architecture - Next Steps

## 현재 상황

Event-driven architecture 구조를 설계했으나, XStateNet API와 맞지 않는 부분이 있습니다.

## XStateNet API 확인 필요사항

### 1. EventBusOrchestrator API
```csharp
// 실제 API
public async Task<EventResult> SendEventAsync(
    string fromMachineId,
    string toMachineId,
    string eventName,
    object? payload = null
)

// 등록
public void RegisterMachine(string machineId, IStateMachine machine)
```

### 2. 필요한 수정사항

#### EventDrivenCMPController.cs 수정 필요:
1. `File.ReadAllText` → `System.IO.File.ReadAllText` 추가
2. `SendEvent` → `SendEventAsync` 사용
3. `IPureStateMachine` → `IStateMachine` 타입 변경
4. `machine.Start()` → API 확인 필요
5. `PureStateMachineContext` → 실제 context 타입 확인
6. Event 객체 생성 방식 확인

## 권장 접근 방식

###  Option 1: 간단한 프로토타입 먼저 만들기

1. **2개 Station만으로 시작**:
   - LoadPort → Polisher (1개 wafer만)
   - Event flow 검증

2. **기본 Event 통신 검증**:
```csharp
// LoadPort sends
await orchestrator.SendEventAsync(
    fromMachineId: "loadport",
    toMachineId: "polisher",
    eventName: "WAFER_ARRIVED",
    payload: new { waferId = 1 }
);
```

3. **State machine이 실제로 작동하는지 확인**

### Option 2: 현재 SimpleTransfer 방식 유지하고 개선

현재 작동하는 `CMPToolController.cs`를 기반으로:

1. **각 Station을 class로 분리**:
```csharp
public class PolisherStation
{
    public event EventHandler<WaferReadyEventArgs> WaferReady;

    public async Task AcceptWafer(int waferId)
    {
        // Process
        await Task.Delay(3000);
        WaferReady?.Invoke(this, new WaferReadyEventArgs(waferId));
    }
}
```

2. **Event handler 연결**:
```csharp
polisher.WaferReady += (s, e) => wtr2.TransferToNextStation(e.WaferId);
```

이 방식이 더 간단하고 빠르게 구현 가능합니다.

## 추천: Option 2 선택

Event-driven 개념은 유지하되, XStateNet의 복잡한 API보다는:
- C# Event/EventHandler 사용
- 각 Station을 독립적인 class로 분리
- State를 enum으로 관리
- Event를 통한 통신

이 방식이 더 명확하고 디버깅이 쉬울 것입니다.

## 다음 단계

사용자에게 물어보기:
1. XStateNet EventBusOrchestrator를 계속 사용하시겠습니까?
   - Yes → API 문서 참고하여 수정
   - No → C# Event 기반으로 재구현

2. 아니면 현재 SimpleTransfer 방식이 충분하신가요?
   - 현재도 pipeline parallelism은 작동합니다
   - Buffer도 작동합니다
   - 다만 "각 Station이 독립적 상태머신"은 아닙니다

## State Machine의 진정한 의미

State machine이 꼭 XStateNet을 써야 하는 것은 아닙니다:

```csharp
public class PolisherStateMachine
{
    private enum State { Idle, Processing, Dispatching }
    private State _currentState = State.Idle;

    public void HandleEvent(string eventType, object payload)
    {
        switch (_currentState)
        {
            case State.Idle when eventType == "WAFER_ARRIVED":
                AcceptWafer(payload);
                _currentState = State.Processing;
                StartProcessing();
                break;

            case State.Processing when eventType == "PROCESSING_COMPLETE":
                _currentState = State.Dispatching;
                DispatchWafer();
                break;

            case State.Dispatching when eventType == "WAFER_PICKED_UP":
                _currentState = State.Idle;
                break;
        }
    }
}
```

이 방식도 완전한 상태머신이며, 더 간단하고 명확합니다.
