# Process Core Optimizer — Architecture (2.0.0)

One owner per responsibility. Cached observations never authorize mutations.

## Layers

- `Views/` — thin UserControls (`ProcessesView`, `CpuView`, `ProfilesView`,
  `HardwareView`, `SettingsView`, `DiagnosticsView`) + `MainWindow` shell.
  No business logic in code-behind except log auto-scroll. Theme resources live
  in `Application.Resources` (resolvable during any view load); `--safe-mode`
  disables all OS mutations with a log line.
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
  - `SessionRestoreService` — in-memory pre-apply baselines (affinity + CPU Sets
  state incl. no-explicit-sets + priority, first baseline wins) and verified
  restore in affinity → CPU Sets → priority order with honest partial results.
  - `AppliedOperationHistory` — bounded diagnostics ring (never persisted).
  - `ProcessIdentityValidator` / `ProfileConflicts` / `ProfilePreviewService` /
    `CorePresets` — pure, unit-tested logic.
- `Models/` — `OperationStatus` outcomes, `OptimizationResult/BatchResult`,
  `ProcessProfile` (schema v2), `ProcessInstanceKey`, `ProcessDetails`,
  `CpuTopologyReport`.

## Identity model

A mutation requires a valid `ProcessMutationTarget` — PID + process start time
(via `GetProcessTimes` on a minimally-privileged handle that always includes
query rights) + process name (normalized, `.exe`-insensitive) + EXE path when
the profile is path-bound. There is no null/default bypass: `TryCreate` rejects
incomplete identities and every mutating method re-checks `IsValid` first.
Name and path are additionally re-verified on a managed `Process` object before
each write, so a recycled PID or replaced executable yields refusal
(`SKIPPED_IDENTITY_MISMATCH` / `SKIPPED_PROTECTED` / `ERR_IDENTITY_MISMATCH`).

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
disposal cancels and forbids further OS writes. Post-restore instances suspend
automatically (`SkippedSuspended`, expiring, in-memory) so the watcher cannot
undo a manual restore. Timer ownership stays in the
ViewModel; the service owns the mechanism.

## Profiles

Schema v2 (compatible read of legacy files). Sanitize keeps priority-only
profiles, drops core-bound mask-0 and no-op profiles, dedups by
name+normalized-path, migrates `Exclusive` → `Affinity`. Precedence:
path-bound, then newest, then Id. Conflicts are computed (`ProfileConflicts`)
and shown; the engine resolves them deterministically, never randomly.

## Startup, privileges, reset, onboarding

- `DesiredStartupConfiguration` (StartWithWindows + WindowMode + RunElevated)
  vs `ActualStartupConfiguration` (registry snapshot + scheduled-task snapshot
  read via COM, locale-independent). `StartupConfigurationPlanner` (pure)
  decides `Disabled / Registry / ElevatedTask / Conflict / Broken` + reasons +
  operations; the target channel is always created before the stale one is
  removed. `StartupConfigurationExecutor` verifies the final state and never
  declares success on mismatch; `StartupApplyCoordinator` serializes concurrent
  applies (latest-wins). Elevated writes go through a self re-launch
  (`--startup-helper create-task|remove-task`), UAC cancel (1223) is a clean
  cancel; in-proc write when already elevated.
- `UserDataResetService` (root-scoped): restore-defaults preserves language;
  factory reset requires typed `RESET`, quiesces the engine, refuses links and
  escaping paths, deletes only allowlisted files, never reimports legacy files
  (one-shot stamp consumed by `MigrateLegacyFileIfNeeded`), removes startup
  separately with its own error channel, truncates the held log afterwards.
- `OnboardingResolver`: fresh installs (and post-wipe launches) show the
  3-page first-run wizard; legacy data upgrades silently; interrupted runs resume.

## Limits (by design)

- Processor group 0 / first 64 logical processors (64-bit affinity mask).
  Multi-group machines are detected (`CpuTopologyReport`) and reported.
- Unknown topology classification (`[?]`) instead of guessing P/E/SMT.
- No memory injection, no drivers, no anti-cheat bypass.
- Updater notifies only when a published release carries an installer asset.
