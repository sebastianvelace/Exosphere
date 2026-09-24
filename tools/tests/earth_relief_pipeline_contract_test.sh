#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "earth_relief_pipeline_contract_test: FAIL: $*" >&2; exit 1; }

manifest="$ROOT/data/provenance/earth_etopo2022_relief.json"
pipeline="$ROOT/tools/prepare_earth_relief.sh"
fallback="$ROOT/tools/prepare_earth_relief.py"
asset="$ROOT/assets/textures/earth_etopo2022_4k_height.png"

[[ -f "$manifest" ]] || fail "Earth relief provenance manifest is missing"
[[ -f "$pipeline" ]] || fail "Earth relief conversion pipeline is missing"
[[ -f "$fallback" ]] || fail "Python conversion fallback is missing"
[[ -f "$asset" ]] || fail "derived Earth relief texture is missing"

rg -q '"name": "ETOPO 2022 Global Relief Model"' "$manifest" \
  || fail "ETOPO source identity is missing"
rg -q '"license": "CC0-1.0"' "$manifest" \
  || fail "ETOPO license is not recorded"
rg -q '"source_resolution_arc_seconds": 60' "$manifest" \
  || fail "selected source resolution is not recorded"
rg -q '"width": 4096' "$manifest" \
  || fail "derived width is not recorded"
rg -q '"height": 2048' "$manifest" \
  || fail "derived height is not recorded"
rg -q '"source_sha256": "a2cc72f8a4292dee928f439069457cdefc1fba319876807c626edce40258ba7a"' "$manifest" \
  || fail "source checksum is not recorded"
rg -q '"sha256": "d35764f899575def469ec93b5d5fa255ad9eab80cad4dfb7183c212e65a4b37c"' "$manifest" \
  || fail "derived asset checksum is not recorded"
rg -q '"physics_authority": false' "$manifest" \
  || fail "relief is not explicitly visual-only"
rg -q 'earth shader uses longitude=atan2\(z,x\)' "$manifest" \
  || fail "Earth shader coordinate convention is not documented"

bash -n "$pipeline" || fail "conversion pipeline has invalid shell syntax"
python3 -m py_compile "$fallback" || fail "Python conversion fallback has invalid syntax"
file "$asset" | rg -q '4096 x 2048, 16-bit grayscale' \
  || fail "derived texture is not a 4096x2048 16-bit grayscale PNG"
rg -q 'gdal_translate' "$pipeline" \
  || fail "pipeline does not use GDAL conversion"
rg -q 'prepare_earth_relief.py' "$pipeline" \
  || fail "pipeline has no unprivileged conversion fallback"
rg -q -- '-ot UInt16' "$pipeline" \
  || fail "pipeline does not preserve uint16 elevation precision"
rg -q -- '-outsize 4096 2048' "$pipeline" \
  || fail "pipeline output dimensions are not bounded"
rg -q -- '-scale -11000 9000 0 65535' "$pipeline" \
  || fail "pipeline elevation range is not explicit"
rg -q 'sha256sum' "$pipeline" \
  || fail "pipeline does not expose source/output checksums"
rg -q 'np\.isfinite' "$fallback" \
  || fail "fallback does not reject non-finite elevation values"

if rg --files "$ROOT/assets" | rg -i '(etopo|earth_etopo).*(\.tif|\.tiff|\.nc)$' >/dev/null; then
  fail "raw global ETOPO data must not be committed under assets"
fi

echo "earth_relief_pipeline_contract_test: PASS (ETOPO provenance and bounded offline conversion)"
