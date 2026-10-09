#!/bin/zsh
# Copies dist/DokiDokiMerchant into the game's mods folder. Your DokiDokiMerchant.cfg (keys) and the
# Merchant's memory of your runs are kept across reinstalls.
set -euo pipefail

project_dir=${0:A:h:h}
source_dir="$project_dir/dist/DokiDokiMerchant"
mods_dir="${STS2_MODS_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods}"
install_dir="$mods_dir/DokiDokiMerchant"

if [[ ! -f "$source_dir/DokiDokiMerchant.dll" ]]; then
  echo 'Build the mod before installing it (scripts/build.sh).' >&2
  exit 1
fi

mkdir -p "$install_dir"
install -m 0644 "$source_dir/DokiDokiMerchant.dll" "$source_dir/DokiDokiMerchant.json" "$source_dir/DokiDokiMerchant.cfg.example" "$install_dir/"
if [[ -d "$source_dir/DokiMicHelper.app" ]]; then
  rm -rf "$install_dir/DokiMicHelper.app"
  cp -R "$source_dir/DokiMicHelper.app" "$install_dir/"
fi

cfg="$install_dir/DokiDokiMerchant.cfg"
if [[ ! -f "$cfg" ]]; then
  cp "$source_dir/DokiDokiMerchant.cfg.example" "$cfg"
  chmod 600 "$cfg"
  # Reuse the hackathon project's Fish key when it's there (never printed).
  fish_env="$project_dir/../fish-hackathon/fish-audio/.env"
  if [[ -f "$fish_env" ]]; then
    python3 - "$cfg" "$fish_env" <<'PY'
import json, sys
cfg, env = sys.argv[1], sys.argv[2]
vals = dict(l.split("=", 1) for l in open(env).read().splitlines() if "=" in l and not l.startswith("#"))
c = json.load(open(cfg))
if vals.get("FISH_API_KEY", "").strip():
    c["fish_api_key"] = vals["FISH_API_KEY"].strip()
json.dump(c, open(cfg, "w"), indent=2)
PY
  fi
  echo "Created $cfg: put your openai_api_key in it."
fi

echo "Installed $install_dir"
