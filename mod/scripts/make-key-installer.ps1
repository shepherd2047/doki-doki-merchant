# Windows version of make-key-installer.sh: makes dist/key-installer/Doki-keys-windows.bat, a one-click
# "install my keys" script for a friend who subscribed to the mod on the Workshop. It carries YOUR
# DokiDokiMerchant.cfg (API keys, with "language" reset to "auto" so the mod follows their game) and writes it
# next to every DokiDokiMerchant.dll it finds in the friend's Steam libraries.
# The output contains your keys: send it privately, never commit or upload it (dist/ is gitignored).
#
#   powershell -ExecutionPolicy Bypass -File scripts\make-key-installer.ps1 [path\to\DokiDokiMerchant.cfg]
param([string]$Cfg = "${env:ProgramFiles(x86)}\Steam\steamapps\common\Slay the Spire 2\mods\DokiDokiMerchant\DokiDokiMerchant.cfg")
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Cfg)) { Write-Error "No config at $Cfg (or pass a cfg path)." }
$c = Get-Content $Cfg -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not "$($c.openai_api_key)".Trim()) { Write-Error 'Config is missing openai_api_key.' }
if ($c.PSObject.Properties.Name -contains 'language') { $c.language = 'auto' }
$b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($c | ConvertTo-Json)))

$ps = @'
$ErrorActionPreference = 'SilentlyContinue'
$b = '__CFG__'
$libs = @()
$sp = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath
if ($sp) { $libs += $sp }
$libs += "${env:ProgramFiles(x86)}\Steam"
foreach ($l in @($libs)) {
  $vdf = Join-Path $l 'steamapps\libraryfolders.vdf'
  if (Test-Path $vdf) { foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { $libs += $m.Groups[1].Value -replace '\\\\', '\' } }
}
$n = 0
foreach ($l in ($libs | ForEach-Object { $_ -replace '/', '\' } | Select-Object -Unique)) {
  foreach ($d in @((Join-Path $l 'steamapps\workshop\content\2868840'), (Join-Path $l 'steamapps\common\Slay the Spire 2'))) {
    if (-not (Test-Path $d)) { continue }
    foreach ($dll in Get-ChildItem $d -Recurse -Filter DokiDokiMerchant.dll) {
      [IO.File]::WriteAllBytes((Join-Path $dll.DirectoryName 'DokiDokiMerchant.cfg'), [Convert]::FromBase64String($b))
      Write-Host "Keys installed: $($dll.DirectoryName)"; $n++
    }
  }
}
if ($n -eq 0) { Write-Host "Couldn't find the mod. Subscribe to Doki Doki Merchant on the Steam Workshop, start the game once, then run me again." }
else { Write-Host 'Done. Restart Slay the Spire 2 and walk into a shop. Hold V to talk (or type).' }
'@.Replace('__CFG__', $b64)

$enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($ps))
$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\key-installer'
New-Item -ItemType Directory -Force $out | Out-Null
$bat = "@echo off`r`nrem Doki Doki Merchant: installs the API keys into the mod (subscribe on the Workshop first). Double-click me.`r`n" +
       "powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand $enc`r`npause`r`n"
[IO.File]::WriteAllText((Join-Path $out 'Doki-keys-windows.bat'), $bat, [Text.Encoding]::ASCII)
Write-Host "Made $out\Doki-keys-windows.bat (contains your API keys: send privately, never commit or upload)."
