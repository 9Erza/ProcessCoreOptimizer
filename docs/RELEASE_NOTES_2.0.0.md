# Process Core Optimizer 2.0.0 — Release Notes

> Status: **ready for maintainer review, NOT published.**
> No public GitHub Release has been created from this branch.

## What is new

**Engine 2.0** — every process mutation now passes an identity gate
(PID + start time + process name + EXE path when bound) verified on a fresh OS
handle immediately before the write. Uncontrolled PID-only mutation paths were
removed. Outcomes use an explicit model (`FullyApplied`, `PartiallyApplied`,
`Failed`, skips) — partial success is reported honestly and retried, never
cached as done. Affinity validates masks up front and reports a failed CPU Sets
cleanup instead of hiding it.

**Profiles 2.0** — deterministic conflict detection with path-bound precedence,
duplicate / import / export (validated JSON), capture-live-settings drafts,
per-instance history, and a dedicated watcher service with reentrancy,
cancellation and shutdown guards.

**CPU & topology** — honest Unknown state when firmware data is missing,
named core presets (All / P-cores / E-cores / physical-without-SMT, only when
valid for the machine), multi-group detection with a clear group-0 limitation
note, snapshot EXE paths, process details (PID, start time, path, affinity
source, last status), and process/profile filters.

**Desktop 2.0** — design tokens (`Themes/`), six views
(Processes, CPU & Topology, Profiles, Hardware, Settings, Diagnostics),
session restore of pre-optimization settings (verified identity only),
releases-aware updater (no false positives before a release is published),
startup registration applied only when startup settings change, and a
collapsible sidebar Developer Hub (version card + official links + in-app
About navigation, full PL/EN).

## Compatibility

- Profiles and settings from 1.3.x load unchanged (schema v2, legacy
  `Exclusive` migrates to `Affinity`).
- Corrupt JSON falls back to `.bak` for profiles and settings.
- Version source of truth: `version.txt` → MSBuild → PE metadata
  (`2.0.0.0` / `2.0.0+sha`), UI, updater, docs.

## Verification

- Release build: 0 warnings, 0 errors.
- Tests: 258 MSTest cases — 255 passing, 3 environment-gated real-startup
  integration tests skipped without `PCO_REAL_STARTUP_TESTS=1`
  (fake backend, read-only checks, negative P0 identity tests, CPU Sets
  restore/suspension matrix, startup planner/coordinator/backend,
  reset safety, onboarding, helper protocol, updater gates (incl.
  no-downgrade-prompt, unparsable-tag, exact-installer-asset, draft/
  prerelease and HTTP/matrix guards), localization parity, XAML
  Run-binding-mode guard, Developer Hub footer guards, update-session
  once/defer rules and no-download scan, and real Windows API tests on
  isolated self-owned `%TEMP%` probe copies). Manually enabled once locally:
  all 3 real startup tests green, zero registry/task leftovers. No user,
  service or agent process is ever touched.
- Startup architecture: plan → execute → verify with create-before-delete
  ordering; UAC cancel and failed-create paths proven by tests.
- GUI: 21/21 UI Automation checks on Windows 11 (1920×1080, 100% DPI):
  all seven views incl. Smart Rules via real clicks, instance-view toggle,
  mini activity console, slim scrollbar, compact hardware empty-state,
  maximize/minimize/restore, tray-aware close, startup status text, scrolled
  startup section, PL toggle with screenshots, safe-mode log line, about links
  with automation names, first-run wizard pages + Finish + settings stamp,
  isolated portable first-run smoke test, Developer Hub collapsed/expanded
  EN+PL with per-row checks, Space-toggle keyboard path, in-app About
  navigation, 1000×700 minimum-size overflow check, update dialog EN+PL
  (real dialog code, canned API answers) and manual check flow.
  Harness: `tools/Run-GuiVerification.ps1` (backs up and restores `%APPDATA%`,
  launches with `--safe-mode`).
- GUI defects found live and fixed: theme scope crash at startup (now
  App-level), version showing `+sha`, truncated process names, clipped log
  lines, profile-button overflow, CPU Sets handle rights (real tests caught it),
  wizard modal missed by UIA tree (Win32 fallback), minimize-to-tray vs UIA
  restore sequencing in harnesses, `Run.Text` TwoWay bindings throwing on
  read-only sources and killing the details panel (now explicit `Mode=OneWay`
  enforced by `XamlBindingModeTests`; proof:
  `docs/screenshots/screen_processes_details.png`), over-narrow IN/RAM/CPU
  columns (widened after screenshot audit), oversized sidebar console (now a
  60-DIP mini console), heavy default scrollbar (now a 5-DIP slim style),
  blank hardware OFF view (now a compact empty-state card).
- GUI defects found live and fixed: theme scope crash at startup (now
  App-level), version showing `+sha`, truncated process names, clipped log
  lines, profile-button overflow, CPU Sets handle rights (real tests caught it),
  wizard modal missed by UIA tree (Win32 fallback), minimize-to-tray vs UIA
  restore sequencing in harnesses, `Run.Text` TwoWay bindings throwing on
  read-only sources and killing the details panel (now explicit `Mode=OneWay`
  enforced by `XamlBindingModeTests`; proof:
  `docs/screenshots/screen_processes_details.png`), over-narrow IN/RAM/CPU
  columns (widened after screenshot audit).
- Performance baseline (Win11, i5-10210U, 8 LP, 100% DPI, published Release):
  startup 1.5–1.6 s; idle 60 s ≈ 190 MB WS / 90 MB Private / 0.55% CPU /
  ~786 handles / 23 threads; 100 synthetic profiles ≈ no CPU delta, +2 MB WS;
  HW monitor ON ≈ +14 MB WS, +60 handles; minimized ≈ 0.27% CPU.
  Harnesses: `tools/Measure-Performance.ps1`, `tools/Measure-Cpu.ps1`;
  raw CSV in gitignored `.evidence/perf`.
- CI: restore → Release build → tests → `git diff --check`, green.
- Local artifacts: self-contained single-file `ProcessCoreOptimizer.exe` 2.0.0
  and `installer/Output/ProcessCoreOptimizer_Setup_2.0.0.exe` (final main
  build: 54,060,618 bytes, SHA-256
  `4EBFC85BFB12A4C4085DD657A9AD9ED47EF5DC681F2F7BA759C6B2DF70E1F52E`,
  PE 2.0.0; reproducible via `tools/Build-Installer.ps1`) — see
  `docs/INSTALLER.md` and `tools/Validate-Release.ps1` (8/8 static checks green).
- Interactive screenshots: real 2.0 UI (7 views + instances toggle + wizard
  pages + portable first-run smoke test + collapsible Developer Hub EN/PL);
  no mockups. README gallery: `ProcessCoreOptimizer/Screenshots/` (6 shots);
  full run: gitignored `.evidence/gui/` (30 captures).
- Install/upgrade/uninstall on a VM: NOT RUN (no isolated VM; dev-machine
  install deliberately avoided). Multi-group hardware: NOT RUN (8-LP machine);
  synthetic mapping discipline tested instead.

## Known limitations

- Group 0 / 64 logical processors UI limit (by design, reported in-app).
- No kernel driver, no memory injection, no anti-cheat bypass; protected
  processes are refused with a reason.
- `Setup.exe` installer identity from 1.3.0 is not reproducible from this
  repository (built externally); see `docs/INSTALLER.md`. No release is
  published without maintainer approval.
