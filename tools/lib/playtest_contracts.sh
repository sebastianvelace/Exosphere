#!/usr/bin/env bash

# Log-only acceptance contracts shared by the live harness and fast synthetic tests.
# Keep these functions free of repository or Godot state so preserved artifacts can be
# re-verified with --verify-only after a run or agent interruption.

playtest_log_field() {
  local line="$1"
  local key="$2"
  awk -v key="$key" '
    {
      for (i = 1; i <= NF; i++) {
        prefix = key "="
        if (index($i, prefix) == 1) {
          print substr($i, length(prefix) + 1)
          exit
        }
      }
    }
  ' <<<"$line"
}

verify_ascent_log_contract() {
  local log="$1"
  local expected_summary="${2:-ASCENT_ORBIT_OK}"
  local require_starship_stage_contract="${3:-0}"

  if [[ ! -s "$log" ]]; then
    echo "ERROR: ascent telemetry log is missing or empty: $log" >&2
    return 1
  fi
  if ! cmp -s "$log" <(tr -d '\000' < "$log"); then
    echo "ERROR: ascent telemetry contains NUL bytes (concurrent writer/truncation)" >&2
    return 1
  fi
  local header_count summary_count
  header_count="$(grep -c '^=== Exosphere visual playtest ' "$log" || true)"
  summary_count="$(grep -c '^SUMMARY reason=' "$log" || true)"
  if (( header_count != 1 || summary_count != 1 )); then
    echo "ERROR: ascent telemetry has multiple/missing run boundaries (headers=$header_count summaries=$summary_count)" >&2
    return 1
  fi
  if grep -Eq '^(FAIL|FALLBACK|GAP) ' "$log"; then
    echo "ERROR: ascent telemetry contains FAIL/FALLBACK/GAP evidence" >&2
    return 1
  fi
  if ! grep -Eq "^SUMMARY reason=${expected_summary}([[:space:]]|$)" "$log"; then
    echo "ERROR: ascent did not finish with SUMMARY reason=${expected_summary}" >&2
    return 1
  fi

  local trace_count
  trace_count="$(grep -c '^TRACE_ASCENT ' "$log" || true)"
  if (( trace_count < 5 )); then
    echo "ERROR: ascent needs at least five TRACE_ASCENT samples (found $trace_count)" >&2
    return 1
  fi
  if grep '^TRACE_ASCENT ' "$log" | grep -qE ' finite=False| destroyed=True| structuralLost=True'; then
    echo "ERROR: ascent telemetry contains a non-finite, destroyed or control-lost state" >&2
    return 1
  fi
  if ! grep -Eq '^TRANSITION_ASCENT .*guidance=Coast([[:space:]]|$)' "$log"; then
    echo "ERROR: ascent telemetry never observed the ballistic Coast phase" >&2
    return 1
  fi
  if ! grep -Eq '^TRANSITION_ASCENT .*guidance=Insert([[:space:]]|$)' "$log"; then
    echo "ERROR: ascent telemetry never observed the orbital Insert phase" >&2
    return 1
  fi
  if grep -q 'invariant=insert_to_coast' "$log"; then
    echo "ERROR: insertion regressed to coast after being latched" >&2
    return 1
  fi

  if [[ "$require_starship_stage_contract" == "1" ]]; then
    if ! verify_starship_stage_telemetry_contract "$log"; then
      return 1
    fi
  fi

  local orbit_line
  orbit_line="$(grep '^CAPTURE orbit ' "$log" | tail -1 || true)"
  if [[ -z "$orbit_line" ]]; then
    echo "ERROR: ascent telemetry has no orbit capture" >&2
    return 1
  fi

  local periapsis atmosphere_top
  periapsis="$(playtest_log_field "$orbit_line" pe)"
  atmosphere_top="$(playtest_log_field "$orbit_line" atmoTop)"
  if [[ ! "$periapsis" =~ ^-?[0-9]+([.][0-9]+)?$ \
    || ! "$atmosphere_top" =~ ^[0-9]+([.][0-9]+)?$ ]]; then
    echo "ERROR: orbit capture has non-numeric pe/atmoTop evidence" >&2
    return 1
  fi
  if ! awk -v pe="$periapsis" -v atmo="$atmosphere_top" \
      'BEGIN { exit !(pe >= atmo) }'; then
    echo "ERROR: orbit milestone is suborbital (pe=$periapsis m, atmosphere=$atmosphere_top m)" >&2
    return 1
  fi
}

# Flight 7/12 must expose the physical active stage, not a hard-coded Ship lookup.
# The harness emits one ENGINE_STAGE row whenever the active engine pool changes.  This
# gate checks the state-qualified selected/lit split and the expected 33 -> 39 -> 6
# sequence without requiring a particular frame cadence during the 1.5 s overlap window.
verify_starship_stage_telemetry_contract() {
  local log="$1"
  awk '
    function value(prefix,    i, pair) {
      for (i = 1; i <= NF; i++)
        if ($i ~ ("^" prefix "=")) {
          split($i, pair, "=")
          return pair[2]
        }
      return ""
    }
    function integer(value) {
      return value ~ /^[0-9]+$/ ? value + 0 : -1
    }
    function reject(message) {
      print "ERROR: stage telemetry gate: " message > "/dev/stderr"
      bad = 1
    }
    /^ENGINE_STAGE / {
      stage = value("stage")
      selected = integer(value("selected"))
      lit = integer(value("lit"))
      ramp = integer(value("ramp"))
      residual = integer(value("residual"))
      failed = integer(value("failed"))
      if (stage == "" || selected < 0 || lit < 0 || ramp < 0 || residual < 0 || failed < 0) {
        reject("malformed ENGINE_STAGE row: " $0)
        next
      }
      if (lit > selected)
        reject(stage " reports lit=" lit " above selected=" selected)
      if (ramp > lit)
        reject(stage " reports ramp=" ramp " above lit=" lit)
      if (residual > selected)
        reject(stage " reports residual=" residual " above selected=" selected)
      if (failed > selected)
        reject(stage " reports failed=" failed " above selected=" selected)

      if (stage == "booster" && !booster) {
        if (selected != 33) reject("Super Heavy selected count=" selected " (expected 33)")
        booster = 1
        order[++seen] = stage
      } else if (stage == "hotstage" && !hotstage) {
        if (!booster) reject("hot-stage overlap appeared before booster stage")
        if (selected != 39) reject("hot-stage selected count=" selected " (expected 39)")
        hotstage = 1
        order[++seen] = stage
      } else if (stage == "ship" && !ship) {
        if (!hotstage) reject("Ship stage appeared before hot-stage overlap")
        if (selected != 6) reject("Ship selected count=" selected " (expected 6)")
        ship = 1
        order[++seen] = stage
      }
    }
    END {
      if (!booster) reject("missing booster ENGINE_STAGE row")
      if (!hotstage) reject("missing hotstage ENGINE_STAGE row")
      if (!ship) reject("missing ship ENGINE_STAGE row")
      if (seen == 3 && (order[1] != "booster" || order[2] != "hotstage" || order[3] != "ship"))
        reject("stage order was " order[1] " -> " order[2] " -> " order[3])
      exit bad
    }
  ' "$log"
}
