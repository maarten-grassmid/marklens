# Installs a packaged MarkLens zip into a portable Notepad++ (the minimum supported
# version), opens docs/sample.md, shows the viewer and checks that the plugin loaded
# and rendered without logging an error. Used by CI on every build; also runnable locally:
#   ./tools/smoke-test.ps1 -Zip dist/MarkLens-0.1.1-x64.zip
param(
    [Parameter(Mandatory)] [string]$Zip,
    [string]$WorkDir = (Join-Path ([IO.Path]::GetTempPath()) 'marklens-smoke'),
    [string]$Screenshot = ''  # optional: path of a PNG of the viewer window
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

# Oldest Notepad++ the plugin supports (npp-compatible-versions in the plugin list).
$nppVersion = '8.3.1'
$nppUrl = "https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v$nppVersion/npp.$nppVersion.portable.x64.zip"
$nppSha256 = '99382154344d7268dfd8612b684456868d97896f2bd23c6bed644bbc0a17739d'

function Get-Sha256([string]$path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($path))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Stop-Npp { Get-Process notepad++ -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }

$Zip = Resolve-Path $Zip
$log = Join-Path ([IO.Path]::GetTempPath()) 'MarkLens.log'
if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force $WorkDir | Out-Null
if (Test-Path $log) { Remove-Item $log -Force }

# Portable Notepad++ (cached next to the work dir so repeated local runs do not re-download).
$cache = Join-Path ([IO.Path]::GetTempPath()) "npp.$nppVersion.portable.x64.zip"
if (-not (Test-Path $cache) -or (Get-Sha256 $cache) -ne $nppSha256) {
    Write-Host "downloading Notepad++ $nppVersion portable"
    Invoke-WebRequest -Uri $nppUrl -OutFile $cache -UseBasicParsing
}
$actual = Get-Sha256 $cache
if ($actual -ne $nppSha256) { throw "Notepad++ download checksum mismatch: expected $nppSha256, got $actual" }
$npp = Join-Path $WorkDir 'npp'
Expand-Archive -Path $cache -DestinationPath $npp
$exe = Join-Path $npp 'notepad++.exe'
if (-not (Test-Path $exe)) { throw 'notepad++.exe missing from the portable package' }

# Install the plugin exactly as a user would: unzip into plugins\MarkLens. Files a browser
# downloaded carry the "mark of the web" zone stream, so stamp it on to reproduce that.
$pluginDir = Join-Path $npp 'plugins\MarkLens'
New-Item -ItemType Directory -Force $pluginDir | Out-Null
Expand-Archive -Path $Zip -DestinationPath $pluginDir
if (-not (Test-Path (Join-Path $pluginDir 'MarkLens.dll'))) { throw 'MarkLens.dll is not at the root of the zip' }
Get-ChildItem $pluginDir -Recurse -File | ForEach-Object {
    Set-Content -Path $_.FullName -Stream 'Zone.Identifier' -Value "[ZoneTransfer]`r`nZoneId=3" -NoNewline
}

# Open the sample and show the viewer through its Plugins menu entry.
$sample = Join-Path $root 'docs\sample.md'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class Win {
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] public static extern IntPtr GetSubMenu(IntPtr menu, int pos);
    [DllImport("user32.dll")] public static extern uint GetMenuItemID(IntPtr menu, int pos);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetMenuString(IntPtr menu, uint pos, StringBuilder s, int n, uint flags);
    public static string MenuText(IntPtr menu, int pos) { var s = new StringBuilder(256); GetMenuString(menu, (uint)pos, s, 256, 0x400 /* MF_BYPOSITION */); return s.ToString(); }
    // Command id of Plugins > <plugin> > <item>, or 0 when not found.
    public static uint PluginCommand(IntPtr npp, string plugin, string item) {
        const uint NPPM_GETMENUHANDLE = 0x400 + 1000 + 25;
        IntPtr menu = SendMessage(npp, NPPM_GETMENUHANDLE, IntPtr.Zero /* NPPPLUGINMENU */, IntPtr.Zero);
        for (int i = 0; i < GetMenuItemCount(menu); i++) {
            if (MenuText(menu, i).Replace("&", "") != plugin) continue;
            IntPtr sub = GetSubMenu(menu, i);
            for (int j = 0; j < GetMenuItemCount(sub); j++)
                if (MenuText(sub, j).Replace("&", "").Split((char)9)[0] == item) return GetMenuItemID(sub, j);
        }
        return 0;
    }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public static List<IntPtr> Children(IntPtr parent) {
        var list = new List<IntPtr>();
        EnumChildWindows(parent, (h, l) => { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    public static string Text(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
    public static string Class(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
}
'@

Stop-Npp
$proc = Start-Process -FilePath $exe -ArgumentList "-multiInst -nosession `"$sample`"" -PassThru
$deadline = (Get-Date).AddSeconds(30)
while ($proc.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500; $proc.Refresh() }
if ($proc.MainWindowHandle -eq 0) { Stop-Npp; throw 'Notepad++ did not open a window' }
Start-Sleep -Seconds 3

$cmd = [Win]::PluginCommand($proc.MainWindowHandle, 'MarkLens', 'Markdown Viewer')
if ($cmd -eq 0) { Stop-Npp; throw 'Plugins > MarkLens > Markdown Viewer is not in the menu (plugin not loaded)' }
[Win]::PostMessage($proc.MainWindowHandle, 0x111 <# WM_COMMAND #>, [IntPtr]$cmd, [IntPtr]::Zero) | Out-Null

# Wait until the viewer panel and its WebView2 (class Chrome_WidgetWin_*) are up.
$panel = [IntPtr]::Zero; $web = [IntPtr]::Zero
$deadline = (Get-Date).AddSeconds(45)
while ((Get-Date) -lt $deadline) {
    foreach ($h in [Win]::Children($proc.MainWindowHandle)) {
        if ([Win]::Text($h) -eq 'MarkLens' -and [Win]::Class($h) -like 'WindowsForms*') { $panel = $h }
        if ([Win]::Class($h) -like 'Chrome_WidgetWin*') { $web = $h }
    }
    if ($panel -ne [IntPtr]::Zero -and $web -ne [IntPtr]::Zero) { break }
    Start-Sleep -Seconds 1
}
Start-Sleep -Seconds 5  # let the first render (and any error) land

if ($Screenshot -and $panel -ne [IntPtr]::Zero) {
    $r = New-Object Win+RECT
    [Win]::GetWindowRect($panel, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap(($r.R - $r.L), ($r.B - $r.T))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    [Win]::PrintWindow($panel, $dc, 2) | Out-Null   # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save($Screenshot, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "screenshot: $Screenshot"
}

Stop-Npp

$failures = @()
if (Test-Path $log) { $failures += "the plugin logged an error:`n" + (Get-Content $log -Raw) }
if ($panel -eq [IntPtr]::Zero) { $failures += 'the MarkLens panel did not appear' }
if ($web -eq [IntPtr]::Zero) { $failures += 'the viewer did not create its WebView2 control' }
if ($failures) { throw ("smoke test failed:`n- " + ($failures -join "`n- ")) }
Write-Host "smoke test passed on Notepad++ $nppVersion"
