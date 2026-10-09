#!/bin/zsh
# Compile-check one UI component in isolation, so parallel work on other components can't break your build.
# Usage: scripts/agent-build.sh RugView.cs [NativePatches.cs ...]
# Copies the project to a private folder, swaps every other src/Ui component for its stub (stubs/), keeps the
# named files from src/Ui, and builds there. Exits non-zero on compile errors.
set -euo pipefail
project_dir=${0:A:h:h}
name=${1:r}
work="${TMPDIR:-/tmp}/doki-agent-build/$name"
rm -rf "$work"; mkdir -p "$work"
rsync -a --exclude bin --exclude obj --exclude dist --exclude mic-helper "$project_dir/" "$work/"
for stub in "$work"/stubs/*.cs; do cp "$stub" "$work/src/Ui/"; done
# Drop other components' extra files (e.g. ChatPanelPatches.cs): they target the real component, not its stub.
for f in "$work"/src/Ui/*.cs; do
  b=${f:t}; [[ $b == Theme.cs || -f "$work/stubs/$b" ]] || rm "$f"
done
for f in "$@"; do cp "$project_dir/src/Ui/$f" "$work/src/Ui/$f"; done
# src/Doki.cs is integrated against the real components; check yours against a minimal entry point instead.
[[ ${DOKI_KEEP_DOKI:-0} == 1 ]] || : > "$work/src/Doki.cs" && printf 'namespace DokiDokiMerchant;\ninternal sealed class Doki { public static void Attach(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom r) {} public static void Detach(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom r) {} }\n' > "$work/src/Doki.cs"
cd "$work"
dotnet build -c Release -nologo -v q -clp:ErrorsOnly 2>&1 | grep -E "error|Build succeeded|warn CS" | sort -u | head -60
exit ${pipestatus[1]}
