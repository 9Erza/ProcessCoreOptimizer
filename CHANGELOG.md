# Changelog

## Process Core Optimizer 2.1.1

**Release date: 2026-11-22** · Windows 10/11 x64 · Installer: `ProcessCoreOptimizer_Setup_2.1.1.exe`

A maintenance update for 2.1.0. Installing it over 2.0.x or 2.1.0 upgrades in place.

### Added

- **Erza Studio** — "Made by Erza Studio · erza.studio" in Settings → About and an
  erza.studio link in the Developer Hub (opens https://erza.studio/pco/).

### Fixed

- Starting PCO a second time (e.g. at logon while it was already running) could crash
  because the log file was locked by the running instance; logging is now shared and
  best-effort.
- Areas of the window not yet repainted (resize, restore, GPU busy with a game) show
  the app background instead of black; opening from the tray no longer goes through a
  minimized → restore round trip.

## Process Core Optimizer 2.1.0

**Release date: 2026-11-22** · Windows 10/11 x64 · Installer: `ProcessCoreOptimizer_Setup_2.1.0.exe`

A redesigned, faster interface with a complete Polish translation, a reliable
autostart, and fixes across the CPU affinity / CPU Sets engine. Installing 2.1.0
over 2.0.0 upgrades it in place; profiles and settings are kept.

### Highlights

- **Interface 2.1** — minimal dark design, icon sidebar, cards and page headers.
- **Native Windows 11 window** — rounded corners, Snap Layouts, dark frame, GPU composition.
- **Autostart fixed** — registered after the first-run wizard, self-repairing, Repair button.
- **CPU engine fixes** — protected games, CPU Sets vs. stale affinity, drift re-apply.
- **Full Polish translation** — core presets, statuses, activity log and window controls.

### Added

- Settings → Startup: **Repair** button that re-registers the startup entry.
- Drift check: applied instances are re-verified every 10 s and re-applied (bounded)
  when a game or launcher resets priority, affinity or CPU Sets.
- Snap Layouts flyout on the maximize button (Windows 11) and Aero Snap.
- Localized Diagnostics statuses, Processes status column and window-button names.

### Changed

- Whole UI rebuilt: new palette with one accent color, primary / secondary / subtle
  buttons, toggle switches in Settings, filter chips, search box with placeholder,
  dark tooltips and context menus, slim scrollbars, focus ring.
- Window uses native WindowChrome instead of a software-rendered layered window
  (lower CPU/GPU use while the window is open).
- CPU & Topology: stat tiles, two-column preset / core map layout, live core loads.
- Hardware: metric cards; RAM shows used / total; missing temperature shows "—".
- Strings switched to sentence case in English and Polish.
- The elevated logon task runs at normal priority instead of below normal.

### Fixed

- PCO did not start with Windows: first-run wizard startup choices were
  saved but never registered; a stale Run entry or the Task Manager "Startup apps"
  disabled flag kept Windows from launching it.
- Second launch brings the running window forward; autostart duplicates exit silently.
- Path-bound profiles now work for elevated / anti-cheat protected games
  (EXE path via QueryFullProcessImageName).
- CPU Sets had no effect when an earlier Affinity profile left a restricted hard affinity.
- Manual Apply was reverted by the watcher when an enabled profile differed.
- Update profile deleted profiles hidden by the list filter.
- EcoQoS-only profiles were dropped on load; duplicate lost EcoQoS.
- Priority-only profiles cleared the core selection.
- Profile editor accepted masks beyond this CPU; the default mask now covers all cores.
- CPU & Topology core loads froze on that tab; preset preview summary never appeared.
- Untranslated labels in process details, Diagnostics column titles, the Smart Rules
  "New" button, clipped Diagnostics and Settings labels.

### Installation

Download `ProcessCoreOptimizer_Setup_2.1.0.exe` from GitHub Releases and verify the
SHA-256 shown in the release notes. Installing over 2.0.0 upgrades in place: the setup
closes a running PCO, replaces the program files and keeps
`%APPDATA%\ProcessCoreOptimizer` (profiles, settings, rules) untouched.

## Process Core Optimizer 2.0.0

**Release date: 2026-11-04** · Windows 10/11 x64 · Installer: `ProcessCoreOptimizer_Setup_2.0.0.exe`

The biggest PCO update since 1.3.0. Process optimization is now safer (verified process
identity before every change), easier to automate (Smart Rules, Profile Studio, EcoQoS),
lighter to observe (Live Insights, mini activity console), and the whole desktop
experience — navigation, Developer Hub, Polish/English localization, updates — was
refined for daily use.

### Highlights

- **Process Explorer 2.0** — grouped processes and individual instances with drill-down.
- **Profile Studio** — improved profile creation and management.
- **Smart Rules** — process launch and foreground-window automation.
- **EcoQoS** — optional Windows efficiency controls, never automatic.
- **Live Insights** — lightweight CPU and memory history charts.
- **Reliability** — verified identity for every change, session restore, factory reset.
- **Interface** — compact sidebar, Developer Hub, full Polish/English.

### Added

- Grouped / Instances view toggle with per-instance drill-down (PID, start time, EXE path,
  CPU, RAM, priority, assigned profile) from a single scan pass.
- Profile Studio editor: name, EXE path with browse, validated hex affinity mask,
  priority, Affinity/CPU Sets/EcoQoS, per-profile apply flags, path-bound conflict hints,
  cancel-safe drafts; duplicate, import and export (validated JSON).
- Smart Rules engine: OnLaunch (optional delay) and OnForeground (Alt+Tab debounce)
  triggers, cooldowns, deterministic precedence, pause toggle, recent-activity feed
  explaining why a rule fired, per-rule override clearing.
- EcoQoS efficiency mode via the official Windows API (Windows 10 1809+): explicit
  on/off/leave-alone per profile, verified on/off/restore round-trip.
- Live Insights: inline CPU, working-set and private-bytes history in the details panel;
  first CPU sample and unavailable data honestly reported as Unknown, never zero.
- Diagnostics tab: "Why this profile?" explainer, rule activity, anonymized on-demand
  ZIP export (user paths masked, notes excluded).
- Sidebar Developer Hub: collapsible `PCO <version>` card with the official links
  (repository, 9Erza, DobryPC.pl, Buy Me a Coffee) plus in-app About navigation.
- Mini activity console in the sidebar (status dot + latest message, click opens
  Diagnostics) replacing the bulky log list.
- Update Available dialog (dark PCO theme, PCO icon, PL/EN) with a manual
  "Check for updates" action and honest statuses in Settings.
- First-run wizard (welcome, Affinity vs CPU Sets, startup) and reorganized Settings
  (General / Startup & Privileges / Monitoring / Hardware / Diagnostics / Updates /
  Data & Reset / About).

### Changed

- New dark design system (`Themes/` tokens) across all seven views.
- Correct numeric sorting, extended filters (name + path, only-optimized, with-profile)
  and stable refresh without scroll or selection resets.
- Session restore of pre-optimization settings, verified per instance (recycled PIDs
  never receive another instance's settings).
- Startup registration is applied only when startup settings actually change; real
  startup state (Disabled / Registry / ElevatedTask / Conflict) is shown in Settings.
- Update checks notify only for a real published release carrying the exact installer
  asset — a bumped `version.txt` alone never notifies; automatic results stay silent
  unless an update is truly available; no modal over games or minimized windows.
- Hardware Monitor stays off until explicitly enabled; the OFF state now shows a
  compact empty-state card instead of an empty view.
- Sidebar: slimmer navigation scrollbar, compact branding, mini console; full
  Polish/English parity enforced by tests.

### Fixed

- Stale snapshots can no longer authorize a change: PID + start time + process name
  (+ EXE path for path-bound profiles) are re-validated from a fresh OS handle before
  every write (`SKIPPED_IDENTITY_MISMATCH` on mismatch, including PID reuse).
- Priority changes no longer fire from merely browsing the priority list; Apply, Save
  and Update remain the explicit mutation points.
- Priority-only profiles are no longer dropped by profile sanitization.
- Core checkbox mapping uses the logical processor index instead of list position.
- Profile Update/Delete resolve by stable `Profile.Id`; name-only saves preserve
  path-bound siblings.
- Affinity failures now surface a failed CPU Sets cleanup explicitly instead of
  hiding it behind a success status.
- Single-instance startup survives a crash instead of failing on an abandoned mutex.
- Scheduler calls time out instead of hanging the UI.
- Closed process-handle leak in hardware detection fixed.
- Compact core selector tiles no longer stretch on maximized windows: fixed
  38 DIP tile height, top-aligned selector, capped scrollable section (8/8 LPs
  verified at 1000×700 and maximized).
- A failed `% Processor Utility` read no longer voids a verified
  `% Processor Time` sample (independent Unknown per metric); the compact
  selector shows — with a dimmed bar instead of a fake 0%.
- Scope label uses correct singular grammar ("Process group — 1 instance").

### Security & Reliability

- Every OS write requires a verified process identity; uncontrolled PID-only mutation
  paths were removed; protected system processes are refused with a reason.
- Partial success is reported honestly (`FullyApplied` / `PartiallyApplied` /
  `Failed` / skips) and retried — never cached as done.
- RealTime priority stays gated behind an explicit opt-in plus confirmation dialog.
- Settings, profiles and rules persist atomically with `.bak` fallback; loads fall
  back when live JSON is truncated.
- Startup executes plan → verify with create-before-delete ordering; UAC cancel and
  failed-create paths are covered by tests.
- Factory reset (typed `RESET`) wipes settings, profiles, backups, logs and caches,
  clears rule memory and never fires rules mid-reset.
- Background watcher: reentrancy gate, cancellation and shutdown guards, bounded
  caches, throttled logging.
- External links (repository, author, website, support) are allowlisted, open only on
  user click, with no tracking and no network requests by the app itself.
- 289 automated tests (286 passing + 3 environment-gated), UI Automation GUI suite
  25/25 (incl. fixed-height 8-tiles-visible core selector gate at 1000×700 and
  maximized, compact-selector Unknown-state contract, category nav), installer validation 8/8 (static).

### Performance

- No new timers for UI, updater or console: the mini console follows existing events,
  checks run once per launch at most.
- Full process scans run only when the Processes tab is visible; the watcher scans
  only processes with enabled profiles; hardware sensors stay off until enabled.
- Typical idle footprint (Processes tab, no enabled profiles): ~0.5% process CPU,
  ~190 MB working set; minimized: ~0.3% CPU, ~188 MB WS (reference i5-10210U/8LP;
  raw CSV in gitignored `.evidence/perf`). No FPS claims are made: PCO pins work to cores, it does not
  boost hardware.

### Installation

- Windows 10/11 x64 only. One official asset: `ProcessCoreOptimizer_Setup_2.0.0.exe`
  (Inno Setup, Start Menu shortcut, optional desktop icon, EN+PL wizard, clean
  Apps & Features entry; user data is never touched by uninstall).
- Verify the SHA-256 published in the GitHub Release notes before installing.
- The installer is not EV code-signed yet: Windows SmartScreen may ask for
  confirmation on first run — this is expected and documented.

### Migration from 1.3.0

- **v1.3.0 cannot be upgraded in place.** Its installer identity is not reproducible
  from this repository (built externally), so 2.0.0 uses a new installer identity
  and appears as a separate entry — this is intentional, not a bug.
- Recommended path: uninstall 1.3.0 first, then install 2.0.0.
- Your data is preserved: `%APPDATA%\ProcessCoreOptimizer` (profiles, settings,
  rules) is never touched by uninstall or install, and 1.3.x profiles/settings
  load unchanged in 2.0.0 (legacy `Exclusive` migrates to `Affinity`).
- Note: already-installed 1.3.0 binaries check only `version.txt`, so they may
  report 2.0.0 as available and point at the releases page. Install 2.0.0 from
  the release asset directly; no automatic 1.3 → 2.0 upgrade is provided.

### Known Limitations

- UI covers processor group 0 (up to 64 logical processors) by design; larger
  machines are reported in-app, not silently mishandled.
- No kernel driver, no memory injection, no anti-cheat bypass; protected processes
  are refused with a reason. Compatibility with online games is not guaranteed.
- Install / upgrade / uninstall on a spare machine is documented but was not run
  by automation; the manual acceptance procedure in `docs/INSTALLER.md` applies.

### Startup responsiveness & context-aware UI (final)
- Measured root cause of first-launch jank (3× runs, `PCO_STARTUP_PROFILE=1`, reference i5-10210U/8LP machine): the first PDH counter warmup (~4.6 s) ran on the UI thread inside the first refresh tick; time-to-first-list ~7.5 s with a frozen window.
- Fix: counter warmup moved off the UI thread (thread-safe init/read/release), immediate first scan instead of waiting a full interval, single Dispatcher hop per refresh with name/profile dictionaries (no O(n²) scans) and paused CollectionView re-sorting during row mutation (no `DeferRefresh` around mutations).
- Result on the same machine: first paint ~1.3–1.7 s, first process list ~1.3 s (was ~7.5 s), no UI stall beyond a ~400 ms one-time XAML build. Event-driven mini console (no timers), full history stays in Diagnostics.
- Process Explorer is now context-aware: full-width list with a select-process hint and loading state when nothing is selected; the right CPU panel appears only with a live process/instance or profile target (overlay drawer under 1130 DIP); the header shows the explicit scope (Process group — N instances / Instance — PID / Profile: name); close clears the target without any mutation; switching Grouped/Instances or tabs drops stale selections; exited processes invalidate the details panel.
- CPU & Topology is an informational hub, not a second editor: processor overview (vendor, logical/physical/SMT/P-E counts, affinity range), legend for `[P]`/`[T]`/`[E]`/`[?]` tags, read-only compact Core Tiles in a wrapping grid, presets as non-mutating previews with highlight and summary, Affinity vs CPU Sets explainer; the duplicate no-target Apply editor was removed.
- Saved Profiles: empty state (Create/Import only), select hint, selection-gated Edit row; Smart Rules: empty state, selection-gated actions.
- Settings: 8-category navigation (General / Startup / Optimization / Hardware / Diagnostics / Updates / Data / About) with a single active panel; factory reset stays an explicit typed-RESET danger zone; category switches persist nothing; About deep link (Developer Hub → Settings → About) preserved.
- Save semantics unchanged and explicit: Save/Update Profile persist and apply to running processes (logged per action); Profile Studio Save persists only.

## Previous releases

### v1.3.0 - Core Refactor & Performance Foundation

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
