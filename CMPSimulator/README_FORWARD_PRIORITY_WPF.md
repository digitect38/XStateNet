# CMP Simulator - Forward Priority WPF Implementation

## 개요

React TypeScript 코드를 기반으로 한 **Forward Priority Scheduler**를 WPF Simulator에 통합했습니다.

## 실행 방법

### 1. 빌드
```bash
cd C:\Develop25\XStateNet\CMPSimulator
dotnet build
```

### 2. 실행
```bash
dotnet run
```

또는 Visual Studio에서 `CMPSimulator` 프로젝트를 시작 프로젝트로 설정하고 F5를 누릅니다.

## Forward Priority Scheduler

### 우선순위 규칙

```
Priority 1 (Highest): C → R2 → B    (Cleaner 자원 해제 최우선)
Priority 2:            P → R2 → C    (Polisher 자원 해제)
Priority 3:            L → R1 → P    (신규 웨이퍼 투입)
Priority 4 (Lowest):   B → R1 → L    (완료 웨이퍼 배출)
```

### 로봇 구성

- **R1 (Robot 1)**: LoadPort ↔ Polisher ↔ Buffer (양방향)
- **R2 (Robot 2)**: Polisher ↔ Cleaner ↔ Buffer (양방향)

### 전략

**공정 장비 자원 해제 우선** → 처리량 극대화

## UI 구성

### Control Panel (상단)
- **▶ Start**: 시뮬레이션 시작
- **⏸ Pause**: 시뮬레이션 일시정지
- **↻ Reset**: 초기 상태로 리셋

### Simulation Area (중앙)
- **LoadPort (L)**: 25개 웨이퍼 적재
- **R1**: Robot 1 (L↔P↔B)
- **Polisher (P)**: 연마 공정 (4초)
- **R2**: Robot 2 (P↔C↔B)
- **Cleaner (C)**: 세척 공정 (5초)
- **Buffer (B)**: 임시 버퍼

각 웨이퍼는 고유한 색상으로 표시되며, 애니메이션과 함께 이동합니다.

### Log Panel (하단)
실시간 로그 출력:
- 우선순위 태그 표시: [P1], [P2], [P3], [P4]
- 전송 시작/완료 메시지
- 공정 완료 메시지
- 상태 스냅샷 (Desktop에 저장)

## 파일 구조

```
CMPSimulator/
├── Controllers/
│   ├── ForwardPriorityController.cs  ← 새로 추가된 Forward Priority 구현
│   ├── XStateCMPController.cs        (기존 분산 Station 기반)
│   └── CMPToolController.cs          (기존)
├── MainWindow.xaml                   ← R1/R2 레이블 업데이트
├── MainWindow.xaml.cs                ← ForwardPriorityController 사용
└── README_FORWARD_PRIORITY_WPF.md    ← 이 파일
```

## 로그 파일

시뮬레이션 실행 시 Desktop에 로그 파일이 생성됩니다:
- `CMPSimulator_ForwardPriority.log`

상세한 상태 변화를 추적할 수 있습니다.

## 구현 특징

### 1. 중앙 집중식 스케줄러
- 단일 `ForwardPriorityController`가 모든 스테이션 상태 관리
- Polling 방식 (100ms 간격)으로 우선순위 검사 및 이벤트 디스패치

### 2. UI 업데이트
- 별도의 `UIUpdateService` 스레드에서 100ms 간격으로 웨이퍼 위치 업데이트
- WPF `Dispatcher`를 통한 안전한 UI 스레드 접근

### 3. 애니메이션
- 웨이퍼 이동 시 800ms 부드러운 애니메이션
- Cubic Ease 효과로 자연스러운 움직임

### 4. Context 상태
```csharp
L_Pending      // 아직 처리되지 않은 웨이퍼
L_Completed    // 완료되어 돌아온 웨이퍼
R1, P, R2, C, B  // 각 스테이션의 현재 웨이퍼
P_Processing, C_Processing  // 공정 진행 중 플래그
R1_Busy, R2_Busy           // 로봇 이동 중 플래그
Completed      // 전체 완료 웨이퍼 리스트
```

## Timing 설정

```csharp
POLISHING = 4000   // 4초
CLEANING = 5000    // 5초
TRANSFER = 800     // 800ms
POLL_INTERVAL = 100 // 100ms
```

## 완료 조건

모든 25개 웨이퍼가 완료되면:
1. State Machine이 "completed" 상태로 전환
2. 로그에 "Simulation Complete" 메시지 출력
3. 완료 통계 표시

## Backward Priority vs Forward Priority 비교

| 구분 | Backward Priority | Forward Priority (현재) |
|------|-------------------|-------------------------|
| **전략** | 귀환 우선 | 공정 장비 해제 우선 |
| **P1** | B→L, C→B | C→B |
| **P2** | P→C | P→C |
| **P3** | L→P | L→P |
| **P4** | - | B→L |
| **목적** | 완료 웨이퍼 빠른 배출 | 공정 장비 가동률 극대화 |
| **Controller** | XStateCMPController | ForwardPriorityController |

## 변경 이력

### 2025-01-XX
- ✅ Forward Priority Scheduler 구현
- ✅ WPF Simulator에 통합
- ✅ R1/R2 레이블 업데이트
- ✅ 중앙 집중식 스케줄러 적용
- ✅ UI 애니메이션 유지
- ✅ 로그 파일 출력

## 향후 개선 사항

1. **Controller 전환 UI**: Backward/Forward Priority를 런타임에 전환할 수 있는 옵션 추가
2. **Timing 조절**: UI에서 공정 시간을 조절할 수 있는 슬라이더 추가
3. **통계 패널**: 처리량(Throughput), 평균 Cycle Time 등 표시
4. **재생 속도 조절**: 1x, 2x, 4x 속도 조절 기능

## 참고 문서

- `FORWARD_PRIORITY_IMPLEMENTATION.md`: Forward Priority 구현 상세 설명
- `ForwardPrioritySchedulerTests.cs`: Unit Test 코드

## 문의

구현 관련 질문이나 개선 제안은 GitHub Issues에 등록해주세요.
