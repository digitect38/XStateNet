# XStateNet Documentation

Comprehensive documentation for the XStateNet semiconductor manufacturing simulation framework.

## 📚 Documentation Index

### Core Documentation

- **[CMP Simulator Documentation](./CMP-Simulator-Documentation.md)** 📱
  - Complete simulator user guide
  - Architecture and design patterns
  - State machine integration
  - UI features and controls
  - Configuration and troubleshooting
  - Development guide for extensions
  - **Version:** 1.4.0 | **Tag:** v1.4.0-ui-blue-outline

- **[Scheduler DSL Documentation](./Scheduler-DSL-Documentation.md)** ⭐
  - XState-based Declarative Scheduling DSL
  - JSON rule engine and syntax
  - Forward Priority scheduling examples
  - API reference and best practices
  - **Version:** 1.3.0 | **Tag:** v1.3.0-scheduler-dsl

### Getting Started

1. **Quick Start Guide** *(Coming soon)*
   - Installation and setup
   - First simulation
   - Basic configuration

2. **Architecture Overview** *(Coming soon)*
   - System components
   - State machine design
   - Event-driven architecture

### Guides

- **Simulator User Guide** ✅ [Available](./CMP-Simulator-Documentation.md)
  - Getting started
  - UI features and controls
  - Configuration and settings
  - Troubleshooting

- **Scheduler DSL Guide** ✅ [Available](./Scheduler-DSL-Documentation.md)
  - Rule creation and syntax
  - Condition evaluation
  - Action execution
  - Troubleshooting

- **State Machine Guide** ✅ [Available](./CMP-Simulator-Documentation.md#state-machines)
   - Creating custom state machines
   - Event handling
   - Service invocation
   - Station and robot machines

- **SEMI Standards Guide** ✅ [Available](./CMP-Simulator-Documentation.md#semi-standards)
   - E87 Carrier Management
   - E90 Substrate Tracking
   - Integration patterns

### API Reference

- **CMP Simulator** ✅ [See Simulator Docs](./CMP-Simulator-Documentation.md#api-reference)
  - OrchestratedForwardPriorityController
  - State machine APIs
  - Configuration APIs
- **DeclarativeSchedulerMachine** ✅ [See Scheduler DSL](./Scheduler-DSL-Documentation.md#declarativeschedulermachine)
- **SchedulingRuleEngine** ✅ [See Scheduler DSL](./Scheduler-DSL-Documentation.md#schedulingruleengine)
- **WaferMachine** ✅ [See Simulator Docs](./CMP-Simulator-Documentation.md#wafermachine)
- **Station Machines** ✅ [See Simulator Docs](./CMP-Simulator-Documentation.md#station-state-machines)
- **EventBusOrchestrator** *(Coming soon)*
- **StateMachineMonitor** *(Coming soon)*

### Examples

- **Forward Priority Scheduling** ✅ [See Scheduler DSL](./Scheduler-DSL-Documentation.md#forward-priority-scheduling)
- **Custom Scheduling Rules** ✅ [See Scheduler DSL](./Scheduler-DSL-Documentation.md#example-rules)
- **Station State Machines** ✅ [See Simulator Docs](./CMP-Simulator-Documentation.md#adding-a-new-station)
- **Robot Coordination** ✅ [See Simulator Docs](./CMP-Simulator-Documentation.md#robot-state-machines)
- **Wafer Tracking (E90)** ✅ [See Simulator Docs](./CMP-Simulator-Documentation.md#e90-wafer-tracking)

## 🎯 Key Features

### Declarative Scheduling DSL
- **JSON-based** rule definition
- **Priority-driven** execution
- **Event-driven** architecture
- **Dynamic** rule modification
- **No recompilation** required

### XState Integration
- Pure state machines
- Event orchestration
- Pub/Sub pattern
- Monitoring and debugging

### SEMI Standards
- E87 Carrier Management
- E90 Substrate Tracking
- Industry-standard workflows

## 🚀 Quick Links

### Latest Features

- [v1.4.0: Blue Outline State Tree](../README.md) - UI improvements
- [v1.3.0: Scheduler DSL](./Scheduler-DSL-Documentation.md) - **⭐ Highlight**
- [v1.2.0: E87/E90 Integration](../README.md) - SEMI standards

### Resources

- [GitHub Repository](https://github.com/digitect38/XStateNet)
- [Issue Tracker](https://github.com/digitect38/XStateNet/issues)
- [Release Notes](../CHANGELOG.md)

## 📖 Documentation Standards

All documentation follows these principles:

1. **Clear Examples**: Every concept includes working code examples
2. **Best Practices**: Recommended patterns and anti-patterns
3. **Troubleshooting**: Common issues and solutions
4. **API Reference**: Complete method signatures and parameters
5. **Visual Aids**: Diagrams and flowcharts where helpful

## 🤝 Contributing

To contribute documentation:

1. Follow the existing structure and format
2. Include practical examples
3. Update the table of contents
4. Add troubleshooting sections
5. Test all code examples

## 📝 Document Templates

### New Document Template

```markdown
# [Feature Name] Documentation

## Overview
Brief description of the feature

## Table of Contents
1. [Architecture](#architecture)
2. [Core Components](#core-components)
3. [API Reference](#api-reference)
4. [Examples](#examples)
5. [Best Practices](#best-practices)

## Architecture
System design and components

## Core Components
Detailed component descriptions

## API Reference
Methods, properties, events

## Examples
Working code examples

## Best Practices
DOs and DON'Ts

## Troubleshooting
Common issues and solutions

## References
Related documentation and resources
```

## 🔍 Search Tips

- Use Ctrl+F in your browser to search documentation
- Check the Table of Contents for specific topics
- Start with the Quick Start Guide for beginners
- Refer to API Reference for method signatures
- Check Examples for working code

## 📌 Version History

### Latest Documentation Updates

| Date | Version | Document | Changes |
|------|---------|----------|---------|
| 2025-10-15 | 1.4.0 | CMP Simulator | Complete simulator user and developer guide |
| 2025-10-15 | 1.3.0 | Scheduler DSL | Initial comprehensive documentation |
| 2025-10-15 | 1.4.0 | UI Updates | Blue outline state tree documentation |

## 📧 Support

For questions about the documentation:

- **GitHub Issues**: Report documentation issues or request new topics
- **Email**: support@xstatenet.dev
- **Discussions**: Share use cases and patterns

---

**Last Updated:** October 15, 2025
**Documentation Version:** 1.3.0
**Project Version:** 1.4.0
