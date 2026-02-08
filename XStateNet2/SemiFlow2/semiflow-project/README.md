# Semi Flow Language (SFL)

Domain-specific language for semiconductor manufacturing scheduling systems.

## Version
2.0.0

## Quick Start
```bash
# Install VSCode extension
code --install-extension ./vscode-extension

# Open example file
code examples/advanced/cyclic_zip_25wafers.sfl
```

## Features
- Hierarchical scheduling (MSC ??WSC ??RSC ??Station)
- Cyclic zip wafer distribution
- Pipeline scheduling with collision prevention
- SEMI standards compliance (E87, E88, E90, E94)
- Real-time transaction tracking
- Pub/Sub messaging with QoS

## File Extensions
- `.sfl` - Semi Flow Language source files
- `.sfli` - Include files
- `.sflc` - Configuration files

## Built-in Rules
- **WAR_001**: Cyclic Zip Distribution
- **PSR_001**: Pipeline Slot Assignment  
- **SSR_001**: Three Phase Steady State

## Example
```sfl
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    CONFIG {
        wafer_distribution: "CYCLIC_ZIP"
        total_wafers: 25
    }
    SCHEDULE PRODUCTION {
        APPLY_RULE("WAR_001")
    }
}
```

## Documentation
- [Grammar Specification](sfl-spec/grammar-spec-v2.0.md)
- [Standard Rules Library](sfl-spec/standard-rules-library.md)
- [User Guide](docs/USER_GUIDE.md)

## License
MIT License - Semiconductor Manufacturing Consortium
