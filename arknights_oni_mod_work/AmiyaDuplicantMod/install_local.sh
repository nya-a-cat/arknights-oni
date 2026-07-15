#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEST="${ONI_LOCAL_MOD_DIR:-/mnt/c/Users/element/Documents/Klei/OxygenNotIncluded/mods/Local/AmiyaDuplicantMod}"

if [[ ! -f "$ROOT/AmiyaDuplicantMod.dll" ]]; then
  echo "Build the DLL first: bash build.sh" >&2
  exit 1
fi

mkdir -p "$DEST"
cp "$ROOT/AmiyaDuplicantMod.dll" "$DEST/"
cp "$ROOT/lib/PLib.dll" "$DEST/"
cp "$ROOT/mod.yaml" "$DEST/"
cp "$ROOT/mod_info.yaml" "$DEST/"
cp "$ROOT/PLIB-LICENSE.txt" "$DEST/"
cp "$ROOT/PLIB-SOURCE.txt" "$DEST/"
cp "$ROOT/SPINE-RUNTIME-LICENSE.txt" "$DEST/"
cp "$ROOT/lib/SPINE-RUNTIME-SOURCE.txt" "$DEST/"
rm -rf "$DEST/assets"
mkdir -p "$DEST/assets"
cp -R "$ROOT/assets/catalog" "$DEST/assets/catalog"
if [[ -d "$ROOT/assets/spine" ]]; then
  cp -R "$ROOT/assets/spine" "$DEST/assets/spine"
fi

echo "Installed to $DEST"
