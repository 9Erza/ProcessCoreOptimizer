# Process Core Optimizer 2.1.0 — Release Notes

> Status: **published 2026-11-22 as stable GitHub Release v2.1.0.**

## What is new

**Interface 2.1** — a full visual rebuild into a minimal dark interface: icon
sidebar with an active indicator, page headers, cards, empty states, one accent
color, toggle switches and filter chips. Every view (Processes, CPU & Topology,
Profiles, Smart Rules, Hardware, Diagnostics, Settings) and dialog was rebuilt.

**Native window** — the main window uses WindowChrome instead of a
software-rendered layered window: GPU composition, Aero Snap, the Windows 11
Snap Layouts flyout, DWM dark frame and rounded corners.

**Autostart** — first-run wizard choices are now registered with Windows; a
missing or stale entry is repaired at launch; the Task Manager "Startup apps"
disabled flag is detected and cleared; Settings → Startup has a Repair button.

**CPU engine** — path-bound profiles work for elevated / anti-cheat protected
games, CPU Sets widen a stale hard affinity, drift is re-applied every 10 s,
manual Apply is no longer reverted by the watcher, and several profile-editing
bugs were fixed.

**Polish translation** — core presets, operation statuses, the activity log,
watcher status and window controls now follow the selected language. File logs
stay in English with stable status codes for diagnostics.

## Upgrade

Installing `ProcessCoreOptimizer_Setup_2.1.0.exe` over 2.0.0 upgrades in place
(same installer identity and folder). The setup closes a running PCO first.
`%APPDATA%\ProcessCoreOptimizer` is never touched.

See [CHANGELOG.md](../CHANGELOG.md) for the full list.
