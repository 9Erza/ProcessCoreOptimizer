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
- Tests: 72 MSTest cases, all passing (identity, PID reuse, masks, statuses,
  conflicts, import/export, watcher, restore, updater, localization parity).
- CI: restore → Release build → tests → `git diff --check`, green.
- Local artifact: self-contained single-file `ProcessCoreOptimizer.exe`
  2.0.0 (see `docs/INSTALLER.md` for SHA-256 and installer status).
- Interactive screenshots: NOT CAPTURED (headless build environment);
  gallery still shows 1.3.x UI and is marked accordingly.

## Known limitations

- Group 0 / 64 logical processors UI limit (by design, reported in-app).
- No kernel driver, no memory injection, no anti-cheat bypass; protected
  processes are refused with a reason.
- `Setup.exe` installer identity from 1.3.0 is not reproducible from this
  repository (built externally); see `docs/INSTALLER.md`. No release is
  published without maintainer approval.
