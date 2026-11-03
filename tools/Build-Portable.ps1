#Requires -Version 5.1
<#
  Repeatable 2.0.0 portable ZIP build (no installer, no registry, no services):
  1. clean dotnet publish (self-contained single-file win-x64, FolderProfile)
  2. stage publish output + README-Portable.txt into release-staging/
  3. Compress-Archive to ProcessCoreOptimizer_Portable_<version>_win-x64.zip
  4. SHA-256 + size report into .evidence/installer
  Portable means "no installer" — user data still lives in
  %APPDATA%\ProcessCoreOptimizer by design (see README-Portable.txt).
  Run: powershell -ExecutionPolicy Bypass -File tools/Build-Portable.ps1
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $root "version.txt") -Raw).Trim()

Write-Host "Publishing portable $version ..."
$publishDir = Join-Path $root "ProcessCoreOptimizer/bin/Release/net10.0-windows/publish/win-x64"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
& dotnet publish (Join-Path $root "ProcessCoreOptimizer/ProcessCoreOptimizer.csproj") -c Release -p:PublishProfile=FolderProfile --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$required = @("ProcessCoreOptimizer.exe", "wpfgfx_cor3.dll", "PresentationNative_cor3.dll",
    "D3DCompiler_47_cor3.dll", "PenImc_cor3.dll", "vcruntime140_cor3.dll",
    "MonoPosixHelper.dll", "libMonoPosixHelper.dll")
foreach ($name in $required) {
    if (!(Test-Path (Join-Path $publishDir $name))) { throw "Publish output incomplete: $name missing." }
}

$stage = Join-Path $root "release-staging/ProcessCoreOptimizer_Portable_${version}_win-x64"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item (Join-Path $publishDir "*") $stage -Recurse -Force
Copy-Item (Join-Path $root "docs/README-Portable.txt") (Join-Path $stage "README-Portable.txt") -Force

$zip = Join-Path $root "release-staging/ProcessCoreOptimizer_Portable_${version}_win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
$size = (Get-Item $zip).Length
$ev = Join-Path $root ".evidence/installer"
New-Item -ItemType Directory -Force -Path $ev | Out-Null
"$hash  ProcessCoreOptimizer_Portable_${version}_win-x64.zip  ($size bytes)" | Out-File (Join-Path $ev "portable-sha256.txt")
Write-Host "OK: $zip"
Write-Host "SHA-256: $hash"
Write-Host "Size: $size bytes"
