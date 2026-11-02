# Changelog

## Unreleased - CPU Engine Hardening (FrameHub lineage, PCO character kept)

### Engine safety (P0)
- Mutations now re-validate PID + process start time + process name (+ EXE path for path-bound profiles) from a fresh OS handle immediately before every write. Stale snapshots never authorize a mutation (`SKIPPED_IDENTITY_MISMATCH` on mismatch).
- `ProcessService` uses minimal handle rights (`QUERY_LIMITED_INFORMATION` for reads, `SET_INFORMATION | QUERY_LIMITED` for priority writes) and `GetProcessTimes` start-time checks to reduce TOCTOU around PID reuse.
- Path-bound profiles never fall back to a same-name executable at a different path.
- Protected system processes are refused on the mutation path (`SKIPPED_PROTECTED`), not only hidden in the UI.
- RealTime remains gated behind `AllowRealtimePriority` + confirmation; `RealTime` normalizes to `High` without opt-in.

### Profiles (P1)
- Fixed priority-only profiles being dropped by `SanitizeProfiles` (`AffinityMask == 0` is valid when `ApplyCoreOptimization == false`).
- Partial successes are no longer cached as fully applied: cache stores only fully-applied signatures, failed halves are retried next watcher tick.
- Fixed `RequiresAdmin` false-positive on successful `OK_CPUSETS_5` (only real `ERR_*` access errors flag elevation now).
- Multi-profile support per process name: path-bound variants coexist, path-bound wins, newest wins on ties.
- `SanitizeProfiles` dedup key is now name+normalized-path (was name only); equivalence check is order-independent and path-aware.
- Priority changes no longer fire from merely browsing the priority ComboBox; Apply/Save/Update remain the explicit mutation points.
- Core checkbox mapping now uses `CoreInfo.Index` (logical processor index) instead of list position.

### Reliability (P1/P2)
- `AtomicFileService` is now per-file locked and uses atomic `File.Replace` with `.bak`; loads fall back to `.bak` when live JSON is truncated/invalid, for both profiles and settings.
- Single-instance mutex survives `AbandonedMutexException` after a crash instead of failing startup.
- `SettingsService` schtasks calls have a 15 s timeout and no longer hang the UI indefinitely.
- Fixed `HardwareService` `Process.GetCurrentProcess().Handle` leak (now disposed).
- `HardwareMonitorService`: Intel iGPU support, busiest-GPU wins on multi-GPU, cached RAM fallback (no per-tick WMI + counter churn).
- `CpuVendor` is cached instead of querying WMI per binding.
- Watcher failure-log throttle no longer keys on PID (restarting processes can't spam) and is bounded to 500 entries.
- `GetCurrentCoreSelection` (CPU Sets first, affinity fallback) is used when reflecting current selection in the UI.

### Tests & CI (P3)
- New `ProcessCoreOptimizer.Tests` (MSTest, 33 cases): PID reuse, name/path mismatch, missing identity, protected processes, zero/invalid masks, priority-only/affinity-only/CPU Sets profiles, partial-success retry, cache invalidation, multi-instance + conflicts, save/load, legacy migration, corrupt JSON fallback, cleanup.
- New lightweight GitHub Actions CI: Release build + tests + `git diff --check`.
- Solution `ProcessCoreOptimizer.slnx` now includes the tests project.

### Docs
- Rewritten `README.md` / `README.pl.md` to FrameHub presentation standard (features, Affinity vs CPU Sets, profiles/watcher, topology/limits, safety, screenshots, build, FrameHub section).
- Screenshots: existing v1.3.0 gallery kept and marked as current; no synthetic screenshots added.

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
