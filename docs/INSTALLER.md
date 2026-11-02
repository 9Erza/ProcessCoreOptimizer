# Installer & distribution (2.0.0)

## Investigation result

- The public v1.3.0 release carries one asset: `ProcessCoreOptimizer_Setup.exe`
  (~53 MB, self-contained). It was attached to the GitHub Release manually.
- This repository contains **no installer script** (no `.iss`, no WiX, no
  installer workflow in `.github/` history). The AppId / ProductCode /
  UpgradeCode of the historical installer cannot be recovered from the repo.
- The existing `Properties/PublishProfiles/FolderProfile.pubxml` produces a
  self-contained single-file `win-x64` build, consistent with the ~53 MB
  compressed Setup size.

Conclusion: the 1.3.0 installer identity is **not reproducible from this
repository**. Inventing new Inno/WiX identifiers and claiming silent upgrade
compatibility would be dishonest.

## Safe alternative for 2.0.0 validation

Build the portable artifact locally (no installer, no registry writes):

```powershell
dotnet publish ProcessCoreOptimizer/ProcessCoreOptimizer.csproj -c Release -p:PublishProfile=FolderProfile
```

Output: `ProcessCoreOptimizer/bin/Release/net10.0-windows/publish/win-x64/ProcessCoreOptimizer.exe`
(self-contained, single file, requires no .NET runtime on target).

Verified 2.0.0 artifact (local build on maintainer machine):

- FileVersion: `2.0.0.0`
- ProductVersion: `2.0.0+<commit-sha>`
- SHA-256: recorded in the release PR / maintainer handoff, recomputed per build
  (hashes are build-specific because the commit SHA is embedded in ProductVersion).

## Before any public 2.0.0 release

1. Maintainer provides the historical Inno Setup project (or approves a fresh
   identity with a documented break from 1.3.0 upgrade path).
2. Build the installer from the published EXE above; verify install,
   upgrade from 1.3.0, uninstall, and that `%APPDATA%\ProcessCoreOptimizer`
   data survives.
3. Attach the installer as the Release asset, then let the in-app updater
   (releases-aware since 2.0.0) pick it up.

**Do not publish a GitHub Release, installer, or version bump from automation
without explicit maintainer approval.**
