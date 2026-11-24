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

### Attitude-lag model

The lateral loop now feeds back the velocity predicted one attitude lag ahead,
`v + a·τ`, from the measured sideways acceleration (filtered over 0.5 s). The thrust
would bias it: in a steady hold the thrust still balances the off-axis engine. The lag
τ is 2.5, 0.5 and 0.3 s with one, two and three engines lit. The velocity and closing
gains are tables by lit engines ([0.12, 0.3, 0.4] /s and [0.03, 0.08, 0.1] /s), so the
one-engine loop stays overdamped (closing gain below a quarter of the velocity gain).

The ±25 m one-engine swings are gone, and the bench catches from all three crossranges,
+15 m included, which never caught before:

| Crossrange | Catch | Engines | Peak compression | Peak pin load |
| ---: | ---: | --- | ---: | ---: |
| 0 m | 59.2 s | 3 → 2 → 1 → 0 | 0.48 m | 1.41 MN |
| −15 m | 71.3 s | 3 → 2 → 1 → 0 | 0.45 m | 1.35 MN |
| +15 m | 61.2 s | 3 → 2 → 1 → 0 | 0.49 m | 1.42 MN |

In the opt-in game chapter 120 Hz was caught at 112.2 s (no relight, 146 t, peak
0.346 m and 0.98 MN); 50 and 60 Hz timed out and landed on their legs. The 60 Hz trace
shows why. The ship reached the pins centred (0.9 m off at 0.7 m clearance), but it rolled
at a steady −0.064 rad/s about its axis. The fittings turned off the rails and the
cutoff gate reported `pin range`. The RCS was armed (both parts) and the roll command
reached 0.69, yet the rate held: the roll the draining propellant drives balances the
torque of the modelled 1.5 kN thrusters. The gate now judges pitch-yaw rate (0.05 rad/s)
apart from roll (0.15 rad/s), and accepts 2.5 m/s of sliding on the rails (pin friction
stops it in about 0.5 m). Neither changed the 60 Hz outcome; both are physically
justified and kept.

Next: the roll authority. The 1.5 kN Starship V3 RCS thrust is an estimate, and the
drain spin-up torque on this return is not yet quantified.

### Single-engine roll authority (physics fix)

The 0.064 rad/s roll was not a lack of thruster force. The RCS never fired: monopropellant
stayed at 4,000 of 4,000 kg while the roll command reached 0.69. `Vessel` leaves an axis to
the gimbals whenever the differential-TVC envelope covers it, and
`PartGraph.GetDifferentialTVCAngularAccelerationEnvelope` credited roll authority whenever
the cluster declared two or more engines (`Definition.EngineCount`). The ship's cluster
declares six, so one lit Raptor still counted. One engine's two gimbal axes cannot hold
pitch, yaw and roll at once. Roll now counts only with two or more lit, gimballed
engines, which is the case the code comment cites (the S-IVB APS flying the single J-2's
roll). `SingleEngineRollAuthorityTests` imposes the game's 0.064 rad/s on the one-engine
bench: without the fix 24 armed thrusters fire nothing; with it they bring the roll
under 0.02 rad/s within 5 s.

No published figure was found for Starship's RCS thrust (forum reports only: ullage-gas
thrusters, 24 jets unconfirmed). The modelled 1.5 kN per thruster stays an estimate. It
suffices for this roll once the thrusters may fire.

The full suite passes with the fix (1,762 tests, including the slow Flight 14 chains).
The opt-in single-burn chapter now catches at 60 Hz (79.6 s, 162 t, peak 0.414 m and
1.22 MN) and 120 Hz (83.7 s, 161 t, peak 0.447 m and 1.33 MN), with no relight and
engines 3 → 2 → 1 → 0. At 50 Hz it reaches a hold 8 m across the arms (outside their
5.5 m reach) and closes too slowly on one engine before the 120 s budget, then lands on
its legs. The descent hold now uses rail slack instead of a fixed 4 m radius, since the
catch zone runs 9.5 m along the arms. It did not change these runs.

### One-engine closure and settle hysteresis

At 50 Hz the approach slope stopped the two-engine descent about 270 m up (two engines
cannot hover), so the ship stepped to one engine 185 m off. The one-engine closing gain
of 0.03 /s then gave about 1.35 m/s at 45 m, too slow for the burn budget. That gain was
set to keep the loop overdamped before the attitude lag was modelled. With the lag
compensated, the one-engine gains are now 0.15 /s (velocity) and 0.06 /s (closing).
The bench catch times fell from 59–71 s to about 40 s for all three crossranges.

That alone broke 60 Hz. There, the ship drifted tangentially at 1.5 m/s about 4 m
from the centre and crossed the 4 m alignment threshold in the last half metre, so the
hold sent it back up to 3 m. Once centred and below the hold clearance, the settle now
continues unless the miss exceeds 1.5 × the tolerance (6 m).

Opt-in single-burn chapter:

| Render cadence | Catch | Mass | Peak compression | Peak pin load |
| --- | ---: | ---: | ---: | ---: |
| 50 Hz | 97.8 s | 153.7 t | 0.385 m | 1.05 MN |
| 60 Hz | 79.0 s | 162.4 t | 0.375 m | 1.06 MN |
| 120 Hz | 77.9 s | 163.0 t | 0.456 m | 1.35 MN |

No relight; engines 3 → 2 → 1 → 0 in all three. The flown catch still took 18 s with one
engine for about 1 s. Here one engine runs for 45–65 s and the burn uses 52–61 t.

A blind check at cadences never used for tuning: 75 Hz was caught at 97.7 s
(153.7 t, peak 0.386 m and 1.06 MN). At 100 Hz the ship circled the cradle 4–10 m out at
about 3 m of clearance, with 1–2.5 m/s of undamped tangential speed, until the 120 s
budget ran out, then landed on its legs. The likely cause is the lateral velocity
integral sustaining an orbit (a proportional-plus-integral velocity loop can limit-cycle
around a point). See the next section.

The default chapter is unchanged and still catches at 50, 60 and 120 Hz.

### Lateral integral removed and rigid two-pin impact prediction

The 100 Hz trace did not show a pure orbit. With one engine the ship crossed the cradle at
about 1.7 m/s, ran out to 10 m, and came back. The lateral velocity integral had been added
to cancel the steady side force of an off-axis Raptor, before the thrust trim did the same.
Integrating a velocity error integrates position (∫v dt = r), so the term acts as a spring
on position. It wound up over the slow one-engine approach and pushed the ship past the
cradle. The integral, its three JSON fields and its validation are removed. On the bench
the +15 m crossrange case caught in 24.6 s instead of about 40 s; the 0 and −15 m cases
are unchanged at about 41 s.

That faster catch exposed a separate fault: the predicted peak compression was too low
in all three bench cases.

| Crossrange | Pin gaps at cutoff | Symmetric prediction | Solver peak |
| --- | --- | ---: | ---: |
| 0 m | 0.035 / 0.247 m | 0.424 m | 0.494 m |
| −15 m | 0.042 / 0.244 m | 0.427 m | 0.493 m |
| +15 m | −0.065 / 0.246 m | 0.431 m | 0.547 m (over the 0.54 m limit) |

One off-axis Raptor needs about 1.3° of axis tilt to keep its thrust vertical, so the pins
reach the cradle about 0.2 m apart. The first pin carries the drop until the ship rotates
onto the second. The symmetric model assumed both pins landed together from the lowest
gap, and its documentation wrongly called that conservative.

`CatchPinPair` now measures each pin's gap and closing speed, its lever along the
horizontal pin line from the centre of mass, and the transverse inertia. The rigid form of
`CatchImpactPredictor.Predict` integrates heave plus rotation about the axis across the pin
line, with the solver's force law on each pin. Both cutoff paths, the burn and the game's
EDL catch phase, use it.

| Crossrange | Rigid prediction | Solver peak |
| --- | ---: | ---: |
| 0 m | 0.511 m | 0.494 m |
| −15 m | 0.510 m | 0.493 m |
| +15 m | 0.539 m | 0.528 m |

Gates in `CatchImpactTests`:

- A level pair reproduces the symmetric prediction.
- Drops tilted 1°, 2° and −1.5° through the contact solver land within −3 %/+5 % of the
  rigid prediction.
- The symmetric form understates those drops by more than 5 %.

The bench now asserts that the solver peak stays within the prediction made at cutoff.

Opt-in single-burn chapter, final code:

| Render cadence | Catch | Mass | Peak compression | Peak pin load |
| --- | ---: | ---: | ---: | ---: |
| 50 Hz | 89.3 s | 157.1 t | 0.427 m | 1.29 MN |
| 60 Hz | 81.9 s | 161.1 t | 0.429 m | 1.29 MN |
| 75 Hz | 87.9 s | 157.7 t | 0.428 m | 1.30 MN |
| 100 Hz | 79.9 s | 162.1 t | 0.439 m | 1.32 MN |
| 120 Hz | 78.3 s | 162.6 t | 0.438 m | 1.31 MN |

All five cadences catch, including 100 Hz, which previously landed on its legs. In every
run there was no relight and the engines stepped 3 → 2 → 1 → 0.

The default chapter still catches (`FLIGHT15_CATCH_VERIFIED`) at 50, 60 and 120 Hz, and its
predicted compression bounds the peak:

| Render cadence | Predicted compression | Peak compression |
| --- | ---: | ---: |
| 50 Hz | 0.459 m | 0.457 m |
| 60 Hz | 0.446 m | 0.445 m |
| 120 Hz | 0.460 m | 0.424 m |

One fault remains open, and it predates this change. In the default chapter the peak *pin
load* exceeds the predicted load: 1.72 MN against 1.18 MN at 60 Hz (identical before this
change), and 2.23 MN against 1.25 MN at 120 Hz. Both stay inside the 3 MN pin limit. See
the next section for the cause.

### Default-chapter pin-load spike (diagnosed, not fixed)

A per-step trace at 120 Hz put the 2.23 MN peak at the first pin contact, under power and
before the cutoff check ran. The pin met the cradle at 4.4 m/s. At 7 mm of compression the
load was almost entirely damping: c·v ≈ 2.2 MN.

The legacy EDL guidance flies the last 30 m as a sawtooth. Descent speed swings between 1.6
and 5.7 m/s, and the engines shut down and relight about every 1.3 s:

1. The catch phase keeps all three centre Raptors (`minimumSafeEngines = maxLandingEngines`).
2. Three engines at their 40 % floor out-thrust the 207 t ship, so it cannot hover.
3. `PoweredLandingGuidance` then commands zero whenever the ship is slower than its profile,
   and the engines shut down.
4. The profile also targets 1.2 m/s at contact, the leg-touchdown value, while the cutoff
   accepts at most 0.65 m/s.

Four trials were run and reverted:

- **One engine below 30 m, continuous descent law, 0.4 m/s contact target.** The ship
  hovered: a proportional loop settled about 0.8 m/s slow, because delivered thrust exceeds
  the rated model by several percent. A bounded vertical integral fixed that, and the
  120 Hz run caught with a 1.49 MN peak load. It landed 4.4 m off-centre and tilted 3.3°,
  on one rail, after 23 t more propellant.
- **Two engines.** Two engines at their floor also out-thrust the ship, so it climbed.
- **Measured-thrust trim ported to the legacy aim.** No effect: the trim measured zero,
  because this vehicle's thrust runs along its axis.
- **Lateral force trace.** The thrust pushed 0.6 m/s² toward the cradle, yet the centre of
  mass drifted away. The default chapter is the scripted demonstration, which writes the
  attitude every frame (`vessel.Orientation = desiredAttitude`). One off-centre engine makes
  a torque. The integrator rotates the ship about its centre of mass, and the snap rotates it
  back about the skirt datum, about 20 m lower. Each frame that moves the centre of mass
  sideways, a translation that is not physical. Three symmetric engines make no torque,
  which is why the original sawtooth showed no drift.

The demonstration's attitude snap cannot fly an asymmetric engine. The physical fix is to fly
the default chapter's terminal approach with the actuator-controlled single-burn controller
(`TowerCatchBurn`), which already holds one engine with thrust trim and no relights.

The single burn still takes 78–89 s, against 18 s on Flight 15.

### Burn-time investigation: physical constraints (work in progress, not merged)

Goal: shorten the single burn (78–89 s in the game, 18 s flown) with an ignition time
computed from physics. The work is stashed as
`wip(flight): ZEM/ZEV catch guidance, physical attitude lag, profile engine guard`
because it caught on the bench but broke the game chapter (missed approach at 60 and
120 Hz). Five physical constraints came out of it.

**Where the time goes.** In the traced 60 Hz game burn:

| Interval | What happens |
| --- | --- |
| 0–7 s | Flip. |
| 7–29 s | The approach-slope hold stops the descent about 340 m up while the ship crosses about 700 m sideways. |
| 29–45 s | One engine falls to 37 m/s, then brakes. |
| 45–80 s | One engine hovers 4–8 m up, closing a 12 m miss. |

**1. Two engines cannot stop.** Two Raptors at their 40 % floor (2.04 MN) out-thrust the
ship near 185 t (1.81 MN). Without relights, a ship that stops high on two engines climbs:
the bench case went past 3 km. The descent must keep decelerating, without stopping, until
the last metres on one engine, which is the flown 3 → 2 → 1 shape. The approach-slope hold
(stop high, then translate) is incompatible with that.

**2. An engine may go out only if the rest can fly the remaining profile.** The selector
compared the instantaneous demand, so a ship nearly stopped 385 m up dropped to one engine.
It then fell to 37 m/s and braked at 3.8 m/s², because one Raptor cannot fly the 7 m/s²
segment. The fix requires `m·(g + profile deceleration)` within the headroom of the engines
that remain.

**3. Attitude lag comes from gimbal authority, not engine count.** The live envelope
(`GetDifferentialTVCAngularAccelerationEnvelope`) measured these α values:

| Lit engines | Throttle | α |
| --- | --- | --- |
| 3 | floor | 0.18 rad/s² |
| 2 | floor | 0.12 rad/s² |
| 1 | near hover | 0.10 rad/s² |

The lag table [2.5, 0.5, 0.3] s therefore made the two-engine loop five times too fast.
The trace shows it: at 14–20 m the command reversed, yet the ship stayed tilted 20° toward
the tower for about 2 s, accelerated to 26 m/s and overshot by 100 m. A lag of √(θ/α)
calibrated to 2.5 s for one engine gives about 2.3 s for two engines at their floor.
Lateral loops faster than about 8× that lag limit-cycle (a 6× trial diverged).

**4. Lateral and vertical need one clock.** Zero-effort-miss / zero-effort-velocity
guidance (`a = −6r/t² − 4v/t`) on the profile's time-to-go nulls offset and velocity
together. It passed its analytic gates. With the measured lag, however, the lateral loop
can only remove small errors during the burn.

**5. The flip throws the ship about 50 m/s toward the tower.** The three engines push
along the nose while it rotates. For the time-to-go guidance, the ideal ignition lead is
the distance flown during the flip plus `v_flip·t_go/2`, which gives a constant-deceleration
lateral arrival. For the game's flown ignition state that is about 1139 m, against the
1142 m it actually flew. A reduced flip model fitted to the full-physics bench left
residuals up to 18 m and 9 m/s.

Consequence: an 18 s burn is possible only if the ship arrives nearly aligned. The ignition
point and the flip have to put it there, as entry guidance does on the real vehicle,
because a long correction on low-authority gimbals cannot. Next step: a flip and burn
predictor that picks the ignition point and time for an aligned arrival, validated against
the full-physics bench, which now includes the game chapter's flown ignition state.


### Flip and ignition sweep on the full-physics bench (2026-11-23)

A temporary sweep flew the 215 t bench from the game chapter's ignition state (31 m/s glide
toward the tower, 116 m/s descent) and varied the lead and the ignition height. It is kept
with the experiments in the stash
`wip(flight): gate-synchronized catch guidance and flip sweep bench (not converged)`.

**The flip is nearly translation invariant.** With the committed controller, the flip took
5.1 s and lost 555 m of height. It moved the ship 294 m toward the tower and ended at
77.6 m/s horizontal and 94 m/s descending, whatever the lead. The throw depends on the glide:

| Glide (m/s) | Flip displacement (m) | Horizontal speed at flip end (m/s) |
| ---: | ---: | ---: |
| 0 | 170 | 58.8 |
| 15 | 242 | 69.0 |
| 31 | 294 | 77.6 |
| 45 | 336 | 84.3 |

The descent speed changes little at flip end: 89–100 m/s between 100 and 130 m/s at ignition.

**The committed controller with a better lead.** Lead 900–1000 m caught in 53.5 s. The
game's 1142 m lead caught in 79 s, and 1300 m lost the vehicle. The game lights about 140 m
too far out for this controller. The remaining time is one engine hovering about 30 s to
close the miss, because the closing law is capped at 15 m/s and 0.5 m/s².

**An aligned arrival in physical terms.** A uniform deceleration of both channels to rest at
the cradle needs `r = v_h·T/2` at flip end, with `T` the vertical time to go. For the game
state this gives about 1015 m of lead with the 7 m/s² profile.

**What the trials showed:**

- *The vertical profile must start from the post-flip state.* The ship leaves the flip
  slower than the 7 m/s² profile. The profile then commands zero vertical thrust, and three
  Raptors at their floor over-brake at about 4.8 m/s². The vertical clock stops: time to go
  stayed near 19–20 s for six seconds. Braking uniformly from the actual state onto a 30 m
  terminal gate fixes this, and time to go then falls about 1 s per second.
- *The engine drop takes the lateral authority.* Two Raptors at their floor (10.2 m/s²
  near 200 t) leave about 2 m/s² sideways while holding the descent. Dropping from three to
  two with 45 m/s of lateral speed left made every trial overshoot the cradle.
- *The flip ending vertical costs about 200 m.* The gimbals turn the ship at about
  0.18 rad/s², so it takes 2–3 s after the flip to tilt back and brake, at about 75 m/s.
  Letting the flip aim at the braking tilt (30° instead of 10°) did not yet improve the catch.
- *Lateral feasibility bounds the burn.* Igniting at 950 m with the lead matched to it
  reached the gate 12 s after ignition but needed about 12 m/s² sideways. That is at the
  30° tilt limit, and the ship overshot. A feasible design point of 5–6 m/s² sideways
  needs about 13 s from flip end to the gate. With the terminal segment, that is about 25 s
  from ignition, against the 18 s flown.
- *The lateral loop is lag-limited, and the lag is not first order.* Every law tried reached
  the cradle with 10–13 m/s of lateral speed left and drifted 40–60 m past it:

  | Law | Result |
  | --- | --- |
  | Cone (velocity command `r·d/h`) | Overshoot |
  | Gate-time cone with a lag-consistent error | Overshoot |
  | Capped closing rate | Overshoot |
  | Pole placement for a first-order lag | Oscillated (pole factors 0.45–0.7) |

**Next step.** Identify the lateral plant on the bench, from commanded side acceleration to
measured side acceleration, through the attitude loop and the gimbals. Then design the
lateral law against it. A predictor that chooses ignition height and lead comes after that,
because the lead that works depends on the in-burn law: about 1000 m for the committed law,
about 800 m for the gate law at 1.25 km ignition.

### Lateral plant identification and a receding-horizon planner (2026-11-23)

Landed with the glide alignment below. The bench fixtures and the fit script live under
`ExosphereSimulation.Tests/Fixtures/flight15_catch/`.

**The lateral plant has a reversal zero.** An upright ship on one, two or three Raptors
received side-acceleration steps through the burn's own actuation path: axis pointing,
then throttle. In every case the side acceleration first goes the wrong way. A 3 m/s²
command on two engines gives −2.1 m/s² before it turns: to tilt the ship the gimbals
push the tail away. Joint fits of `K(1 − T_z s)/(T²s² + 2ζTs + 1)` near the throttle floor:

| Lit | K | T_z (s) | T (s) | ζ | Relative RMS |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 1.26 | 1.81 | 1.02 | 0.96 | 0.10 |
| 2 | 1.15 | 1.68 | 0.90 | 0.91 | 0.13 |
| 3 | 1.38 | 1.24 | 0.79 | 0.85 | 0.32 |

T_z shrinks as thrust grows (0.85 s for three Raptors at half throttle), as expected for
TVC. The right-half-plane zero caps feedback bandwidth near `1/(2T_z)`, 0.3–0.4 rad/s,
so about 10 s to correct an error. That explains every overshoot of the earlier laws.

**Planner.** `TowerCatchLateralPlanner` solves, at each step, the smoothest
piecewise-constant command sequence that brings offset, velocity and plant state to zero
at the end of the horizon through the identified model. It returns the first command,
which is linear in `[r, v, z₁, z₂]`. An internal model is driven by the applied command,
started from the measured acceleration at flip end, and pulled toward the measurement
with a 1 s observer. The horizon is the time to a 30 m terminal gate. Above the gate the
vertical channel brakes uniformly onto it. The horizon never drops below 5·T_z: at 4·T_z
the one-engine terminal regulator oscillated, and at 7·T_z the catch was slower.

Three more physical corrections came with it:

- *Throttle.* The throttle holds the vertical through the actual tilt, so the tilt carries
  the side force. Dividing the total by the alignment had turned a 36° overshoot into
  20 m/s² of side braking on two engines.
- *Engine drops.* They count the planned side acceleration, unclipped.
- *Two at their floor.* Two Raptors at their floor may stay lit only while that floor is
  below the vertical need. Otherwise the ship hovers 120 m up.

Results on the 215 t bench:

| Case | Committed | Planner |
| --- | ---: | ---: |
| Nominal (380 m lead, no glide) | 41 s | 31 s |
| Game ignition state (1142 m, 31 m/s glide) | 79 s | 40 s |
| Eight leads between 660 and 1100 m, ignition 1.09–1.40 km | 53–79 s | 33–41 s, all caught |

Engines go 3 → 2 → 1 → 0 with no relight, and the rigid two-pin prediction still bounds
the pin peak. `LateralPlantIdentificationTests` ties the declared model to the bench.

**In the game it still misses.** The 50, 60 and 120 Hz runs all ended in a missed
approach, against catches at 78–98 s with the committed law. The exact ignition state is
now captured (`EXOSPHERE_CATCH_IGNITION_SAVE`, SaveGameV2 plus the environment switches)
and replayed on the bench in seconds. The replay reproduces the game: a 9.3 s flip and a
missed approach.

- *Heading.* The nose arrives 18° off the line to the tower (`noseTowardTower` 0.95); the
  belly roll is only 3–4°. The flip aim then mixes in a side command outside the nose's
  pitch plane, so the ship flips partly in yaw on the gimbals alone. Rotating the same
  state to point at the tower caught in 46.5 s with a 6.5 s flip. Restricting the flip's
  side command to the pitch plane brings the game flip back to 6.1–6.4 s.
- *What still fails.* Three Raptors at their floor over-brake the vertical, so the time
  to the gate stalls (15.7 s to 13.9 s over 5 s) while the side channel runs on. The ship
  then crosses the cradle at 13–20 m/s. On the aligned replay, the 2 → 1 drop at 160 m
  left a 40 m crossrange that one Raptor could not close.

**Next.** Plan the side channel on the vertical time the engines can actually fly, with
the floor included. Keep the second engine until the side plan is done. Then promote the
game-ignition replay to a permanent gate.

### Glide heading aligned before ignition: the game catches (2026-11-23)

**Diagnosis on the replay.** Measured in a horizontal frame fixed at flip end, the aero
residual (measured acceleration less thrust and gravity) stays at 1–5 m/s², almost all
drag. The miss came from the flip:

- The flip left the ship 114 m off the tower line and drifting out at 20 m/s. Its thrust,
  14 m/s² horizontal along the nose, was 18° off that line. The braking then had 85 m/s
  to stop in 670 m.
- With a 30° tilt limit, the attitude loop lost the ship once the angle of attack passed
  45°. The gimbals pinned at 8.5° and the pointing error grew from 20° to 74°.
- With 15°, attitude held but the side authority could not stop the ship before the tower.

**Cause.** Falling at 115 m/s with 31 m/s of glide, the airflow is nearly vertical and
the belly-flop lift lies almost horizontal. The axis leans toward the lift, so the nose
points where the glide guidance asks to accelerate, not where the ship is going. At
ignition the glide planner was still turning (velocity 7° off, nose 18° off).

**Fix.** Over the last `glideFinalAlignmentSeconds` (8 s) before ignition, the glide lift
blends onto the line of sight to the tower (`TowerCatchApproachPlanner.TowerLineLift`).
The blend is full over the last half of the window (`FinalAlignmentWeight`). The nose,
and with it the flip throw, then points at the cradle. Only the single-burn chapter
(`EXOSPHERE_SINGLE_BURN_CATCH=1`) uses it.

**Game results.**

| Rate | `noseTowardTower` at ignition | Engines | Cutoff | Result |
| ---: | ---: | --- | ---: | --- |
| 60 Hz | 1.00 (was 0.95) | 3 → 2 @14.2 s → 1 @21.5 s | 38.3 s | caught |
| 50 Hz | 1.00 | 3 → 2 @13.9 s → 1 @21.5 s | 38.9 s | caught |
| 120 Hz | — | 3 → 2 @14.1 s → 1 @21.7 s | 38.3 s | caught |

The committed law took 78–98 s. Every run settles on two pins with no relight:

| Rate | Peak pin load | Peak compression |
| ---: | ---: | ---: |
| 60 Hz | 1.32 MN | 0.46 m |
| 50 Hz | 1.28 MN | 0.45 m |
| 120 Hz | 1.36 MN | 0.48 m |

No run exceeds the travel or load limits. Flight 15 flew about 18 s, so the remaining
gap is the braking itself.

**Gate.** `Flight15CatchIgnitionReplayTests` replays the aligned ignition captures at
50 and 60 Hz. It requires a belly roll within 5° and a nose toward the tower above 0.99.
It also requires a catch within 50 s, with a first count of three, monotone engine drops
and no relight. The bench catches these states in 41–47 s. It steps control and physics
once per frame, so it is not bit-for-bit with the game.

### Where the 38 s go, and what did not shorten them (2026-11-23)

The game run at 60 Hz spends its time in three parts:

| Phase | Span | What happens |
| --- | ---: | --- |
| Flip | 0–7 s | The ship ends with 86 m/s toward the tower and 630 m to go. |
| Three and two engines | 7–22 s | The ship crosses the cradle at about 30 m/s and overshoots by 50–90 m. |
| One engine | 22–38 s | The ship walks back and descends 130 m. |

**The side channel is at its physical limit from flip end.** Stopping 86 m/s in 630 m needs
5.8 m/s². The ship achieves about 5.6 m/s², close to `vertical·tan 30°`, so the overshoot
is not a planner defect. The commands shown are plant inputs, and with K ≈ 1.4 they deliver
that acceleration. The horizontal speed to stop is the glide toward the tower (33 m/s) plus
the flip throw (about 55 m/s).

**Tried on the replay and reverted:**

- *Planning the flip on its predicted throw.* The throw is (a/ω)(1 − cos θ) along the
  leaning axis. On the replay it cut the 50 Hz catch from 46.8 to 41 s. In the game it
  left 50 and 120 Hz at 39 s and made 60 Hz worse, at 50.6 s.
- *A shorter side horizon* (0.6–0.9 of the time to the gate) and *less plan smoothing*
  (0.03–0.3 s²). Neither changed the command profile or the catch time; 0.6 missed.
- *Less glide at ignition.* Editing the ignition state's velocity leaves the belly flop
  trimmed for the old glide. With a near-vertical airflow the flip then stalls near 50° for
  about 5 s. That makes the edit invalid as a test, not evidence against less glide.

**What would shorten it.** Arrive with less horizontal speed to stop at flip end, or ignite
lower. The vertical alone needs about 25 s from 1425 m at 115 m/s with uniform braking.
Flight 15's 18 s implies a lower or faster-braking ignition. Both quantities are set upstream
by the glide guidance (`glideMaximumClosingSpeedMps`, the ignition lead and clearance). They
must be swept by flying the terminal glide and the burn together, not by editing the
ignition state.

### Glide-and-burn bench, and the kinematic glide attitude (2026-11-23)

**One law for the game and the bench.** The catch glide's lift guidance now lives in the
simulation as `TowerCatchGlideGuidance`. The belly-flop attitude reference filter now lives
there as `BellyFlopAttitudeReference`. Both moved verbatim out of `EDLController`, which now
calls them. A 60 Hz game run after the move reproduced the earlier run exactly:

- ignition 1124 m out;
- engine steps at 14.2, 21.5 and 38.3 s;
- peak pin load 1 321 067 N.

The EDL can now save a terminal-glide state (`EXOSPHERE_CATCH_GLIDE_SAVE`, below
`EXOSPHERE_CATCH_GLIDE_SAVE_ALT_M`, default 4 km). `Flight15CatchBench` restores a game save
and flies the glide, the ignition test and `TowerCatchBurn`. It uses
`IPhysicsStepController`, so control runs at the universe's 0.02 s period, as in the game,
whatever the frame rate. The 60 Hz ignition replay now matches the game's engine steps
within 0.3 s.

**The game's glide attitude is not flown.** The Flight 15 catch chapter starts from the
reentry demonstration setup, which sets `IsTowerCatchDemonstration`. Through the aero
phases the EDL then writes the reference orientation onto the vessel each step, with zero
rate and zero torque command, and drives only the flaps. The game trace shows `pyr=0`, an
attitude error of 0.1° and ω = 0 for the whole glide. Flown physically from the same 4 km
state with the same law, the bench ship does not hold that reference. With pitch saturated,
the error grows at the reference slew rate (6°/s) and the ship ignites with its nose away
from the tower. The single-burn burn itself is physical, because `TowerCatchBurn` runs
before that override. The glide that sets up its ignition is not.

### Physical belly flop: trim limit, roll mode and glide authority (2026-11-23)

These measurements come from the 4 km glide state, at catch mass (215 t), with the ship's
own distributed hull and plate-flap model.

**The 70° entry angle is not trimmable here.** The body-flap moment about the
angle-of-attack axis, holding the flow each step:

| Flap setting | Trim angle of attack |
| --- | ---: |
| Forward flaps out, aft flaps in | ≈ 77° |
| Neutral (45°) | ≈ 92° |
| Forward flaps in, aft flaps out | > 100° |

The trim is statically stable. Flown physically at 70°, the aft flaps sat at 90° and the
forward ones at 0–4° on trim alone, with nothing left to steer.

**The belly flop has a lightly damped roll mode.** Rolling about the long axis at a belly
flop is sideslip. Sideslip loads the two flap pairs unequally, which gives a restoring
2.2 rad/s² per radian. The mode has a natural frequency of 1.47 rad/s and a period of
4.3 s. With the EDL's catch gains (5.5/2.4 on a normalised command), the roll command
banged between its limits and the rates reached about 1 rad/s.

**A correction.** An earlier sweep in this investigation pinned the orientation built from
the initial flow for 5 s. The trajectory turned under it by 3.5° of sideslip. The roll it
measured with symmetric flaps, and the 1.2 rad/s² roll attributed to the flap allocator,
came from that sideslip, not from the model. With the flow tracked each step, symmetric
flaps give zero roll and the allocator returns symmetric deflections.

**The physical descent law holds it.** The Flight 14/15 descent attitude law,
`Flight14DescentController.ComputeGuidanceCommand` (here with the catch glide's lift
reference), does three things:

- it limits the angle of attack to a trim-feasible value, with the 80° Flight 15 terminal
  angle and a 15° trim margin;
- it slew- and lead-limits the belly-first reference;
- it commands through `Flight14AerodynamicAttitude`.

With it, the angle of attack settles at 80° and the peak rate falls from 1.4 to 0.33 rad/s.
`Flight15PhysicalGlideTests` gates this.

**What it exposes next.** The terminal glide guidance assumes 3 m/s² of side lift
(`glideLiftAccelerationMps2`) and turns of 0.08 rad/s. Both were measured on the
kinematic 70° attitude. At a physically trimmed 80° there is far less lift. The guidance
keeps commanding turns the ship cannot make, the nose heading turns at about 0.25 rad/s,
and the ship ignites facing away from the tower. The glide authority, the glide planner
and the ignition lead must be re-identified on the physical belly flop. Only then can the
game chapter drop the demonstration attitude.

### Physical glide authority: lift is there, heading is not steerable (2026-11-23)

These runs fly glide only from the 4 km state, under the physical Flight 15 descent law,
with the lift reference forced to a fixed world direction (`Flight15CatchBench.LiftOverride`,
`GlideOnly`).

**The side acceleration is close to the guidance's assumption.** The lift gives
2.5–3.2 m/s² along the nose heading, against the 3 m/s² in `glideLiftAccelerationMps2`.

**The heading does not follow the command.** Lift-up, 45° and 90° bank requests produced
identical trajectories. In every case the nose heading turned from +0.83 to −1.0 relative to
the requested side in about 18 s, near 9°/s, with peak heading rates of 0.25 rad/s.

**Why.** The flap angular authority at this state:

| Axis | Authority (rad/s²) |
| --- | ---: |
| Long axis (roll / sideslip) | 2.4 |
| Angle of attack | 0.2 |
| Belly normal, which is the nose heading in a near-vertical fall | 0.03–0.09 |

The heading command sat at +1 while an aerodynamic moment turned the ship the other way.
The catch glide guidance steers by pointing the nose, and so the lift, toward a heading
(`ComputeEntryAxisForLift`). That spends the weakest axis, and the kinematic demonstration
attitude hid it.

**Open design question.** A physical terminal glide has to steer through the strong axis,
that is, sideslip held by roll, or accept the heading the airframe settles to and plan
around it. Either path needs the hull's sideslip side force checked against published
Starship data before the glide guidance, the planner and the ignition lead are
recalibrated.

### Sideslip steering is ineffective; the angle of attack steers along the nose (2026-11-23)

The attitude was held kinematically for 2 s each, from the 4 km state, and the measurement
is the aerodynamic acceleration (measured less gravity) in the local horizontal. The table
gives the component along the nose heading, and in brackets the change in the cross
component as the sideslip goes from −15° to +15°:

| Angle of attack | Along the nose | Change across the nose |
| ---: | ---: | ---: |
| 80° | +0.35 m/s² | −0.40 m/s² |
| 90° | −1.14 m/s² | −0.38 m/s² |
| 100° | −2.75 m/s² | −0.33 m/s² |

The cross component at zero sideslip, about 2 m/s², is the drag of the horizontal glide.

- **Sideslip.** About 0.013 m/s² per degree, about ±0.2 m/s² at ±15°. Rolling a circular
  hull about its own axis only turns its crossflow, which produces no side force. In the
  model only the flaps break the symmetry, and they mainly restore the roll
  (0.19 rad/s² at 5° with neutral flaps). This is the expected physics, not a defect. Steering the
  glide by sideslip through roll is not effective.
- **Angle of attack.** About −0.155 m/s² per degree along the nose heading, which makes the
  angle of attack the usable steering channel. It acts only along the nose, and the nose
  heading cannot be held (0.03–0.09 rad/s² of authority, see above).

### Where the belly-flop heading drift comes from (2026-11-23)

**Aerodynamics produce no heading moment.** With the belly facing the flow, turning the
nose heading about a near-vertical flow leaves the flow identical in the body frame. The
measured body moments were constant across all twelve headings from −150° to +180°; only
their projection on the vertical changed.

**The flaps' heading moment is second order.** Moving one plate at a time at an 80° angle
of attack, each flap's moment about the belly normal is the same at 0° and 90° and has zero
slope at its 45° neutral. Flap 0, for example, gives +0.018 rad/s² at both ends and 0 at 45°.
Roll and the angle-of-attack moment are monotonic. The allocator linearises at neutral, so
it sees no heading authority. With the attitude held and no sideslip:

| Command | Response |
| --- | --- |
| Heading +1 | −0.036 rad/s² of heading (wrong sign) and 0.28 rad/s² of roll |
| Roll ±1 | the same flaps, [0, 0, 90, 90], and no roll at all, because trim had saturated the plates |

That positive feedback, together with the heading-pointing glide law, made the heading
turn on the flown glide.

**An allocator fix tried and reverted.** Two changes were tried together:

- inertia-weighted residuals, in angular-acceleration space;
- a second Gauss–Newton start from the current deflections.

They restored the roll channel (±1.14 rad/s² with the right sign) and gave heading +1 the
right sign. They passed every Flight 14 test, including the slow chains and the historical
gates. But they did not make the heading steerable. Commands of 0°, +90° and −90° produced
the same trajectory, with the heading drifting up to 0.15 rad/s at a nearly constant
0.01 rad/s². They also slowed the catch replay (52 s at 50 Hz, no catch in 80 s at 60 Hz) and
loosened the physical glide's angle-of-attack hold, from a mean deviation of 4.6° to 10°.

**Conclusion.** With these plate flaps, hinged on the hull sides at a 45° neutral, the
belly-flop heading has at most about 0.05 rad/s² of second-order authority. It cannot be
held. Steering a physical terminal glide by heading, by sideslip or by a heading-dependent
lift is not available in this model. A physical catch glide has to plan around the heading
the airframe takes. Otherwise the flap model must gain heading authority that is backed by
data on Starship's flap geometry and actuation.

### Published flap data and the model's flap layout (2026-11-23)

**Little is published.** Public sources give no flap hinge geometry, travel, neutral angle
or servo rate. The provenance records already mark travel and rate as unknown
(`rigid_body_fidelity_2026.json`, status `estimated`). Popular sources describe the four
flaps as controlling pitch, roll and yaw independently, and the belly flop as roughly
60–70°. That figure is ambiguous between angle of attack and body attitude, and none of
these sources is primary.

**One published layout change matters.** For Block 2 (Flight 7), SpaceX made the forward
flaps smaller and moved them toward the tip and away from the heat shield. NASASpaceflight
reports that on Block 1 the flaps were 180° apart, and describes the Block 2 forward flaps
as moved up and leeward. The model's V3 forward hinges sit at (0, y, ±4.5), exactly 180°
apart, which is the Block 1 layout. A leeward hinge azimuth would break the symmetry that
makes the heading moment second order. The amount of the shift is not published.

**RCS does not change the picture.** The declared cold-gas thrusters (24 at 1500 N) are
armed, but they add nothing to `GetAttitudeAuthorityBody`. Even fully counted, each gives
about 0.0005 rad/s² about the heading axis at this mass. The forward store is empty.

### Leeward forward-flap hinges tried and reverted (2026-11-23)

A hinge azimuth was added to the plate model, carrying each flap's whole frame round the
hull toward the leeward side. It was swept on the V3 forward flaps from 0° to 30°.

**Each plate gains a first-order heading moment.** At neutral it grows from
0.02 mrad/s² per degree at 0° to 0.52 at 30°.

**The heading still does not follow the command.** Requests of +90° and −90° ended with the
nose on the same physical side. The heading drifts to a fixed direction, and the azimuth
only changes which side it settles on. An earlier read of this sweep took it as
controllability, which was wrong. With a 10° estimate in the V3 data, a heading-hold test
failed. The catch replays also stopped catching, and the physical glide's angle-of-attack
hold loosened to a mean deviation of 9.8°.

**Result.** The code, the data and the provenance are reverted. The leeward shift is
published but its amount is not, and as modelled it does not give heading control. The
glide bench keeps a part-catalogue hook (`Flight15CatchBench.Restore(..., catalogEdit)`) for
geometry experiments.

### Literature review: Starship belly-flop aerodynamics and control (2026-11-23)

Sources read for this review:

- Li Z. et al., "Performance and characteristics analysis on the Starship aerodynamic
  configuration", Acta Aerodynamica Sinica 40(5), 2022 (RANS/SST, Mach 12/2/0.8,
  α 0–120°): <https://html.rhhz.net/KQDLXXB/2022-05-1.htm>.
- Zhang Y. et al., "Numerical simulation on aerodynamic characteristics of new type control
  surface of Starship", Acta Aeronautica et Astronautica Sinica 42(2), 2021, 624058; abstract
  only: <https://hkxb.buaa.edu.cn/EN/abstract/abstract18170.shtml>.
- Chen L. et al., "Optimization of Flip-Landing Trajectories for Starship based on a Deep
  Learned Simulator", arXiv 2508.06520 (S31 RANS, Mach < 0.3, 2-D):
  <https://arxiv.org/abs/2508.06520>.
- NASASpaceflight Flight 7 / Block 2 coverage:
  <https://www.nasaspaceflight.com/2025/01/starship-flight-7-block-2/>.

**What the literature agrees on, and how the model compares:**

| Quantity | Literature | Exosphere (4 km glide state, 215 t) |
| --- | --- | --- |
| Subsonic belly-flop attitude | near 90° (Li: subsonic phase near 90°; Chen: "near-90°") | Flight 15 descent law flies 80° |
| Subsonic trim range | Ma 0.8, X_cg 0.58: forward pair alone 65–89°; aft pair at 80° extends it to 120° (Li) | ≈ 77° with full deflection to > 100° (X_cg ≈ 0.55 from the nose) |
| Lift at 90° | C_L ≈ 0 near 90–95° (Li Ma 0.8; Chen S31 RANS) | +0.35 m/s² along the nose at 80°, −1.1 at 90° |
| Roll versus sideslip | C_lβ < 0, roll-stable (Li) | restoring, 2.2 rad/s² per radian |
| Directional | C_nβ > 0 statically, but the dynamic criterion C_nβ,dyn is stable at Ma 0.8 for 65–120° (Li) | not measured |
| Heading control | by bank-to-turn, rolling about the velocity vector (Li). Forward flaps mainly drive yaw at low α; at high α all three moments still respond to both pairs (Zhang) | the flaps' moment about the belly normal is second order; heading is not controllable |
| Forward flap hinge | follows the nose-cone generatrix; aft hinge parallel to the body axis (Li). Block 2 moved the forward flaps toward the tip (SpaceX via NSF) | all four hinges parallel to the body axis; the V3 forward flaps sit on the barrel, below the 14 m nose |
| Broadside drag | C_D ≈ 9.5–11 on the base area at 90°, Ma 0.8, which gives C_D·S ≈ 640 m² (Li) | ≈ 360 m² (aerodynamic load at 125 m/s, 4 km) |

On drag: the model's crossflow follows Jorgensen with Reynolds dependence. A 9 m cylinder
at about 120 m/s is at a transcritical Reynolds number, around 7·10⁷, where the crossflow
C_d is about 0.5–0.7, not 1.2. The Flight 14 anchor of 363 km/h at 1.2 km fits the lower
figure. Li's case does not report its Reynolds number. The difference is a known Reynolds
effect, not evidence against the model.

**The likely missing physics is the forward hinge tilt.** On a tangent ogive (R = 4.5 m,
L = 14 m, ogive radius 24 m) the surface is inclined 35.7° at the cone base and about 25°
4 m above it. A forward flap hinged along that generatrix has a normal with a component
along the body axis. Its normal force then has an axial part acting at a ±4.5 m lateral
arm, and differential deflection of the pair gives a first-order moment about the belly
normal, which is the heading. A hinge parallel to the axis, as modelled, has no axial
component, which leaves only the second-order term measured above. This matches Zhang's
finding that the forward pair mainly drives yaw.

**Proposed path:**

1. *Model.* Place the V3 forward flaps on the nose cone, with the hinge along the ogive
   generatrix and the tilt derived from the declared nose geometry. Record it as derived,
   with an estimated station. First check: the three-axis channel-sign bench with
   first-order heading authority, and no Flight 14 regressions.
2. *Gates against the literature.* Subsonic trim range against Li (Ma 0.8). C_L near zero
   at 90–95°. Restoring roll with sideslip. Terminal speed against the Flight 14 anchor,
   allowing for the mass.
3. *Guidance.* Turn by banking about the velocity vector, as Li describes. Do the lateral
   targeting while lift exists (α 60–70°, faster flight). Hold trim near 85–90° in the
   terminal phase, where C_L ≈ 0 and the fall is nearly ballistic; predict the arrival;
   use heading control only to point the nose at the tower for the flip.
4. *Game.* Drop the demonstration attitude for the single-burn chapter, re-place the
   ignition, and then shorten the burn.

If step 1 does not give a heading moment of the right sign in both directions, the
fallback is a terminal phase that ignores heading: plan the ignition and flip on the
heading the airframe takes.

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
