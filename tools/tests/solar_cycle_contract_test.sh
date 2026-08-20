#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SUN="$ROOT/scripts/SunController.cs"
SITE_TESTS="$ROOT/ExosphereSimulation.Tests/LaunchSiteFrameTests.cs"
fail() { echo "solar_cycle_contract_test: FAIL: $*" >&2; exit 1; }

[[ -f "$SUN" ]] || fail "missing SunController"
[[ -f "$SITE_TESTS" ]] || fail "missing timed launch-site tests"

rg -q --fixed-strings 'public static double SolarElevationDegrees' "$SUN" \
  || fail "solar elevation telemetry is not exposed"
rg -q --fixed-strings 'public static string SolarPhase' "$SUN" \
  || fail "solar phase telemetry is not exposed"
rg -q --fixed-strings 'ClassifySolarPhase(double elevationDegrees)' "$SUN" \
  || fail "continuous elevation is not classified into twilight bands"
rg -q --fixed-strings 'elevationDegrees >= -6.0 ? "CIVIL_TWILIGHT"' "$SUN" \
  || fail "civil twilight threshold is missing"
rg -q --fixed-strings 'elevationDegrees >= -18.0 ? "ASTRONOMICAL_TWILIGHT"' "$SUN" \
  || fail "astronomical twilight threshold is missing"
rg -q --fixed-strings 'PERF_SOLAR_CYCLE time=' "$SUN" \
  || fail "solar-cycle telemetry is missing"
rg -q --fixed-strings 'universe.CurrentTime' "$SUN" \
  || fail "solar cycle is not tied to simulation time"
rg -q --fixed-strings 'TimeScale' "$SUN" \
  || fail "solar cycle telemetry does not expose time warp"
rg -q --fixed-strings 'TimedSurfacePositionMovesAtRotationalSurfaceVelocity' "$SITE_TESTS" \
  || fail "surface rotation regression test is missing"
rg -q --fixed-strings 'TimedSurfacePositionCompletesOneSiderealDay' "$SITE_TESTS" \
  || fail "one-sidereal-day rotation regression test is missing"

echo "solar_cycle_contract_test: PASS (simulation-time solar elevation, twilight bands and rotating surface coverage)"
