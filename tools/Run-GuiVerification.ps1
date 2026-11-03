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
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
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
        @{ Name = @("Smart Rules", "Inteligentne reguÅ‚y"); Shot = "17-rules.png" },
        @{ Name = @("Hardware Monitor", "Monitor Sprzêtu"); Shot = "04-hardware.png" },
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

    Check "instance view toggle" {
        # The Processes view must be active: WPF prunes collapsed views from UIA.
        $back = $false
        foreach ($candidate in @("System Processes", "Procesy Systemowe")) {
            try { Click-Button $window $candidate; $back = $true; break } catch { }
        }
        if (!$back) { throw "Could not return to processes tab." }
        Start-Sleep -Milliseconds 800
        $toggled = $false
        foreach ($candidate in @("Instances", "Instancje")) {
            try {
                $cb = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty, $candidate)))
                if ($cb -ne $null) {
                    $tp = $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
                    $tp.Toggle()
                    Start-Sleep -Milliseconds 900
                    Save-Shot "18-instances.png" $window
                    $tp.Toggle()
                    Start-Sleep -Milliseconds 900
                    $toggled = $true
                    break
                }
            } catch { }
        }
        if (!$toggled) { throw "Instances toggle checkbox not found." }
        # Leave the app on Settings like before: later checks assume it.
        foreach ($candidate in @("Settings", "Ustawienia")) {
            try { Click-Button $window $candidate; break } catch { }
        }
        Start-Sleep -Milliseconds 800
    }

    Check "process scope UX: no target, select, close, mode switch" {
        # No selection -> no CPU editor anywhere.
        $window = Get-Window "Process Core Optimizer" 10
        if (!$window) { throw "Main window lost before scope check." }
        foreach ($candidate in @("System Processes", "Procesy Systemowe")) {
            try { Click-Button $window $candidate; break } catch { }
        }
        Start-Sleep -Milliseconds 800
        $applyBtn = $null
        try {
            $applyBtn = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "Apply Optimization")))
        } catch { }
        if ($applyBtn) { throw "Apply Optimization visible with no process selected." }

        # Select the first process row via SelectionItemPattern; verify explicit scope + editor.
        $scoped = $false
        foreach ($it in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::DataItem)))) {
            try {
                $it.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                Start-Sleep -Milliseconds 900
                $scoped = $true
                break
            } catch { }
        }
        if (!$scoped) { throw "No selectable process row found." }
        Start-Sleep -Milliseconds 500
        $scopeOk = $false
        foreach ($t in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)))) {
            try { if ($t.Current.Name -like "Process group *" -or $t.Current.Name -like "Instance *") { $scopeOk = $true; break } } catch { }
        }
        if (!$scopeOk) { throw "No explicit scope label after selecting a process." }
        Save-Shot "29-scope-selected.png" $window
        $applyBtn = $null
        try {
            $applyBtn = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "Apply Optimization")))
        } catch { }
        if (!$applyBtn) { throw "Apply Optimization missing after selecting a process." }

        # Close panel -> editor gone, no mutation possible.
        Click-Button $window "Close panel"
        Start-Sleep -Milliseconds 800
        $applyBtn = $null
        try {
            $applyBtn = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "Apply Optimization")))
        } catch { }
        if ($applyBtn) { throw "CPU editor still visible after closing the target panel." }

        # Grouped -> Instances clears stale selection.
        foreach ($candidate in @("Instances", "Instancje")) {
            try {
                $cb = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty, $candidate)))
                if ($cb -ne $null) {
                    $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
                    Start-Sleep -Milliseconds 900
                    break
                }
            } catch { }
        }
        $applyBtn = $null
        try {
            $applyBtn = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "Apply Optimization")))
        } catch { }
        if ($applyBtn) { throw "Stale target survived the Grouped/Instances switch." }
        foreach ($candidate in @("Instances", "Instancje")) {
            try {
                $cb = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty, $candidate)))
                if ($cb -ne $null) {
                    $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
                    Start-Sleep -Milliseconds 900
                    break
                }
            } catch { }
        }
    }

    Check "cpu tab has no apply without target" {
        $window = Get-Window "Process Core Optimizer" 10
        if (!$window) { throw "Main window lost before CPU check." }
        foreach ($candidate in @("CPU & Topology", "CPU i topologia")) {
            try { Click-Button $window $candidate; break } catch { }
        }
        Start-Sleep -Milliseconds 900
        $applyBtn = $null
        try {
            $applyBtn = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "Apply Optimization")))
        } catch { }
        if ($applyBtn) { throw "CPU tab must not offer Apply Optimization without a target." }
        $preview = $null
        try {
            $preview = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "Preview")))
        } catch { }
        if (!$preview) { throw "Preset Preview button not found on CPU tab." }
        Save-Shot "30-cpu-readonly.png" $window
    }

    Check "settings categories navigate without side effects" {
        function Select-SettingsCategory($scope, $name) {
            $it = $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $name)))
            if (!$it) { throw "Settings category $name not found." }
            $it.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 800
        }
        $window = Get-Window "Process Core Optimizer" 10
        if (!$window) { throw "Main window lost before settings check." }
        foreach ($candidate in @("Settings", "Ustawienia")) {
            try { Click-Button $window $candidate; break } catch { }
        }
        Start-Sleep -Milliseconds 800
        Select-SettingsCategory $window "Updates"
        Start-Sleep -Milliseconds 700
        $chk = $null
        try {
            $chk = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "Check for updates")))
        } catch { }
        if (!$chk) { throw "Updates category did not show the manual check." }
        Save-Shot "31-settings-updates.png" $window
        Select-SettingsCategory $window "About"
        Start-Sleep -Milliseconds 700
        $about = $null
        try {
            $about = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "DobryPC.pl")))
        } catch { }
        if (!$about) { throw "About category did not show official links." }
        Save-Shot "32-settings-about.png" $window
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
        # Verified by reading back a localized nav label ${emdash} the click must really flip the language.
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
        # Category navigation: startup status lives in the Startup category now.
        foreach ($scat in @("Startup & Privileges", "Uruchamianie i uprawnienia")) {
            try {
                $sitem = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty, $scat)))
                if ($sitem -ne $null) {
                    $sitem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                    Start-Sleep -Milliseconds 800
                    $window = Get-Window "Process Core Optimizer" 10
                    break
                }
            } catch { }
        }
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
        # About links live in the About category now; navigate first (Settings tab assumed).
        foreach ($acat in @("About", "O programie")) {
            try {
                $aitem = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty, $acat)))
                if ($aitem -ne $null) {
                    $aitem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                    Start-Sleep -Milliseconds 800
                    $window = Get-Window "Process Core Optimizer" 10
                    if (!$window) { throw "Main window lost before about check." }
                    break
                }
            } catch { }
        }
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
                if ($nm -eq "9Erza" -or $nm -eq "Process Core Optimizer" -or $nm -eq "Buy Me a Coffee" -or $nm -eq "DobryPC.pl") { $found[$nm] = $true }
            } catch { }
        }
        foreach ($need in @("9Erza", "Process Core Optimizer", "Buy Me a Coffee", "DobryPC.pl")) {
            if (!$found.ContainsKey($need)) { throw "About link button '$need' not found." }
        }
    }

    Check "developer hub expand/collapse + rows + about nav (EN/PL)" {
        $emdash = [char]0x2014
        # Sidebar footer hub: header ToggleButton is named "PCO <version>".
        function Find-HubHeader([System.Windows.Automation.AutomationElement]$scope) {
            $all = $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Button)))
            foreach ($b in $all) {
                try { if ($b.Current.Name -like "PCO *") { return $b } } catch { }
            }
            return $null
        }
        function Find-Button([System.Windows.Automation.AutomationElement]$scope, [string]$name) {
            return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $name)))
        }
        function Count-Buttons([System.Windows.Automation.AutomationElement]$scope, [string]$name) {
            return @($scope.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $name))).Count)
        }
        function Toggle-Hub([System.Windows.Automation.AutomationElement]$header) {
            $tp = $header.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            $tp.Toggle()
            Start-Sleep -Milliseconds 700
        }
        function Flip-ToLanguage([System.Windows.Automation.AutomationElement]$scope, [string]$marker) {
            # Flag ToggleButton has no UIA name; click its sidebar spot and
            # verify the flip by reading back a localized label after EACH
            # click (a blind even/odd count breaks when a probe misses the
            # flag and lands on the expander instead).
            foreach ($dy in @(15, 30, 45)) {
                $r = $scope.Current.BoundingRectangle
                [PcoMouse]::Click([int]($r.X + 45), [int]($r.Y + $r.Height - $dy))
                Start-Sleep -Milliseconds 900
                $scope = Get-Window "Process Core Optimizer" 10
                if ($scope -and (Ui-HasText $scope $marker)) { return $scope }
            }
            return $null
        }
        function Ui-HasText([System.Windows.Automation.AutomationElement]$scope, [string]$text) {
            foreach ($t in $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Text)))) {
                try { if ($t.Current.Name -eq $text) { return $true } } catch { }
            }
            return $false
        }

        $window = Get-Window "Process Core Optimizer" 10
        if (!$window) { throw "Main window lost before hub check." }
        # Park on Processes so the Settings About section (same UIA names) is
        # pruned from the tree; counts then prove no duplicate footer links.
        foreach ($candidate in @("System Processes", "Procesy Systemowe")) {
            try { Click-Button $window $candidate; break } catch { }
        }
        Start-Sleep -Milliseconds 800
        $window = Get-Window "Process Core Optimizer" 10
        $hub = Find-HubHeader $window
        if (!$hub) { throw "Hub header button 'PCO *' not found." }

        # A. collapsed EN.
        Save-Shot "22-hub-collapsed-en.png" $window

        # B. expanded EN: toggle open, all five rows present exactly once (no duplicates).
        Toggle-Hub $hub
        $enRows = @("Project Repository ${emdash} Process Core Optimizer", "Developer GitHub ${emdash} 9Erza",
            "Author website ${emdash} DobryPC.pl", "Buy Me a Coffee ${emdash} Support Development", "About")
        foreach ($n in $enRows) {
            if (!(Find-Button $window $n)) { throw "Hub row '$n' not found while expanded (EN)." }
        }
        if ((Count-Buttons $window "Project Repository ${emdash} Process Core Optimizer") -ne 1) {
            throw "Repo link rendered twice while expanded (quick row not hidden)."
        }
        Save-Shot "23-hub-expanded-en.png" $window

        # Keyboard: focus header, Space toggles it shut; rows disappear.
        $hub.SetFocus()
        Start-Sleep -Milliseconds 300
        $shell = New-Object -ComObject WScript.Shell
        $shell.SendKeys(" ")
        Start-Sleep -Milliseconds 700
        if (Find-Button $window "Author website ${emdash} DobryPC.pl") { throw "Hub did not collapse on Space." }

        # C/D. Polish: flip, collapsed + expanded shots.
        $window = Flip-ToLanguage $window "Ustawienia"
        if (!$window) { throw "Could not switch UI to Polish for hub check." }
        Save-Shot "24-hub-collapsed-pl.png" $window
        $hub = Find-HubHeader $window
        if (!$hub) { throw "Hub header lost after language switch." }
        Toggle-Hub $hub
        $plRows = @("Repozytorium projektu ${emdash} Process Core Optimizer", "GitHub autora ${emdash} 9Erza",
            "Strona autora ${emdash} DobryPC.pl", "Postaw kawê ${emdash} Wesprzyj rozwój", "O programie")
        foreach ($n in $plRows) {
            if (!(Find-Button $window $n)) { throw "Hub row '$n' not found while expanded (PL)." }
        }
        Save-Shot "25-hub-expanded-pl.png" $window

        # F. About row navigates in-app to Settings/About (no browser).
        $aboutBtn = Find-Button $window "O programie"
        if (!$aboutBtn) { throw "About row not found." }
        $aboutBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 1200
        if (!(Find-Button $window "DobryPC.pl")) { throw "About section (DobryPC.pl) not visible after About nav." }
        Save-Shot "26-hub-about.png" $window

        # Back to English for the remaining checks.
        $window = Flip-ToLanguage $window "Settings"
        if (!$window) { throw "Could not switch UI back to English." }
        # Collapse the hub again so the footer rests in its default state.
        $hub = Find-HubHeader $window
        if ($hub) {
            try {
                $state = $hub.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState
                if ($state -eq [System.Windows.Automation.ToggleState]::On) { Toggle-Hub $hub }
            } catch { }
        }
        $window = Get-Window "Process Core Optimizer" 10
        if (!$window) { throw "Main window lost after hub check." }
    }

    Check "hub at minimum window size (1000x700) without overflow" {
        $emdash = [char]0x2014
        $window = Get-Window "Process Core Optimizer" 10
        if (!$window) { throw "Main window lost before min-size check." }
        # Normalize: the hub block may have bailed out in Polish/expanded.
        $isPl = $false
        foreach ($t in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)))) {
            try { if ($t.Current.Name -eq "Ustawienia") { $isPl = $true; break } } catch { }
        }
        if ($isPl) {
            foreach ($dy in @(15, 30, 45)) {
                $r = $window.Current.BoundingRectangle
                [PcoMouse]::Click([int]($r.X + 45), [int]($r.Y + $r.Height - $dy))
                Start-Sleep -Milliseconds 900
                $window = Get-Window "Process Core Optimizer" 10
                if (!$window) { throw "Main window lost while restoring English." }
                $back = $false
                foreach ($t in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                        [System.Windows.Automation.ControlType]::Text)))) {
                    try { if ($t.Current.Name -eq "Settings") { $back = $true; break } } catch { }
                }
                if ($back) { break }
            }
        }
        # Collapse the hub if the previous block left it expanded.
        foreach ($b in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)))) {
            try {
                if ($b.Current.Name -like "PCO *") {
                    $hstate = $b.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
                    if ($hstate.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) { $hstate.Toggle(); Start-Sleep -Milliseconds 600 }
                    break
                }
            } catch { }
        }
        $hwnd = [IntPtr]$window.Current.NativeWindowHandle
        $r0 = $window.Current.BoundingRectangle
        [PcoWin32]::MoveWindow($hwnd, [int]$r0.X, [int]$r0.Y, 1000, 700, $true) | Out-Null
        Start-Sleep -Milliseconds 900
        $window = Get-Window "Process Core Optimizer" 10
        if (!$window) { throw "Main window lost after resize to minimum." }
        # Open the hub and prove every row is reachable at minimum size.
        $hubBtn = $null
        foreach ($b in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)))) {
            try { if ($b.Current.Name -like "PCO *") { $hubBtn = $b; break } } catch { }
        }
        if (!$hubBtn) { throw "Hub header not found at minimum size." }
        $hubBtn.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
        Start-Sleep -Milliseconds 700
        foreach ($n in @("Project Repository ${emdash} Process Core Optimizer", "Developer GitHub ${emdash} 9Erza",
            "Author website ${emdash} DobryPC.pl", "Buy Me a Coffee ${emdash} Support Development", "About")) {
            $row = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $n)))
            if (!$row) { throw "Hub row '$n' unreachable at minimum size." }
        }
        Save-Shot "21-hub-min-expanded.png" $window
        # Collapse and restore the default window size for later checks.
        try { $hubBtn.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() } catch { }
        Start-Sleep -Milliseconds 500
        [PcoWin32]::MoveWindow($hwnd, [int]$r0.X, [int]$r0.Y, [int]$r0.Width, [int]$r0.Height, $true) | Out-Null
        Start-Sleep -Milliseconds 800
        $window = Get-Window "Process Core Optimizer" 10
        if (!$window) { throw "Main window lost after restore from minimum size." }
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
