# 🔬 Stress Test Explained - Simple Guide

## 🎯 What is the Stress Test?

The stress test is like a **marathon race** for all 12 scheduler types. Instead of running just once, it runs **1000 cycles** to see:
- ✅ **Reliability**: Does it crash or fail?
- ⏱️ **Performance**: How fast can it complete?
- 🐛 **Failure Modes**: What goes wrong and when?

## 📋 How It Works (Step by Step)

### Step 1: Setup (Before the Race)
```
1. Create a fresh actor system (like a clean factory)
2. Create 10 wafers (like products to manufacture)
3. Create 3 robots (Robot 1, Robot 2, Robot 3)
4. Create 3 stations (Polisher, Cleaner, Buffer)
5. Choose which scheduler to test (Lock, Actor, XState, etc.)
```

### Step 2: Run 1000 Cycles (The Marathon)
The test runs **1000 cycles** (like 1000 simulation steps):

#### 🚛 Cycle 1: First Carrier Arrives
- Carrier C1 arrives with **5 wafers** (IDs: 1, 2, 3, 4, 5)
- System starts processing these wafers through the journey:
  ```
  Carrier → Robot 1 → Polisher → Robot 2 → Cleaner → Robot 3 → Buffer → Robot 1 → Carrier
  ```

#### 🚛 Cycle 500: Second Carrier Arrives
- Carrier C2 arrives with **5 more wafers** (IDs: 6, 7, 8, 9, 10)
- Now the system processes BOTH carriers simultaneously

#### 🔍 Every 100 Cycles: Health Check
The test checks:
- **Queue size**: How many transfer requests are waiting?
- **Stall detection**: If queue > 50, something is stuck!
- **Progress**: Are wafers moving through their journey?

Example output:
```
  Cycle 100: Queue=3
  Cycle 200: Queue=1
  Cycle 300: Queue=0
  Cycle 400: Queue=2
```

### Step 3: Measure Results (After the Race)
When all 1000 cycles complete, the test measures:

```
✓ Completed:        1000/1000 cycles (Did it finish?)
✓ Time:             12.45s           (How fast?)
✓ Wafers Completed: 10/10            (All wafers done?)
✓ Wafers Stuck:     0                (Any stuck wafers?)
✓ Errors:           0                (Any failures?)
```

## ✅ Success Criteria

A scheduler **PASSES** the stress test if:
1. **Fewer than 10 failures** during 1000 cycles
2. **At least 8 out of 10 wafers** completed their journey

A scheduler **FAILS** if:
- It crashes or throws too many errors
- It gets stuck (queue stalls)
- Less than 8 wafers complete

## 📊 Example Results

### 🏆 Good Result (Passed)
```
Testing: Lock-based
═══════════════════════════════════════════════════════════════
  Cycle 100: Queue=2
  Cycle 200: Queue=1
  Cycle 300: Queue=0
  ...
  ✓ Completed: 1000/1000 cycles
  ✓ Time: 12.45s
  ✓ Wafers Completed: 10/10  ✅
  ✓ Wafers Stuck: 0
  ✓ Errors: 0
  Result: PASS ✅
```

### ❌ Bad Result (Failed)
```
Testing: Broken-Scheduler
═══════════════════════════════════════════════════════════════
  Cycle 100: Queue=2
  Cycle 200: Queue=15
  Cycle 300: Queue=52  ⚠️ STALL DETECTED!
  Cycle 400: Error - Deadlock detected
  ...
  ✓ Completed: 423/1000 cycles
  ✓ Time: 8.32s
  ✓ Wafers Completed: 3/10  ❌
  ✓ Wafers Stuck: 5  ⚠️
  ✓ Errors: 12  ❌
  Result: FAIL ❌
```

## 🏁 Final Summary Report

After testing all 12 schedulers, you get a summary:

```
═══════════════════════════════════════════════════════════════
🏆 PERFORMANCE RANKINGS (Successful Tests Only):
═══════════════════════════════════════════════════════════════
  🥇 Lock-based              - 12.45s  (80 cycles/sec)
  🥈 Actor-based             - 13.22s  (75 cycles/sec)
  🥉 XState Array            - 14.10s  (70 cycles/sec)
  4. Single Publication      - 15.32s  (65 cycles/sec)
  ...

❌ FAILED TESTS:
  ❌ Broken-Scheduler        - Failed at cycle 423 (too many errors)
  ❌ Stalled-Scheduler       - Failed at cycle 678 (queue stall)
```

## 🎓 Why 1000 Cycles?

**1000 cycles** is enough to:
- ✅ Process 2 carriers with 5 wafers each (10 total)
- ✅ Detect memory leaks or performance degradation
- ✅ Find race conditions or deadlocks
- ✅ Test scheduler reliability under extended load
- ✅ See if the scheduler can handle carrier changes

## 🚀 How to Run

```bash
# Run the stress test
dotnet run --stress-test

# Or use the short form
dotnet run --stress
```

## 📈 What Gets Tested

| Scheduler Type | Icon | Description |
|---------------|------|-------------|
| Lock-based | 🔒 | Simple lock-based coordination |
| Actor-based | 🎭 | Akka.NET actor messaging |
| XState (FrozenDict) | 🔄 | XStateNet2 with dictionary |
| XState (Array) | ⚡ | XStateNet2 with byte arrays |
| Autonomous | 🤖 | Self-polling robots |
| Hybrid | 🚀 | Array + Autonomous |
| Event-Driven | ⚡🔥 | Event-based coordination |
| Actor Mailbox | 📬⚡ | Mailbox dispatch |
| Ant Colony | 🐜 | Decentralized autonomy |
| Publication-Based | 📡 | Pub/sub per robot |
| Single Publication | 📡⚡ | Single pub/sub scheduler |
| Array Single Publication | 📡⚡🎯 | Array + single pub/sub |

## 🎯 Key Metrics Explained

### 1. **Cycles/Second**
How many simulation cycles the scheduler can process per second
- **Higher = Better** (more efficient)
- Example: 80 cycles/sec means 80 simulation steps per second

### 2. **Total Time**
How long it took to complete all 1000 cycles
- **Lower = Better** (faster completion)
- Example: 12.45s to complete entire test

### 3. **Wafers Completed**
How many of the 10 wafers successfully completed their journey
- **10/10 = Perfect** ✅
- **8-9/10 = Good** ✅
- **< 8/10 = Failed** ❌

### 4. **Queue Size**
Number of pending transfer requests
- **0-5 = Healthy** ✅
- **5-20 = Busy** ⚠️
- **> 50 = Stalled** ❌ (likely deadlock)

### 5. **Errors**
Number of exceptions or failures
- **0 = Perfect** ✅
- **1-9 = Acceptable** ⚠️
- **≥ 10 = Failed** ❌

## 💡 Understanding the Output

When you run the stress test, you'll see output like this:

```
═══════════════════════════════════════════════════════════════
Testing: Lock-based
═══════════════════════════════════════════════════════════════
  Cycle 100: Queue=3        <- Every 100 cycles, show queue size
  Cycle 200: Queue=1        <- Queue going down = good progress
  Cycle 300: Queue=0        <- Queue empty = very efficient!
  Cycle 400: Queue=2
  Cycle 500: Queue=5        <- C2 arrives, queue increases (normal)
  Cycle 600: Queue=2        <- Processing C2 wafers
  Cycle 700: Queue=0
  Cycle 800: Queue=0
  Cycle 900: Queue=0
  Cycle 1000: Queue=0       <- Final check

  ✓ Completed: 1000/1000 cycles   <- All cycles completed
  ✓ Time: 12.45s                  <- Total time
  ✓ Wafers Completed: 10/10       <- All wafers finished!
  ✓ Wafers Stuck: 0               <- No stuck wafers
  ✓ Errors: 0                     <- No errors
  Result: PASS ✅
```

## 🔧 Troubleshooting

### ❓ What if a scheduler fails?
The test will:
1. Stop that scheduler after 10 errors
2. Record the failure
3. Continue testing other schedulers
4. Show detailed error messages in the summary

### ❓ What if the queue grows too large?
- Queue > 50 = **Stall detected**
- This usually means:
  - Deadlock (robots waiting for each other)
  - Station stuck in wrong state
  - Race condition in scheduler

### ❓ What if wafers get stuck?
- Check which journey stage they're stuck in
- Common stuck points:
  - `→ ToPolisher` (waiting for Robot 1)
  - `Processing` (stuck in station)
  - `→ ToBuffer` (waiting for Robot 3)

## 🎊 Summary

The stress test is a **reliability marathon** that:
1. ✅ Runs each scheduler through 1000 cycles
2. ✅ Processes 2 carriers with 10 total wafers
3. ✅ Checks for stalls, errors, and stuck wafers
4. ✅ Measures speed and completion rate
5. ✅ Ranks all schedulers by performance

**Goal**: Find the most reliable and efficient scheduler architecture! 🏆
