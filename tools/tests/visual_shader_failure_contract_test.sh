#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
scratch="$(mktemp -d /tmp/exo_shader_gate.XXXXXX)"
trap 'rm -rf "$scratch"' EXIT
python3 - "$ROOT/tools/visual_playtest.sh" "$scratch/check.sh" <<'PYEXTRACT'
from pathlib import Path
import sys
s = Path(sys.argv[1]).read_text()
a = s.index('verify_post_run_contracts() {')
b = s.index('\n}\n', a) + 3
Path(sys.argv[2]).write_text("""#!/usr/bin/env bash
set -euo pipefail
CONSOLE_LOG="$1"
LOG="$2"
RENDERER=compatibility
SUN_ELEVATION_SET=0
CAMERA_PRESET=""
MODE=smoke
""" + s[a:b] + '\nverify_post_run_contracts\n')
PYEXTRACT
printf '%s\n' 'RENDERER_ACTUAL gl_compatibility' > "$scratch/state.log"
printf '%s\n' 'WARNING: invalid UID; falling back to text path' 'ALSA: no audio device' > "$scratch/console.log"
bash "$scratch/check.sh" "$scratch/console.log" "$scratch/state.log"
for diagnostic in 'SHADER ERROR: Unknown identifier' 'ERROR: Shader compilation failed.' 'SCRIPT ERROR: Invalid call'; do
  printf '%s\n' "$diagnostic" > "$scratch/console.log"
  if bash "$scratch/check.sh" "$scratch/console.log" "$scratch/state.log" > "$scratch/output.log" 2>&1; then
    echo "FAIL rendering failure accepted: $diagnostic" >&2
    exit 1
  fi
done
echo "visual_shader_failure_contract_test: PASS (benign diagnostics accepted; shader/script failures rejected)"
