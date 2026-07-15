#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -n "${ONI_GAME_ROOT:-}" ]]; then
  GAME_ROOT="$ONI_GAME_ROOT"
elif [[ -f "/mnt/c/Program Files (x86)/Steam/steamapps/common/OxygenNotIncluded/OxygenNotIncluded_Data/Managed/Assembly-CSharp.dll" ]]; then
  GAME_ROOT="/mnt/c/Program Files (x86)/Steam/steamapps/common/OxygenNotIncluded"
else
  GAME_ROOT="/mnt/c/Program Files (x86)/Steam/steamapps/downloading/457140"
fi
MANAGED="$GAME_ROOT/OxygenNotIncluded_Data/Managed"
OUT="$ROOT/AmiyaDuplicantMod.dll"
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

find "$ROOT/src" "$ROOT/lib/spine-csharp-src" -name '*.cs' | sort > "$RSP"

"$MCS_BIN" \
  -target:library \
  -langversion:latest \
  -out:"$OUT" \
  -r:"$MANAGED/0Harmony.dll" \
  -r:"$MANAGED/netstandard.dll" \
  -r:"$MANAGED/System.Net.Http.dll" \
  -r:"$MANAGED/System.Runtime.Serialization.dll" \
  -r:"$MANAGED/Assembly-CSharp-firstpass.dll" \
  -r:"$MANAGED/Assembly-CSharp.dll" \
  -r:"$MANAGED/Newtonsoft.Json.dll" \
  -r:"$MANAGED/UnityEngine.CoreModule.dll" \
  -r:"$MANAGED/UnityEngine.dll" \
  -r:"$MANAGED/UnityEngine.AnimationModule.dll" \
  -r:"$MANAGED/UnityEngine.ImageConversionModule.dll" \
  -r:"$MANAGED/UnityEngine.InputLegacyModule.dll" \
  -r:"$MANAGED/UnityEngine.TextRenderingModule.dll" \
  -r:"$MANAGED/UnityEngine.UI.dll" \
  -r:"$MANAGED/UnityEngine.UIModule.dll" \
  -r:"$ROOT/lib/PLib.dll" \
  @"$RSP"

echo "Built $OUT"
