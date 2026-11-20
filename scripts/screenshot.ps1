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
  [int] $WaitSeconds = 8
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
$exePath = (Resolve-Path $Exe).Path
$log = New-Object System.Collections.Generic.List[string]

foreach ($shot in ($Shots -split ';')) {
  $parts = $shot.Trim() -split '\s+'
  $page = $parts[0]
  $extra = if ($parts.Length -gt 1) { $parts[1..($parts.Length - 1)] } else { @() }
  $name = ($shot.Trim() -replace '--select ', '' -replace '--tab ', 'tab' -replace '\s+', '-')
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
  [Win]::MoveWindow($h, 0, 0, 1440, 900, $true) | Out-Null
  [Win]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Seconds 2

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

  # 2) Screen copy of the window area as a fallback/comparison.
  $bmp2 = New-Object System.Drawing.Bitmap $w, $hh
  $g2 = [System.Drawing.Graphics]::FromImage($bmp2)
  $g2.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hh))
  $g2.Dispose()
  $bmp2.Save((Join-Path $OutDir "$name.screen.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp2.Dispose()

  $log.Add("${name}: ${w}x${hh} printwindow=$ok")
  Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 1
}

$log | Tee-Object -FilePath (Join-Path $OutDir "log.txt")
