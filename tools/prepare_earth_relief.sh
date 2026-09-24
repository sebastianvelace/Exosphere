#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEFAULT_OUTPUT="$ROOT/assets/textures/earth_etopo2022_4k_height.png"

usage() {
  echo "Usage: $0 SOURCE_GEOTIFF [OUTPUT_PNG] [--check-only]" >&2
}

[[ $# -ge 1 ]] || { usage; exit 2; }

SOURCE="$1"
OUTPUT="$DEFAULT_OUTPUT"
CHECK_ONLY=false

shift
while [[ $# -gt 0 ]]; do
  case "$1" in
    --check-only)
      CHECK_ONLY=true
      shift
      ;;
    *)
      [[ "$OUTPUT" == "$DEFAULT_OUTPUT" ]] || { usage; exit 2; }
      OUTPUT="$1"
      shift
      ;;
  esac
done

[[ -f "$SOURCE" ]] || {
  echo "ERROR: source GeoTIFF not found: $SOURCE" >&2
  exit 1
}

echo "SOURCE=$SOURCE"
echo "SOURCE_SHA256=$(sha256sum "$SOURCE" | awk '{print $1}')"

if command -v gdalinfo >/dev/null 2>&1 && command -v gdal_translate >/dev/null 2>&1; then
  echo "CONVERTER=GDAL"
  echo "SOURCE_METADATA_BEGIN"
  gdalinfo "$SOURCE"
  echo "SOURCE_METADATA_END"

  if [[ "$CHECK_ONLY" == true ]]; then
    echo "EARTH_RELIEF_CHECK=PASS"
    exit 0
  fi

  mkdir -p "$(dirname "$OUTPUT")"
  gdal_translate \
    -of PNG \
    -ot UInt16 \
    -outsize 4096 2048 \
    -scale -11000 9000 0 65535 \
    -a_nodata none \
    "$SOURCE" "$OUTPUT"

  echo "OUTPUT=$OUTPUT"
  echo "OUTPUT_SHA256=$(sha256sum "$OUTPUT" | awk '{print $1}')"
  echo "EARTH_RELIEF_CONVERSION=PASS"
  exit 0
fi

command -v python3 >/dev/null 2>&1 || {
  echo "ERROR: GDAL or Python 3 is required for ETOPO conversion" >&2
  exit 1
}

echo "CONVERTER=Python-Pillow-NumPy"
python3 -c 'import numpy; from PIL import Image' >/dev/null 2>&1 || {
  echo "ERROR: fallback conversion requires Python packages Pillow and NumPy" >&2
  exit 1
}

if [[ "$CHECK_ONLY" == true ]]; then
  python3 "$ROOT/tools/prepare_earth_relief.py" "$SOURCE" "$OUTPUT" --check-only
else
  python3 "$ROOT/tools/prepare_earth_relief.py" "$SOURCE" "$OUTPUT"
fi
