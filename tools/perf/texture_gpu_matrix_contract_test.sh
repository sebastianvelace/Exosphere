#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HARNESS="$ROOT/tools/perf/texture_gpu_matrix.sh"
TEST_DIR="$(mktemp -d /tmp/exo_texture_gpu_matrix_contract.XXXXXX)"
trap 'rm -rf "$TEST_DIR"' EXIT

bash -n "$HARNESS" "$ROOT/tools/perf/texture_gpu_matrix_contract_test.sh"
bash "$HARNESS" --help >/dev/null

good="$TEST_DIR/good"
mkdir -p "$good"
cat > "$good/matrix.meta" <<'EOF'
format_version=texture_gpu_matrix_v1
status=BLOCKED
source_commit=0123456789abcdef0123456789abcdef01234567
variant_count=4
display_mode=xvfb
rendering_driver_request=opengl3
rendering_method_request=gl_compatibility
resolution=1920x1080
frames_requested=60
visual_mode=none
physical_gpu_gate=BLOCKED
software_renderer_observed=true
EOF
cat > "$good/matrix.rows.tsv" <<'EOF'
variant_id	mipmaps	size_limit	status	import_cache_bytes	probe_status	probe_report	visual_status	visual_summary	nonsoftware_adapter
8k_nomip	false	0	PASS	1000	MEASURED	8k_nomip/probe/gpu_probe.tsv	NOT_RUN	NOT_RUN	false
8k_mip	true	0	PASS	900	MEASURED	8k_mip/probe/gpu_probe.tsv	NOT_RUN	NOT_RUN	false
4k_mip	true	4096	PASS	700	MEASURED	4k_mip/probe/gpu_probe.tsv	NOT_RUN	NOT_RUN	false
2k_mip	true	2048	PASS	500	MEASURED	2k_mip/probe/gpu_probe.tsv	NOT_RUN	NOT_RUN	false
EOF
bash "$HARNESS" --validate "$good"
echo "PASS valid blocked matrix accepted"

expect_failure() {
  local name="$1" fixture="$2"
  if bash "$HARNESS" --validate "$fixture" >/dev/null 2>&1; then
    echo "FAIL invalid matrix fixture accepted: $name" >&2
    exit 1
  fi
  echo "PASS invalid matrix fixture rejected: $name"
}

bad_rows="$TEST_DIR/bad_rows"
cp -R "$good" "$bad_rows"
sed -i 's/^2k_mip\t/unknown\t/' "$bad_rows/matrix.rows.tsv"
expect_failure "unknown variant" "$bad_rows"

bad_count="$TEST_DIR/bad_count"
cp -R "$good" "$bad_count"
sed -i 's/^variant_count=4$/variant_count=3/' "$bad_count/matrix.meta"
expect_failure "wrong variant count" "$bad_count"

bad_format="$TEST_DIR/bad_format"
cp -R "$good" "$bad_format"
sed -i 's/^format_version=.*/format_version=other/' "$bad_format/matrix.meta"
expect_failure "unsupported format" "$bad_format"

echo "texture_gpu_matrix_contract_test: 1 valid and 3 invalid fixtures passed"
