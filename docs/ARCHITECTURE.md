# Process Core Optimizer — Architecture (2.0.0)

One owner per responsibility. Cached observations never authorize mutations.

## Layers

- `Views/` — thin UserControls (`ProcessesView`, `CpuView`, `ProfilesView`,
  `HardwareView`, `SettingsView`, `DiagnosticsView`) + `MainWindow` shell.
  No business logic in code-behind except log auto-scroll.
- `Themes/` — `Colors.xaml` design tokens, `Controls.xaml` control styles.
- `ViewModels/MainViewModel` — composition root: timers, commands, row sync,
  selection, settings apply. Delegates engine work to services.
- `Services/` — single owners:
  - `ProcessService` — the only code that calls mutating Win32 APIs
    (affinity, CPU Sets, priority). No PID-only mutation overloads remain.
  - `OptimizationService` — profile application + per-instance signature cache.
  - `ProfileWatcherService` — background scan → apply → cleanup pipeline
    (reentrancy, cancellation, shutdown guards).
  - `ProcessScannerService` — the only CPU sampler; full scan vs targeted
    profile scan.
  - `ProfileService` — load/sanitize/migrate/import/export, path-aware identity.
  - `HardwareService` — topology + CPU Set map + report (group 0 limit).
  - `HardwareMonitorService` — lazy LibreHardwareMonitor metrics.
  - `SessionRestoreService` — in-memory pre-apply baselines + verified restore.
  - `AppliedOperationHistory` — bounded diagnostics ring (never persisted).
  - `ProcessIdentityValidator` / `ProfileConflicts` / `ProfilePreviewService` /
    `CorePresets` — pure, unit-tested logic.
- `Models/` — `OperationStatus` outcomes, `OptimizationResult/BatchResult`,
  `ProcessProfile` (schema v2), `ProcessInstanceKey`, `ProcessDetails`,
  `CpuTopologyReport`.

## Identity model

A mutation requires, re-acquired immediately before the OS call:

- PID + process start time (via `GetProcessTimes` on a minimal handle),
- process name (normalized, `.exe`-insensitive),
- EXE path when the profile is path-bound (never falls back to same-name),
- not a protected system process.

`default` start time, mismatch, or unreadable identity → refusal
(`SKIPPED_IDENTITY_MISMATCH` / `SKIPPED_PROTECTED`). Priority is written
through the same verified handle (`SetPriorityClass`); affinity is set after
the handle check plus a managed re-check.

## Operation outcomes

`OperationStatus`: `FullyApplied`, `PartiallyApplied`, `Failed`,
`SkippedAlreadyApplied`, `SkippedIdentityMismatch`, `SkippedProtected`,
`SkippedNoPermission`, `Unsupported`, `Cancelled`.
Full success = every requested action applied. Only full success is cached —
partial results are retried on the next pass. Pure failure + elevation
evidence = `SkippedNoPermission` (retry without elevation is pointless).

## Watcher

Single pipeline (`ProfileWatcherService.RunOnceAsync`): snapshot profiles →
targeted `GetProcessesByName` scan → per-instance apply → stale-cache cleanup
→ history + diagnostics. Overlapping runs serialize (second returns null);
disposal cancels and forbids further OS writes. Timer ownership stays in the
ViewModel; the service owns the mechanism.

## Profiles

Schema v2 (compatible read of legacy files). Sanitize keeps priority-only
profiles, drops core-bound mask-0 and no-op profiles, dedups by
name+normalized-path, migrates `Exclusive` → `Affinity`. Precedence:
path-bound, then newest, then Id. Conflicts are computed (`ProfileConflicts`)
and shown; the engine resolves them deterministically, never randomly.

## Limits (by design)

- Processor group 0 / first 64 logical processors (64-bit affinity mask).
  Multi-group machines are detected (`CpuTopologyReport`) and reported.
- Unknown topology classification (`[?]`) instead of guessing P/E/SMT.
- No memory injection, no drivers, no anti-cheat bypass.
- Updater notifies only when a published release carries an installer asset.
