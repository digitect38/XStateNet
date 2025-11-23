# Semi Flow Language Standard Rules Library v2.0

## Built-in Rules

### WAR_001: Cyclic Zip Distribution
Distributes wafers across WSCs in cyclic pattern
- Input: 25 wafers, 3 WSCs
- Output: WSC1=[W1,W4,W7...], WSC2=[W2,W5,W8...], WSC3=[W3,W6,W9...]

### PSR_001: Pipeline Slot Assignment
Assigns wafers to pipeline slots without collision

### SSR_001: Three Phase Steady State
Maintains steady state with 3-phase operation
