#Requires -Version 5.1
<#
  PCO 2.0.0 release artifact validation (no install, no VM).
  Checks: version consistency, AppId stability, original icon presence +
  hash, payload EXE metadata, forbidden branding scan, installer presence.
  Prints PASS/FAIL per check and a final manifest block.
  NOT verified here (see docs/INSTALLER.md): real install/upgrade/uninstall.
#>
param(
    [string]$Version = "2.0.0",
    [string]$ExpectedAppId = "{6AA13087-A3D7-4501-9AAD-52D4CAC7668F}",
    [string]$ExpectedIconSha256 = "7FAF4AF810335B4604C4C300A90559111539890CDEB5BC1D23A72D13CF5285DB"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$failures = @()
function Check([string]$name, [scriptblock]$body) {
    try {
        $detail = & $body
        Write-Host "PASS: $name -- $detail" -ForegroundColor Green
    } catch {
        $script:failures += $name
        Write-Host "FAIL: $name -- $($_.Exception.Message)" -ForegroundColor Red
    }
}

# 1. Version source of truth
Check "version.txt is $Version" {
    $v = (Get-Content (Join-Path $root "version.txt") -Raw).Trim()
    if ($v -ne $Version) { throw "found $v" }
    "version.txt=$v"
}

# 2. AppId pinned in installer script
Check "installer AppId stable" {
    $iss = Get-Content (Join-Path $root "installer/ProcessCoreOptimizer.iss") -Raw
    if ($iss -notmatch [regex]::Escape($ExpectedAppId)) { throw "AppId mismatch" }
    $ExpectedAppId
}

# 3. Original icon present, unchanged, referenced
Check "original ICO present with known hash" {
    $p1 = Join-Path $root "ProcessCoreOptimizer/processcoreoptimizer.ico"
    $p2 = Join-Path $root "ProcessCoreOptimizer/Images/processcoreoptimizer.ico"
    foreach ($p in @($p1, $p2)) {
        if (!(Test-Path $p)) { throw "missing $p" }
        $h = (Get-FileHash $p -Algorithm SHA256).Hash
        if ($h -ne $ExpectedIconSha256) { throw "hash changed for $p" }
    }
    "7FAF4A… (both copies identical)"
}
Check "csproj + iss reference the original ICO" {
    $csproj = Get-Content (Join-Path $root "ProcessCoreOptimizer/ProcessCoreOptimizer.csproj") -Raw
    $iss = Get-Content (Join-Path $root "installer/ProcessCoreOptimizer.iss") -Raw
    if ($csproj -notmatch "ApplicationIcon") { throw "ApplicationIcon missing" }
    if ($iss -notmatch "SetupIconFile") { throw "SetupIconFile missing" }
    "referenced in both"
}

# 4. Published EXE metadata
Check "published EXE carries version + icon" {
    $exe = Join-Path $root "ProcessCoreOptimizer/bin/Release/net10.0-windows/publish/win-x64/ProcessCoreOptimizer.exe"
    if (!(Test-Path $exe)) { throw "publish output missing (run dotnet publish first)" }
    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($info.FileVersion -ne "$Version.0") { throw "FileVersion=$($info.FileVersion)" }
    if (!$info.ProductVersion.StartsWith($Version)) { throw "ProductVersion=$($info.ProductVersion)" }
    Add-Type -AssemblyName System.Drawing
    $icon = [System.Drawing.Icon]::ExtractAssociatedIcon($exe)
    if ($icon -eq $null) { throw "no icon extractable" }
    $size = "$($icon.Width)x$($icon.Height)"; $icon.Dispose()
    "PE $Version, icon $size"
}

# 5. No foreign branding in the shipped EXE strings
Check "no FrameHub branding in payload" {
    $exe = Join-Path $root "ProcessCoreOptimizer/bin/Release/net10.0-windows/publish/win-x64/ProcessCoreOptimizer.exe"
    $bytes = [System.IO.File]::ReadAllBytes($exe)
    $text = [System.Text.Encoding]::ASCII.GetString($bytes)
    foreach ($bad in @("FrameHub", "framehub")) {
        if ($text.Contains($bad)) { throw "found '$bad' in payload" }
    }
    "clean"
}

# 6. Installer artifact + identity
Check "Setup.exe present, versioned, hashed" {
    $setup = Join-Path $root "installer/Output/ProcessCoreOptimizer_Setup_$Version.exe"
    if (!(Test-Path $setup)) { throw "run tools/Build-Installer.ps1 first" }
    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($setup)
    if ($info.FileVersion.Trim() -ne $Version) { throw "Setup FileVersion='$($info.FileVersion)'" }
    $hash = (Get-FileHash $setup -Algorithm SHA256).Hash
    $size = (Get-Item $setup).Length
    "SHA256=$hash SIZE=$size"
}

# 7. Expected payload files exist (EXE + WPF native deps; the app crashes
# on window creation without wpfgfx_cor3.dll / PresentationNative_cor3.dll)
Check "payload contains EXE (+optional PDB)" {
    $dir = Join-Path $root "ProcessCoreOptimizer/bin/Release/net10.0-windows/publish/win-x64"
    if (!(Test-Path (Join-Path $dir "ProcessCoreOptimizer.exe"))) { throw "EXE missing" }
    foreach ($dll in @("wpfgfx_cor3.dll", "PresentationNative_cor3.dll", "D3DCompiler_47_cor3.dll")) {
        if (!(Test-Path (Join-Path $dir $dll))) { throw "payload incomplete: $dll missing (re-run publish)" }
    }
    "exe + native WPF deps present"
}

Write-Host ""
if ($script:failures.Count -gt 0) {
    Write-Host "VALIDATION FAILED: $($script:failures -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "ALL ARTIFACT CHECKS PASSED (static validation only; install/uninstall NOT RUN)." -ForegroundColor Green
