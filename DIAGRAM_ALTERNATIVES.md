# Diagram Guidelines for XStateNet Documentation

## ✅ Use Mermaid Diagrams Only

**All diagrams in this project must use Mermaid format.** ASCII box diagrams, HTML tables, and other alternatives do not render properly in this environment.

## 📦 Setup

Install the Mermaid extension for VS Code:
- Extension: "Markdown Preview Mermaid Support"
- Or search for "Mermaid" in VS Code extensions

## 📝 Mermaid Diagram Examples

### Example 1: Architecture Diagram

```mermaid
graph TB
    subgraph "Machine: localhost"
        subgraph ProcessA["Process A (UI App)"]
            MachineA["State Machine<br/>'ui-main'"]
            CtxA["InterProcCtx"]
            ClientA["IPC Client"]
            MachineA --> CtxA --> ClientA
        end

        subgraph ProcessB["Process B (Worker)"]
            MachineB["State Machine<br/>'worker-1'"]
            CtxB["InterProcCtx"]
            ClientB["IPC Client"]
            MachineB --> CtxB --> ClientB
        end

        ClientA <--> Pipe["Named Pipe / IPC<br/>'XStateNet.Events'"]
        ClientB <--> Pipe
    end
```

### Example 2: Flow Diagram

```mermaid
graph LR
    A[Start] --> B{Decision}
    B -->|Yes| C[Action 1]
    B -->|No| D[Action 2]
    C --> E[End]
    D --> E
```

### Example 3: Sequence Diagram

```mermaid
sequenceDiagram
    participant UI
    participant Worker
    participant DB

    UI->>Worker: Request Work
    Worker->>DB: Query Data
    DB-->>Worker: Return Data
    Worker-->>UI: Send Result
```

## 🚫 What NOT to Use

❌ **ASCII Box Diagrams** - Font-dependent, breaks alignment
❌ **HTML Tables** - Not supported in all viewers
❌ **Simple Text Diagrams** - Poor readability
❌ **Pre-rendered Images** - Hard to maintain, no version control

## 📚 Mermaid Resources

- [Mermaid Documentation](https://mermaid.js.org/)
- [Mermaid Live Editor](https://mermaid.live/)
- [Graph Types](https://mermaid.js.org/intro/syntax-reference.html)

## ✏️ Migration Status

All ASCII diagrams have been converted to Mermaid format in:
- ✅ INTERPROCESS_ORCHESTRATED_PATTERN.md
- ✅ TESTING_INTERPROCESS_GUIDE.md
