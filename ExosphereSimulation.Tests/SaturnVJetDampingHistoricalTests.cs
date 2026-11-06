namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// F4 historical gate: the AS-506 (Apollo 11) S-IC jet damping, −(İ + J_out)·ω, reproduced
/// from flight-reconstructed data. No directly measured Saturn vehicle jet-damping coefficient
/// has been found (the Apollo GSOP-501 171 ft·lbf/(rad/s) is SPS gimbal-actuator damping and is
/// not used). The coefficient is instead fixed by quantities the flight evaluation published:
/// <list type="bullet">
/// <item>MPR-SAT-FE-69-9 (AS-506 flight evaluation), Table 20-10 "Mass Characteristics
/// Comparison", actual: at hold-down arm release 2,899,010 kg, CG X-station 30.276 m, pitch
/// inertia 914.293·10⁶ kg·m²; at outboard engine cutoff signal 827,294 kg, 46.453 m,
/// 440.511·10⁶ kg·m².</item>
/// <item>Same report, Table 5-I: reconstructed F-1 total flowrates 2594, 2569, 2590, 2613 and
/// 2582 kg/s (35–38 s slice).</item>
/// <item>MSFC/Boeing drawing "Saturn V Apollo Flight Configuration": bottom of the F-1 engines
/// (exit planes) at X-station −115.36 in (−2.930 m); the gimbal plane at 100.00 in.</item>
/// </list>
/// J_out about the pitch axis is Σ q (l² + z²) over the exits; İ enters as the pitch-inertia
/// change over the S-IC burn. Tolerances come from the model's declared approximations (equal-
/// volume tank cylinders, estimated S-II/S-IVB tank stations of ±1 m, ±2 m suction-line
/// stations, point exits) and from the weight of each term in D = İ + J_out, not from fitting.
/// Observed (2026-11-06): release CG −0.03 m, J_out +0.6 %; cutoff CG +0.50 m, J/q +2.0 %;
/// ΔI +5.6 % (spacecraft module stations and the S-II/S-IVB tank estimates dominate it).
/// </summary>
[Trait("FidelityGate", "F4")]
[Trait("GateKind", "historical")]
public sealed class SaturnVJetDampingHistoricalTests(ITestOutputHelper output)
{
    private const double ExitStationM = -2.930;
    private const double ReleaseCgM = 30.276, ReleaseMassKg = 2_899_010, ReleasePitchInertia = 914.293e6;
    private const double CutoffCgM = 46.453, CutoffMassKg = 827_294, CutoffPitchInertia = 440.511e6;
    private const double OutboardLateralM = 3.27;
    private static readonly double[] FlightFlowsKgPerS = [2594, 2569, 2590, 2613, 2582];

    private readonly record struct Snapshot(double Elapsed, double MassKg, double CgStationM, double PitchInertia,
        double PitchOutflowTensor, double PitchDampingTensor, double OutflowKgPerS);

    [Fact]
    public void As506SicJetDampingMatchesTheFlightReconstructedMassHistory()
    {
        Snapshot? release = null, cutoff = null, secondStage = null;
        SaturnVAscentFlight.Fly(variantFile: "apollo11_saturn5_as506_1969.json", afterTick: (vessel, elapsed, sicAttached) =>
        {
            if (!sicAttached && secondStage == null)
            {
                var stack = vessel.Parts.GetMassProperties();
                // The upper stack's datum is the S-II J-2 exits: X-station 44.67 m in this stack
                // (F-1 exits -2.930 m, plus the 5.8 m cluster, 36.3 m tank and 5.5 m interstage).
                var j2 = vessel.Parts.Parts.First(p => p.Definition.HasVehicleRole("sii_engine_cluster"));
                Assert.True(vessel.Parts.TryGetPartLocalPosition(j2, out var j2Centre));
                double datumStation = ExitStationM + 5.8 + 36.3 + 5.5 - (j2Centre.Y - 0.5 * j2.Definition.LengthM);
                secondStage = new Snapshot(elapsed, vessel.TotalMass, stack.CenterOfMassBody.Y + datumStation,
                    0.5 * (stack.InertiaBody.M11 + stack.InertiaBody.M33), 0, 0, 0);
            }
            if (!sicAttached || vessel.LastVariableMassStatus != "active" || vessel.LastTransportSample is not { } sample)
                return;
            var mass = vessel.Parts.GetMassProperties();
            var snapshot = new Snapshot(elapsed, vessel.TotalMass, mass.CenterOfMassBody.Y + ExitStationM,
                0.5 * (mass.InertiaBody.M11 + mass.InertiaBody.M33),
                0.5 * (sample.OutflowTensorBody.M11 + sample.OutflowTensorBody.M33),
                0.5 * (sample.JetDampingTensorBody.M11 + sample.JetDampingTensorBody.M33), sample.OutflowKgPerS);
            // Events are vehicle states: the flight's release state is the mass it then had
            // (the model's pad start-up burns less before release than the 6.7 s F-1 sequence).
            if (release == null && snapshot.MassKg <= ReleaseMassKg) release = snapshot;
            if (sample.OutflowKgPerS > 9_000) cutoff = snapshot; // the last four-engine burn step
        });
        Assert.NotNull(secondStage);
        output.WriteLine($"second stage t={secondStage!.Value.Elapsed:F2}s m={secondStage.Value.MassKg:F0} cg={secondStage.Value.CgStationM:F3} I={secondStage.Value.PitchInertia / 1e6:F2}e6");
        Assert.NotNull(release);
        Assert.NotNull(cutoff);
        var start = release!.Value;
        var end = cutoff!.Value;
        output.WriteLine($"release t={start.Elapsed:F2}s m={start.MassKg:F0} cg={start.CgStationM:F3} I={start.PitchInertia / 1e6:F2}e6 "
            + $"J={start.PitchOutflowTensor / 1e6:F3}e6 D={start.PitchDampingTensor / 1e6:F3}e6 q={start.OutflowKgPerS:F0}");
        output.WriteLine($"cutoff t={end.Elapsed:F2}s m={end.MassKg:F0} cg={end.CgStationM:F3} I={end.PitchInertia / 1e6:F2}e6 "
            + $"J={end.PitchOutflowTensor / 1e6:F3}e6 D={end.PitchDampingTensor / 1e6:F3}e6 q={end.OutflowKgPerS:F0}");

        // Flight-derived exhaust-flux moments about the reconstructed centre of mass.
        double releaseLever = ReleaseCgM - ExitStationM, cutoffLever = CutoffCgM - ExitStationM;
        double flightRelease = FlightFlowsKgPerS.Sum() * releaseLever * releaseLever
            + FlightFlowsKgPerS.Take(4).Sum() * OutboardLateralM * OutboardLateralM;
        double flightCutoff = FlightFlowsKgPerS.Take(4).Sum() * (cutoffLever * cutoffLever + OutboardLateralM * OutboardLateralM);
        output.WriteLine($"flight J release {flightRelease / 1e6:F3}e6, cutoff {flightCutoff / 1e6:F3}e6; "
            + $"flight ΔI {(CutoffPitchInertia - ReleasePitchInertia) / 1e6:F2}e6, model ΔI {(end.PitchInertia - start.PitchInertia) / 1e6:F2}e6");

        // Mass: the vehicle data and burn timeline close the flight's masses.
        Assert.InRange(start.MassKg / ReleaseMassKg, 0.99, 1.01);
        Assert.InRange(end.MassKg / CutoffMassKg, 0.98, 1.02);
        // Centre of mass: ±0.3 m at release and ±0.6 m at cutoff, the effect of the ±1 m
        // estimated upper-stage tank stations on 480 t of S-II load (0.15 m and 0.45 m) plus the
        // equal-volume tank cylinders and the ±2 m suction-line stations.
        Assert.InRange(start.CgStationM, ReleaseCgM - 0.3, ReleaseCgM + 0.3);
        Assert.InRange(end.CgStationM, CutoffCgM - 0.6, CutoffCgM + 0.6);
        // Exhaust-flux moment J_out at release against the reconstructed flowrates: 2·Δl/l from
        // the CG band plus the 1 % engine-to-engine flowrate spread.
        Assert.InRange(start.PitchOutflowTensor / flightRelease, 0.97, 1.03);
        // At cutoff the flight's flowrates are not tabulated (Table 5-I is the 35-38 s slice; F-1
        // flow and thrust rise with the acceleration head on the pumps), so the flow-weighted
        // lever J/q is checked against the reconstructed CG: ±2·0.6/49.4 ≈ ±2.5 %.
        double cutoffLeverSquared = cutoffLever * cutoffLever + OutboardLateralM * OutboardLateralM;
        Assert.InRange(end.PitchOutflowTensor / end.OutflowKgPerS / cutoffLeverSquared, 0.975, 1.025);
        // İ over the burn, as the pitch-inertia drop between the two events. At release |İ| is
        // under a tenth of J_out (D − J below), so ±15 % here holds the jet-damping coefficient
        // D = İ + J_out within ±5 % together with the J_out band above.
        Assert.True(System.Math.Abs(start.PitchDampingTensor - start.PitchOutflowTensor) < 0.1 * start.PitchOutflowTensor);
        Assert.InRange((end.PitchInertia - start.PitchInertia) / (CutoffPitchInertia - ReleasePitchInertia), 0.85, 1.15);
        // The vehicle damps: −(İ + J_out)ω opposes a pitch rate through the whole burn.
        Assert.True(start.PitchDampingTensor > 0 && end.PitchDampingTensor > 0);
    }
}
