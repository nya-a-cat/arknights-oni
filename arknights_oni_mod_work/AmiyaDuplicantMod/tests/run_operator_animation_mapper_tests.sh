#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="/tmp/OperatorAnimationMapperTests-${UID}.exe"

cleanup() {
  rm -f "$OUT"
}
trap cleanup EXIT

mcs \
  -langversion:latest \
  -out:"$OUT" \
  -r:System.Core \
  "$ROOT/src/OperatorAnimationMapper.cs" \
  "$ROOT/tests/OperatorAnimationMapperTests.cs"

mono "$OUT"
