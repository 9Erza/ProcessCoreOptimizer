#Requires -Version 5.1
<#
  Repeatable 2.0.0 installer build:
  1. dotnet publish (self-contained single-file win-x64, FolderProfile)
  2. ISCC.exe on installer/ProcessCoreOptimizer.iss with PCO_VERSION from version.txt
  3. SHA-256 + size report into .evidence/installer
  Run: powershell -ExecutionPolicy Bypass -File tools/Build-Installer.ps1
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $root "version.txt") -Raw).Trim()
$env:PCO_VERSION = $version

Write-Host "Publishing $version ..."
& dotnet publish (Join-Path $root "ProcessCoreOptimizer/ProcessCoreOptimizer.csproj") -c Release -p:PublishProfile=FolderProfile --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$isccCandidates = @(
    "$env:LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe",
    "C:/Program Files/Inno Setup 6/ISCC.exe",
    "C:/Program Files (x86)/Inno Setup 6/ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$iscc) { throw "ISCC.exe not found. Install Inno Setup 6 (winget install -e --id JRSoftware.InnoSetup)." }

Write-Host "Compiling installer with $iscc ..."
& $iscc (Join-Path $root "installer/ProcessCoreOptimizer.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed." }

$setup = Join-Path $root "installer/Output/ProcessCoreOptimizer_Setup_$version.exe"
if (!(Test-Path $setup)) { throw "Expected output missing: $setup" }
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
$size = (Get-Item $setup).Length
$ev = Join-Path $root ".evidence/installer"
New-Item -ItemType Directory -Force -Path $ev | Out-Null
"$hash  ProcessCoreOptimizer_Setup_$version.exe  ($size bytes)" | Out-File (Join-Path $ev "installer-sha256.txt")
Write-Host "OK: $setup"
Write-Host "SHA-256: $hash"
Write-Host "Size: $size bytes"
