<div align="center">

<img src="ProcessCoreOptimizer/Images/processcoreoptimizer.ico" alt="Process Core Optimizer logo" width="160" />

# Process Core Optimizer

**Direct Windows process and CPU control — affinity, CPU Sets, priorities and profiles.**

[**English**](README.md) · [**Polski**](README.pl.md)

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)
[![CI](https://github.com/9Erza/ProcessCoreOptimizer/actions/workflows/ci.yml/badge.svg)](https://github.com/9Erza/ProcessCoreOptimizer/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-2EA44F?style=flat-square)](LICENSE)
![Release](https://img.shields.io/badge/release-v1.3.0-EAB45D?style=flat-square)

</div>

> [!NOTE]
> **Version: v1.3.0.** A focused, standalone utility for process CPU management.
> No hidden tweaks, no background bloat — every change is explicit, reversible and logged.

## What is Process Core Optimizer?

**Process Core Optimizer (PCO)** is a lightweight Windows desktop tool for advanced users
who want direct control over how processes use the CPU.

Instead of black-box "boosters", PCO gives you explicit control over:

- **which process** is affected,
- **which cores** it may use,
- **which priority** it runs at,
- **when** the setting is applied (manually or automatically via profiles).

PCO grew out of real-world CPU scheduling work and now shares its hardened engine
lineage with [FrameHub](#framehub) — while remaining a separate, specialized tool
with no gaming feature bloat.

Typical uses:

- pin a game or renderer to P-cores, keep background apps on E-cores;
- lower the priority of launchers, updaters and overlays;
- test how affinity vs. CPU Sets behaves on Intel Hybrid or AMD SMT systems;
- keep per-process settings persistent across restarts via profiles.

Performance gains are workload-dependent and are **never promised**.

---

## Features

- **Process priority management** — Idle → High, with RealTime hidden behind an explicit opt-in.
- **Classic CPU affinity** — hard binding via `ProcessorAffinity`.
- **Windows CPU Sets** — softer scheduling hint via `SetProcessDefaultCpuSets`.
- **Saved profiles** — per-process (optionally path-bound) Affinity / CPU Sets / priority presets.
- **Automatic watcher** — lightweight background loop that applies enabled profiles to newly started processes.
- **Identity-safe mutations** — PID + start time + process name (+ EXE path when bound) re-validated before every OS write; recycled PIDs are refused, never guessed.
- **P/E-core and SMT awareness** — topology from Windows CPU Sets with `[P] / [T] / [E]` labels where supported.
- **Quick core tools** — select all, clear all, disable SMT/HT threads, disable E-cores.
- **Optional hardware telemetry** — CPU / GPU / RAM via LibreHardwareMonitor, lazy and off by default.
- **Bilingual UI** — English / Polski, switchable in-app.
- **Safe persistence** — atomic JSON writes (`tmp` + `File.Replace` + `.bak`) with backup fallback on corrupt files.
- **Single instance** — named mutex with crash-recovery (`AbandonedMutex`) handling.

---

## Affinity vs CPU Sets

PCO supports two CPU modes. They are **not** interchangeable.

| | **Affinity** | **CPU Sets** |
|---|---|---|
| OS mechanism | `ProcessorAffinity` — hard binding | `SetProcessDefaultCpuSets` — scheduling preference |
| Strictness | Process can run **only** on selected logical processors | Scheduler **prefers** selected sets but may still decide under load |
| Clears the other? | Yes — applying Affinity clears process CPU Sets | No — CPU Sets coexist with the affinity mask |
| Best for | Strict isolation tests, reproducible pinning | Day-to-day guiding on Hybrid CPUs, less restrictive |
| Risk | Over-restriction can hurt throughput if you pin too few cores | Weaker guarantee, but friendlier to the Windows scheduler |

> [!IMPORTANT]
> A limited affinity mask also limits what CPU Sets can achieve for that process.
> PCO never claims that CPU Sets can bypass an existing affinity restriction.

Legacy `Exclusive` profiles are automatically migrated to `Affinity`.

---

## Profiles and watcher

A profile targets a **process name** (e.g. `notepad`), optionally bound to an **exact EXE path**
(e.g. `C:\Apps\editor\app.exe`). Path-bound profiles never fall back to a same-name
executable elsewhere — a different path means **no match, no mutation**.

Each profile independently toggles:

- `ApplyCoreOptimization` (Affinity / CPU Sets + mask),
- `ApplyPriority` (priority value).

Priority-only profiles (mask `0`) are valid and preserved. Core-bound profiles require a non-zero mask.

The background watcher:

1. scans **only** enabled profile names (`GetProcessesByName`, no full table walk),
2. re-validates identity per instance (PID + start time + name + path),
3. applies the best matching profile (path-bound wins over name-only),
4. caches **only fully-applied** signatures — partial failures are retried next tick,
5. throttles failure logging (60 s per reason, bounded cache).

Manual **Apply / Save / Update** actions use `force: true` and bypass the cache.

---

## CPU topology and limits

- Topology is read from `GetSystemCpuSetInformation` (group 0) with a WMI fallback.
- Cores are labeled `[P]` performance, `[T]` SMT/HT thread, `[E]` efficiency where the firmware exposes efficiency classes.
- **Current UI limit: processor group 0 / first 64 logical processors.** Classic affinity masks are 64-bit; multi-group workstations are detected and logged honestly instead of pretending full support.
- AMD SMT and Intel Hybrid (P/E) layouts are handled through the same topology path; classification falls back to a conservative heuristic when efficiency data is unavailable.

---

## Requirements

- Windows 10 / 11 x64
- .NET 10 Desktop Runtime (WPF)
- No driver, service or kernel component. No memory injection, no DLL injection.
- Some operations (priority/affinity on elevated or protected processes) require running PCO as administrator; otherwise the OS refuses the change and PCO reports it instead of bypassing it.

---

## Install and run

1. Open [GitHub Releases](https://github.com/9Erza/ProcessCoreOptimizer/releases) and download the latest `ProcessCoreOptimizer` build.
2. Extract and run `ProcessCoreOptimizer.exe` on Windows 10/11 x64.
3. Optional startup flags: `--minimized`, `--tray`, `--no-update-check`, `--safe-mode`.

Portable user data is **never** stored next to the EXE. Everything lives under:

```text
%APPDATA%\ProcessCoreOptimizer\settings.json
%APPDATA%\ProcessCoreOptimizer\profiles.json
%APPDATA%\ProcessCoreOptimizer\ProcessCoreOptimizer.log
```

Legacy files next to the EXE are migrated one-way on first launch.

---

## Screenshots

<details>
  <summary><b>Click to expand / hide gallery</b></summary>
  <br>
  <table align="center">
    <tr>
      <td align="center" width="50%">
        <b>System Processes</b><br>
        <img src="ProcessCoreOptimizer/Screenshots/screen_system-processes.png" width="100%" alt="System Processes" />
      </td>
      <td align="center" width="50%">
        <b>Profiles Manager</b><br>
        <img src="ProcessCoreOptimizer/Screenshots/screen_saved-profiles.png" width="100%" alt="Saved Profiles" />
      </td>
    </tr>
    <tr>
      <td align="center">
        <b>Hardware Monitor</b><br>
        <img src="ProcessCoreOptimizer/Screenshots/screen_hardware-monitor-1.png" width="100%" alt="Hardware Monitor 1" />
      </td>
      <td align="center">
        <b>Detailed Telemetry</b><br>
        <img src="ProcessCoreOptimizer/Screenshots/screen_hardware-monitor-2.png" width="100%" alt="Hardware Monitor 2" />
      </td>
    </tr>
  </table>
  <p align="center"><i>Screenshots reflect v1.3.0 UI. The layout is intentionally conservative — no gaming overlays.</i></p>
</details>

---

## Safety and anti-cheat

PCO changes process-level Windows settings. Incorrect settings can reduce stability.

- System processes (`svchost`, `lsass`, `csrss`, `dwm`, …) and session-0 services are classified as non-user and never auto-optimized; direct mutation paths refuse them.
- RealTime priority is gated behind `AllowRealtimePriority` + an explicit confirmation dialog. Without opt-in, `RealTime` normalizes to `High`.
- Every mutation re-checks identity from a fresh OS handle. Stale snapshots, recycled PIDs, renamed or replaced executables result in `SKIPPED_IDENTITY_MISMATCH` — safe refusal, not a guess.
- Compatibility with anti-cheat systems is **not guaranteed**. PCO does not read process memory, inject DLLs, install drivers, or bypass protections. If the OS or anti-cheat refuses a change, PCO surfaces the Win32 error instead of working around it.
- Use with online games at your own risk.

---

## Build from source

```powershell
dotnet restore ProcessCoreOptimizer.slnx
dotnet build ProcessCoreOptimizer.slnx -c Release
dotnet test ProcessCoreOptimizer.Tests/ProcessCoreOptimizer.Tests.csproj -c Release
```

- Solution: `ProcessCoreOptimizer.slnx` (primary). `ProcessCoreOptimizer/ProcessCoreOptimizer.sln` is legacy.
- Target: `net10.0-windows`, `win-x64`, WPF + WinForms (tray icon).
- Tests: MSTest, 30+ cases, no real processes are mutated (fake backend + read-only self-PID checks).
- CI: `.github/workflows/ci.yml` — restore, Release build, tests, `git diff --check`.

---

## Tech stack

- C# / .NET 10 / WPF
- Windows Kernel32: affinity, CPU Sets, priority, process times
- LibreHardwareMonitorLib (optional telemetry)
- System.Management (topology / RAM fallback)
- MSTest (engine regression tests)

---

## FrameHub

PCO was the original project. The idea grew into **[FrameHub](https://github.com/9Erza/FrameHub)** —
a broader Windows gaming performance hub (game library, session optimization, CS2 tools,
PresentMon benchmarks, Companion, HUDs).

- **PCO** stays a focused, technical CPU/process utility — for apps *and* games, without gaming modules.
- **FrameHub** is the larger hub where game-specific workflows live.
- PCO's current engine incorporates hardening lessons learned over months of FrameHub work (identity gates, partial-success cache, path-bound profiles, atomic persistence) — adapted to PCO's architecture, with no dependency on FrameHub and no copied gaming code.

Neither project is abandoned; they have different jobs.

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md). Current public version: **v1.3.0**.
New engine work is tracked under `Unreleased` until the next tagged release.
No release, installer or version bump is published from this branch without explicit approval.

---

## Author

Developed by **[Eryk / 9Erza](https://github.com/9Erza)**.

---

## License

MIT License. See [LICENSE](LICENSE).
