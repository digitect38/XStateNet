# TimeoutProtectedStateMachine - Complete Documentation Index

## 📚 Quick Navigation

### 🚀 Getting Started
- **[README_TIMEOUT_PROTECTION.md](README_TIMEOUT_PROTECTION.md)** - **START HERE** - Complete guide
- **[TIMEOUT_PROTECTION_QUICK_REFERENCE.md](TIMEOUT_PROTECTION_QUICK_REFERENCE.md)** - Quick lookup & code snippets

### 🎯 When & Why
- **[WHEN_TO_USE_TIMEOUT_PROTECTION.md](WHEN_TO_USE_TIMEOUT_PROTECTION.md)** - Real-world scenarios & decision tree

### 🔄 Migration
- **[TIMEOUT_PROTECTION_MIGRATION_GUIDE.md](TIMEOUT_PROTECTION_MIGRATION_GUIDE.md)** - Migrate from deprecated implementation

### 🏗️ Architecture
- **[XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md](XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md)** - Technical evaluation & deprecation rationale

### 🔗 Orchestrator Integration
- **[TIMEOUT_PROTECTED_ORCHESTRATOR_INTEGRATION.md](TIMEOUT_PROTECTED_ORCHESTRATOR_INTEGRATION.md)** - **NEW!** Orchestrator integration guide
- **[ORCHESTRATOR_INTEGRATION_SUMMARY.md](ORCHESTRATOR_INTEGRATION_SUMMARY.md)** - **NEW!** Summary of changes

---

## 📖 Document Descriptions

### README_TIMEOUT_PROTECTION.md
**What:** Complete guide to timeout protection
**For:** Understanding all features and capabilities
**Contains:**
- Quick start examples
- Configuration options
- Common scenarios
- Best practices
- Troubleshooting
- FAQ

---

### TIMEOUT_PROTECTION_QUICK_REFERENCE.md
**What:** Quick lookup reference
**For:** Developers who need quick answers
**Contains:**
- TL;DR comparison
- 3-step migration
- Code snippets
- Minimal explanations

---

### WHEN_TO_USE_TIMEOUT_PROTECTION.md
**What:** Use case guidance
**For:** Deciding if you need timeout protection
**Contains:**
- Real-world scenarios (✅ use it)
- Scenarios to avoid (❌ don't use)
- Decision tree
- Comparison with alternatives (CancellationToken, Polly, Circuit Breaker)

---

### TIMEOUT_PROTECTION_MIGRATION_GUIDE.md
**What:** Migration from deprecated XStateNetTimeoutProtectedStateMachine
**For:** Teams using the old implementation
**Contains:**
- What was deprecated and why
- Before/after code examples
- Step-by-step migration
- Feature comparison
- FAQ

---

### XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md
**What:** Technical evaluation and deprecation rationale
**For:** Understanding why two implementations existed
**Contains:**
- Code analysis
- Architecture comparison
- Risk assessment
- Deprecation decision
- Technical details

---

### TIMEOUT_PROTECTED_ORCHESTRATOR_INTEGRATION.md
**What:** Orchestrator integration guide (**NEW in 2025-10-05**)
**For:** Using timeout-protected machines in orchestrated systems
**Contains:**
- Quick start examples
- API reference
- Architecture diagrams
- Use cases
- Configuration
- Best practices
- Troubleshooting

---

### ORCHESTRATOR_INTEGRATION_SUMMARY.md
**What:** Summary of orchestrator integration changes (**NEW in 2025-10-05**)
**For:** Quick overview of what changed
**Contains:**
- Changes made
- Key features
- Test results
- Benefits
- Migration path
- Design decisions

---

## 🎯 Which Document Should I Read?

### I'm new to TimeoutProtectedStateMachine
👉 Start with **[README_TIMEOUT_PROTECTION.md](README_TIMEOUT_PROTECTION.md)**

### I need quick code examples
👉 Use **[TIMEOUT_PROTECTION_QUICK_REFERENCE.md](TIMEOUT_PROTECTION_QUICK_REFERENCE.md)**

### I'm deciding if I need timeout protection
👉 Read **[WHEN_TO_USE_TIMEOUT_PROTECTION.md](WHEN_TO_USE_TIMEOUT_PROTECTION.md)**

### I'm using XStateNetTimeoutProtectedStateMachine (deprecated)
👉 Follow **[TIMEOUT_PROTECTION_MIGRATION_GUIDE.md](TIMEOUT_PROTECTION_MIGRATION_GUIDE.md)**

### I want to understand why deprecation happened
👉 Read **[XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md](XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md)**

### I need orchestrator integration
👉 Read **[TIMEOUT_PROTECTED_ORCHESTRATOR_INTEGRATION.md](TIMEOUT_PROTECTED_ORCHESTRATOR_INTEGRATION.md)**

### I want summary of recent changes
👉 Read **[ORCHESTRATOR_INTEGRATION_SUMMARY.md](ORCHESTRATOR_INTEGRATION_SUMMARY.md)**

---

## 📋 Feature Overview

### Core Features
- ✅ State timeout monitoring
- ✅ Transition timeout monitoring
- ✅ Action timeout monitoring
- ✅ Dead Letter Queue integration
- ✅ Adaptive timeout learning
- ✅ Statistics collection
- ✅ Configurable per state/transition/action

### Integration Features (NEW)
- ✅ EventBusOrchestrator registration
- ✅ Automatic registration (constructor)
- ✅ Explicit registration (method)
- ✅ Channel group support
- ✅ Dependency injection support
- ✅ Backward compatible

### Resilience Features
- ✅ Timeout detection
- ✅ Automatic recovery (optional)
- ✅ Circuit breaker integration
- ✅ Retry policy integration
- ✅ Monitoring & telemetry

---

## 🧪 Test Coverage

**Test Suite:** `XStateNet.Distributed.Tests/Resilience/TimeoutProtectedStateMachineTests.cs`

**Status:** ✅ **11/11 Tests Passing**

**Tests:**
1. TimeoutProtectedStateMachine_StartsSuccessfully
2. TimeoutProtectedStateMachine_TransitionCompletesWithinTimeout
3. TimeoutProtectedStateMachine_ConfiguresStateTimeout
4. TimeoutProtectedStateMachine_ConfiguresTransitionTimeout
5. TimeoutProtectedStateMachine_CollectsStatistics
6. TimeoutProtectedStateMachine_MultipleTransitions
7. TimeoutProtectedStateMachine_WithOptions
8. TimeoutProtectedStateMachine_WrapsExistingMachine
9. TimeoutProtectedStateMachine_StopsCleanly
10. TimeoutProtectedStateMachine_RequiresInnerMachine
11. TimeoutProtectedStateMachine_RequiresTimeoutProtection

---

## 🔄 Version History

### 2025-10-05 - Orchestrator Integration
- ✅ Added orchestrator integration to TimeoutProtectedStateMachine
- ✅ Created TIMEOUT_PROTECTED_ORCHESTRATOR_INTEGRATION.md
- ✅ Created ORCHESTRATOR_INTEGRATION_SUMMARY.md
- ✅ All tests passing (11/11)

### 2025-10-05 - Initial Documentation
- ✅ XStateNetTimeoutProtectedStateMachine marked deprecated
- ✅ Created comprehensive documentation suite:
  - README_TIMEOUT_PROTECTION.md
  - TIMEOUT_PROTECTION_QUICK_REFERENCE.md
  - WHEN_TO_USE_TIMEOUT_PROTECTION.md
  - TIMEOUT_PROTECTION_MIGRATION_GUIDE.md
  - XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md

---

## 🚦 Status Summary

| Component | Status | Notes |
|-----------|--------|-------|
| **XStateNetTimeoutProtectedStateMachine** | ⚠️ Deprecated | Marked for removal in v2.0 |
| **TimeoutProtectedStateMachine** | ✅ Active | Production-ready, recommended |
| **Orchestrator Integration** | ✅ New | Added 2025-10-05 |
| **Test Coverage** | ✅ Passing | 11/11 tests |
| **Documentation** | ✅ Complete | 7 documents |

---

## 📞 Support

### Documentation Issues
- Missing information → Check this index for the right document
- Unclear examples → See **[TIMEOUT_PROTECTION_QUICK_REFERENCE.md](TIMEOUT_PROTECTION_QUICK_REFERENCE.md)**

### Migration Help
- Deprecated usage → See **[TIMEOUT_PROTECTION_MIGRATION_GUIDE.md](TIMEOUT_PROTECTION_MIGRATION_GUIDE.md)**
- Decision making → See **[WHEN_TO_USE_TIMEOUT_PROTECTION.md](WHEN_TO_USE_TIMEOUT_PROTECTION.md)**

### Technical Issues
- Bug reports → https://github.com/anthropics/claude-code/issues
- Architecture questions → See **[XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md](XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md)**

### Integration Help
- Orchestrator setup → See **[TIMEOUT_PROTECTED_ORCHESTRATOR_INTEGRATION.md](TIMEOUT_PROTECTED_ORCHESTRATOR_INTEGRATION.md)**
- Recent changes → See **[ORCHESTRATOR_INTEGRATION_SUMMARY.md](ORCHESTRATOR_INTEGRATION_SUMMARY.md)**

---

## 🎓 Learning Path

### Beginner
1. **[README_TIMEOUT_PROTECTION.md](README_TIMEOUT_PROTECTION.md)** - Understand what timeout protection is
2. **[WHEN_TO_USE_TIMEOUT_PROTECTION.md](WHEN_TO_USE_TIMEOUT_PROTECTION.md)** - Learn when to use it
3. **[TIMEOUT_PROTECTION_QUICK_REFERENCE.md](TIMEOUT_PROTECTION_QUICK_REFERENCE.md)** - Get started with code

### Intermediate
1. **[TIMEOUT_PROTECTED_ORCHESTRATOR_INTEGRATION.md](TIMEOUT_PROTECTED_ORCHESTRATOR_INTEGRATION.md)** - Learn orchestrator integration
2. **[TIMEOUT_PROTECTION_MIGRATION_GUIDE.md](TIMEOUT_PROTECTION_MIGRATION_GUIDE.md)** - Migrate from old implementation
3. Experiment with examples in documentation

### Advanced
1. **[XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md](XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md)** - Understand architectural decisions
2. **[ORCHESTRATOR_INTEGRATION_SUMMARY.md](ORCHESTRATOR_INTEGRATION_SUMMARY.md)** - Review implementation details
3. Study test suite: `TimeoutProtectedStateMachineTests.cs`

---

**Last Updated:** 2025-10-05
**Documents:** 7 total (2 new)
**Test Status:** ✅ 11/11 passing
