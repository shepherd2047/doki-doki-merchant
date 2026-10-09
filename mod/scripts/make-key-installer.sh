#!/bin/zsh
# Makes a one-click "install my keys" script for a friend who subscribed to the mod on the Workshop:
# dist/key-installer/Doki-keys-mac.command and Doki-keys-windows.bat. Each carries YOUR DokiDokiMerchant.cfg
# (API keys) and writes it next to every DokiDokiMerchant.dll it finds in the friend's Steam libraries.
# The output contains your keys: send it privately, never commit or upload it (dist/ is gitignored).
set -euo pipefail

project_dir=${0:A:h:h}
mods_dir="${STS2_MODS_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods}"
cfg="${1:-$mods_dir/DokiDokiMerchant/DokiDokiMerchant.cfg}"
out="$project_dir/dist/key-installer"

[[ -f "$cfg" ]] || { echo "No config at $cfg (or pass a cfg path)." >&2; exit 1; }
mkdir -p "$out"
python3 - "$cfg" "$out" <<'PY'
import base64, json, os, sys
cfg_path, out = sys.argv[1], sys.argv[2]
raw = open(cfg_path, "rb").read()
c = json.loads(raw)
missing = [k for k in ("openai_api_key", "fish_api_key") if not str(c.get(k, "")).strip()]
if missing:
    sys.exit("Config is missing: " + ", ".join(missing))
b64 = base64.b64encode(raw).decode()

mac = r'''#!/bin/zsh
# Doki Doki Merchant: installs the API keys into the mod (subscribe on the Workshop first). Double-click me.
cfg_b64='__CFG__'
steam="$HOME/Library/Application Support/Steam"
libs=("$steam")
vdf="$steam/steamapps/libraryfolders.vdf"
[[ -f "$vdf" ]] && libs+=(${(f)"$(sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)"/\1/p' "$vdf")"})
n=0
for lib in "${(u)libs[@]}"; do
  for d in "$lib/steamapps/workshop/content/2868840" "$lib/steamapps/common/Slay the Spire 2"; do
    [[ -d "$d" ]] || continue
    for dll in ${(f)"$(find "$d" -name DokiDokiMerchant.dll 2>/dev/null)"}; do
      [[ -n "$dll" ]] || continue
      echo "$cfg_b64" | base64 -D > "${dll:h}/DokiDokiMerchant.cfg" && chmod 600 "${dll:h}/DokiDokiMerchant.cfg" && echo "Keys installed: ${dll:h}" && n=$((n+1))
    done
  done
done
if (( n == 0 )); then
  echo "Couldn't find the mod. Subscribe to Doki Doki Merchant on the Steam Workshop, start the game once, then run me again."
else
  echo "Done. Restart Slay the Spire 2 and walk into a shop. Hold V to talk."
fi
read -k1 "?Press any key to close."
'''.replace("__CFG__", b64)

ps = r'''$ErrorActionPreference = 'SilentlyContinue'
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
else { Write-Host 'Done. Restart Slay the Spire 2 and walk into a shop. Hold V to talk.' }
'''.replace("__CFG__", b64)
enc = base64.b64encode(ps.encode("utf-16-le")).decode()
bat = "@echo off\r\nrem Doki Doki Merchant: installs the API keys into the mod (subscribe on the Workshop first). Double-click me.\r\n" \
      f"powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand {enc}\r\npause\r\n"

for name, body, mode in (("Doki-keys-mac.command", mac, 0o700), ("Doki-keys-windows.bat", bat, 0o600)):
    p = os.path.join(out, name)
    with open(p, "w", newline="") as f:
        f.write(body)
    os.chmod(p, mode)
PY
echo "Made $out (contains your API keys: send privately, never commit or upload)."
ls "$out"
