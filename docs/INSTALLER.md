# Installer & distribution (2.0.0)

## Phase 2 investigation result (verified 2026-11-02)

- Public v1.3.0 asset: `ProcessCoreOptimizer_Setup.exe`, 53,727,056 bytes,
  SHA-256 `C1AC13BE79B6999FE41D2B547C846076E00271803727C267E2BBD6805BA22F04`
  (downloaded via `gh release download v1.3.0`, digest matches the release API).
- PE metadata: FileVersion `1.3.0.0`, ProductVersion `1.3.0`,
  Company `Erza`, Description `Process Core Optimizer Installer`.
- Binary inspection (read-only, never executed): embedded markers
  `Inno Setup Setup Data (6.7.0)` → **Inno Setup 6.7.x**, single-file self-contained payload.
- The compiled Inno script (AppId, DefaultDirName, flags) is compressed inside
  the Setup binary and cannot be recovered without an unpacker; none is installed
  and running the historical installer on the dev machine is out of scope.
- PCO is not installed on the build machine (no Uninstall registry entry to mine).
- No installer script exists in any repo tag/commit.

Conclusion unchanged: the 1.3.0 installer identity is **not reproducible**.
No identifiers were invented to fake upgrade compatibility.

## 2.0.0 installer (new identity, Phase 4 branch build 2026-11-02)

- Script: `installer/ProcessCoreOptimizer.iss` (in repo, versioned).
- AppId (new, deliberate): `{6AA13087-A3D7-4501-9AAD-52D4CAC7668F}`
  (not the FrameHub ID, not a guess at the 1.3.0 ID).
- Built with Inno Setup 6.7.3 (`tools/Build-Installer.ps1`: publish + ISCC + SHA-256).
- Output: `installer/Output/ProcessCoreOptimizer_Setup_2.0.0.exe`
  (gitignored build artifact; reproducible on demand).
  - Phase 4 final build from `main` 835fa1e: 50,800,314 bytes,
    SHA-256: `8F8BDE7A00B06072FE0E634BC0D671AC58231021363A2C4A3A668AF2711D34E1`,
    PE FileVersion `2.0.0`.
  - Earlier branch builds differ by embedded commit SHA only (expected).
  - The FINAL release-candidate installer MUST be rebuilt after the final
    merge from `main` (commit SHA lands in ProductVersion metadata), then
    re-validated with `tools/Validate-Release.ps1` — done for this state, see below.
- Final manifest (main 835fa1e): Setup 50,800,314 B,
  SHA-256 `8F8BDE7A00B06072FE0E634BC0D671AC58231021363A2C4A3A668AF2711D34E1`,
  AppId `{6AA13087-A3D7-4501-9AAD-52D4CAC7668F}`, ICO `7FAF4AF8…`,
  validation 8/8 PASS (static; install/uninstall NOT RUN — see below).
- Icon: original PCO ICO (`processcoreoptimizer.ico`, SHA-256
  `7FAF4AF810335B4604C4C300A…`, 6 sizes 16–256) used as `SetupIconFile`,
  app `ApplicationIcon`, window icons and tray source — verified in the
  binary, on taskbar/title bar, and in screenshots (`.evidence/gui/`).
- Payload: self-contained single-file `ProcessCoreOptimizer.exe` (+PDB),
  Start Menu shortcut, optional desktop icon, admin install to
  `{autopf}\Process Core Optimizer`, proper Uninstall entry.
- User data (`%APPDATA%\ProcessCoreOptimizer`) is never touched by
  install/uninstall: profiles/settings survive.

## Compatibility & test status

- **Not advertised as an in-place upgrade over 1.3.0.** Different AppId means
  side-by-side entries if 1.3.0 is kept; recommended path: uninstall 1.3.0
  first (data preserved), then install 2.0.0.
- Install / upgrade-1.3→2.0 / uninstall scenarios: **NOT RUN on this machine.**
  No virtual machines are used in this project. The installer is intentionally
  never executed without the owner's explicit, separate consent.
- Upgrade safety net (even untested): versioned script + data-preserving
  uninstall + migration-tolerant loaders (v1.3 profiles/settings load as-is).

## Future real install/uninstall procedure (physical Windows, owner consent only)

When the owner explicitly approves, on a non-production physical machine:

1. Back up `%APPDATA%\ProcessCoreOptimizer` and record installed-programs state.
2. Install `ProcessCoreOptimizer_Setup_2.0.0.exe` (verify UAC prompt, Start Menu
   entry, optional desktop shortcut, Apps & Features entry with correct icon).
3. Launch from the shortcut; verify version 2.0.0, tray icon and first-run flow.
4. Upgrade path: with 1.3.0 installed, install 2.0.0, confirm side-by-side
   entries (no silent overwrite), uninstall 1.3.0, confirm 2.0.0 and user data intact.
5. Uninstall 2.0.0: confirm binary removal and that `%APPDATA%\ProcessCoreOptimizer`
   is preserved.
6. Record results (installer SHA-256 verified before install) in this file.

Until then the checklist above stays NOT RUN — this is declared openly, not hidden.
2. Attach the verified `ProcessCoreOptimizer_Setup_2.0.0.exe` as the Release asset.
3. The in-app updater (releases-aware) will then pick it up; no false positives before that.

**Do not publish a GitHub Release, installer, or version bump from automation
without explicit maintainer approval.**
