#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
OUT_DIR="${OUT_DIR:-/tmp/exo_spectral_validation}"

dotnet run --project tools/SpectralValidation/SpectralValidation.csproj \
  --no-restore -- "$OUT_DIR"
