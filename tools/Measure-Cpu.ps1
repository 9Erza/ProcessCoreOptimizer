#Requires -Version 5.1
<#
  Compact CPU pass: locale-independent CPU% via TotalProcessorTime deltas.
  Idle vs watcher-100 vs scan burst. Appends to .evidence/perf/perf-summary.txt.
#>
param(
    [string]$ExePath = "ProcessCoreOptimizer/bin/Release/net10.0-windows/publish/win-x64/ProcessCoreOptimizer.exe",
    [string]$EvidenceDir = ".evidence/perf"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root $ExePath
$ev = Join-Path $root $EvidenceDir
$dataDir = Join-Path $env:APPDATA "ProcessCoreOptimizer"
$backupDir = Join-Path ([System.IO.Path]::GetTempPath()) ("pco_cpuqa_" + [Guid]::NewGuid().ToString("N"))
$hadData = Test-Path $dataDir
if ($hadData) { New-Item -ItemType Directory -Force -Path $backupDir | Out-Null; Copy-Item (Join-Path $dataDir "*") $backupDir -Recurse -Force }

function Restore-Data {
    try { Stop-Process -Name ProcessCoreOptimizer -Force -ErrorAction SilentlyContinue } catch { }
    Start-Sleep -Milliseconds 1500
    if ($hadData) {
        for ($i = 0; $i -lt 5; $i++) {
            try {
                Remove-Item (Join-Path $dataDir "*") -Recurse -Force -ErrorAction Stop
                Copy-Item (Join-Path $backupDir "*") $dataDir -Recurse -Force -ErrorAction Stop
                Remove-Item $backupDir -Recurse -Force -ErrorAction SilentlyContinue
                return
            } catch { Start-Sleep -Milliseconds 1000 }
        }
        Write-Host "WARNING: restore failed; backup at $backupDir" -ForegroundColor Red
    }
}

function Start-App {
    $p = Start-Process -FilePath $exe -ArgumentList "--no-update-check" -PassThru
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500; $p.Refresh()
        if ($p.HasExited) { throw "App exited during startup." }
        if ($p.MainWindowHandle -ne 0) { return $p }
    }
    throw "Main window did not appear."
}

function Sample-Cpu([System.Diagnostics.Process]$p, [int]$seconds) {
    $lp = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
    $prevCpu = $p.TotalProcessorTime; $prevT = Get-Date
    $vals = @()
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) {
        Start-Sleep -Seconds 5; $p.Refresh()
        if ($p.HasExited) { break }
        $now = Get-Date; $cur = $p.TotalProcessorTime
        $el = ($now - $prevT).TotalSeconds
        $vals += [math]::Round((($cur - $prevCpu).TotalMilliseconds / 1000) / ($el * $lp) * 100, 2)
        $prevCpu = $cur; $prevT = $now
    }
    return $vals
}

function Stats($vals) {
    if (!$vals -or $vals.Count -eq 0) { return "NO SAMPLES" }
    $m = $vals | Measure-Object -Average -Maximum
    return "n=$($vals.Count) avg $([math]::Round($m.Average,2))% max $([math]::Round($m.Maximum,2))% values=[$($vals -join ', ')]"
}

$out = @()
try {
    $p = Start-App; Start-Sleep -Seconds 10
    $out += "CPU idle 60s Processes, watcher 0 profiles: " + (Stats (Sample-Cpu $p 60))
    Stop-Process -Id $p.Id -Force; $p.WaitForExit(8000); Start-Sleep -Seconds 3

    $profiles = @()
    for ($i = 0; $i -lt 100; $i++) {
        $profiles += [pscustomobject]@{ Id = [Guid]::NewGuid().ToString("N"); SchemaVersion = 2; ProcessName = "pco_synth_$i"; DisplayName = "pco_synth_$i"; AffinityMask = 255; Priority = "Normal"; OptimizationMode = "Affinity"; ApplyPriority = $true; ApplyCoreOptimization = $true; IsEnabled = ($i % 2 -eq 0); Notes = ""; CreatedAt = "2026-01-01T00:00:00Z"; UpdatedAt = "2026-01-01T00:00:00Z" }
    }
    ($profiles | ConvertTo-Json -Depth 4) | Out-File (Join-Path $dataDir "profiles.json") -Encoding utf8
    $p = Start-App; Start-Sleep -Seconds 10
    $out += "CPU idle 60s, watcher 100 synthetic profiles: " + (Stats (Sample-Cpu $p 60))
    Stop-Process -Id $p.Id -Force; $p.WaitForExit(8000)
    $out | Out-File (Join-Path $ev "cpu-summary.txt")
    $out | ForEach-Object { Write-Host $_ }
}
finally { Restore-Data }
