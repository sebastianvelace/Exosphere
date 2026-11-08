# MA-6 native propulsion reconstruction worksheet

Status: investigation only. The isolated experiments below do not change production or
close F6. Retain the existing ascent, orbit, crew-load and full-mission acceptance bounds.

## Primary configuration and propellant utilization

[Detailed Test Objectives for NASA Mission MA-6](https://prdownloads.sourceforge.net/mscorbaddon/Mercury_AtlasTestObjectives.pdf?download=),
TOR-930(2101)-3, 10 November 1961, identifies these relevant inputs:

- Section 5.2, printed p. 5-1/PDF p. 36: MA-5 on 109D is a redesignation without
  significant hardware differences from the preceding MA-2 system. Do not select
  a later uprated Atlas solely by the MA-5 label.
- Section 5.2.10.1: booster NA 115102, sustainer NA 225102 and vernier chambers.
- Sections 6.10.1-2, printed pp. 6-7/6-8, PDF pp. 68-69: PU-computer nominal
  liftoff weight ratio 2.28:1; sustainer mixture/valve positions 1.93/42.9,
  2.27/28.5 and 2.61/23.0 (the limiting mixtures are marked -15%/+15%).
- Appendix B, pp. B-1 to B-4: preflight loads, propulsion, pitch rates and event
  trajectory. These are a preflight simulation, distinct from the final MA-6 measurements.

The published source therefore includes an active propellant-utilization system.
The present engine model's fixed mixture ratio cannot reproduce that control mechanism.
Its valve settings are not a measured actuator response or a thrust/Isp-versus-mixture
map. Resolve those definitions before implementing a physical PU controller; do not
replace the missing system by fitting one vacuum Isp or restoring propellant in flight.

Original p. B-4 reads **.312 lb** of unexpended fuel (0.141521 kg), not the negative
312 lb emitted by OCR. It lists 712 lb of unexpended oxidizer. Available loads less
event mass loss and jettison leave 714 lb; the remaining 1.688 lb/0.765664 kg difference
is compatible with source rounding. See `historical_recalibration.md` for the arithmetic
and `/tmp/exo-f6-atlas-residual-detail.png` for the magnified source.

## Isolated guidance and mixture comparisons

`/tmp/exo-f6-atlas-closedloop` retains the earlier approximate engine ratings and source
preflight loads. It uses the original first-stage pitch rates in a launch-fixed inertial
frame, BECO 131.3 s, separate booster jettison 134.1 s, and tower jettison 154.1 s.
`SelectEngineCount(0)` commands booster cutoff before its mass is jettisoned. The original
2 degree/s turn runs from BECO+5 through BECO+24; an engineering time-to-go insertion
controller takes over at 160 s. The latter is a reconstruction using the existing
`ClosedLoopInsertionGuidance`, not the original Atlas guidance computer implementation.
SECO is 303.8 s. The source-defined liftoff inventory/running-engine initial condition
remains an isolated benchmark; it does not model the historical ground-start sequence.

| Experiment | Model cutoff altitude (km) | Radial/tangential velocity (m/s) | Final mass (kg) | Original gate |
| --- | --- | --- | --- | --- |
| Approximate ratings, old 2.25 mixtures | 161.210 | 72 / 7090 | 5935 | FAIL |
| Same ratings; source nominal 2.273 booster / 2.27 sustainer | 161.025 | 25 / 7489 | 5202 | FAIL |
| 1961 Atlas-Centaur nominal main-engine envelope, no verniers | 161.101 | 13 / 7537 | 5202 | FAIL |
| Same envelope with ideal bounded PU allocation, no verniers | 161.081 | 3 / 7774 | 4809 | FAIL |

The original preflight cutoff is 528427 ft/25695 ft/s (161.065 km/7831.836 m/s),
10465 lb/4746.844 kg. Model altitude is geodetic; source altitude and insertion-reference
datums must be reconciled before presenting an exact altitude residual. None of these
experiments reaches the original orbital acceptance. The first two differ only in mixture
inputs, showing that a constant 2.25 assumption materially changes the available impulse.
This does not identify the correct time-varying PU response. The 160 s feedback handover
also produces a transient under the old pointing gains; do not attribute every velocity
residual solely to hardware performance.

The last row is an allocation bound: after booster jettison it commands the remaining
oxidizer/fuel ratio needed to leave the source residuals, clipped to 1.93-2.61. It retains
the rated thrust/Isp map and has no measured valve dynamics or mixture-performance response.
It increases periapsis to about 42.9 km, still below the unchanged 140 km minimum. The
single-vessel test mutates the shared part definition for this probe only; that is not a
production command API and must not be copied into the runtime. It demonstrates why PU
matters without establishing a complete Atlas model.

Local evidence, including numeric failure output and preserved TRX:

- `/tmp/exo-f6-atlas-closedloop.log` and `/tmp/exo-f6-atlas-closedloop-results/closedloop.trx`.
- `/tmp/exo-f6-atlas-closedloop-mixture.log` and
  `/tmp/exo-f6-atlas-closedloop-mixture-results/closedloop-mixture.trx`.
- `/tmp/exo-f6-atlas-handbook-no-vernier.log` and
  `/tmp/exo-f6-atlas-handbook-no-vernier-results/handbook-no-vernier.trx`.
- Earlier approximate-rating files are backed up in
  `/tmp/exo-f6-atlas-closedloop-before-mixture` and `/tmp/exo-f6-atlas-before-handbook`.
- Ideal allocation: `/tmp/exo-f6-atlas-ideal-pu.log` and
  `/tmp/exo-f6-atlas-ideal-pu-results/ideal-pu.trx`; pre-probe test source is retained in
  `/tmp/exo-f6-atlas-closedloop-before-ideal-pu.cs.txt`.

## Contemporaneous nominal envelope, not a 109D acceptance rating

Melvyn Savage's *Launch Vehicle Handbook*, 11 August 1961 (reproduced as NASA-TM-74984),
section F, printed F-7/PDF p. 63, was inspected in the original scan. Its Atlas-Centaur
table lists these per-chamber values:

| Chamber | Sea-level thrust (lbf) | Vacuum thrust (lbf) | Sea-level Isp (s) | Vacuum Isp (s) |
| --- | --- | --- | --- | --- |
| Each of two boosters | 154500 | 178500 | 251.19 | 291.00 |
| Sustainer | 57000 | 80732 | 214.7 | 308.1 |
| Each of two verniers, as rounded in the table | 1000 | 1000 | 207.5 | 238.8 |

This is a preliminary-planning compilation for Atlas-Centaur, not the specific 109D
flight acceptance. The coarse vernier thrust figures and different Isp values must not
be interpreted as an exact constant-flow map. Match configuration and conditions first;
the last experiment above omits verniers and is explicitly incomplete.

The historical NTRS link currently resolves to a missing page. The original NASA scan
was retrieved from its [Commons facsimile](https://commons.wikimedia.org/wiki/Special:Redirect/file/NASA_Launch_Vehicle_Handbook.pdf),
not from a secondary numerical transcription. Local PDF:
`/tmp/exo-f6-nasa-launch-handbook-1961.pdf` (8424426 bytes, 132 reproduced pages),
SHA-256 `fd6d790b2c066315f75bc77468f032296715a14193499d3b85388c9e751232d1`.
Inspected table: `/tmp/exo-f6-atlas-centaur-1961-performance.png`.

## Next physical unit

Reconcile 109D's engine acceptance/flow basis and model the separate verniers together
with PU behavior. A runtime mixture command must pass through the existing funded
per-engine/store flow solve and its F4 transport reactions; mutable shared catalogue
definitions are not per-vessel control state. Address persistence and the physical valve/
performance response explicitly. Validate initial inventory, event masses, mass-flow and
thrust histories before orbit and full-mission gates. Keep all current gates unchanged;
a successful engineering orbit alone would not prove a native historical reconstruction.

`PartGraph.ConsumePropellantFromPool` currently assigns each engine-instance demand its
own mass flow but the owning part's `Definition.MixtureRatio`. Merely placing several model IDs in one mixed engine cluster
does not give each mount a different feed mixture. Separate vernier parts or an explicitly
extended per-engine funding contract are required if their mixture differs from the
controlled sustainer; preserve source-store ownership and funded F4 reactions either way.
