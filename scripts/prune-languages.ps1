<#
.SYNOPSIS
  Removes translated resource folders (de, fr, ja, zh-Hans, ...) from a publish folder and keeps English and Swedish.
  Only folders whose name is a real culture name are touched, so folders such as "lib" or "runtimes" are never removed.
  Prints the size of the folder before and after, and the largest remaining folders.
#>
param(
  [Parameter(Mandatory = $true)] [string] $Path,
  [string[]] $Keep = @('en', 'sv'),
  # x64 or ARM64: native runtimes of other architectures are removed. Empty keeps them all.
  [string] $Arch = '',
  # Removes the ONNX Runtime and DirectML libraries that the Windows App SDK brings for its AI features. Cshubben does not use them.
  [switch] $DropAi
)

$ErrorActionPreference = 'Stop'
function SizeMb($p) { [math]::Round(((Get-ChildItem $p -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum) / 1MB, 1) }

$before = SizeMb $Path
$cultures = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($c in [System.Globalization.CultureInfo]::GetCultures([System.Globalization.CultureTypes]::AllCultures)) {
  if ($c.Name) { [void]$cultures.Add($c.Name) }
}

$removed = 0
Get-ChildItem $Path -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
  if (-not (Test-Path $_.FullName)) { return }
  $name = $_.Name
  if (-not $cultures.Contains($name)) { return }
  $lang = ($name -split '-')[0]
  if ($Keep -contains $lang) { return }
  Remove-Item $_.FullName -Recurse -Force
  $removed++
}

if ($Arch) {
  $rid = if ($Arch -ieq 'ARM64') { 'win-arm64' } else { 'win-x64' }
  $runtimes = Join-Path $Path 'runtimes'
  if (Test-Path $runtimes) {
    Get-ChildItem $runtimes -Directory | Where-Object { $_.Name -ne $rid -and $_.Name -ne ($rid -replace '^win-', 'win10-') } |
      ForEach-Object { Remove-Item $_.FullName -Recurse -Force; "Removed runtimes/$($_.Name)" }
  }
}

if ($DropAi) {
  Get-ChildItem $Path -Recurse -File | Where-Object { $_.Name -match '^(onnxruntime|DirectML)' } |
    ForEach-Object { Remove-Item $_.FullName -Force; "Removed $($_.Name)" }
}

$after = SizeMb $Path
"Pruned $removed language folders: $before MB -> $after MB (kept: $($Keep -join ', '))"
"Largest folders left:"
Get-ChildItem $Path -Directory | ForEach-Object { [pscustomobject]@{ Folder = $_.Name; MB = SizeMb $_.FullName } } |
  Sort-Object MB -Descending | Select-Object -First 8 | Format-Table -AutoSize | Out-String | Write-Host
"Largest files left:"
Get-ChildItem $Path -Recurse -File | Sort-Object Length -Descending | Select-Object -First 8 |
  ForEach-Object { "{0,8:N1} MB  {1}" -f ($_.Length / 1MB), $_.FullName.Substring($Path.Length).TrimStart('\','/') }
