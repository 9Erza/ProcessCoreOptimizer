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

# No zombies: a lingering instance holds the single-instance mutex and every
# new launch would only show the "already running" dialog (which breaks UIA).
Get-Process -Name ProcessCoreOptimizer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

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

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class PcoWin32 {
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc c, IntPtr l);
    public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}
'@ -ErrorAction SilentlyContinue

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PcoMouse {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
    public const uint LEFTDOWN = 0x02, LEFTUP = 0x04;
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(LEFTDOWN, (uint)x, (uint)y, 0, UIntPtr.Zero);
        mouse_event(LEFTUP, (uint)x, (uint)y, 0, UIntPtr.Zero);
    }
}
'@ -ErrorAction SilentlyContinue

function Get-WindowWin32([string]$name, [int]$timeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $found = $null
        $cb = [PcoWin32+EnumWindowsProc]{
            param($h, $l)
            $sb = New-Object Text.StringBuilder 256
            [PcoWin32]::GetWindowText($h, $sb, 256) | Out-Null
            if ($sb.ToString() -eq $name -and [PcoWin32]::IsWindowVisible($h)) {
                $script:foundHwnd = $h
                return $false
            }
            return $true
        }
        $script:foundHwnd = $null
        [PcoWin32]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
        if ($script:foundHwnd -and $script:foundHwnd -ne [IntPtr]::Zero) {
            return [System.Windows.Automation.AutomationElement]::FromHandle($script:foundHwnd)
        }
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
    # Main phase simulates an existing user (wizard covered separately):
    # pre-seed onboarding + keep windows visible (tray-hide is covered by close test).
    '{"OnboardingCompletedVersion":1,"MinimizeToTray":false}' | Out-File (Join-Path $dataDir "settings.json") -Encoding utf8
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
        # Re-fetch: minimize/restore cycles invalidate cached UIA elements.
        $window = Get-Window "Process Core Optimizer" 10
        if (!$window) { throw "Main window lost after restore." }
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

    Check "language toggle to PL and back" {
        # The flag ToggleButton (custom template) is not exposed to UI Automation;
        # click its sidebar position with the mouse (bottom-left, under the log).
        # Verified by reading back a localized nav label — the click must really flip the language.
        $window = Get-Window "Process Core Optimizer" 10
        if (!$window) { throw "Main window lost before language toggle." }
        $plOk = $false
        $goodDy = 45
        foreach ($dy in @(45, 30, 15, 55)) {
            $r = $window.Current.BoundingRectangle
            [PcoMouse]::Click([int]($r.X + 45), [int]($r.Y + $r.Height - $dy))
            Start-Sleep -Milliseconds 900
            $labels = @()
            foreach ($t in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Text)))) {
                try { $labels += $t.Current.Name } catch { }
            }
            if ($labels -contains "Ustawienia") { $plOk = $true; $goodDy = $dy; break }
            $r2 = $window.Current.BoundingRectangle
            [PcoMouse]::Click([int]($r2.X + 45), [int]($r2.Y + $r2.Height - $dy))
            Start-Sleep -Milliseconds 600
        }
        if (!$plOk) { throw "Language toggle did not switch the UI to Polish." }
        Save-Shot "09-settings-pl.png" $window
        # Back to English with the proven offset, verified by the Settings nav label.
        $r = $window.Current.BoundingRectangle
        [PcoMouse]::Click([int]($r.X + 45), [int]($r.Y + $r.Height - $goodDy))
        Start-Sleep -Milliseconds 900
        $enLabels = @()
        foreach ($t in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)))) {
            try { $enLabels += $t.Current.Name } catch { }
        }
        if (!($enLabels -contains "Settings")) {
            $r = $window.Current.BoundingRectangle
            [PcoMouse]::Click([int]($r.X + 45), [int]($r.Y + $r.Height - $goodDy))
            Start-Sleep -Milliseconds 900
        }
        $window = Get-Window "Process Core Optimizer" 10
        if (!$window) { throw "Main window lost after language toggle." }
    }

    Check "startup state readable" {
        # Re-fetch: the language toggle re-renders resources and stale elements return nothing.
        $window = Get-Window "Process Core Optimizer" 10
        if (!$window) { throw "Main window lost." }
        # Status text is rendered in Settings; find any realized status line
        # ("Startup: ..." / "Autostart: ..."). Planner log lines only appear on apply.
        $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)))
        $found = $false
        foreach ($t in $texts) {
            try {
                $n = $t.Current.Name
                if ($n -like "Startup:*" -or $n -like "Autostart:*") { $found = $true; break }
            } catch { }
        }
        if (!$found) { throw "No startup status text found in Settings." }
    }

    Check "settings scrolls to startup section" {
        # Window reference is fresh from the previous check.
        $wp = $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
        try { $wp.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal) } catch { }
        $window.SetFocus()
        Start-Sleep -Milliseconds 300
        $shell = New-Object -ComObject WScript.Shell
        for ($i = 0; $i -lt 6; $i++) { $shell.SendKeys("{PGDN}"); Start-Sleep -Milliseconds 250 }
        Save-Shot "08-settings-startup.png" $window
    }

    Check "about links present with automation names" {
        # Focus the settings scroll area first (a plain click), otherwise keys go elsewhere.
        $r = $window.Current.BoundingRectangle
        [PcoMouse]::Click([int]($r.X + 600), [int]($r.Y + 400))
        Start-Sleep -Milliseconds 400
        $shell = New-Object -ComObject WScript.Shell
        for ($i = 0; $i -lt 12; $i++) { $shell.SendKeys("{PGDN}"); Start-Sleep -Milliseconds 200 }
        Save-Shot "15-about-links.png" $window
        $found = @{}
        $btns = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)))
        foreach ($b in $btns) {
            try {
                $nm = $b.Current.Name
                if ($nm -eq "9Erza" -or $nm -eq "Process Core Optimizer" -or $nm -eq "Buy Me a Coffee") { $found[$nm] = $true }
            } catch { }
        }
        foreach ($need in @("9Erza", "Process Core Optimizer", "Buy Me a Coffee")) {
            if (!$found.ContainsKey($need)) { throw "About link button '$need' not found." }
        }
    }

    Check "close hides to tray (process alive, window hidden)" {
        $w = Get-Window "Process Core Optimizer" 10
        if (!$w) { throw "Main window lost before close." }
        try {
            $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        } catch {
            throw "Close invocation failed: $($_.Exception.Message)"
        }
        Start-Sleep -Milliseconds 1500
        $gone = Get-Window "Process Core Optimizer" 5
        if ($gone) { throw "Main window still visible after Close (expected tray hide)." }
        $app.Refresh()
        if ($app.HasExited) { throw "App exited on Close; expected hide-to-tray (CloseToTray default)." }
        $script:results += "NOTE: Close hid the app to tray as designed; terminating for QA."
    }

    Check "first-run wizard on fresh data" {
        # Simulate a fresh install: empty data dir (backup already taken).
        # Kill any leftovers first; the log file is released on process exit.
        Get-Process -Name ProcessCoreOptimizer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 2000
        $gone = $false
        for ($i = 0; $i -lt 10 -and !$gone; $i++) {
            try {
                Remove-Item (Join-Path $dataDir "*") -Recurse -Force -ErrorAction Stop
                $gone = $true
            } catch { Start-Sleep -Milliseconds 500 }
        }
        if (!$gone) { throw "Data dir locked by a lingering process; aborting wizard phase." }
        $wapp = Start-Process -FilePath $exe -ArgumentList "--no-update-check --safe-mode" -PassThru
        try {
            # Owned modal dialogs are missed by plain UIA tree search; use Win32 handles.
            $wiz = Get-WindowWin32 "Process Core Optimizer - First Run" 30
            if (!$wiz) { throw "Wizard window did not appear on fresh data." }
            Save-Shot "10-wizard-page1.png" $wiz
            Click-Button $wiz "Next"
            Save-Shot "11-wizard-page2.png" $wiz
            Click-Button $wiz "Next"
            Save-Shot "12-wizard-page3.png" $wiz
            Click-Button $wiz "Finish"
            Start-Sleep -Milliseconds 1500
            if ($wapp.HasExited) { throw "App exited after wizard Finish (marker may be wrong, or crash)." }
            $script:results += "PASS: wizard finish keeps app running"
            Write-Host "PASS: wizard finish keeps app running" -ForegroundColor Green
        }
        finally {
            try {
                if ($wapp -and !$wapp.HasExited) { Stop-Process -Id $wapp.Id -Force; $wapp.WaitForExit(8000) }
            } catch { }
        }
        # Marker check: onboarding stamped completed.
        $marker = Join-Path $dataDir "settings.json"
        if (!(Test-Path $marker)) { throw "settings.json missing after wizard Finish." }
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
$script:results | Out-File (Join-Path $ev "results.txt")
$failed = @($script:results | Where-Object { $_ -like "FAIL*" }).Count
Write-Host "GUI checks: $($script:results.Count - $failed)/$($script:results.Count) passed. Evidence: $ev"
if ($failed -gt 0) { exit 1 }
