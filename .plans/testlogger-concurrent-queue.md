# TestLogger — Replace Coarse Lock with ConcurrentQueue

**Status:** planned

## Motivation

`TestLogFile.Write` uses a coarse `lock(Lock)` around every JSONL line write.
A TODO in the file (line 36) calls out the bottleneck: "replace with a lock-free
concurrent queue + dedicated writer thread if test-suite parallelism grows."
Test parallelism is already enabled in `xunit.runner.json`; the lock is a
latent bottleneck. This slice resolves the TODO cleanly.

## Context

- File: `Src/Tests/Infrastructure/TestLogger.cs`
- `TestLogFile` is a static JSONL sink shared across all test threads.
- Current design: `lock(Lock)` guard on `Writer.Value.WriteLine(line)` in `Write()`.
- The per-instance `TestLogger<T>.Events` list is not shared — no change needed there.

## Scope

**In scope:**
- Replace `static readonly object Lock` in `TestLogFile` with a
  `ConcurrentQueue<string>` field.
- Add a single background `Thread` (daemon, `IsBackground = true`) that loops:
  dequeue → write → flush, with a brief `Thread.Sleep(1)` spin.
- `Write()` becomes: `_queue.Enqueue(line); Console.WriteLine(...)` — no lock.
- `ProcessExit` handler: drain the queue fully before close (spin until empty,
  then `Flush()` + `Close()`).
- Remove the `// TODO:` comment once the change is in.

**Out of scope:**
- Changing `TestLogger<T>.Events` (per-instance, not shared).
- Changing any test that reads `Events`.
- Any other file.

## Approach

1. Read current `TestLogFile` implementation.
2. Add `private static readonly ConcurrentQueue<string> _queue = new();`
3. Add background writer thread in `Open()`: daemon thread that spins on
   `_queue.TryDequeue` and writes to the `StreamWriter`.
4. Update `Write()` to just `_queue.Enqueue(line)`.
5. Update `ProcessExit` to drain queue before flush/close.
6. Remove the `// TODO:` comment.
7. `using System.Collections.Concurrent;` if not already present.

## Verification

```
dotnet build Src/RimBob.sln
dotnet test Src/RimBob.sln --no-build
```

All tests must pass. Build must produce zero errors.

## Where to see it (dashboard)

No dashboard surface — this is test infra only.
Verify by running the test suite and confirming the JSONL log file is written
correctly (check `Src/Tests/bin/.../logs/test-*.jsonl` after the run).

## Open questions

None. Change is mechanical and bounded to one file.

---

## Summary (landed 2026-05-21)

**Motivation.** `TestLogFile.Write` used a coarse `lock(Lock)` on every JSONL line write, a latent bottleneck the in-file TODO called out. This slice resolves that TODO cleanly ahead of further test-suite parallelism growth.

**Context.** Single test-infra file: `Src/Tests/Infrastructure/TestLogger.cs`. No production code, no dashboard surface.

**Scope.** What shipped:
- `ConcurrentQueue<string>` replaces the coarse lock.
- Dedicated `IsBackground = true` daemon thread (`RimBob.TestLogFile.Writer`) drains the queue with a 1 ms spin.
- `ProcessExit` handler drains queue, joins the writer thread, then flushes/closes.
- `Write()` is now lock-free: enqueue + `Console.WriteLine`.
- `// TODO:` comment removed.
- Stylistic: local `var` → explicit types inside `TestLogFile` (Codex sweep; no behavior change).

**How to verify (human).**
- Commands: `dotnet build Src/RimBob.sln && dotnet test Src/RimBob.sln --no-build` — 302/302 pass.
- Files to glance at: [`Src/Tests/Infrastructure/TestLogger.cs`](Src/Tests/Infrastructure/TestLogger.cs)

**Codex run:** 20260521-172500-testlogger-concurrent-queue · branch `codex/prompt-20260521-172500-testlogger-concurrent-queue` · landed commit `<tbd>`
