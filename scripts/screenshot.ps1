<#
  Launches the published app once per page with demo data and saves a PNG of its window.
  Used by CI (artifact "screenshots"); also works locally on Windows.

  Usage: ./scripts/screenshot.ps1 -Exe publish/x64/Cshubben.exe -OutDir shots
         ./scripts/screenshot.ps1 -Exe ... -OutDir shots -Shots "overview;reviews --select r3"
  Each shot is "<page> [extra app args]". The file name is derived from the shot.
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [Parameter(Mandatory)] [string] $OutDir,
  [string] $Shots = "overview;overview --select a1;feed;mine --select a3;reviews --select r3;drift;linear;notes;stats;settings",
  [int] $WaitSeconds = 4,
  [string] $Resolution = "1920x1080"
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = New-Object System.Collections.Generic.List[string]

# Best effort: the hosted runner starts at 1024x768, which hides how the app looks on a normal monitor.
if ($Resolution) {
  try {
    $w0, $h0 = $Resolution -split 'x'
    # Built-in on some images; otherwise fall back to the DisplayConfig module.
    if (-not (Get-Command Set-DisplayResolution -ErrorAction SilentlyContinue)) {
      Install-Module -Name DisplayConfig -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop | Out-Null
      Import-Module DisplayConfig -ErrorAction Stop
    }
    Set-DisplayResolution -Width ([int]$w0) -Height ([int]$h0) -Force -ErrorAction Stop
    Start-Sleep -Seconds 3
    $log.Add("resolution: set to $Resolution")
  } catch {
    $log.Add("resolution: could not set $Resolution ($($_.Exception.Message))")
  }
}
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$log.Add("screen: $($screen.Width)x$($screen.Height)")
$exePath = (Resolve-Path $Exe).Path

foreach ($shot in ($Shots -split ';')) {
  $parts = $shot.Trim() -split '\s+'
  $page = $parts[0]
  $rest = if ($parts.Length -gt 1) { $parts[1..($parts.Length - 1)] } else { @() }
  # A token like @1000x800 sets the window size for this shot (default 1440x900).
  $sizeTok = $rest | Where-Object { $_ -like '@*' } | Select-Object -First 1
  $extra = @($rest | Where-Object { $_ -notlike '@*' })
  $sw, $sh = if ($sizeTok) { ($sizeTok.Substring(1) -split 'x') | ForEach-Object { [int]$_ } } else { 1440, 900 }
  $name = ($shot.Trim() -replace '--select ', '' -replace '--tab ', 'tab' -replace '@', '' -replace '\s+', '-')
  $appArgs = @('--demo', '--start', $page) + $extra

  $proc = Start-Process -FilePath $exePath -ArgumentList $appArgs -PassThru
  $deadline = (Get-Date).AddSeconds(40)
  while ((Get-Date) -lt $deadline -and -not $proc.HasExited -and $proc.MainWindowHandle -eq [IntPtr]::Zero) {
    Start-Sleep -Milliseconds 300
    $proc.Refresh()
  }
  if ($proc.HasExited) {
    $log.Add("${name}: app exited early with code $($proc.ExitCode)")
    continue
  }
  Start-Sleep -Seconds $WaitSeconds
  $proc.Refresh()
  $h = $proc.MainWindowHandle
  [Win]::MoveWindow($h, 0, 0, [Math]::Min($sw, $screen.Width), [Math]::Min($sh, $screen.Height), $true) | Out-Null
  [Win]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 800

  $r = New-Object Win+RECT
  [Win]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = [Math]::Max(1, $r.Right - $r.Left); $hh = [Math]::Max(1, $r.Bottom - $r.Top)

  # 1) PrintWindow of the window itself (works even if it is partly off-screen).
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  $ok = [Win]::PrintWindow($h, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $bmp.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

  $log.Add("${name}: ${w}x${hh} printwindow=$ok")
  Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 300
}

$log | Tee-Object -FilePath (Join-Path $OutDir "log.txt")
