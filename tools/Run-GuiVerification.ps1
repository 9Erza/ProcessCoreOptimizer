#Requires -Version 5.1
<#
  PCO 2.0 Phase 2 - interactive GUI verification harness.
  - Backs up %APPDATA%\ProcessCoreOptimizer, launches the published app with
    --no-update-check --safe-mode (no OS mutations possible), drives all six
    tabs via UI Automation InvokePattern (real clicks, not tree existence),
    exercises maximize/minimize/restore, captures screenshots to .evidence/gui.
  - Restores user data and terminates the app at the end.
  Run from repo root: powershell -ExecutionPolicy Bypass -File tools/Run-GuiVerification.ps1
#>
param(
    [string]$ExePath = "ProcessCoreOptimizer/bin/Release/net10.0-windows/publish/win-x64/ProcessCoreOptimizer.exe",
    [string]$EvidenceDir = ".evidence/gui"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root $ExePath
if (!(Test-Path $exe)) { throw "Build output not found: $exe. Run dotnet publish first." }

$ev = Join-Path $root $EvidenceDir
New-Item -ItemType Directory -Force -Path $ev | Out-Null

$dataDir = Join-Path $env:APPDATA "ProcessCoreOptimizer"
$backupDir = Join-Path ([System.IO.Path]::GetTempPath()) ("pco_guiqa_" + [Guid]::NewGuid().ToString("N"))
$hadData = Test-Path $dataDir
if ($hadData) {
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    Copy-Item (Join-Path $dataDir "*") $backupDir -Recurse -Force
    Write-Host "User data backed up to $backupDir"
}

function Get-Window([string]$name, [int]$timeoutSec = 25) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $w = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, $name)))
        if ($w) { return $w }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Click-Button([System.Windows.Automation.AutomationElement]$scope, [string]$name) {
    $btn = $scope.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)))
    if (!$btn) { throw "Button '$name' not found under scope." }
    $invoke = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    return $true
}

function Save-Shot([string]$file, [System.Windows.Automation.AutomationElement]$window) {
    Start-Sleep -Milliseconds 900
    $r = $window.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save((Join-Path $ev $file), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "shot: $file ($([int]$r.Width)x$([int]$r.Height))"
}

$script:results = @()
function Check([string]$name, [scriptblock]$body) {
    try { & $body; $script:results += "PASS: $name"; Write-Host "PASS: $name" -ForegroundColor Green }
    catch { $script:results += "FAIL: $name -- $($_.Exception.Message)"; Write-Host "FAIL: $name -- $($_.Exception.Message)" -ForegroundColor Red }
}

$app = $null
try {
    $app = Start-Process -FilePath $exe -ArgumentList "--no-update-check --safe-mode" -PassThru
    $window = Get-Window "Process Core Optimizer"
    if (!$window) { throw "Main window did not appear." }

    $tabs = @(
        @{ Name = @("System Processes", "Procesy Systemowe"); Shot = "01-processes.png" },
        @{ Name = @("CPU & Topology", "CPU i topologia"); Shot = "02-cpu-topology.png" },
        @{ Name = @("Saved Profiles", "Zapisane Profile"); Shot = "03-profiles.png" },
        @{ Name = @("Hardware Monitor", "Monitor Sprz\u0119tu"); Shot = "04-hardware.png" },
        @{ Name = @("Diagnostics", "Diagnostyka"); Shot = "05-diagnostics.png" },
        @{ Name = @("Settings", "Ustawienia"); Shot = "06-settings.png" }
    )
    foreach ($t in $tabs) {
        $label = $t.Shot
        Check "open tab $($t.Shot)" {
            $clicked = $false
            foreach ($candidate in $t.Name) {
                try { Click-Button $window $candidate; $clicked = $true; break } catch { }
            }
            if (!$clicked) { throw "No tab button matched: $($t.Name -join ' / ')" }
            Save-Shot $label $window
        }
    }

    Check "maximize/restore" {
        $wp = $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
        $wp.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
        Start-Sleep -Milliseconds 800
        Save-Shot "07-maximized.png" $window
        $wp.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
        Start-Sleep -Milliseconds 600
    }

    Check "minimize/restore" {
        $wp = $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
        $wp.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized)
        Start-Sleep -Milliseconds 800
        $wp.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
        Start-Sleep -Milliseconds 800
    }

    Check "safe-mode notice logged" {
        # ListBox items are virtualized and not reliably exposed to UI Automation;
        # the file log is the stable assertion surface (screenshots show the line too).
        Start-Sleep -Milliseconds 1500
        $log = Join-Path $env:APPDATA "ProcessCoreOptimizer/ProcessCoreOptimizer.log"
        if (!(Test-Path $log)) { throw "Log file not found: $log" }
        $hits = Select-String -Path $log -Pattern "Safe mode active" -SimpleMatch | Select-Object -Last 1
        if (!$hits) { throw "Safe-mode line missing from file log." }
    }

    Check "close window" {
        # CloseToTray may intercept Close and hide to tray instead of exiting.
        $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        if (!$app.WaitForExit(5000)) {
            Stop-Process -Id $app.Id -Force -ErrorAction Stop
            if (!$app.WaitForExit(8000)) { throw "App did not exit after Close." }
            $script:results += "NOTE: Close was intercepted by tray; terminated for QA."
        }
    }
}
finally {
    try {
        if ($app -and !$app.HasExited) {
            Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
            $app.WaitForExit(8000)
        }
    } catch { }
    Start-Sleep -Milliseconds 1500
    if ($hadData) {
        $restored = $false
        for ($i = 0; $i -lt 5 -and !$restored; $i++) {
            try {
                Remove-Item (Join-Path $dataDir "*") -Recurse -Force -ErrorAction Stop
                Copy-Item (Join-Path $backupDir "*") $dataDir -Recurse -Force -ErrorAction Stop
                $restored = $true
            } catch { Start-Sleep -Milliseconds 1000 }
        }
        if ($restored) {
            Remove-Item $backupDir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "User data restored."
        } else {
            Write-Host "WARNING: user data restore failed; backup kept at $backupDir" -ForegroundColor Red
        }
    }
}

$results | Out-File (Join-Path $ev "results.txt")
$failed = @($script:results | Where-Object { $_ -like "FAIL*" }).Count
Write-Host "GUI checks: $($script:results.Count - $failed)/$($script:results.Count) passed. Evidence: $ev"
if ($failed -gt 0) { exit 1 }
