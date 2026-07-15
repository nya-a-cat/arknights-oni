#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANAGED="${ONI_MANAGED_DIR:-/mnt/c/Program Files (x86)/Steam/steamapps/common/OxygenNotIncluded/OxygenNotIncluded_Data/Managed}"
TEST_ROOT="/tmp/arknights-operator-resolver-test-${UID}"
OUT="/tmp/OperatorAssetResolverIntegrationTests-${UID}.exe"
RSP="/tmp/OperatorAssetResolverIntegrationTests-${UID}.rsp"
DEPS="$TEST_ROOT/deps"

cleanup() {
  rm -f "$OUT" "$RSP"
  rm -rf "$TEST_ROOT"
}
trap cleanup EXIT

rm -rf "$TEST_ROOT"
mkdir -p "$DEPS"
cp "$MANAGED/Newtonsoft.Json.dll" "$DEPS/"
find "$ROOT/lib/spine-csharp-src" -name '*.cs' | sort > "$RSP"
printf '%s\n' \
  "$ROOT/src/AtomicFile.cs" \
  "$ROOT/src/PrtsAssetClient.cs" \
  "$ROOT/src/PrtsResourceService.cs" \
  "$ROOT/src/ResourceIndex.cs" \
  "$ROOT/src/OperatorAssetResolver.cs" \
  "$ROOT/tests/OperatorAssetResolverIntegrationTests.cs" >> "$RSP"

mcs \
  -langversion:latest \
  -out:"$OUT" \
  -r:"$MANAGED/Newtonsoft.Json.dll" \
  -r:System.Core \
  -r:System.Net.Http \
  -r:System.Runtime.Serialization \
  @"$RSP"

MONO_PATH="$DEPS" mono "$OUT" "$TEST_ROOT"
