# Semi Flow Language (SFL) for Visual Studio Code

Syntax highlighting and code snippets for the Semi Flow Language - a domain-specific language for semiconductor manufacturing scheduling systems.

## Features

- **Syntax Highlighting** for `.sfl`, `.sfli`, and `.sflc` files
- **Code Snippets** for rapid development
- **Bracket Matching** and auto-closing pairs
- **Code Folding** support

## Syntax Highlighting

Keywords, identifiers, and special constructs are highlighted:

- **Schedulers**: `MASTER_SCHEDULER`, `WAFER_SCHEDULER`, `ROBOT_SCHEDULER`, `STATION`
- **Layers**: `L1`, `L2`, `L3`, `L4`
- **Scheduler IDs**: `MSC_001`, `WSC_001`, `RSC_001`
- **Wafer IDs**: `W001`, `W002`, etc.
- **Rules**: `APPLY_RULE`, `VERIFY`, `FORMULA`

## Snippets

| Prefix | Description |
|--------|-------------|
| `msc` | Master Scheduler (L1) |
| `wsc` | Wafer Scheduler (L2) |
| `rsc` | Robot Scheduler (L3) |
| `station` | Station (L4) |
| `schedule` | Schedule block with rules |
| `rule` | APPLY_RULE statement |
| `config` | CONFIG block |
| `transaction` | Transaction definition |
| `publish` | Publish to topic |
| `subscribe` | Subscribe to topic |
| `verify` | VERIFY block |
| `statemachine` | State machine |
| `system` | System architecture |

## Example

```sfl
MASTER_SCHEDULER MSC_001 {
    LAYER: L1

    CONFIG {
        wafer_distribution: "CYCLIC_ZIP"
        total_wafers: 25
    }

    SCHEDULE PRODUCTION_RUN {
        APPLY_RULE("WAR_001")
        APPLY_RULE("PSR_001")
    }
}
```

## Installation

1. Download the `.vsix` file
2. In VSCode: `Extensions` > `...` > `Install from VSIX`
3. Select the downloaded file

Or via command line:
```bash
code --install-extension semiflow-language-2.0.0.vsix
```

## File Extensions

| Extension | Purpose |
|-----------|---------|
| `.sfl` | Semi Flow source files |
| `.sfli` | Include/header files |
| `.sflc` | Configuration files |

## License

MIT License
