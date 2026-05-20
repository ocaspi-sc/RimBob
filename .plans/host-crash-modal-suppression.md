# Plan: Suppress the Windows crash modal on Host exceptions

Status: proposed
Owner: unassigned
Related: `Src/ApiHost/Program.cs`, `run-rimbob.ps1`, `/run-rimbob` skill

## Problem

When an agent runs RimBob and `RimBob.Host` throws, a **Windows Error Reporting
modal** ("RimBob.Host.exe has stopped working") appears and blocks until a human
dismisses it. Agents cannot click it, so the run hangs and the failure is opaque
until someone is physically at the machine.

## Root cause

The modal is the OS reaction to **any exception escaping `Main`**. `Program.cs`
lets that happen in two ways:

1. **`Program.cs:335-343`** — the startup `try/catch` logs `Log.Fatal(...)` then
   `throw;`. Re-throwing propagates the exception out of `Main`, so the runtime
   crashes the process and WER pops the modal. This is the common path: RIMAPI
   unreachable, port already bound, bad `appsettings`, Gemini key issues, etc.
2. **`Program.cs:15-214` (above the `try`)** is entirely unprotected:
   - `await MinisterOutputStore.LoadAsync(...)` (`:29`)
   - `await ColonyStateSnapshotStore.LoadAsync(...)` (`:31`)
   - `builder.Build()`
   - `.ValidateOnStart()` options validation (`:51`)
   A failure in any of these escapes `Main` with **no catch whatsoever**.

Why it is worse for agents: the default `run-rimbob.ps1` path
(`Start-HostNotificationArea` → `Start-HostNotificationIcon`,
`run-rimbob.ps1:335-342`) starts the host with `CreateNoWindow=$true` /
`UseShellExecute=$false`. The host has no console, so the WER modal is the only
visible UI; the tray `Timer` (`run-rimbob.ps1:374-381`) keeps the launcher
process alive while the modal waits for a click that never comes.

Note: ASP.NET Core `BackgroundService` defaults to
`BackgroundServiceExceptionBehavior.StopHost`, so a throwing hosted service
(`DayTickOrchestrator`, etc.) currently *stops the host gracefully* rather than
crashing — that path is already modal-free. The gap is unhandled exceptions on
the main path and on raw background threads.

## Goal

`RimBob.Host` must **never terminate via an unhandled exception**. Any fatal
condition should: log `Fatal` to Serilog, flush, and exit with a non-zero code.
A failed-but-clean process + logs is the desired agent-visible outcome.

## Proposed change (general fix, not a workaround)

All in `Src/ApiHost/Program.cs`.

### 1. One guard around the whole program

Move the unprotected top-of-file startup (the two `LoadAsync` awaits,
`builder.Build()`, `ValidateOnStart`) inside the existing `try`. Replace the
re-throw with a clean exit:

```csharp
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception during startup/run");
    Console.Error.WriteLine($"FATAL: {ex.Message} (see logs)");
}
finally
{
    Log.CloseAndFlush();
}

Environment.Exit(hadFatal ? 1 : 0);
```

A clean `Environment.Exit(1)` does **not** trigger WER. Keep a meaningful
non-zero code so `dotnet run` / the tray timer sees failure.

### 2. Catch off-main-thread crashes

Register, as the very first lines of `Main`:

```csharp
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Log.Fatal(e.ExceptionObject as Exception, "AppDomain unhandled exception");
    Log.CloseAndFlush();
    Environment.Exit(1);
};
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error(e.Exception, "Unobserved task exception");
    e.SetObserved();
};
```

### 3. Defense-in-depth: suppress the WER UI for this process

For crashes that `try/catch` cannot intercept (stack overflow, access
violation, `Environment.FailFast`), disable the OS error box at process start:

```csharp
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
static extern uint SetErrorMode(uint uMode);
// SEM_FAILCRITICALERRORS (0x1) | SEM_NOGPFAULTERRORBOX (0x2)
if (OperatingSystem.IsWindows()) SetErrorMode(0x0001 | 0x0002);
```

(1)+(2) is the primary fix; (3) is belt-and-suspenders for the uncatchable
cases. (3) is Windows-only — guard with `OperatingSystem.IsWindows()`.

## Out of scope / rejected

- **Machine-wide registry tweak** (`Windows Error Reporting\DontShowUI`):
  rejected — affects every app on the box, and requires per-machine setup that
  new agents/worktrees would not inherit.
- **Launcher-only mitigation** (set child `ProcessStartInfo` error mode in
  `run-rimbob.ps1`): would miss the `-Foreground` / `-HostOnly` paths and the
  `dotnet run` apphost child. The app-level fix is strictly more general.

## Verification

1. Force a startup failure (e.g. point `RimBob:ListenUrl` at an in-use port, or
   a bad `appsettings.Local.json`).
2. Run `.\run-rimbob.ps1` (default tray path) and `.\run-rimbob.ps1 -Foreground`.
3. Expect: **no modal**, process exits non-zero, `Fatal` line in
   `logs/rimbob-*.log`, tray timer detects `HasExited` and unwinds.
4. Healthy boot still reaches `/api/system/health` (no regression).

## Dashboard impact

None directly. Optional follow-up: surface "last host fatal" (timestamp +
message from the Serilog `Fatal` line) on the SYSTEM panel so a crashed boot is
visible in-dashboard rather than only in log files.
