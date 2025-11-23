# Semi Flow Language User Guide

## 1. Installation

### VSCode Extension
1. Open VSCode
2. Go to Extensions (Ctrl+Shift+X)
3. Click "Install from VSIX"
4. Select `semiflow-language.vsix`

## 2. Writing Your First SFL Program

### Basic Structure
Every SFL program consists of schedulers organized in layers:
- L1: Master Scheduler (MSC)
- L2: Wafer Scheduler (WSC)
- L3: Robot Scheduler (RSC)
- L4: Station

### Example
```sfl
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    CONFIG {
        total_wafers: 25
    }
}
```

## 3. Using Rules

### Apply Built-in Rules
```sfl
SCHEDULE PRODUCTION {
    APPLY_RULE("WAR_001")  // Cyclic Zip
    APPLY_RULE("PSR_001")  // Pipeline Slots
}
```

### Verify Results
```sfl
VERIFY {
    all_wafers_assigned: true
    no_conflicts: true
}
```

## 4. Commands

### Compile
```bash
sfl compile file.sfl
```

### Validate
```bash
sfl validate file.sfl
```

### Run Simulation
```bash
sfl simulate file.sfl --wafers 25 --time 3600
```
