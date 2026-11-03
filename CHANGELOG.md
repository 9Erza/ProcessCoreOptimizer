# Changelog

## v2.0.0 - Engine 2.0, Profiles 2.0 & Desktop 2.0 (+ Major Functional Expansion)

### Process Explorer 2.0 (P0/P1)
- Grouped / Instances view toggle (local switch, no rescan): per-instance drill-down (PID, start time, EXE path, CPU, RAM, priority, profile) from the same scan pass.
- Correct numeric sorting (raw sort twins, semantic priority rank), extended filters (name + path, only-optimized, with-profile), stable refresh without scroll/selection resets.
- Instance details panel: PID, start, uptime inputs, CPU/RAM, affinity, CPU Sets, profile, last status + "why this profile?" explanation with honest Unknown placeholders.
- Context menus (keyboard accessible): details, create/capture/preview/apply/restore, copy PID/path, open file location. No destructive actions, no mutation on menu open.
- P0 scope safety: instance apply touches only the selected PID (`ApplyProfileToInstances`); Update/Delete resolve by stable `Profile.Id`; name-only saves preserve path-bound siblings.

### Profile Studio (P0/P1)
- Modal editor: name, EXE path (+browse), affinity mask (hex, validated), priority, Affinity/CPU Sets/EcoQoS, apply flags, enable, notes; path-bound conflict hint; cancel-safe drafts.
- Profile grid sorting, context menu (edit/enable-delete), New/Edit/Toggle commands. Legacy 1.3/2.0 schemas still load unchanged.

### Smart Rules engine (P0/P1)
- OnLaunch (optional delay) and OnForeground (stability debounce for Alt+Tab) triggers, cooldowns, priorities, deterministic precedence, in-memory pending/override state.
- No extra timers or scans: evaluates on the existing UI + watcher snapshots; global AutomationPaused stops both channels (observation continues).
- Rule actions: apply profile (scoped to one PID) or session restore; missing-profile misconfiguration is logged with throttle, never retried hot.
- UI: Smart Rules tab, rule editor, pause toggle, recent-activity feed ("why this rule fired"), per-rule override clearing.

### EcoQoS / Efficiency mode (P1)
- Official `SetProcessInformation`/`GetProcessInformation` API with `Version=1` preset; support detection (Win10 1809+), real-state reads, explicit on/off only (never automatic).
- Profile support (`EcoQos` tri-state: on/off/leave-alone, null preserves old profiles), engine status integration, session capture + restore, preview row, clear access-denied/unsupported errors.

### Live Insights (P1)
- Single sampler on the existing refresh tick (no new timers): 120-point ring buffer for CPU + working set + private bytes; PID-recycle resets baseline; first CPU point honestly Unknown.
- Inline history charts in the details panel; unavailable data shown as unavailable, never zero.

### Diagnostics / explainability (P1/P2)
- "Why this profile?" explainer (path-bound/newest/only/disabled cases, PL/EN), rule activity in Diagnostics, anonymized on-demand ZIP export (user-dir prefixes masked, notes excluded).

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
- Removed dead code: PID-only overloads, unused `ChangePriority`, unused `GetPriorityString` (replaced by `TryGetPriority`), legacy `ApplyWindowsStartup`/schtasks paths (replaced by the coordinator), obsolete settings keys (migrated).
- Phase 4 hardening: persistent per-file legacy suppression (no data resurrection after Factory Reset, order-independent readers), fail-closed path handling, tray/Window Icon from the original ICO with proper disposal and language-aware menu rebuild, explicit Startup window-mode semantics (Tray always hides; Minimized respects MinimizeToTray), wizard forced visible even from tray startup, single-instance failover on restart (no dual watchers), developer/support links (allowlisted, no telemetry).

### Phase 3 — startup, administration, reset, first-run (this branch)
- New startup architecture (1.1–1.4): `StartupWindowMode` Normal/Minimized/Tray, desired vs actual evaluation (`Disabled/Registry/ElevatedTask/Conflict/Broken`), COM-based task reads, create-before-delete ordering (the old code deleted the registry entry before the task existed), UAC-cancel handling, privileged helper protocol, cooperative elevated restart with single-instance handoff.
- New Application Data & Reset: restore-defaults (language preserved, startup reconciled first), factory reset with typed `RESET`, shutdown coordination, link/junction guards, no legacy reimport (persistent per-file `.nolegacy.*` stamps), startup removal reported separately.
- New 3-page first-run wizard (welcome+language, Affinity vs CPU Sets, startup) gated by onboarding marker; silent upgrade for existing data; reopenable from Settings; forced visible even from tray startup.
- Settings reorganized into General / Startup & Privileges / Monitoring / Hardware / Diagnostics / Updates / Data & Reset / About with real startup status, privileges display and admin actions.
- Startup window semantics fixed: Minimized stays on the taskbar (MinimizeToTray no longer hijacks it); Tray always hides; elevated-restart mutex failover shuts down instead of risking dual watchers.
- Inno Setup 2.0 project (`installer/`, AppId `{6AA13087-…}`) with repeatable `tools/Build-Installer.ps1` and static artifact validation (`tools/Validate-Release.ps1`, 8/8); historical 1.3.0 identity confirmed unrecoverable.
- 137+ tests (planner/coordinator/backend/onboarding/reset/helper/startup-modes, guarded real-startup integration, real Windows API, GUI harness 16–17/17, perf baselines).
- Developer links: allowlisted 9Erza GitHub / PCO repository / Buy Me a Coffee with vector icons in the sidebar and About section, localized tooltips + automation names, no tracking.

### Tests & CI (P1/P2)
- `ProcessCoreOptimizer.Tests` (MSTest, 143 passing + 3 environment-gated): identity, PID reuse, TOCTOU refusal, path-bound, CPU Sets mapping, mask validation, statuses, partial-success retry, cache invalidation, watcher lifecycle incl. post-restore suspension, conflicts, import/export, preview, full CPU Sets restore, startup planner/coordinator/backend/onboarding/reset/helper/URL-allowlist/window-modes, updater gates, localization parity, persistence, real Windows API on isolated probes.
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
