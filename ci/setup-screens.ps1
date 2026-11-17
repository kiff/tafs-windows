# The setup's first and last screens in one language (docs/install.md), on CI's x64 runner, where installing needs no
# prompt. It opens the real setup with Burn's /lang, saves the Welcome screen, presses Install, saves the finished screen
# (the one with Open tafs), closes it, and uninstalls quietly. Words come from installer/words/<lang>.wxl, so the buttons
# are found by their own language's names.
param(
    [Parameter(Mandatory = $true)] [string] $Setup,
    [Parameter(Mandatory = $true)] [string] $Lang,
    [Parameter(Mandatory = $true)] [int] $Lcid,
    [Parameter(Mandatory = $true)] [string] $Out
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Screen {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
}
'@
[void][Screen]::SetProcessDPIAware()
$Setup = (Resolve-Path $Setup).Path
New-Item -ItemType Directory -Force $Out | Out-Null

[xml] $wxl = Get-Content -Raw -Encoding utf8 (Join-Path $PSScriptRoot "..\installer\words\$Lang.wxl")
function Word([string] $id) { ($wxl.WixLocalization.String | Where-Object Id -eq $id).Value.Replace('&', '') }
$install = Word 'InstallInstallButton'
$open = Word 'SuccessLaunchButton'
$close = Word 'SuccessCloseButton'

$ua = [System.Windows.Automation.AutomationElement]
$tree = [System.Windows.Automation.TreeScope]
function Find-Window {
    $class = New-Object System.Windows.Automation.PropertyCondition($ua::ClassNameProperty, 'WixStdBA')
    $ua::RootElement.FindFirst($tree::Children, $class)
}
# WiX's buttons are Win32 Buttons that UI Automation shows as panes with no Invoke, so they're found by their window class
# and name, and pressed with BM_CLICK.
function Find-Button($window, [string] $name) {
    $button = New-Object System.Windows.Automation.PropertyCondition($ua::ClassNameProperty, 'Button')
    $named = New-Object System.Windows.Automation.PropertyCondition($ua::NameProperty, $name)
    $window.FindFirst($tree::Descendants, (New-Object System.Windows.Automation.AndCondition($button, $named)))
}
function Press($button) { [void][Screen]::SendMessage([IntPtr]$button.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) }
function Wait-For([scriptblock] $what, [int] $seconds, [string] $why) {
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) { $found = & $what; if ($found) { return $found }; Start-Sleep -Milliseconds 500 }
    throw "waited ${seconds}s for $why"
}
function Save-Window($window, [string] $file) {
    [void][Screen]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)
    Start-Sleep -Seconds 1
    # The window as drawn (DWMWA_EXTENDED_FRAME_BOUNDS), without its invisible resize border.
    $r = New-Object Screen+RECT
    [void][Screen]::DwmGetWindowAttribute([IntPtr]$window.Current.NativeWindowHandle, 9, [ref]$r, 16)
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose(); $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "saved $file"
}

$p = Start-Process $Setup -ArgumentList '/lang', $Lcid, '/log', (Join-Path $Out "setup-$Lang.log") -PassThru
try {
    $window = Wait-For { Find-Window } 60 'the setup window'
    $installButton = Wait-For { Find-Button $window $install } 60 "the '$install' button"
    Save-Window $window (Join-Path $Out "setup-$Lang-first.png")
    Press $installButton
    # The finished screen is the one with Open tafs; a failed one has only Close.
    [void](Wait-For { Find-Button $window $open } 300 "the '$open' button, the finished screen")
    Save-Window $window (Join-Path $Out "setup-$Lang-last.png")
    Press (Find-Button $window $close)
    if (-not $p.WaitForExit(60000)) { throw 'the setup did not close' }
}
finally {
    if (-not $p.HasExited) { $p.Kill() }
    $u = Start-Process $Setup -ArgumentList '/uninstall', '/quiet', '/norestart' -Wait -PassThru
    Write-Host "uninstalled: $($u.ExitCode)"
}
