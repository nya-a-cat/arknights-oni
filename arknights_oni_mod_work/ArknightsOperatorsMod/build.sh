#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$ROOT/../.." && pwd)"
SOURCE_PREFIX="arknights_oni_mod_work/ArknightsOperatorsMod"
CHANNEL_INPUT="${1:-Stable}"
if [[ $# -gt 1 ]]; then
  echo "Usage: bash build.sh [Stable|Dev|RC]" >&2
  exit 2
fi

case "${CHANNEL_INPUT,,}" in
  stable)
    CHANNEL="Stable"
    OUTPUT_NAME="ArknightsOperatorsMod.dll"
    ;;
  dev)
    CHANNEL="Dev"
    OUTPUT_NAME="ArknightsOperatorsTesting.dll"
    ;;
  rc)
    CHANNEL="RC"
    OUTPUT_NAME="ArknightsOperatorsTesting.dll"
    ;;
  *)
    echo "Unknown build channel '$CHANNEL_INPUT'; expected Stable, Dev, or RC." >&2
    exit 2
    ;;
esac

if [[ -n "${ONI_GAME_ROOT:-}" ]]; then
  GAME_ROOT="$ONI_GAME_ROOT"
elif [[ -f "/mnt/c/Program Files (x86)/Steam/steamapps/common/OxygenNotIncluded/OxygenNotIncluded_Data/Managed/Assembly-CSharp.dll" ]]; then
  GAME_ROOT="/mnt/c/Program Files (x86)/Steam/steamapps/common/OxygenNotIncluded"
else
  GAME_ROOT="/mnt/c/Program Files (x86)/Steam/steamapps/downloading/457140"
fi
MANAGED="$GAME_ROOT/OxygenNotIncluded_Data/Managed"
OUT="$ROOT/$OUTPUT_NAME"
RSP="$ROOT/build.sources.rsp"
MCS_BIN="${MCS_BIN:-$(command -v mcs || true)}"
if [[ -z "$MCS_BIN" && -x "/home/linuxbrew/.linuxbrew/bin/mcs" ]]; then
  MCS_BIN="/home/linuxbrew/.linuxbrew/bin/mcs"
fi

if [[ ! -f "$MANAGED/Assembly-CSharp.dll" ]]; then
  echo "Could not find ONI Assembly-CSharp.dll under: $MANAGED" >&2
  exit 1
fi

if [[ ! -f "$ROOT/lib/PLib.dll" ]]; then
  echo "Missing audited PLib dependency: $ROOT/lib/PLib.dll" >&2
  exit 1
fi

if [[ -z "$MCS_BIN" ]]; then
  echo "Could not find mcs; install Mono or set MCS_BIN" >&2
  exit 1
fi

: > "$RSP"
while IFS= read -r -d '' source_path; do
  printf '"%s/%s"\n' "$REPO_ROOT" "$source_path" >> "$RSP"
done < <(
  git -C "$REPO_ROOT" ls-files -z -- \
    ":(glob)$SOURCE_PREFIX/src/**/*.cs" \
    ":(glob)$SOURCE_PREFIX/lib/spine-csharp-src/**/*.cs" | sort -z
)
if [[ ! -s "$RSP" ]]; then
  echo "No tracked C# sources were found for the Mod build." >&2
  exit 1
fi

"$MCS_BIN" \
  -target:library \
  -langversion:latest \
  -out:"$OUT" \
  -r:"$MANAGED/0Harmony.dll" \
  -r:"$MANAGED/netstandard.dll" \
  -r:"$MANAGED/System.Net.Http.dll" \
  -r:"$MANAGED/System.IO.Compression.dll" \
  -r:"$MANAGED/System.Runtime.Serialization.dll" \
  -r:"$MANAGED/Assembly-CSharp-firstpass.dll" \
  -r:"$MANAGED/Assembly-CSharp.dll" \
  -r:"$MANAGED/Newtonsoft.Json.dll" \
  -r:"$MANAGED/UnityEngine.CoreModule.dll" \
  -r:"$MANAGED/UnityEngine.dll" \
  -r:"$MANAGED/UnityEngine.AnimationModule.dll" \
  -r:"$MANAGED/UnityEngine.ImageConversionModule.dll" \
  -r:"$MANAGED/UnityEngine.IMGUIModule.dll" \
  -r:"$MANAGED/UnityEngine.InputLegacyModule.dll" \
  -r:"$MANAGED/UnityEngine.TextRenderingModule.dll" \
  -r:"$MANAGED/UnityEngine.UI.dll" \
  -r:"$MANAGED/UnityEngine.UIModule.dll" \
  -r:"$ROOT/lib/PLib.dll" \
  @"$RSP"

echo "Built $CHANNEL assembly: $OUT"
