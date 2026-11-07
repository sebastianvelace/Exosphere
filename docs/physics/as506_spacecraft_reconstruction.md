# AS-506 spacecraft reconstruction worksheet

Status: source reconciliation in progress. The experiments below do not change production
data and do not close F6. Keep the existing historical acceptance limits.

## Independent module interfaces

[CSM/LM Spacecraft Operational Data Book, volume I, part I, revision 3](https://www.ibiblio.org/apollo/Documents/CSM_Data_Book_Part_I_Rev_3_Partial.pdf),
15 April 1970, figure 2-2, printed p. 2-3 (PDF p. 16), gives both the Saturn V structural
stations and the CSM frame. The original drawing was inspected. Dimensions exclude CM
ablative material; it is a configuration drawing, not a mission-specific mass measurement.

| Interface | Launch-vehicle station (in) | Station (m) |
| --- | --- | --- |
| SM aft / SLA forward | 3594.555 | 91.301697 |
| CM aft | 3756.555 | 95.416497 |
| CM forward | 3890.055 | 98.807397 |
| Tower separation plane on CM | 3841.182 | 97.566023 |
| LES tip | 4240.792 | 107.716117 |

At the CM aft plane, the drawing equates launch station 3756.555 in with CSM X_A=1000 in.
Thus `X_LV_m = (X_A_in + 2756.555) * 0.0254`; these are not interchangeable origins.
The SM structural span is 162 in = 4.1148 m. The SPS nozzle occupies space inside the
SLA rather than adding its entire length between the SLA forward plane and the SM body.
The tower reaches below the CM forward plane; the LES attachment is not its lowest point.

The current catalogue instead places the CM aft plane at X=98.870 m, **3.453503 m too
high**, with a 6.5 m SM and another 1 m serial SPS part. Its LES tip is 107.770 m, so the
nearly correct total height masks the internal interface error. This is an independently
identified geometry defect, not a shift inferred solely from aggregate CG.

A read-only in-memory experiment changes the SM span, overlaps the SPS attachment plane,
uses the published CM span and LES bounds, and retains all production component masses
and their existing internal-CG assumptions. Its observations are:

| Configuration | Spacecraft mass (kg) | CG X (m) | CM aft X (m) | LES tip X (m) |
| --- | --- | --- | --- | --- |
| Production baseline | 49821.434822 | 94.005595 | 98.870000 | 107.770000 |
| Source interface geometry | 49821.434822 | 92.425324 | 95.484800 | 107.784420 |
| Also infer jettison-package CG below | 49821.434822 | 92.451338 | 95.484800 | 107.784420 |

The 68 mm remaining interface offset comes from the unchanged lower assembly/ring datum.
These are mass-property observations, not a flight acceptance run. Do not copy the probe's
in-memory edits into production without provenance, interface regressions, ascent/docking
checks and visual verification of the overlapping geometry.

## Postflight mass-property constraints

[NASA SP-238, Apollo 11 Mission Report](https://ntrs.nasa.gov/api/citations/19710015566/downloads/19710015566.pdf),
1971, appendix B, printed pp. 211-212 (table B-I, PDF p. 224), states that module weights
and CGs were measured and updated for changes after weighing; inertias were calculated.
The table distinguishes these phases:

| Phase | Mass (lb) | CSM X_A CG (in) |
| --- | --- | --- |
| Liftoff | 109666.6 | 847.0 |
| Earth-orbit insertion | 100756.4 | 807.2 |
| CSM at transposition/docking | 63473.0 | 934.0 |

The liftoff mass is 49743.933004 kg and, using the drawing's frame translation, CG is
91.530297 m. These are additional postflight constraints. FER table 20-10 instead lists
48626 kg / 91.658 m, whereas FER table 20-1 lists 49735 kg. Retain the source/basis
difference; do not alter a launch load to the lighter table merely to reduce a residual.

Subtracting the first two phase mass moments gives a removed group of 8910.2 lb and
X_A=1297.057768 in, or launch X=102.961764 m. Its mass agrees with the catalogue's
8910 lb LES/BPC allocation to 0.2 lb. This is a derived jettison-package CG, subject to
phase consumables, table rounding and the assumption that the mass difference belongs
to that package. It is not a direct measurement of the tower alone.

Subtracting the CSM transposition mass/moment from the insertion total gives a remaining
group of 37283.4 lb = 16911.465768 kg, X_A=591.329763 in, or launch X=85.036273 m.
This group includes LM/SLA-associated hardware. It is not the LM alone. The current
SLA-plus-LM part is a uniform 16879.986457 kg at X=87.070 m and the model also carries a
separate 100 kg ring. Reconcile that ownership and the CSM's intervening consumables
before adopting the subtraction as an exact component allocation.

Using all three CG coordinates and the parallel-axis theorem, the same subtraction gives
remaining-group central inertia diagonals approximately 43897.56 / 55410.07 / 55312.03
kg m2 in source X/Y/Z axes. The corresponding radii of gyration are 1.61113 / 1.81010 /
1.80850 m. The subtraction uses insertion diagonals 67108 / 713136 / 715672 and CSM
diagonals 34445 / 76781 / 79530 slug ft2. Phase and allocation uncertainty also applies
to these derived quantities; map source axes and product-of-inertia signs explicitly
before using them in the simulation's local Y-up frame.

## Local evidence

- Source report: `/tmp/exo-f6-apollo11-mass-report.pdf` and `.txt`.
- Configuration drawing: `/tmp/exo-f6-csm-data-book.pdf`, `.txt`, and
  `/tmp/exo-f6-csm-stations.png`.
- Read-only helper: `/tmp/exo-f6-module-probe/`; it references the built production DLL.
- Baseline component stations: `/tmp/exo-f6-module-stations.log`.
- In-memory comparison: `/tmp/exo-f6-module-stations-source-geometry.log`.

Next implementation should correct the independently sourced interfaces first and then
apply phase-consistent component mass geometry. Preserve physical thrust/flow references,
split ownership and the original ascent/TLI/docking/conservation gates.
