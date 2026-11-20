# Flight 15 capture, orbital return, and integrated GPU

## Ship capture

The 70 km entry chapter reproduced a crash after both pins touched the cradle.
A descent-rate gate evaluated on each control step resumed thrust during compression.
The first two-pin contact became a single-pin contact as the ship moved beyond the
5 m footprint. The contact trace is opt-in via `EXOSPHERE_TRACE_CATCH=1`.

Guidance now checks both actual pin centres against that same footprint and measures
clearance along the cradle normal. A committed cutoff leaves the finite shutdown and
contact integrator in charge of the settling phase. The settle timer requires two pins
in contact, the existing relative-speed limits, and loads and compression inside
the existing limits. A static 200 t regression catches the previous false positive.
The V3 preset declares 2.5 MN/m per pin as an estimated effective compliance. At
225 t the two springs deflect about 0.44 m under static Earth gravity. The existing
0.60 m travel and 3 MN load limit per pin remain in effect.

The nominal seed reached `CAUGHT` at all three render cadences with two contacts.
The settled sample understates the impact. `Vessel.CatchPeakCompressionM` and
`CatchPeakPinLoadN` now keep the largest step-end values since the vessel entered
the 500 m catch gate, and the `CAUGHT` log reports both.

| Render cadence | Mass | Settled compression | Peak compression | Peak pin load |
| --- | ---: | ---: | ---: | ---: |
| 50 Hz headless | 208.1 t | 0.430 m | 0.459 m | 1.25 MN |
| 60 Hz headless | 198.2 t | 0.367 m | 0.591 m | 2.57 MN |
| 120 Hz headless | 207.0 t | 0.425 m | 0.434 m | 2.16 MN |

At 60 Hz the first compression used 98.5 % of the 0.60 m pin travel. The trace
(`EXOSPHERE_TRACE_CATCH=1`) shows why: trace samples within 1 m of the pins read 2.5 to 5.5 m/s
descending, with the three-engine cluster held at its 40 % floor, and that floor exceeds the
weight. The cutoff then happens inside the compression (cutoff gap −0.35 to −0.56 m),
not above the pins. Guidance holds three engines through the whole catch phase.
The leg-landing path steps down to one Raptor in `FINAL_DESCENT`.

Two trials that step the catch approach to one engine diverged. A one-engine floor
for the whole phase lost the arrest at 56 m/s. A one-engine window below 20 m and
3 m/s reached the pins at 9 to 14 m/s and failed the chapter at all three cadences. Both were reverted.
A controlled engine-count handoff above the tower remains open work.

The cutoff now goes through `TowerCatchCutoff.Decide`. It keeps the earlier speed
and clearance gates, rejects angular rates above the 0.05 rad/s settle limit, keeps
both pins 0.10 m plus the drift until contact inside the 5 m cradle, and predicts the
first compression peak with `CatchImpactPredictor`. The predictor uses the contact
solver's force law. It stays within 3 % of the rigid-body solver, and the cutoff
requires the predicted peak within 90 % of the travel and load limits. On the
nominal seed the gate reproduced the baseline trajectories at all three cadences.
It cannot bound a powered impact that happens before cutoff.

Both Super Heavy parts relied on the 1.2 MN/m default stiffness. At the 214 t dry
part mass that deflects 0.87 m, beyond the 0.60 m travel, so the over-travel check in
the settle criterion could never accept a booster. Both parts now declare
4.2 MN/m (0.35 m static at 300 t), with provenance. `CatchImpactTests` checks every
catch-pin part against its mass envelope.

The entry state and compliance are engineering estimates. This chapter starts from
its own 70 km state and uses the existing demonstration attitude guidance before
cutoff. Continuous orbital return and booster diversion remain separate work.

Run the full chapter with:

```sh
CAPTURE_MENU_MODAL=flight15catchverify "$GODOT_BIN" --headless --path .   --fixed-fps 60 --script tools/capture_menu.gd
```

Run the same command on a real framebuffer and set `CAPTURE_MENU_OUTPUT` to a PNG
path to draw a frame after the physical `CAUGHT` event.

## Flown Flight 15 catch versus the model

Ship 42 was caught at Starbase Pad 2 on 2026-11-14 at about 21:20 UTC, roughly 22.5 h
after liftoff. No primary telemetry is public: the SpaceX mission page did not render
for text extraction, and the YouTube replays are video only. The following comes from
secondary reporting of the SpaceX timeline and posts (Autonocion, Geekspin, Tesla
Oracle, drohnen.de, Space.com, KeepTrack, EarthSky), read 2026-11-19.

| T+ | Reported event |
| --- | --- |
| 22:32:16 | Three sea-level Raptors light out of the belly flop |
| 22:32:26 | Two engines; the ship moves between the arms |
| 22:32:33 | One engine, nearly hovering |
| 22:32:34 | Arms close, last engine cut; catch |

That is one ignition and about 18 s from ignition to catch, with a monotonic
3 → 2 → 1 → 0 sequence. The ship stayed flat until it was low and the tower had it
less than a minute after it came out of the clouds. The two catch fittings sit
directly under the forward flaps and protrude about one foot (Eric Berger, Ars
Technica, via Autonocion). After the catch the ship hung above the launch mount.
Altitudes, speeds, mass and the catch height were not published.

The model differed in three ways.

- **Pins in the nose ogive.** `catch_pin_offset_y_m` was +5.0 m, inside the ogive,
  where the hull radius is below the 4.4 m pin offset. The pins now sit just below the
  modelled forward-flap hinge line (−9.5 m on the V3 nose, −9.25 m on the earlier nose)
  and stand 0.3 m proud of the hull. `CatchGeometryTests` checks every catch-pin part.
- **Fixed 55 m arms.** The cradle was the idle arm height for every vehicle, which left
  the caught ship's tail 3 m above grade and below the 19.8 m mount top. The carriage now
  rides to `LaunchComplexSpec.GetCatchCarriageHeightM`: the vehicle's pin height plus an
  estimated 5 m clearance above the mount. That is about 62 m for the V3 ship and about
  90 m for Super Heavy. The rendered arm assembly follows (`SetCatchCarriageHeightM`).
- **Burn length.** The game flips at 4.5 km and follows a linear profile with a 28.6 s
  time constant, about 140 s of powered descent. It also shuts every engine off whenever
  the demand falls below one engine and relights later.

With the raised cradle and the moved pins, the existing game guidance still reached
`CAUGHT` at all three cadences, with the tail about 25.6 m above grade:

| Render cadence | Mass | Peak compression | Peak pin load | Pin range at cutoff |
| --- | ---: | ---: | ---: | ---: |
| 50 Hz headless | 208.2 t | 0.452 m | 1.34 MN | 4.49 m |
| 60 Hz headless | 208.5 t | 0.449 m | 1.55 MN | 4.48 m |
| 120 Hz headless | 207.2 t | 0.516 m | 2.69 MN | 4.49 m |

The 120 Hz peak load is at 90 % of the 3 MN pin limit. The approach still hits the pins
under thrust before cutoff.

### Single-burn controller

`TowerCatchBurn` is the single burn as flown. It reuses the `Flight14LandingBurn`
vector law, calibrated to the Flight 14 broadcast, and its monotonic engine selection,
now shared as `MonotoneLandingEngineSelection`. The clearance is measured to the cradle,
the horizontal channel homes onto it, the ship holds 3 m above the pins until centred,
and the cutoff goes through `TowerCatchCutoff`. `ShouldIgnite` fires when the descent,
carried through the flip, meets the profile. It is not yet wired into the game.

The bench (`TowerCatchBurnTests`: 215 t, belly flop at 100 m/s with the long axis 80°
from vertical) now flies a stable single burn. On the nominal approach, igniting 320 m
short of the tower with the nose toward it, it lights at 1.12 km, flips in 5.3 s and
steps 3 → 2 at 7.0 s and 2 → 1 at 21.2 s, with no relight. It still does not settle on
the pins. Each change below came from a measured failure:

- **Flip throw.** The three-engine flip pushes the ship 58 m/s and about 220 m along the
  nose. The ignition point therefore has to be planned short of the tower along the
  nose direction; 320 m ends the flip near the cradle. The flip may aim only 10° past
  vertical. At 30° the rotation carried on to 40° and two engines then drove 20 m/s²
  sideways.
- **Irreversible shutdown.** An engine may shut down only if the remaining engines still
  arrest the current descent within the clearance left. Without that check the ship
  dropped to one engine at 140 m and 37 m/s and was lost.
- **Lateral bandwidth.** One Raptor lags the commanded tilt by 2 to 3 s. The lateral loop
  tracks a bounded closing speed with a gain scaled by the lit engines (0.15/s on one,
  0.25/s on three). A velocity integral, active only on one engine, cancels the steady
  side force of the off-axis engine. Higher gains limit-cycled at ±25 m/s and ±70 m.
- **Two-engine window.** Two Raptors at their floor out-thrust the 215 t ship by about
  3 m/s², so the two-engine phase lasts only as long as the descent: roughly 7 s from
  20 m/s, the duration flown on Flight 15. The second engine stays lit while the ship
  still descends faster than 0.5 m/s and is more than 5 m off or moving faster than 2 m/s
  sideways. Holding it longer launched the ship upward.

### Closing arm rails

The 5 m capture disc is replaced by `CatchArmRails`. Each arm is a rail along the arm
(east from the tower face). Closing arms stop against the hull, so each rail's inner
edge sits one hull radius either side of the vehicle axis. A fitting rests on a rail
while it stands proud of the hull over it, inside a ±9.5 m catch zone along the arm,
with the axis inside the open arms (estimated 10 m half-gap). That bounds roll about
the axis to about ±20°. A ship 5 m off the centreline is now caught, where the disc
rejected 0.7 m (`CatchArmRailsTests`). Over-travel, load and settle limits are unchanged.
With the rails, the cutoff accepts 1.5 m/s of sideways speed: the drift until contact
is charged against rail slack, and pin friction stops the sliding.

Both controllers roll the fittings across the arms. The bench burn also feeds forward
the measured offset between the thrust and the axis: with one or two of the three ring
Raptors lit, holding attitude turns the thrust off the axis by enough to push the ship
about 0.8 m/s² sideways.

Single-burn bench, 215 t, ignition 380 m short of the tower with the nose toward it:

| Crossrange | Cutoff | Catch | Engines | Peak compression | Peak pin load |
| ---: | ---: | ---: | --- | ---: | ---: |
| 0 m | 36.2 s | 38.0 s | 3 → 2 (7.2 s) → 1 (21.1 s) → 0 | 0.46 m | 1.27 MN |
| +15 m | 36.0 s | 37.1 s | 3 → 2 → 1 → 0 | 0.44 m | 1.21 MN |
| −15 m | 38.5 s | 42.4 s | 3 → 2 → 1 → 0 | 0.45 m | 1.28 MN |

No engine relit. The flown catch took 18 s; this controller still holds on one engine
for 15 s instead of about 1 s, and the ship comes to rest near the end of the catch
zone along the arm.

### Game chapter with rails

With the rails, the existing game guidance crashed at all three cadences: the pins
passed through the cradle plane with no contact. The Flight 15 chapter is a scripted
demonstration that writes the ship's orientation in the retro and catch phases
(`IsTowerCatchDemonstration`). That is also why the log reports a flip completed in
0.0 s. The imposed attitude carried an arbitrary roll. It now carries the fittings
across the arms, and the chapter catches again:

| Render cadence | Mass | Peak compression | Peak pin load |
| --- | ---: | ---: | ---: |
| 50 Hz headless | 208.4 t | 0.457 m | 1.24 MN |
| 60 Hz headless | 208.8 t | 0.445 m | 1.72 MN |
| 120 Hz headless | 207.2 t | 0.424 m | 2.23 MN |

The game's flip and terminal attitude are still imposed, not flown, in the default
chapter.

### Single burn in the game (opt-in)

`EXOSPHERE_SINGLE_BURN_CATCH=1` flies the chapter with `TowerCatchBurn`. The EDL lights
it out of the belly flop when `ShouldIgnite` fires instead of at the fixed 4.5 km gate,
and the simulation then flies the flip, engines and contact with no imposed attitude.
The aero descent aims short of the tower by `IgnitionLeadDistanceM`: 380 m with no glide
plus 13.4 s per m/s of glide. On the bench, 850 m with 35 m/s of glide caught in 42.8 s.

The default chapter seeds a 12 % main-tank fill and reaches the flip at 313 t and
142 m/s. At that mass and dynamic pressure the three-engine flip stalled between 35°
and 60° of tilt and the ship struck the ground at 126 m/s. The pin stiffness is also
sized for 225 t. The opt-in chapter therefore enters at 215 t from the `singleBurn`
block of `starship_flight15_entry_estimate.json`, with its entry seed recalibrated for
that lighter belly flop (171.7 km upstream, 2.1 km south).

Measured at 60 Hz: ignition at 1,479 m pin clearance, 117.5 m/s down, 554 m from the
tower; flip in 8.9 s; two engines at +15.0 s, one at +30.8 s; no relight. The burn
then timed out about 1.6 km from the tower and diverted to a leg landing with no
damage. Two causes remain:

- The ship reaches ignition with 27.7 m/s of crossrange drift, so the nose, and the flip
  throw with it, points about 48° away from the tower. The aero corridor has not removed
  that drift by 1.5 km.
- During braking the roll alignment saturates (roll command −0.77) while the ship is
  tilted 25–36°, and the ship accelerates away from the tower to 81 m/s. Roll alignment
  should wait for low tilt or the final metres.

Both were then addressed. Roll alignment now runs only below 10° of tilt and within
150 m of the cradle, with the added roll command capped at 0.3; the bench still catches
in all three cases (38.0, 41.9 and 40.9 s). With the opt-in flag, the terminal entry
corridor also tightens its crossrange deadband with altitude, as it already did for
downrange; it had stayed at 20 km down to the flip.

That was not enough. The ship still reached ignition 625 m from the tower and moving
away from it at 9.9 m/s, with 29.7 m/s of horizontal speed. After the flip it carried
88 m/s sideways, far more than the 30° tilt cone can remove before the ship is low,
and the burn again timed out near 1.5 km and diverted to a safe leg landing. The
belly flop cannot redirect its glide from 1.5 km. A catch needs the approach heading
set high up: the glide must point at the tower, and the planned lead must follow the
glide the flip will add to. That calls for an ignition-point planner that predicts the
flip throw from the belly-flop state, used by the corridor guidance from the start of
the terminal descent.

### Ignition-point planner

`TowerCatchApproachPlanner` sets the approach from 10 km down. Low in the belly flop the
fall is nearly vertical, so the lift lies almost horizontal and turning the ship about
the vertical points it at any heading. Below `terminalGlideAltitudeM` the lift tracks a
closing speed toward an ignition point on the horizontal line of sight, short of the
tower by the ignition lead. The corridor prediction targets the same point.

At 60 Hz the ship then reached ignition 759 m from the tower, flying toward it at
22.7 m/s with its nose 0.94 aligned with the line to the tower. Before the planner it
was 625 m away and moving away from the tower. After the flip it passed 111 m from the
cradle at 12 s, still carrying 75 m/s, and overshot by about 500 m. Raising the lead to
match that overshoot (33 s per m/s of glide) made the glide loop swing ±25 m/s about
its target. The ship then reached ignition moving away, the flip took 19.7 s, and it
struck the ground at 10 m/s 1.9 km from the tower. The lead is back at 13.4 s per m/s.

That crash exposed a gap: the burn flew to the pins, not the ground under them. A ship
below 150 m of pin clearance and more than 150 m from the tower now abandons the catch
(`missed-approach`) and the EDL lands it on its legs. The last run diverted that way
at 16 s and landed intact.

Five glide laws were then tried at 60 Hz; the first is the one kept:

| Glide law | Nose to tower | Speed to tower | Outcome |
| --- | ---: | ---: | --- |
| Close on the ignition point, lead 380 m + 13.4 s × glide | 0.94 | +22.7 m/s | passed 111 m from the cradle, missed approach, leg landing |
| Same, lead 33 s × glide | 0.64 | −5.9 m/s | ground impact at 10 m/s (before the missed-approach guard) |
| Arrive at the point at ignition (range ÷ time to go) | 0.89 | +5.5 m/s | missed approach |
| Same, without the entry vertical-lift floor | 0.36 | −12.0 m/s | missed approach |
| Heading law cos θ = r² | 0.03 | −0.7 m/s | missed approach |
| Heading law, lead from glide magnitude, 15 s turn margin | — | — | diverted at 5 km, 2.1 km off |

What these runs show about the belly flop:

- Its glide speed is nearly fixed (about 28–42 m/s, set by the angle of attack). Steering
  only turns the glide; the ship cannot wait over a point. The arrival-time law passed
  2 m from its point 3.4 km up and flew on.
- It turns at roughly 0.1 rad/s (about 3 m/s² of lift on a 30 m/s glide), so 180° takes
  about 30 s. Laws that ask for faster heading changes swing around their target.
- A lead sized from the measured flip throw (61 m/s along the nose, 8.4 s flip, 5.1 m/s²
  of post-flip braking) gives about 1.1 km at 22.7 m/s of glide. Flying to a point that
  far out made the closing loop overshoot, and with a lead from the glide magnitude the
  point ended up behind the ship.

The profile inversions `IgnitionClearanceM` and `TimeToIgnitionS` are kept and tested.
A catch from the opt-in chapter needs a planner that predicts the belly-flop turn and the
flip throw together, for example by propagating the glide with its measured turn rate,
rather than a feedback law on the current state.

### Predictive glide planner

`TowerCatchGlidePredictor` replaces feedback on the glide with prediction. Every 0.5 s it
propagates candidate S-turns to the moment of ignition: turn away by 0–180° at the
planning turn rate (0.08 rad/s, below the measured 0.1), turn back until the glide
points at the tower, then fly straight in. It keeps the candidate that ends one lead
short of the tower flying head-on, and flies only its present turn. The lift turns that
far sideways; the rest keeps the glide going.

The approach is now repeatable across cadences:

| Render cadence | Range at ignition | Speed toward tower | Nose to tower |
| --- | ---: | ---: | ---: |
| 50 Hz | 1,136 m | 30.6 m/s | 0.93 |
| 60 Hz | 1,142 m | 30.9 m/s | 0.95 |
| 120 Hz | 1,145 m | 30.9 m/s | 0.95 |

The burn then also needs an approach slope: descend no lower than one metre of pin
clearance per metre of miss beyond the alignment tolerance. Without it the descent
outran the lateral closure and the missed-approach guard fired 300 m out. With the
slope and a 15 m/s far-field closing speed, the 60 Hz run was caught: no relight, engines
3 → 2 → 1 → 0, peak compression 0.315 m, peak pin load 0.89 MN, 142 t at the catch. The
catch came at 120.8 s, after about 40 s hovering on one engine within 10 m of the cradle,
and the cutoff fell 0.1 s inside the raised 120 s burn budget. At 50 and 120 Hz the same
approach ended in that hover and diverted to a leg landing. On the bench, the 0 and
−15 m crossrange cases catch (38 and 77 s); +15 m does not and is kept as a skipped test.

The flown burn lasted 18 s with one engine for its last second. Here the ship still
reaches its one-engine hold metres off the cradle and burns about 73 t instead of 25–35 t.
Alignment has to finish while two engines still descend, or with steering that does not
depend on one off-axis Raptor.

### Two-engine alignment trial (not kept)

A follow-up tried to finish the alignment on two engines, as flown. The lateral closing
speed was synchronised with the descent (2 × miss ÷ time to the pins, bounded by a
1 m/s² braking profile with that deceleration fed forward), only with two or more
engines lit. A guard also held the third engine while more than 10 m off and descending
faster than 15 m/s. On the bench (0 m crossrange) the step to two engines moved from
7.0 s to 13.0 s (Flight 15: 10 s), and the ship reached the step to one engine 8 m from
the cradle at 0.5 m/s, where it had crossed it at 9.3 m/s before.

It still did not catch, and both previously passing bench cases timed out, so the trial
was reverted. The cause is attitude lag on two and one engines: braking the sideways
speed leaves the axis tilted toward the cradle when that speed reaches zero. The tilt
then takes about 3 s to come back and pushes the ship 3 m/s the other way (a ±25 m
swing on one engine). Resetting the off-axis thrust trim at engine changes did not help.
The lateral loop needs a model of that lag (command the tilt rate, or predict the
velocity one attitude time constant ahead) before the alignment can finish on two engines.

The default chapter is unchanged and still catches at 50, 60 and 120 Hz.

## Orbital return feasibility

The simulated ascent and all 26 releases produced a 237.381 × 296.885 km
orbit at T+4,660.02 s, with a 5,382.45 s period and a 210.523 t ship. A temporary
study propagated this orbit for a window of one period centred on T+21:35:57.
It tried retrograde impulses targeting periapses between −90 and +50 km, then
propagated the resulting inbound 120 km crossing to a 2.5 km altitude gate with
the existing reduced-order entry model. Fixed bank angles ranged from −75° to
+75°. The shallow-bank cases reached the predictor’s 80 m/s speed floor above
the altitude gate. The expanded 60 s grid produced 1,260 candidates that passed
the altitude gate. The closest passed 93.65 km from Starbase with 71.26 m/s of
retrograde impulse, a 35 km vacuum periapsis, and +75° bank.

Three local refinements around the 10 s grid minimum reduced the closest
modeled pass to 1.45 km from Starbase at 2.5 km altitude. The candidate burn is
at T+78,552.89 s, with 63.17 m/s retrograde impulse, a 53.33 km vacuum
periapsis, and +62.89° bank. Its inbound interface is at T+79,972.82 s and
7.44 km/s airspeed. This is a starting point for a closed-loop return trial
from the active ship.

This study assumes a Keplerian coast, an instantaneous burn, constant bank,
and a spherical projection onto the rotating Earth. It estimates a search
window for a continuous return.

### Finite-burn trial

Run the pad-to-entry diagnostic from the repository root.

```sh
dotnet run --project tools/Flight14TrajectoryProbe -c Release -- \
  data /tmp/exo-flight15-return --flight15-return
```

The diagnostic keeps the original ship and 26 released satellites in the
isolated-Earth universe. It coasts with the ordinary ×100 warp scheduler after
deployment, then uses the 20 ms control cadence for alignment, deorbit, and
entry. The return estimate keeps the main-tank reserve and uses a signed lift
bank about the air-relative velocity.

The ignition command at T+78,549.00 s reached the osculating periapsis target
at T+78,554.10 s. Thrust then took 0.24 s to fall below the shutdown threshold.
The one-engine burn used 4,040.28 kg and left a 48.30 km radial periapsis after
shutdown, below the 53.33 km command. The physical 120 km geodetic interface
was reached at T+79,862.48 s at 7.42 km/s and −125.6 m/s vertical speed.
At the descending 70 km geodetic witness the ship still had 7.02 km/s airspeed
and was 1,127 km from Starbase along the surface. The independent catch chapter
begins at about 1.80 km/s. The full approach needs
a guided hypersonic descent, geographic targeting, and validation of heating
and control authority before handing off to terminal guidance.

A separate trial with the same commands and a 2.5 km diagnostic altitude
repeated the burn and 120 km entry. At T+80,740.20 s it reached 2.5 km at
26.6965° N, 94.9326° W, about 235 km from Starbase by spherical great-circle
distance. Airspeed was 120.6 m/s, vertical speed was −120.1 m/s, and the
angular rate in the adjacent integration sample was 0.51 rad/s. This is an
altitude crossing with substantial geographic and attitude errors. Between
44.7 and 30.7 km, the sampled angular rate climbed from 0.003 to 0.269 rad/s.
That interval gives a starting point for validating the authority-aware
aerodynamic descent controller on the Flight 15 trajectory. For a repeat,
set `diagnosticEndAltitudeM` to `2500` in a
copy of the estimate and run the same diagnostic against that data directory.

### Actuator-controlled descent

The same continuous trial can hand the original ship to the descent controller
at the 70 km witness. The controller schedules angle of attack, limits the
attitude reference by available flap trim, and constrains vertical lift from
the current descent rate. The Flight 15 estimate adds a nominal bank to this
control law. Every setting is an estimate calibrated against EXOSPHERE.

```sh
dotnet run --project tools/Flight14TrajectoryProbe -c Release -- \
  data /tmp/exo-flight15-descent --flight15-descent
dotnet run --project tools/Flight14TrajectoryProbe -c Release -- \
  data /tmp/exo-flight15-world --flight15-descent-world
```

| Trial | Ground range to Starbase at 2.5 km | Peak angular rate after handoff |
| --- | ---: | ---: |
| Inherited descent controller | 1,150 km | 0.629 rad/s |
| Generic footprint steering experiment | 119 km | 1.139 rad/s |
| Initial calibration, isolated Earth | 2.43 km | 0.021 rad/s |
| Initial calibration, all eight bodies | 38.43 km | 0.031 rad/s |
| Recalibration, all eight bodies | 6.25 km | 0.018 rad/s |
| Retained estimate, all eight bodies | 0.68 km | 0.018 rad/s |

The generic footprint experiment produced a large angular excursion and was
discarded. The calibrated profile reached 2.5 km at about 121 m/s, with
the heat shield facing the airflow and with stable actuator control. Its
geographic result is sensitive to the world model. The 2.5 km witness is a
descent diagnostic; the existing powered flip may start above that gate.
The retained estimate is calibrated against the full-world setup. The
continuous flight passed 429 m from Starbase at 5.39 km altitude, then
crossed the 2.5 km witness at 120.64 m/s with a final angular rate of
0.0004 rad/s.

An isolated-atmosphere replay preserved a sampled flight state at 8 km and
entered the existing powered game guidance at the same body-relative state.
The range closed from 1.60 km to about 0.78 km before the flare. It grew past
2 km during the flip. The game diverted at 3.08 km altitude and the vehicle
was destroyed during the fallback. The final capture sequence still requires
a powered handoff that controls this lateral excursion.
An upright-biased flip also diverted. The Flight 14 powered descent law
reached 100 m about 980 m from Starbase at 56.2 m/s, exceeding its terminal
speed gate. Starting that law at 1.8 km or 6.5 km also exceeded the speed
gate. These trials provide no capture acceptance.

A Godot replay from the retained full-world profile started at 8 km with
about 142 m of ground range. The game started the flip at 4.50 km. The
powered approach again exceeded its 2 km horizontal limit and diverted
at 2.51 km altitude. The vehicle was destroyed during the fallback. That
replay exercised the game controller from a saved physical state.
Continuous capture from the menu remains unverified.

## Integrated GPU

Native X11, AMD Radeon Graphics (RADV RENOIR), Vulkan Forward+, Integrated GPU preset
(0.75 render scale). Xvfb selected llvmpipe and was excluded from GPU evidence.
A moving ascent run collected 292 samples between T+10.28 and T+35.14. GPU times were
55.06 ms at p50 and 133.87 ms at p95. CPU render submission was 5.54 ms at p50.
The moving run reached its wall-clock limit before the final screenshot.

A separate run paused at T+10.04 with a 1920 × 1136 viewport. Each row reports the
median of 72 samples following 24 warm-up frames.

| Isolation | GPU time |
| --- | ---: |
| Baseline | 53.27 ms |
| Ground hidden | 53.04 ms |
| Directional shadows disabled | 51.31 ms |
| Sky disabled | 44.56 ms |
| Vehicle and its effects hidden | 47.97 ms |
| Launch effects hidden | 48.85 ms |
| 3D render scale set to 0.60 | 32.98 ms |
| Baseline repeated | 47.44 ms |

Fog was already disabled in the preset. A separate repeat with fewer conditions
measured 50.45 ms at baseline, 44.07 ms with the sky disabled, and 49.02 ms at the
repeated baseline. Ground, shadow, vehicle, and launch-effect differences were within
the baseline drift of the extended run. Render scale produced the largest observed
improvement, with GPU time still around 33 ms in this paused scene. The experiment
left the shipped preset at 0.75. A separate framebuffer comparison at T+10.08
showed a comparable HUD and softer vehicle panel seams and thin tower edges at
0.60. A moving comparison is needed to assess temporal stability before selecting
a lower scale as a default. The hardware run covered the integrated GPU.

```sh
EXOSPHERE_GRAPHICS_PRESET=integrated "$GODOT_BIN" --path . --display-driver x11   --rendering-method forward_plus --rendering-driver vulkan --resolution 1920x1080   --script tools/perf/flight15_gpu_probe.gd
```

Set `EXOSPHERE_GPU_CAPTURE_DIR` to an existing directory to save an image for each arm.
