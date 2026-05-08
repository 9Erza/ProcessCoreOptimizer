# Changelog

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
