# 🎯 Stress Test - Super Simple Explanation

## What Is It?

The stress test is like a **video game speed run** for schedulers:
- 🎮 Run the same game 1000 times
- ⏱️ See who finishes fastest
- 🏆 Find the champion!

## The Rules

1. **Run 1000 cycles** (like 1000 game levels)
2. **Process 10 wafers** (5 in carrier C1, 5 in carrier C2)
3. **Don't crash or fail** (less than 10 errors allowed)
4. **Finish the job** (at least 8 out of 10 wafers must complete)

## What Happens?

```
START (Cycle 1)
   ↓
🚛 Carrier C1 arrives with 5 wafers
   ↓
⚙️ Process wafers through: Polisher → Cleaner → Buffer → Done
   ↓
   ... 499 cycles later ...
   ↓
🚛 Carrier C2 arrives with 5 more wafers
   ↓
⚙️ Process these wafers too
   ↓
   ... 500 more cycles ...
   ↓
🏁 FINISH (Cycle 1000)
   ✅ Check: Did all 10 wafers complete?
   ✅ Check: Did it crash?
   ✅ Check: How long did it take?
```

## The Output Looks Like This

```bash
$ dotnet run --stress-test

Testing: Lock-based
═══════════════════════════════════════════════
  Cycle 100: Queue=2    ← Every 100 cycles, show status
  Cycle 200: Queue=1
  Cycle 300: Queue=0
  Cycle 400: Queue=2
  Cycle 500: Queue=5    ← Carrier 2 arrives here
  Cycle 600: Queue=2
  Cycle 700: Queue=0
  Cycle 800: Queue=0
  Cycle 900: Queue=0
  Cycle 1000: Queue=0

  ✓ Completed: 1000/1000 cycles
  ✓ Time: 12.45s
  ✓ Wafers Completed: 10/10  ✅ PERFECT!
  ✓ Wafers Stuck: 0
  ✓ Errors: 0
  Result: PASS ✅

[Repeats for all 12 schedulers...]

═══════════════════════════════════════════════
🏆 FINAL RANKINGS:
═══════════════════════════════════════════════
  🥇 Single Publication      - 11.98s  (WINNER!)
  🥈 Array Single Pub        - 12.01s
  🥉 Lock-based              - 12.45s
  4. Actor-based             - 13.22s
  ...

❌ FAILED:
  ❌ Publication-Based - Crashed at cycle 423
```

## What Does "Queue" Mean?

**Queue** = Number of jobs waiting to be done

- **Queue = 0**: No jobs waiting (excellent! ✅)
- **Queue = 1-5**: A few jobs waiting (normal ✅)
- **Queue = 5-20**: Busy but working (okay ⚠️)
- **Queue > 50**: STUCK! Something is wrong! (bad ❌)

Think of it like a line at a coffee shop:
- 0-5 people in line = fast service ✅
- 50+ people in line = something's broken ❌

## Pass or Fail?

### ✅ PASS if:
- Runs all 1000 cycles without crashing
- At least 8 out of 10 wafers complete
- Less than 10 errors

### ❌ FAIL if:
- Crashes before cycle 1000
- Less than 8 wafers complete
- More than 10 errors
- Queue gets stuck > 50

## Why Run This Test?

To answer the question:
> **"Which scheduler is the MOST RELIABLE?"**

Because:
- ✅ A scheduler might be fast but crash after 500 cycles
- ✅ A scheduler might work once but fail under stress
- ✅ We want to find schedulers that work reliably for a LONG time

## The Bottom Line

**The stress test runs each scheduler through 1000 cycles to find the most reliable one.**

Think of it like a car reliability test:
- 🚗 Drive 1000 miles
- ⏱️ Measure speed
- 🔧 Count breakdowns
- 🏆 Find the best car

That's it! It's just a **reliability marathon** for schedulers! 🏃‍♂️💨

## Quick Command Reference

```bash
# Run the stress test
dotnet run --stress-test

# Or use short form
dotnet run --stress
```

Then grab some coffee ☕ and watch the results! The test takes about 2-5 minutes to run all 12 schedulers.
