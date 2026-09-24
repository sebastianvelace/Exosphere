#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HARNESS="$ROOT/tools/visual_playtest.sh"

fail() {
  echo "orbital_plume_visual_contract_test: FAIL: $*" >&2
  exit 1
}

bash -n "$HARNESS" || fail "visual playtest harness is not valid shell"
rg -q -- '--orbital-plume' "$HARNESS" \
  || fail "dedicated orbital-plume mode is missing"
rg -q 'MODE="orbital_plume"' "$HARNESS" \
  || fail "orbital-plume parser does not set its mode"
rg -q 'SUMMARY reason=ORBITAL_PLUME_OK' "$HARNESS" \
  || fail "orbital-plume success summary is missing"
rg -q 'VISUAL_ORBITAL_PLUME' "$HARNESS" \
  || fail "orbital-plume runtime telemetry is missing"
rg -q 'anchoredUnits' "$HARNESS" \
  || fail "orbital-plume anchor evidence is missing"
rg -q 'coreOpacity' "$HARNESS" \
  || fail "orbital-plume optical-layer evidence is missing"
rg -q 'pointsInFrame' "$HARNESS" \
  || fail "orbital-plume framing evidence is missing"
rg -q 'plumeTailContrast' "$HARNESS" \
  || fail "orbital-plume framebuffer contrast evidence is missing"
rg -q 'GetActivePlumeSystem' "$HARNESS" \
  || fail "orbital-plume telemetry is not scoped to the active vessel renderer"
rg -q '_orbitalPlumeStableFrames >= 3' "$HARNESS" \
  || fail "orbital-plume capture does not require a stable delivered burn"
rg -Fq 'Finish("ORBITAL_PLUME_NOT_READY")' "$HARNESS" \
  || fail "orbital-plume readiness wait has no fail-closed timeout"
rg -q 'float axialEnv' "$ROOT/assets/shaders/raptor_plume.gdshader" \
  || fail "shader has no axial intensity falloff"
rg -q 'float sheathAlpha' "$ROOT/assets/shaders/raptor_plume.gdshader" \
  || fail "shader has no separate transparent sheath"

TEST_DIR="$(mktemp -d /tmp/exo_orbital_plume_contract.XXXXXX)"
trap 'rm -rf "$TEST_DIR"' EXIT
OUT_DIR="$TEST_DIR/out"
mkdir -p "$OUT_DIR"

python3 - "$OUT_DIR/exo_play_orbital_plume.png" <<'PY'
import struct
import sys
import zlib

path = sys.argv[1]
width, height = 1920, 1080
raw = b"".join(
    b"\x00" + b"".join(
        bytes((18 + ((x + y) % 32), 30 + ((x * 3 + y) % 40),
               58 + ((x + y * 2) % 64), 255))
        for x in range(width)
    )
    for y in range(height)
)

def chunk(kind, payload):
    return (
        struct.pack(">I", len(payload)) + kind + payload
        + struct.pack(">I", zlib.crc32(kind + payload) & 0xffffffff)
    )

png = (
    b"\x89PNG\r\n\x1a\n"
    + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    + chunk(b"IDAT", zlib.compress(raw, 1))
    + chunk(b"IEND", b"")
)
with open(path, "wb") as stream:
    stream.write(png)
PY

good="$TEST_DIR/good.log"
printf '%s\n' \
  '=== Exosphere visual playtest fixture mode=orbital_plume ===' \
  'VISUAL_ORBITAL_PLUME slug=orbital_plume body=earth altitudeM=196900.0 geocentricAltitudeM=200000.0 pressureRatio=0.0000 expansion=1.000 deliveredThrottle=1.000 farField=False visibleUnits=6 anchoredUnits=6 longestLengthRender=21.00 coreVisible=True sheathVisible=True interactionParticles=False coreOpacity=0.480 sheathOpacity=0.120 projectedPoints=12 pointsInFrame=12 plumeTailLuma=0.05500 plumeBackdropLuma=0.01000 plumeTailContrast=0.04500 cameraMode=Chase cameraDistanceRender=34.00 rendererVisible=True hudVisible=True imageWidth=1920 imageHeight=1080' \
  'IMAGE slug=orbital_plume width=1920 height=1080 mean=0.18000 darkFrac=0.24000 clippedFrac=0.00100' \
  'SUMMARY reason=ORBITAL_PLUME_OK frames=120' > "$good"

bash "$HARNESS" --orbital-plume --verify-only \
  --out-dir "$OUT_DIR" --log "$good" >/dev/null \
  || fail "valid orbital-plume fixture was rejected"

bad="$TEST_DIR/bad.log"
python3 - "$good" "$bad" <<'PY'
import sys

source, target = sys.argv[1], sys.argv[2]
text = open(source, encoding="utf-8").read()
text = text.replace("anchoredUnits=6", "anchoredUnits=5")
open(target, "w", encoding="utf-8").write(text)
PY

if bash "$HARNESS" --orbital-plume --verify-only \
    --out-dir "$OUT_DIR" --log "$bad" >/dev/null 2>&1; then
  fail "anchor regression fixture was accepted"
fi

zero_throttle="$TEST_DIR/zero-throttle.log"
python3 - "$good" "$zero_throttle" <<'PY'
import sys

source, target = sys.argv[1], sys.argv[2]
text = open(source, encoding="utf-8").read()
text = text.replace("deliveredThrottle=1.000", "deliveredThrottle=0.000")
open(target, "w", encoding="utf-8").write(text)
PY

if bash "$HARNESS" --orbital-plume --verify-only \
    --out-dir "$OUT_DIR" --log "$zero_throttle" >/dev/null 2>&1; then
  fail "zero-throttle orbital-plume fixture was accepted"
fi

invisible="$TEST_DIR/invisible.log"
python3 - "$good" "$invisible" <<'PY'
import sys

source, target = sys.argv[1], sys.argv[2]
text = open(source, encoding="utf-8").read()
text = text.replace("plumeTailLuma=0.05500", "plumeTailLuma=0.03900")
text = text.replace("plumeTailContrast=0.04500", "plumeTailContrast=0.02400")
open(target, "w", encoding="utf-8").write(text)
PY

if bash "$HARNESS" --orbital-plume --verify-only \
    --out-dir "$OUT_DIR" --log "$invisible" >/dev/null 2>&1; then
  fail "framebuffer-invisible orbital-plume fixture was accepted"
fi

echo "orbital_plume_visual_contract_test: PASS (stable burn, vacuum state, layer anchor, framing, image gate)"
