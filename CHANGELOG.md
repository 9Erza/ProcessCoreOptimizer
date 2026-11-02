# Changelog

## v2.0.0 - Engine 2.0, Profiles 2.0 & Desktop 2.0

### Engine safety (P0)
- Mutations now re-validate PID + process start time + process name (+ EXE path for path-bound profiles) from a fresh OS handle immediately before every write. Stale snapshots never authorize a mutation (`SKIPPED_IDENTITY_MISMATCH` on mismatch).
- `ProcessService` uses minimal handle rights (`QUERY_LIMITED_INFORMATION` for reads, `SET_INFORMATION | QUERY_LIMITED` for priority writes) and `GetProcessTimes` start-time checks to reduce TOCTOU around PID reuse.
- Removed uncontrolled PID-only mutation overloads (`SetProcessAffinity`, `SetPriority`, `ApplyCoreOptimization` without identity); every OS write requires a verified identity.
- New `OperationStatus` model (`FullyApplied`, `PartiallyApplied`, `Failed`, `Skipped*`, `Unsupported`, `Cancelled`): full success means every requested action applied; partial success is reported and retried, never cached as done; pure failure with elevation evidence is `SkippedNoPermission`.
- Core masks validated up front (`ERR_EMPTY_MASK`, `ERR_MASK_OUT_OF_RANGE` against the known processor map).
- Affinity apply surfaces a failed CPU Sets cleanup as `OK_AFFINITY_CPUSETS_NOT_CLEARED` instead of hiding it behind `OK_AFFINITY`.
- Path-bound profiles never fall back to a same-name executable at a different path.
- Protected system processes are refused on the mutation path (`SKIPPED_PROTECTED`), not only hidden in the UI.
- RealTime remains gated behind `AllowRealtimePriority` + confirmation; `RealTime` normalizes to `High` without opt-in.
- Phase 2 hardening: `ProcessMutationTarget` makes identity mandatory (no null/default bypass); CPU Sets handles carry query rights for the start-time gate; new `SkippedSuspended` status keeps post-restore instances out of the watcher's way until expiry.

### Profiles & watcher (P1)
- Deterministic conflict detection (`ProfileConflicts`): path-bound wins, then newest, then Id; conflicts shown in UI instead of silently picking.
- Duplicate / export / import profiles (validated JSON, pure merge, no code execution), capture-live-settings drafts, per-instance session history.
- Background watcher extracted to `ProfileWatcherService`: reentrancy gate, cancellation, shutdown guards (no OS writes after dispose), per-run profile snapshots, diagnostics (`LastRun`, throttled logging, bounded caches).
- Fixed priority-only profiles being dropped by `SanitizeProfiles` (`AffinityMask == 0` is valid when `ApplyCoreOptimization == false`).
- Fixed `RequiresAdmin` false-positive on successful `OK_CPUSETS_5` (only real `ERR_*` access errors flag elevation now).
- Priority changes no longer fire from merely browsing the priority ComboBox; Apply/Save/Update remain the explicit mutation points.
- Core checkbox mapping now uses `CoreInfo.Index` (logical processor index) instead of list position.

### CPU topology & process management (P1)
- Honest Unknown classification (`[?]`) when CPU Set data is unavailable; no more confident P/E guesses from WMI fallback.
- Named core presets (All / P-cores / E-cores / physical-without-SMT), offered only when valid for the detected machine.
- Multi-group detection (`CpuTopologyReport`) with an explicit group-0 / 64-LP limitation note; no fake multi-group support.
- Snapshot EXE paths, process details (PID, start time, EXE path, affinity source, priority, assigned profile, last status), process/profile filters.

### Desktop 2.0 (P2)
- Design system: `Themes/Colors.xaml` tokens + `Themes/Controls.xaml` styles; `MainWindow.xaml` split into six views (Processes, CPU & Topology, Profiles, Hardware, Settings, Diagnostics).
- New Diagnostics tab: watcher status, recent operations, conflicts, log.
- Session restore of pre-optimization affinity/priority (verified identity per instance, recycled PIDs never restored into).
- Releases-aware updater: notifies only when a published release carries an installer asset (no false positives from a bumped `version.txt`).
- Startup registration applied only when startup settings actually change.
- One-line version truth: `version.txt` → `Directory.Build.props` → PE metadata (`2.0.0.0`), UI, updater, docs.
- Localization parity enforced by tests (EN/PL key sets identical).

### Reliability (P1/P2)
- `AtomicFileService` is now per-file locked and uses atomic `File.Replace` with `.bak`; loads fall back to `.bak` when live JSON is truncated/invalid, for both profiles and settings.
- Single-instance mutex survives `AbandonedMutexException` after a crash instead of failing startup.
- `SettingsService` schtasks calls have a 15 s timeout and no longer hang the UI indefinitely.
- Fixed `HardwareService` `Process.GetCurrentProcess().Handle` leak (now disposed).
- `HardwareMonitorService`: Intel iGPU support, busiest-GPU wins on multi-GPU, cached RAM fallback (no per-tick WMI + counter churn).
- `CpuVendor` is cached instead of querying WMI per binding.
- Watcher failure-log throttle no longer keys on PID (restarting processes can't spam) and is bounded to 500 entries.
- `GetCurrentCoreSelection` (CPU Sets first, affinity fallback) is used when reflecting current selection in the UI.
- Removed dead code: PID-only overloads, unused `ChangePriority`, unused `GetPriorityString` (replaced by `TryGetPriority`).

### Tests & CI (P1/P2)
- `ProcessCoreOptimizer.Tests` (MSTest, 97 cases): identity, PID reuse, TOCTOU refusal, path-bound, CPU Sets mapping, mask validation, statuses, partial-success retry, cache invalidation, watcher lifecycle incl. post-restore suspension, conflicts, import/export, preview, full CPU Sets restore, updater gates, localization parity, persistence, real Windows API on isolated probes.
- No user/system process is ever touched: fake backend, read-only self-PID checks, and real mutations only on self-owned `%TEMP%` probe copies (terminated + deleted).
- Lightweight GitHub Actions CI: Release build + tests + `git diff --check`.
- Solution `ProcessCoreOptimizer.slnx` includes the tests project.

### Docs
- `README.md` / `README.pl.md` updated for 2.0 (features, tabs, presets, diagnostics, restore, updater, build, installer status).
- New: `docs/ARCHITECTURE.md`, `docs/RELEASE_NOTES_2.0.0.md` (+`.pl.md`), `docs/INSTALLER.md`.
- Screenshots: real PCO 2.0 UI captured on Windows 11 (1920×1080, 100% DPI) via UI Automation; no mockups. Evidence harness output stays in gitignored `.evidence/`.
- GUI QA fixes from live screenshots: clean display version (no `+sha`), wider process-name column, wrapped console logs, two-column profile actions, App.xaml-level theme scope (fixed startup crash).
- `--safe-mode` is now a real diagnostic mode (observes only, blocks manual/apply/restore mutations with a log line).

## v1.3.0 - Core Refactor & Performance Foundation

### Core architecture
- Added `ProcessScannerService` to separate full UI process scans from lightweight profile watching.
- Added `OptimizationService` with structured optimization results and per-process-instance cache.
- Added stable `ProcessInstanceKey` based on PID + start time to avoid Windows PID reuse issues.
- Added `UpdateService` and moved update-checking logic out of `MainViewModel`.
- Added `LocalizationService` as a transition step toward full resource-based localization.
- Added `AtomicFileService` for safer JSON writes.

### Performance
- Full process list scanning now runs only when the Processes tab is visible.
- Background profile watcher scans only processes that have enabled profiles.
- Per-core CPU performance counters are initialized lazily.
- Hardware monitor is initialized lazily only when the Hardware tab is opened.
- Storage sensors are disabled by default to avoid unnecessary disk polling.
- DataGrid row and column virtualization enabled.

### Stability
- Added single-instance guard using a named mutex.
- Added safer startup argument parsing: `--minimized`, `--tray`, `--no-update-check`, `--safe-mode`.
- Profile and settings JSON files are now written atomically with `.tmp` and `.bak` fallback.
- Logger now rotates large log files.
- Version is now read from assembly metadata instead of being hardcoded in the ViewModel.

### Profiles
- Profile schema upgraded to v2 with future-ready fields: ID, display name, optional EXE path, created/updated timestamps, notes and per-action toggles.
- Legacy profiles remain compatible.
- Legacy `Exclusive` profiles continue to be normalized to `Affinity`.

### Settings
- Added update-check toggle.
- Added advanced storage sensor toggle.
