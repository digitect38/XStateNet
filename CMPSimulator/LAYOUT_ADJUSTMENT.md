# UI Layout Adjustment - Logical Positioning

## 변경 사항

배치를 더 논리적이고 직관적으로 조정했습니다.

## Before (비논리적 배치)

```
LoadPort(50,150)    R1(250,300)    Polisher(420,250)    R2(590,300)    Cleaner(760,250)
                                                                         R3(760,420)
                                                          Buffer(590,500)
```

**문제점**:
- R3가 Cleaner 아래에 있어서 C→B 경로가 명확하지 않음
- Buffer가 R2 아래에 있어서 B→R1 경로가 혼란스러움
- 전체 흐름이 좌우 대칭이 아님

## After (논리적 배치)

```
LoadPort(50,150)    R1(250,300)    Polisher(420,250)    R2(590,300)    Cleaner(760,250)
                                   Buffer(420,500)       R3(590,420)
```

**개선점**:
- ✅ R3가 R2 바로 아래 → C→R3→B 경로가 명확
- ✅ Buffer가 Polisher 아래 → 중앙 집중형 배치
- ✅ 좌우 대칭: 왼쪽(R1, Buffer), 중앙(Polisher), 오른쪽(R2, R3, Cleaner)

## 배치 구조

### 세로 레이어

```
Layer 1 (Y=150-250): LoadPort, Polisher, Cleaner (Main Equipment)
Layer 2 (Y=300):     R1, R2 (Upper Robots)
Layer 3 (Y=420):     R3 (Lower Robot)
Layer 4 (Y=500):     Buffer (Return path hub)
```

### 가로 위치

```
X=50:   LoadPort
X=250:  R1
X=420:  Polisher, Buffer (Vertical alignment)
X=590:  R2, R3 (Vertical alignment)
X=760:  Cleaner
```

## Flow Path (흐름 경로)

### Forward Path (전진 경로)
```
L(50,150) → R1(250,300) → P(420,250) → R2(590,300) → C(760,250)
```

### Return Path (귀환 경로)
```
C(760,250) → R3(590,420) → B(420,500) → R1(250,300) → L(50,150)
```

## 시각적 레이아웃

```
   [LoadPort]           [Polisher]           [Cleaner]
        |                    |                    |
      [R1] ------------> (P) ----> [R2] -------> (C)
        |                    |        |           |
        |                    |      [R3] <--------|
        |                    |        |
        |                [Buffer] <---|
        |                    |
        <--------------------|
```

## 좌표 변경 내역

### ForwardPriorityController.cs

```csharp
// Before
_stations["R3"] = new StationPosition("R3", 760, 420, 80, 80, 0);
_stations["Buffer"] = new StationPosition("Buffer", 590, 500, 80, 80, 1);

// After
_stations["R3"] = new StationPosition("R3", 590, 420, 80, 80, 0);     // R2 아래
_stations["Buffer"] = new StationPosition("Buffer", 420, 500, 80, 80, 1);  // Polisher 아래
```

### MainWindow.xaml

```xml
<!-- Before -->
<Border Canvas.Left="760" Canvas.Top="420">  <!-- R3 -->
<Border Canvas.Left="590" Canvas.Top="500">  <!-- Buffer -->

<!-- After -->
<Border Canvas.Left="590" Canvas.Top="420">  <!-- R3: R2 바로 아래 -->
<Border Canvas.Left="420" Canvas.Top="500">  <!-- Buffer: Polisher 바로 아래 -->
```

### Flow Path Arrows

```xml
<!-- Return Path 수정 -->
<!-- Before: Cleaner(760,330) → (800,420) → (760,460) → (670,540) -->
<!-- After:  Cleaner(760,330) → (670,460) → R3(590,460) → Buffer(500,540) -->

<PathFigure StartPoint="760,330">
    <LineSegment Point="670,460"/>  <!-- Cleaner → R3로 대각선 -->
</PathFigure>
<PathFigure StartPoint="590,460">
    <LineSegment Point="500,540"/>  <!-- R3 → Buffer로 대각선 -->
</PathFigure>
```

## 장점

### 1. 명확한 흐름
- Forward: 좌 → 우 (L → R1 → P → R2 → C)
- Return: 우 → 좌 (C → R3 → B → R1 → L)

### 2. 로봇 역할 명확화
- **R1**: 왼쪽 영역 (LoadPort ↔ Polisher ↔ Buffer)
- **R2**: 오른쪽 상단 (Polisher ↔ Cleaner)
- **R3**: 오른쪽 하단 (Cleaner ↔ Buffer)

### 3. 중심점: Buffer
- Polisher 바로 아래
- R1과 R3의 교차점
- Return path의 허브 역할

### 4. 시각적 균형
```
      LoadPort              Polisher              Cleaner
                               |
        R1  <------------> Polisher <------> R2
         |                     |              |
         |                     |            R3
         |                 Buffer <----------|
         |                     |
         <--------------------|
```

## 실행 시 확인 사항

WPF 앱 실행 후 확인:
```bash
dotnet run
```

**확인 포인트**:
1. R3가 R2 바로 아래에 위치
2. Buffer가 Polisher 바로 아래에 위치
3. 웨이퍼 흐름: C → R3 → Buffer로 대각선 이동
4. 웨이퍼 흐름: Buffer → R1 → LoadPort로 대각선 이동
5. 전체 레이아웃이 좌우 균형

## 코드 변경 파일

1. ✅ `Controllers/ForwardPriorityController.cs` - Station positions
2. ✅ `MainWindow.xaml` - Canvas positions and flow paths

## 관련 문서

- `R3_ROBOT_IMPLEMENTATION.md`: R3 로봇 추가 전체 설명
- `README_FORWARD_PRIORITY_WPF.md`: Forward Priority 개요

## 결론

✅ **R3를 R2 아래 배치 → 로봇 그룹화**
✅ **Buffer를 Polisher 아래 배치 → 중앙 집중**
✅ **논리적 흐름과 시각적 균형 확보**

이제 UI가 실제 설비 배치와 유사하고 흐름을 이해하기 쉽습니다!
