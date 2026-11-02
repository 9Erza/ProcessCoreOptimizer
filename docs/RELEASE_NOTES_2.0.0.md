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
and startup registration applied only when startup settings change.

## Compatibility

- Profiles and settings from 1.3.x load unchanged (schema v2, legacy
  `Exclusive` migrates to `Affinity`).
- Corrupt JSON falls back to `.bak` for profiles and settings.
- Version source of truth: `version.txt` → MSBuild → PE metadata
  (`2.0.0.0` / `2.0.0+sha`), UI, updater, docs.

## Verification

- Release build: 0 warnings, 0 errors.
- Tests: 97 MSTest cases, all passing —
  fake-backend logic, read-only self-PID checks, negative P0 identity tests,
  CPU Sets restore/suspension matrix, updater gates, localization parity, and
  real Windows API tests on isolated self-owned `%TEMP%` probe copies
  (affinity/CPU Sets/priority round-trips with real Win32 codes, exited-process
  refusal, watcher apply + restore + suspension, path-bound mismatch).
  No user, service or agent process is ever touched.
- GUI: 11/11 UI Automation checks on Windows 11 (session 1, 1920×1080, 100% DPI):
  all six tabs via real clicks, maximize/minimize/restore, tray-intercept close,
  safe-mode log line. Harness: `tools/Run-GuiVerification.ps1` (backs up and
  restores `%APPDATA%`, launches with `--safe-mode`).
- GUI defects found live and fixed: theme scope crash at startup (now
  App-level), version showing `+sha`, truncated process names, clipped log
  lines, profile-button overflow, CPU Sets handle rights (real tests caught it).
- Performance baseline (Win11, i5-10210U, 8 LP, 100% DPI): startup 1.5–1.6 s;
  idle 60 s ≈ 180 MB WS / 85 MB Private / 0.5% CPU / ~740 handles / 21 threads;
  100 synthetic profiles ≈ +5 MB WS, no CPU delta; HW monitor ON ≈ +10 MB WS,
  +80 handles; minimized ≈ idle. Harness: `tools/Measure-Performance.ps1`,
  `tools/Measure-Cpu.ps1`; raw CSV in gitignored `.evidence/perf`.
- CI: restore → Release build → tests → `git diff --check`, green.
- Local artifacts: self-contained single-file `ProcessCoreOptimizer.exe` 2.0.0
  and `installer/Output/ProcessCoreOptimizer_Setup_2.0.0.exe` (50,784,844 bytes,
  SHA-256 `11DE86C0…D6795D9F1`, PE 2.0.0) — see `docs/INSTALLER.md`.
- Interactive screenshots: real 2.0 UI (6 screens) published; no mockups.
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
