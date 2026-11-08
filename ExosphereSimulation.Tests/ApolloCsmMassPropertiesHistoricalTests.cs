namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Parts;
using Xunit.Abstractions;

/// <summary>
/// F6 historical gate: CSM-107 module mass properties against NASA SP-238 (Apollo 11 Mission
/// Report, 1971) appendix B table B-I, a postflight reconstruction from measured module weights
/// and CGs and calculated inertias. The CM and SM dry centres and gyrations are derived from
/// the two isolated-module rows (CM at entry, SM after CM/SM separation, with the table 8-X,
/// 8-XI and 8-XII residuals); see <c>data/provenance/as506_csm_mass_properties_1971.json</c>.
/// The docked-free CSM rows at transposition and at transearth-injection ignition are not used
/// by the derivation and test the composition at full and partial SPS fill.
/// </summary>
[Trait("FidelityGate", "F6")]
[Trait("GateKind", "historical")]
public sealed class ApolloCsmMassPropertiesHistoricalTests(ITestOutputHelper output)
{
    private const double Lb = 0.45359237, Inch = 0.0254, SlugFt2 = 1.3558179483314;

    private readonly record struct Loading(double CmMonoLb, double SpsFuelLb, double SpsOxidizerLb, double SmMonoLb);

    private readonly record struct Row(double WeightLb, double CgXaIn, double Ixx, double Iyy, double Izz)
    {
        public double MassKg => WeightLb * Lb;
        public double AxialGyrationM => System.Math.Sqrt(Ixx * SlugFt2 / MassKg);
        public double TransverseGyrationM => System.Math.Sqrt(0.5 * (Iyy + Izz) * SlugFt2 / MassKg);
    }

    private readonly record struct Model(double MassKg, double CgXaIn, double AxialGyrationM, double TransverseGyrationM);

    // Table B-I rows (printed p. 212). SPS loads from table 8-X: 15712/25091 lb loaded,
    // 1958/3106 lb remaining at separation; transearth injection used 36965.7 - 26792.7 lb,
    // split at the engine's nominal 1.6 mixture ratio. SM RCS from table 8-XI.
    private static readonly Row CmEntry = new(12095.5, 1040.5, 6253, 5463, 4994);
    private static readonly Row SmSeparation = new(14549.1, 896.1, 9143, 14540, 16616);
    private static readonly Row CsmTransposition = new(63473.0, 934.0, 34445, 76781, 79530);
    private static readonly Row CsmTransearthIgnition = new(36965.7, 943.8, 20681, 56775, 63303);
    private const double TransearthBurnLb = 36965.7 - 26792.7;

    [Fact]
    public void IsolatedModulesCloseTheirDerivationRows()
    {
        var cm = Measure(["apollo11_command_module_csm107"], new Loading(245.9, 0, 0, 0));
        var sm = Measure(["apollo11_service_module_csm107", "apollo11_sps_engine_csm107"],
            new Loading(0, 1958, 3106, 780));
        Report("CM entry", cm, CmEntry);
        Report("SM separation", sm, SmSeparation);
        // The derivation solves these rows' CG and gyration radii exactly (to the 1e-7 m
        // rounding of the data); radii, not inertias, so the catalogue keeps its launch mass.
        Assert.Equal(CmEntry.CgXaIn, cm.CgXaIn, 3);
        Assert.Equal(SmSeparation.CgXaIn, sm.CgXaIn, 3);
        Assert.Equal(CmEntry.AxialGyrationM, cm.AxialGyrationM, 4);
        Assert.Equal(CmEntry.TransverseGyrationM, cm.TransverseGyrationM, 4);
        Assert.Equal(SmSeparation.AxialGyrationM, sm.AxialGyrationM, 4);
        Assert.Equal(SmSeparation.TransverseGyrationM, sm.TransverseGyrationM, 4);
        // Mass: the catalogue keeps launch loading. The SM's 390 lb excess is the 388.9 lb of
        // cryogenics table 8-XII reports consumed by separation; the CM keeps launch consumables.
        Assert.InRange(sm.MassKg / Lb - SmSeparation.WeightLb, 388.9 - 5.0, 388.9 + 5.0);
        Assert.InRange(cm.MassKg / CmEntry.MassKg, 1.0, 1.02);
    }

    [Fact]
    public void CsmCompositionMatchesTheIndependentTranspositionAndTransearthRows()
    {
        string[] csm = ["apollo11_command_module_csm107", "apollo11_service_module_csm107", "apollo11_sps_engine_csm107"];
        var transposition = Measure(csm, new Loading(245.9, 15712, 25091, 1340));
        var transearth = Measure(csm, new Loading(245.9, 1958 + TransearthBurnLb / 2.6,
            3106 + TransearthBurnLb * 1.6 / 2.6, 800));
        Report("CSM transposition", transposition, CsmTransposition);
        Report("CSM TEI ignition", transearth, CsmTransearthIgnition);
        foreach (var (model, row) in new[] { (transposition, CsmTransposition), (transearth, CsmTransearthIgnition) })
        {
            // Mass closes to the cryogenics consumed before the event (at most 1.5 %).
            Assert.InRange(model.MassKg / row.MassKg, 0.995, 1.015);
            // CG ±0.2 m: the SPS propellant (up to 64 % of the CSM) is a uniform part-centred
            // store, while the flown load sits in four off-axis storage/sump tanks that drain
            // storage-first; half its settled-column spread over the ~0.5 m it can move
            // weighted by the propellant fraction bounds the composite offset.
            Assert.InRange((model.CgXaIn - row.CgXaIn) * Inch, -0.2, 0.2);
            // Gyration ±10 % (inertia about ±20 %): the same store has the 1.955 m module
            // radius and length instead of four 1.1–1.3 m-diameter tanks at ~1.3 m from the axis.
            Assert.InRange(model.AxialGyrationM / row.AxialGyrationM, 0.9, 1.1);
            Assert.InRange(model.TransverseGyrationM / row.TransverseGyrationM, 0.9, 1.1);
        }
    }

    private Model Measure(string[] stackTopToBottom, Loading loading)
    {
        var assembly = new VesselAssembly(LoadParts());
        var current = assembly.AddRoot(stackTopToBottom[0]);
        foreach (string id in stackTopToBottom.Skip(1))
            current = assembly.AttachPartAutomatically(current.InstanceId, id);
        var vessel = assembly.ToVessel("csm-mass");
        foreach (var part in vessel.Parts.Parts)
        {
            if (part.Definition.HasVehicleRole("command_module"))
                part.Monopropellant = loading.CmMonoLb * Lb;
            if (part.Definition.HasVehicleRole("service_module"))
            {
                part.LiquidFuel = loading.SpsFuelLb * Lb;
                part.Oxidizer = loading.SpsOxidizerLb * Lb;
                part.Monopropellant = loading.SmMonoLb * Lb;
            }
        }
        var positions = vessel.Parts.ComputePartLocalPositions();
        // CSM X_A = 1000 in at the CM aft plane, which is the SM forward plane
        // (SNA-8-D-027(I) rev. 3, figure 2-2).
        var top = vessel.Parts.Parts.First(p => p.Definition.Id == stackTopToBottom[0]);
        double interface1000 = top.Definition.HasVehicleRole("command_module")
            ? positions[top].Y - 0.5 * top.Definition.LengthM
            : positions[top].Y + 0.5 * top.Definition.LengthM;
        var mass = vessel.Parts.GetMassProperties();
        double m = mass.Mass;
        var inertia = mass.InertiaBody;
        return new Model(m, 1000.0 + (mass.CenterOfMassBody.Y - interface1000) / Inch,
            System.Math.Sqrt(inertia.M22 / m), System.Math.Sqrt(0.5 * (inertia.M11 + inertia.M33) / m));
    }

    private void Report(string name, Model model, Row row) =>
        output.WriteLine($"{name}: model {model.MassKg / Lb:F1} lb, X_A {model.CgXaIn:F1} in, k axial "
            + $"{model.AxialGyrationM:F4} m, k transverse {model.TransverseGyrationM:F4} m | SP-238 {row.WeightLb} lb, "
            + $"X_A {row.CgXaIn} in, k {row.AxialGyrationM:F4} / {row.TransverseGyrationM:F4} m");

    private static PartCatalog LoadParts()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return PartCatalog.LoadFromDirectory(Path.Combine(directory!.FullName, "data", "parts"));
    }
}
