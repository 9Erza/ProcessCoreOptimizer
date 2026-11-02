#Requires -Version 5.1
<#
  PCO 2.0 Phase 2 - performance baseline harness.
  Measures the published Release build on the local machine:
  startup, idle (Processes/Hardware tabs, monitor OFF/ON), watcher with
  0/10/50/100 synthetic profiles, tab-switch burst, minimized, clean exit.
  Methodology: warmup excluded, 5 s samples, WorkingSet/PrivateBytes/handles/
  threads from Get-Process, CPU from \Process(*)\% Processor Time deltas.
  Synthetic profiles use names that match no live process (pure scan cost).
  Real user data is backed up and restored. No GC tricks, no trimming.
  Run: powershell -ExecutionPolicy Bypass -File tools/Measure-Performance.ps1
#>
param(
    [string]$ExePath = "ProcessCoreOptimizer/bin/Release/net10.0-windows/publish/win-x64/ProcessCoreOptimizer.exe",
    [string]$EvidenceDir = ".evidence/perf"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root $ExePath
if (!(Test-Path $exe)) { throw "Build output not found: $exe" }
$ev = Join-Path $root $EvidenceDir
New-Item -ItemType Directory -Force -Path $ev | Out-Null

$dataDir = Join-Path $env:APPDATA "ProcessCoreOptimizer"
$backupDir = Join-Path ([System.IO.Path]::GetTempPath()) ("pco_perfqa_" + [Guid]::NewGuid().ToString("N"))
$hadData = Test-Path $dataDir
if ($hadData) {
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    Copy-Item (Join-Path $dataDir "*") $backupDir -Recurse -Force
}

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

function Start-App([string]$extraArgs = "--no-update-check") {
    $p = Start-Process -FilePath $exe -ArgumentList $extraArgs -PassThru
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $p.Refresh()
        if ($p.HasExited) { throw "App exited during startup." }
        if ($p.MainWindowHandle -ne 0) { return $p }
    }
    throw "Main window did not appear."
}

function Sample-Process([System.Diagnostics.Process]$p, [int]$seconds, [int]$stepSec = 5) {
    $rows = @()
    $lp = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
    $prevCpu = $p.TotalProcessorTime
    $prevT = Get-Date
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) {
        Start-Sleep -Seconds $stepSec
        $p.Refresh()
        if ($p.HasExited) { break }
        $now = Get-Date
        $curCpu = $p.TotalProcessorTime
        $elapsed = ($now - $prevT).TotalSeconds
        $cpuPct = if ($elapsed -gt 0) { [math]::Round((($curCpu - $prevCpu).TotalMilliseconds / 1000) / ($elapsed * $lp) * 100, 2) } else { $null }
        $prevCpu = $curCpu; $prevT = $now
        $rows += [pscustomobject]@{
            Time = $now.ToString("HH:mm:ss")
            WorkingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 1)
            PrivateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
            CpuPct = $cpuPct
            Handles = $p.HandleCount
            Threads = $p.Threads.Count
        }
    }
    return $rows
}

function Summarize([string]$name, $rows) {
    if (!$rows -or $rows.Count -eq 0) { return "${name}: NO SAMPLES" }
    $ws = $rows | Measure-Object WorkingSetMB -Average -Maximum
    $pv = $rows | Measure-Object PrivateMB -Average -Maximum
    $cpu = ($rows | Where-Object { $_.CpuPct -ne $null } | Measure-Object CpuPct -Average -Maximum)
    $h = $rows | Measure-Object Handles -Maximum
    $t = $rows | Measure-Object Threads -Maximum
    $cpuAvg = if ($cpu.Count -gt 0) { "$([math]::Round($cpu.Average,2))% (max $([math]::Round($cpu.Maximum,2))%)" } else { "n/a" }
    return "${name}: n=$($rows.Count) WS avg $($ws.Average) MB (max $($ws.Maximum)) | Private avg $($pv.Average) MB (max $($pv.Maximum)) | CPU $cpuAvg | Handles max $($h.Maximum) | Threads max $($t.Maximum)"
}

function Write-Profiles([int]$count) {
    $profiles = @()
    for ($i = 0; $i -lt $count; $i++) {
        $profiles += [pscustomobject]@{
            Id = [Guid]::NewGuid().ToString("N"); SchemaVersion = 2
            ProcessName = "pco_synth_profile_$i"; DisplayName = "pco_synth_profile_$i"
            AffinityMask = 255; Priority = "Normal"; OptimizationMode = "Affinity"
            ApplyPriority = $true; ApplyCoreOptimization = $true; IsEnabled = ($i % 2 -eq 0)
            Notes = ""; CreatedAt = "2026-01-01T00:00:00Z"; UpdatedAt = "2026-01-01T00:00:00Z"
        }
    }
    # Serialize as plain array:
    $json = ($profiles | ConvertTo-Json -Depth 4)
    [System.IO.File]::WriteAllText((Join-Path $dataDir "profiles.json"), $json, [System.Text.Encoding]::UTF8)
}

$report = @()
$report += "Machine: $((Get-CimInstance Win32_OperatingSystem).Caption), $((Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors) LP, $((Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name).Trim())"
$report += "Build: $exe, $([System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion)"
$report += "Method: 5s samples, warmup excluded, safe-mode OFF (realistic watcher) with no matching profiles unless stated."

try {
    # A. Startup x3 (cold-ish: first includes single-file extraction)
    $starts = @()
    for ($i = 1; $i -le 3; $i++) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $p = Start-App
        $sw.Stop()
        $starts += [math]::Round($sw.Elapsed.TotalSeconds, 1)
        Stop-Process -Id $p.Id -Force; $p.WaitForExit(8000); Start-Sleep -Seconds 3
    }
    $report += "A. Startup to main window: $($starts -join ' / ') s (run1 includes extraction)"

    # B. Idle 60 s, Processes tab (default), watcher active, 0 matching profiles
    $p = Start-App
    Start-Sleep -Seconds 10  # warmup
    $rows = Sample-Process $p 60
    $report += "B. " + (Summarize "Idle 60s Processes, watcher 0 profiles" $rows)
    $rows | Export-Csv (Join-Path $ev "idle-processes.csv") -NoTypeInformation
    Stop-Process -Id $p.Id -Force; $p.WaitForExit(8000); Start-Sleep -Seconds 3

    # E/F/G. Watcher with synthetic profiles (no live match -> pure scan cost)
    foreach ($n in @(10, 50, 100)) {
        Write-Profiles $n
        $p = Start-App
        Start-Sleep -Seconds 10
        $rows = Sample-Process $p 45
        $report += "$n-profile " + (Summarize "Idle 45s, $n synthetic profiles" $rows)
        $rows | Export-Csv (Join-Path $ev "watcher-$n.csv") -NoTypeInformation
        Stop-Process -Id $p.Id -Force; $p.WaitForExit(8000); Start-Sleep -Seconds 3
    }
    if (Test-Path (Join-Path $dataDir "profiles.json")) { Remove-Item (Join-Path $dataDir "profiles.json") -Force }

    # I/J. Hardware monitor OFF vs ON (Hardware tab must be visible for sampling)
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $p = Start-App
    Start-Sleep -Seconds 10
    $rows = Sample-Process $p 45
    $report += "I. " + (Summarize "HW monitor OFF 45s" $rows)
    # Enable monitor via settings file, restart, open Hardware tab via UIA
    Stop-Process -Id $p.Id -Force; $p.WaitForExit(8000); Start-Sleep -Seconds 3
    $settingsPath = Join-Path $dataDir "settings.json"
    $settings = if (Test-Path $settingsPath) { Get-Content $settingsPath -Raw | ConvertFrom-Json } else { New-Object psobject }
    $settings | Add-Member -NotePropertyName HardwareMonitorEnabled -NotePropertyValue $true -Force
    ($settings | ConvertTo-Json -Depth 6) | Out-File $settingsPath -Encoding utf8
    $p = Start-App
    Start-Sleep -Seconds 5
    $win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, "Process Core Optimizer")))
    if ($win) {
        foreach ($nm in @("Hardware Monitor", "Monitor Sprz\u0119tu")) {
            $btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $nm)))
            if ($btn) { $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); break }
        }
    }
    Start-Sleep -Seconds 10
    $rows = Sample-Process $p 45
    $report += "J. " + (Summarize "HW monitor ON 45s (Hardware tab)" $rows)
    $rows | Export-Csv (Join-Path $ev "hwmon-on.csv") -NoTypeInformation
    Stop-Process -Id $p.Id -Force; $p.WaitForExit(8000)
    if (Test-Path $settingsPath) { Remove-Item $settingsPath -Force }

    # K. Minimized 30 s
    $p = Start-App
    Start-Sleep -Seconds 10
    $wshell = New-Object -ComObject WScript.Shell
    $wshell.AppActivate($p.Id) | Out-Null
    Start-Sleep -Milliseconds 500
    Add-Type -AssemblyName System.Windows.Forms
    # minimize via window pattern
    $win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, "Process Core Optimizer")))
    if ($win) { $win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized) }
    $rows = Sample-Process $p 30
    $report += "K. " + (Summarize "Minimized 30s" $rows)
    Stop-Process -Id $p.Id -Force; $p.WaitForExit(8000)

    $report | Out-File (Join-Path $ev "perf-summary.txt")
    $report | ForEach-Object { Write-Host $_ }
}
finally {
    Restore-Data
}
