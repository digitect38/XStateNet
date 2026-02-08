# Semi Flow Language (SFL) Grammar Specification v2.0

## 1. File Extension
- Primary: `*.sfl`
- Include: `*.sfli`
- Config: `*.sflc`

## 2. Basic Syntax

### Keywords
```
MASTER_SCHEDULER, WAFER_SCHEDULER, ROBOT_SCHEDULER, STATION
SCHEDULE, APPLY_RULE, VERIFY, FORMULA
LAYER, L1, L2, L3, L4
CONFIG, transaction, publish, subscribe
```

### Identifiers
- Schedulers: `MSC_001`, `WSC_001`, `RSC_001`
- Wafers: `W001`, `W002`, ..., `W025`
- Stations: `STN_CMP01`, `STN_CLN02`
- Transactions: `TXN_20240101_00001_A3F2`

## 3. Structure
```sfl
SCHEDULER_TYPE IDENTIFIER {
    LAYER: L1
    CONFIG { ... }
    SCHEDULE NAME { ... }
}
```
