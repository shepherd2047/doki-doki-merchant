#!/bin/zsh
# Builds the mod into dist/DokiDokiMerchant: the DLL, its manifest, the config template and (on macOS) the
# microphone helper app.
set -euo pipefail

project_dir=${0:A:h:h}
dotnet_bin=/opt/homebrew/opt/dotnet@9/bin/dotnet
export DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec

"$dotnet_bin" build "$project_dir/DokiDokiMerchant.csproj" -c Release

package_dir="$project_dir/dist/DokiDokiMerchant"
rm -rf "$package_dir"
mkdir -p "$package_dir"
cp "$project_dir/bin/Release/net9.0/DokiDokiMerchant.dll" "$package_dir/"
cp "$project_dir/DokiDokiMerchant.json" "$project_dir/DokiDokiMerchant.cfg.example" "$package_dir/"

if [[ "$(uname)" == Darwin ]]; then
  app="$package_dir/DokiMicHelper.app"
  build="$project_dir/mic-helper/build"
  mkdir -p "$build" "$app/Contents/MacOS"
  for arch in arm64 x86_64; do
    swiftc -O -target "$arch-apple-macos11" "$project_dir/mic-helper/main.swift" -o "$build/DokiMicHelper-$arch"
  done
  lipo -create "$build"/DokiMicHelper-arm64 "$build"/DokiMicHelper-x86_64 -output "$app/Contents/MacOS/DokiMicHelper"
  cp "$project_dir/mic-helper/Info.plist" "$app/Contents/Info.plist"
  codesign --force --sign - "$app"
fi

echo "Built $package_dir"
