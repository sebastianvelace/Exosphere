#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VISUAL="$ROOT/tools/visual_playtest.sh"
CONTRACTS="$ROOT/tools/lib/playtest_contracts.sh"
BRIDGE="$ROOT/scripts/SimulationBridge.cs"
MAP="$ROOT/scripts/MapViewController.cs"
SPECTRAL="$ROOT/tools/SpectralValidation/Program.cs"
GPU="$ROOT/tools/perf/texture_gpu_matrix.sh"
TMP="$(mktemp -d /tmp/exo_optimization_phase23_contract.XXXXXX)"
trap 'rm -rf "$TMP"' EXIT

pass_count=0
fail_count=0

pass_gate() {
  echo "PASS $1"
  pass_count=$((pass_count + 1))
}

fail_gate() {
  echo "FAIL $1" >&2
  fail_count=$((fail_count + 1))
}

expect_failure() {
  local name="$1"
  shift
  if "$@" >/dev/null 2>&1; then
    echo "FAIL invalid fixture accepted: $name" >&2
    exit 1
  fi
  echo "PASS invalid fixture rejected: $name"
}

require_text() {
  local file="$1" pattern="$2" description="$3"
  if rg -q --fixed-strings -- "$pattern" "$file"; then
    pass_gate "$description"
  else
    fail_gate "$description (missing '$pattern' in ${file#$ROOT/})"
  fi
}

bash -n "$VISUAL" "$CONTRACTS" "$GPU" \
  "$ROOT/tools/tests/optimization_phase23_contract_test.sh"

require_text "$VISUAL" '--ascent' 'visual ascent mode is declared'
require_text "$VISUAL" '--edl' 'visual EDL/catch mode is declared'
require_text "$VISUAL" '--saturn' 'visual Saturn jump mode is declared'
require_text "$VISUAL" '--atmosphere' 'visual atmosphere matrix mode is declared'
require_text "$VISUAL" '--atmosphere-bodies' 'Mars/Venus atmosphere matrix mode is declared'
require_text "$VISUAL" 'ATMOSPHERE_BODIES_OK' 'Mars/Venus atmosphere terminal state is gated'
require_text "$VISUAL" 'mars_10km_day' 'Mars low-altitude day case is declared'
require_text "$VISUAL" 'mars_400km_day' 'Mars orbital day case is declared'
require_text "$VISUAL" 'mars_10km_night' 'Mars night case is declared'
require_text "$VISUAL" 'venus_10km_day' 'Venus low-altitude day case is declared'
require_text "$VISUAL" 'venus_400km_day' 'Venus orbital day case is declared'
require_text "$VISUAL" 'venus_10km_night' 'Venus night case is declared'
require_text "$VISUAL" '--spectral' 'offline spectral mode is declared'
require_text "$VISUAL" '--verify-only' 'visual artifacts have a reproducible verify-only path'
require_text "$VISUAL" 'SUMMARY reason=CAUGHT' 'catch terminal state is gated'
require_text "$VISUAL" 'CHECK tower_catch caught=True' 'catch has physical contact telemetry'
require_text "$VISUAL" 'SUMMARY reason=SATURN_OK' 'Saturn gate has a terminal state'
require_text "$VISUAL" 'ATMOS_STATE' 'atmosphere gate records physical state'
require_text "$VISUAL" 'SPECTRAL_ORACLE' 'atmosphere matrix records spectral provenance'
require_text "$MAP" 'case Key.J when Visible && _selectedTarget != null:' 'J input is target-gated'
require_text "$BRIDGE" 'CancelGuidanceForTeleport();' 'body jump cancels stale guidance'
require_text "$BRIDGE" 'v.PrepareForTeleport();' 'body jump clears rigid-body state'
require_text "$BRIDGE" 'FindCatchAnchorVessel()' 'tower presentation uses indexed catch-anchor lookup'
require_text "$BRIDGE" 'HasCatchApproach(padEarth.Id)' 'tower visibility uses indexed catch-approach lookup'
require_text "$BRIDGE" 'for (int vesselIndex = 0;' 'tower target refresh avoids interface enumeration'
require_text "$BRIDGE" 'HasStarshipRole(catchAnchorVessel, "command")' 'tower visibility uses bounded role scan'
require_text "$BRIDGE" 'private SphereMesh? _planetSphereMesh;' 'planet presentation mesh cache is declared'
require_text "$BRIDGE" 'GetSharedPlanetSphereMesh()' 'planet presentation reuses shared geometry'
require_text "$BRIDGE" 'var sphere = GetSharedPlanetSphereMesh();' \
  'lazy planet spawn uses shared geometry'
require_text "$BRIDGE" 'mesh_cache=created radial=96 rings=48 shared=True' \
  'planet mesh cache telemetry is explicit'
require_text "$SPECTRAL" 'decision=order4-official-order5-diagnostic' \
  'spectral promotion decision remains explicit'
require_text "$ROOT/ExosphereSimulation/Universe.cs" '_bodiesView = _bodies.AsReadOnly();' \
  'Universe caches the bodies read-only view'
require_text "$ROOT/ExosphereSimulation/Universe.cs" 'public IReadOnlyList<CelestialBody> Bodies  => _bodiesView;' \
  'Universe exposes the stable bodies view'
require_text "$ROOT/ExosphereSimulation.Tests/PerformanceAcceptanceTests.cs" \
  'UniverseCollectionViewsAreStableAndAllocationFreeAfterConstruction' \
  'Universe collection view allocation regression is covered'
if rg -q --fixed-strings 'ActiveEngines.Any(' "$ROOT/ExosphereSimulation/Universe.cs"; then
  fail_gate 'Universe scheduler still enumerates ActiveEngines through LINQ'
else
  pass_gate 'Universe scheduler uses the concrete active-engine buffer'
fi
require_text "$GPU" 'physical_gpu_gate' 'GPU matrix records physical-adapter status'
require_text "$GPU" 'software_renderer_observed' 'GPU matrix records software-renderer evidence'
require_text "$GPU" 'final_status=BLOCKED; physical_gate=BLOCKED' \
  'GPU matrix fails closed without physical evidence'

source "$CONTRACTS"

write_ascent_log() {
  local target="$1"
  {
    echo '=== Exosphere visual playtest fixture mode=ascent ==='
    for t in 1 2 3 4 5; do
      echo "TRACE_ASCENT t=$t guidance=Coast finite=True destroyed=False structuralLost=False"
    done
    echo 'TRANSITION_ASCENT t=6 from=Ascent guidance=Coast'
    echo 'TRANSITION_ASCENT t=7 from=Coast guidance=Insert'
    echo 'CAPTURE orbit alt=200000 pe=150000 atmoTop=140000'
    echo 'SUMMARY reason=ASCENT_ORBIT_OK'
  } > "$target"
}

ascent_good="$TMP/ascent-good.log"
write_ascent_log "$ascent_good"
verify_ascent_log_contract "$ascent_good"
pass_gate 'ascent valid fixture accepted'

ascent_bad="$TMP/ascent-bad.log"
sed '/guidance=Insert/d' "$ascent_good" > "$ascent_bad"
expect_failure 'ascent missing insertion transition' verify_ascent_log_contract "$ascent_bad"

validate_edl_log() {
  local log="$1"
  [[ -s "$log" ]] || return 1
  ! grep -Eq '^(FAIL|GAP) ' "$log" || return 1
  if grep -q '^SUMMARY reason=CAUGHT' "$log"; then
    grep -Eq '^CHECK tower_catch caught=True pins=[2-9][0-9]* relativeSpeed=[0-9.]+ angularSpeed=[0-9.]+' "$log"
    return
  fi
  if grep -q '^SUMMARY reason=LANDED' "$log"; then
    grep -Eq '^CAPTURE touchdown .*contacts=[3-9][0-9]* .*settled=True' "$log"
    return
  fi
  return 1
}

edl_good="$TMP/edl-good.log"
printf '%s\n' \
  'CAPTURE entry alt=70000' \
  'CAPTURE retro_burn alt=15000' \
  'CAPTURE flip_complete alt=12000' \
  'CHECK tower_catch caught=True pins=2 relativeSpeed=0.20 angularSpeed=0.01' \
  'SUMMARY reason=CAUGHT' > "$edl_good"
validate_edl_log "$edl_good"
pass_gate 'EDL/catch valid dual-contact fixture accepted'

edl_bad="$TMP/edl-bad.log"
sed 's/pins=2/pins=1/' "$edl_good" > "$edl_bad"
expect_failure 'EDL/catch single pin rejected' validate_edl_log "$edl_bad"

validate_saturn_log() {
  local log="$1"
  grep -q '^SUMMARY reason=SATURN_OK' "$log" || return 1
  awk '
    /^IMAGE slug=saturn_ring / {
      for (i = 1; i <= NF; i++) {
        if ($i ~ /^mean=/) { split($i, p, "="); mean = p[2] + 0 }
        if ($i ~ /^p95=/) { split($i, p, "="); p95 = p[2] + 0 }
      }
      found = 1
    }
    END { exit !(found && mean > 0.02 && p95 > 0.20) }
  ' "$log"
}

saturn_good="$TMP/saturn-good.log"
printf '%s\n' \
  'IMAGE slug=saturn_ring mean=0.08 p95=0.42' \
  'SUMMARY reason=SATURN_OK' > "$saturn_good"
validate_saturn_log "$saturn_good"
pass_gate 'Saturn body/ring valid fixture accepted'

saturn_bad="$TMP/saturn-bad.log"
sed 's/p95=0.42/p95=0.02/' "$saturn_good" > "$saturn_bad"
expect_failure 'Saturn out-of-frame/low-signal fixture rejected' validate_saturn_log "$saturn_bad"

validate_atmosphere_log() {
  local log="$1"
  awk '
    function value(key,    i, pair) {
      for (i = 1; i <= NF; i++) if ($i ~ ("^" key "=")) {
        split($i, pair, "="); return pair[2]
      }
      return ""
    }
    function abs(v) { return v < 0 ? -v : v }
    function reject() { bad = 1 }
    /^ATMOS_APPLY / {
      slug = value("slug")
      targetAlt[slug] = value("targetAlt") + 0
      targetSun[slug] = value("targetSunElevation") + 0
      requested[slug] = 1
    }
    /^ATMOS_STATE / {
      slug = value("slug")
      if (!(slug in requested)) { reject(); next }
      actualAlt[slug] = value("actualAlt") + 0
      actualSun[slug] = value("sunElevation") + 0
      visibility[slug] = value("eclipseVisibility") + 0
      runtime[slug] = value("solarVisibility") + 0
      if (abs(actualAlt[slug] - targetAlt[slug]) > 2.0) reject()
      if (abs(actualSun[slug] - targetSun[slug]) > 0.25) reject()
      if (visibility[slug] < -1e-6 || visibility[slug] > 1.000001) reject()
      if (abs(visibility[slug] - runtime[slug]) > 0.05) reject()
      if (value("exposureSettled") != "True") reject()
      seen[slug] = 1
    }
    /^SUMMARY reason=ATMOSPHERE_OK$/ { summary = 1 }
    END {
      for (slug in requested) if (!(slug in seen)) reject()
      if (!summary) reject()
      if (!("eclipse_clear" in visibility)) reject()
      if (!("eclipse_partial_central" in visibility)) reject()
      if (!("eclipse_partial_limb" in visibility)) reject()
      if (!("eclipse_total" in visibility)) reject()
      if (!(visibility["eclipse_clear"] > visibility["eclipse_partial_limb"])) reject()
      if (!(visibility["eclipse_partial_limb"] > visibility["eclipse_partial_central"])) reject()
      if (!(visibility["eclipse_partial_central"] > visibility["eclipse_total"])) reject()
      if (visibility["eclipse_clear"] < 0.999 || visibility["eclipse_total"] > 0.02) reject()
      exit bad
    }
  ' "$log"
}

atmos_good="$TMP/atmos-good.log"
{
  for row in \
    'eclipse_clear 120000 45 1.00' \
    'eclipse_partial_central 120000 45 0.40' \
    'eclipse_partial_limb 120000 45 0.70' \
    'eclipse_total 120000 45 0.00'; do
    read -r slug altitude sun visibility <<< "$row"
    echo "ATMOS_APPLY slug=$slug targetAlt=$altitude targetSunElevation=$sun"
    echo "ATMOS_STATE slug=$slug actualAlt=$altitude sunElevation=$sun solarVisibility=$visibility eclipseVisibility=$visibility exposureSettled=True"
  done
  echo 'SUMMARY reason=ATMOSPHERE_OK'
} > "$atmos_good"
validate_atmosphere_log "$atmos_good"
pass_gate 'atmosphere/eclipse valid fixture accepted'

atmos_bad="$TMP/atmos-bad.log"
sed 's/eclipse_partial_central actualAlt=120000/eclipse_partial_central actualAlt=120010/' \
  "$atmos_good" > "$atmos_bad"
expect_failure 'atmosphere target/state mismatch rejected' validate_atmosphere_log "$atmos_bad"

validate_atmosphere_bodies_log() {
  local log="$1"
  awk '
    function value(key,    i, pair) {
      for (i = 1; i <= NF; i++) if ($i ~ ("^" key "=")) {
        split($i, pair, "="); return pair[2]
      }
      return ""
    }
    function abs(v) { return v < 0 ? -v : v }
    function finite(v) { return v != "" && v == v && v !~ /^(nan|NaN|inf|Inf|-inf|-Inf)$/ }
    function reject(message) { print "ERROR body fixture: " message > "/dev/stderr"; bad = 1 }
    BEGIN {
      expected["mars_10km_day"] = "mars"; expected["mars_400km_day"] = "mars"
      expected["mars_10km_night"] = "mars"
      expected["venus_10km_day"] = "venus"; expected["venus_400km_day"] = "venus"
      expected["venus_10km_night"] = "venus"
    }
    /^ATMOS_APPLY / {
      slug = value("slug"); body = value("body")
      if (!(slug in expected) || body != expected[slug]) reject("apply identity " slug)
      targetAlt[slug] = value("targetAlt") + 0
      targetSun[slug] = value("targetSunElevation") + 0
      requested[slug]++
    }
    /^ATMOS_STATE / {
      slug = value("slug"); body = value("body")
      if (!(slug in expected) || body != expected[slug]) reject("state identity " slug)
      actualAlt = value("actualAlt"); actualSun = value("sunElevation")
      solar = value("solarVisibility"); energy = value("spectralEnergy")
      if (!finite(actualAlt) || !finite(actualSun) || !finite(solar) || !finite(energy))
        reject("non-finite state " slug)
      if (abs(actualAlt + 0 - targetAlt[slug]) > 2.0) reject("altitude " slug)
      if (abs(actualSun + 0 - targetSun[slug]) > 0.25) reject("solar elevation " slug)
      if (value("eclipse") != "none" || value("exposureSettled") != "True") reject("optics state " slug)
      seen[slug]++
    }
    /^IMAGE / {
      slug = value("slug")
      if (!(slug in expected)) reject("unexpected image identity " slug)
      image[slug]++
    }
    /^SUMMARY reason=ATMOSPHERE_BODIES_OK([[:space:]]|$)/ { summary++ }
    END {
      for (slug in expected) {
        if (requested[slug] != 1 || seen[slug] != 1 || image[slug] != 1)
          reject("missing/duplicate apply/state/image " slug)
      }
      if (summary != 1) reject("summary")
      exit bad
    }
  ' "$log"
}

atmos_bodies_good="$TMP/atmos-bodies-good.log"
{
  for row in \
    'mars mars_10km_day 10000 35 1.0 1.2E-3' \
    'mars mars_400km_day 400000 35 1.0 0.0E+0' \
    'mars mars_10km_night 10000 -35 1.0 1.0E-4' \
    'venus venus_10km_day 10000 35 1.0 2.1E-2' \
    'venus venus_400km_day 400000 35 1.0 0.0E+0' \
    'venus venus_10km_night 10000 -35 1.0 1.0E-4'; do
    read -r body slug altitude sun visibility energy <<< "$row"
    echo "ATMOS_APPLY body=$body slug=$slug targetAlt=$altitude targetSunElevation=$sun cockpit=False eclipse=none"
    echo "IMAGE slug=$slug mean=0.10 clippedFrac=0.01"
    echo "ATMOS_STATE body=$body slug=$slug actualAlt=$altitude sunElevation=$sun solarVisibility=$visibility eclipse=none eclipseVisibility=1.000000 spectralEnergy=$energy exposureSettled=True"
  done
  echo 'SUMMARY reason=ATMOSPHERE_BODIES_OK frames=709'
} > "$atmos_bodies_good"
validate_atmosphere_bodies_log "$atmos_bodies_good"
pass_gate 'Mars/Venus atmosphere valid fixture accepted'

atmos_bodies_bad="$TMP/atmos-bodies-bad.log"
sed 's/ATMOS_STATE body=venus slug=venus_10km_night actualAlt=10000/ATMOS_STATE body=mars slug=venus_10km_night actualAlt=10000/' \
  "$atmos_bodies_good" > "$atmos_bodies_bad"
expect_failure 'Mars/Venus body identity mismatch rejected' validate_atmosphere_bodies_log "$atmos_bodies_bad"

validate_spectral_log() {
  local log="$1" line finite monotonic order4 decision
  line="$(grep '^SPECTRAL_SUMMARY ' "$log" | tail -n 1)"
  [[ -n "$line" ]] || return 1
  finite="$(sed -n 's/.*finite=\([^ ]*\).*/\1/p' <<< "$line")"
  monotonic="$(sed -n 's/.*monotonic=\([^ ]*\).*/\1/p' <<< "$line")"
  order4="$(sed -n 's/.*order4NoWorse=\([^ ]*\).*/\1/p' <<< "$line")"
  decision="$(sed -n 's/.*decision=\([^ ]*\).*/\1/p' <<< "$line")"
  [[ "$finite" == True && "$monotonic" == True ]] || return 1
  [[ "$decision" == order4-official-order5-diagnostic ]] || return 1
  [[ "$order4" == True || "$order4" == False ]] || return 1
}

spectral_good="$TMP/spectral-good.log"
printf '%s\n' \
  'SPECTRAL_SUMMARY finite=True monotonic=True order4NoWorse=False officialOrder=4 experimentalOrder=5 decision=order4-official-order5-diagnostic' \
  > "$spectral_good"
validate_spectral_log "$spectral_good"
pass_gate 'spectral finite/monotonic diagnostic fixture accepted'

spectral_bad="$TMP/spectral-bad.log"
sed 's/finite=True/finite=False/' "$spectral_good" > "$spectral_bad"
expect_failure 'spectral non-finite fixture rejected' validate_spectral_log "$spectral_bad"

j_good="$TMP/j-good.cs"
cp "$BRIDGE" "$j_good"
check_jump_source() {
  local source="$1"
  rg -q --fixed-strings 'CancelGuidanceForTeleport();' "$source" \
    && rg -q --fixed-strings 'v.PrepareForTeleport();' "$source" \
    && rg -q --fixed-strings 'case Key.J when Visible && _selectedTarget != null:' "$MAP"
}
check_jump_source "$j_good"
pass_gate 'J/Saturn source fixture accepted'

j_bad="$TMP/j-bad.cs"
sed '/CancelGuidanceForTeleport();/d' "$j_good" > "$j_bad"
expect_failure 'J/Saturn stale-guidance source fixture rejected' check_jump_source "$j_bad"

write_gpu_fixture() {
  local dir="$1" status="$2" physical="$3" software="$4"
  mkdir -p "$dir"
  printf '%s\n' \
    'format_version=texture_gpu_matrix_v1' \
    "status=$status" \
    'source_commit=phase23-fixture' \
    'variant_count=4' \
    'display_mode=xvfb' \
    'rendering_driver_request=opengl3' \
    'rendering_method_request=gl_compatibility' \
    'resolution=1920x1080' \
    'frames_requested=20' \
    'visual_mode=none' \
    "physical_gpu_gate=$physical" \
    "software_renderer_observed=$software" > "$dir/matrix.meta"
  printf '%s\n' \
    $'variant_id\tmipmaps\tsize_limit\tstatus\timport_cache_bytes\tprobe_status\tprobe_report\tvisual_status\tvisual_summary\tnonsoftware_adapter' \
    $'8k_nomip\tfalse\t0\tPASS\t1000\tMEASURED\tprobe\tNOT_RUN\tNOT_RUN\tfalse' \
    $'8k_mip\ttrue\t0\tPASS\t900\tMEASURED\tprobe\tNOT_RUN\tNOT_RUN\tfalse' \
    $'4k_mip\ttrue\t4096\tPASS\t800\tMEASURED\tprobe\tNOT_RUN\tNOT_RUN\tfalse' \
    $'2k_mip\ttrue\t2048\tPASS\t700\tMEASURED\tprobe\tNOT_RUN\tNOT_RUN\tfalse' \
    > "$dir/matrix.rows.tsv"
}

validate_gpu_fail_closed() {
  local meta="$1" status physical software
  status="$(sed -n 's/^status=//p' "$meta")"
  physical="$(sed -n 's/^physical_gpu_gate=//p' "$meta")"
  software="$(sed -n 's/^software_renderer_observed=//p' "$meta")"
  if [[ "$software" == true && ( "$status" != BLOCKED || "$physical" != BLOCKED ) ]]; then
    return 1
  fi
  if [[ "$physical" == PASS && "$status" != PASS ]]; then
    return 1
  fi
}

gpu_good="$TMP/gpu-good"
write_gpu_fixture "$gpu_good" BLOCKED BLOCKED true
bash "$GPU" --validate "$gpu_good" >/dev/null
validate_gpu_fail_closed "$gpu_good/matrix.meta"
pass_gate 'software-renderer GPU fixture remains BLOCKED'

gpu_bad="$TMP/gpu-bad"
cp -R "$gpu_good" "$gpu_bad"
sed -i -e 's/^status=BLOCKED/status=PASS/' -e 's/^physical_gpu_gate=BLOCKED/physical_gpu_gate=PASS/' \
  "$gpu_bad/matrix.meta"
expect_failure 'software-renderer GPU PASS fixture rejected' validate_gpu_fail_closed "$gpu_bad/matrix.meta"

echo "optimization_phase23_contract_test: summary pass=$pass_count fail=$fail_count"
if (( fail_count > 0 )); then
  exit 1
fi
