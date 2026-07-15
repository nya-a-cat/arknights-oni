#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANAGED="${ONI_MANAGED_DIR:-/mnt/c/Program Files (x86)/Steam/steamapps/common/OxygenNotIncluded/OxygenNotIncluded_Data/Managed}"
TEST_ROOT="/tmp/arknights-operator-catalog-test-${UID}"
OUT="/tmp/OperatorAppearanceCatalogTests-${UID}.exe"
DEPS="$TEST_ROOT/deps"

cleanup() {
  rm -f "$OUT"
  rm -rf "$TEST_ROOT"
}
trap cleanup EXIT

mkdir -p "$DEPS"
cp "$MANAGED/Newtonsoft.Json.dll" "$DEPS/"

mcs \
  -langversion:latest \
  -out:"$OUT" \
  -r:"$MANAGED/Newtonsoft.Json.dll" \
  -r:System.Core \
  "$ROOT/src/OperatorAppearanceCatalog.cs" \
  "$ROOT/tests/OperatorAppearanceCatalogTests.cs"

MONO_PATH="$DEPS" mono "$OUT" \
  "$ROOT/assets/catalog/operator_appearances_20260604.json"
