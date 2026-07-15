#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANAGED="${ONI_MANAGED_DIR:-/mnt/c/Program Files (x86)/Steam/steamapps/common/OxygenNotIncluded/OxygenNotIncluded_Data/Managed}"
TEST_ROOT="/tmp/arknights-resource-index-test-${UID}"
OUT="/tmp/ResourceIndexTests-${UID}.exe"
DEPS="$TEST_ROOT/deps"

cleanup() {
  rm -f "$OUT"
  rm -rf "$TEST_ROOT"
}
trap cleanup EXIT

rm -rf "$TEST_ROOT"
mkdir -p "$DEPS"
cp "$MANAGED/Newtonsoft.Json.dll" "$DEPS/"

mcs \
  -langversion:latest \
  -out:"$OUT" \
  -r:"$MANAGED/Newtonsoft.Json.dll" \
  -r:System.Core \
  "$ROOT/src/AtomicFile.cs" \
  "$ROOT/src/ResourceIndex.cs" \
  "$ROOT/tests/ResourceIndexTests.cs"

MONO_PATH="$DEPS" mono "$OUT" "$TEST_ROOT"
