namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Xunit;

/// <summary>
/// Data contracts on the aero-thermal geometry the Flight 12 / V3 Starship actually flies.
///
/// <para>Two fields decide whether the orientation-dependent models do anything at all, and
/// both were undeclared on every V3 part:</para>
///
/// <para><c>nose_radius_m</c> — <see cref="ThermalModel.EffectiveNoseRadius"/> blends the
/// sharp nose radius against the hull radius in cos²α, and Sutton-Graves makes q ∝ Rn^(−1/2),
/// so nose-on entry must be materially hotter than broadside. With the field absent,
/// <see cref="Exosphere.Simulation.Parts.PartGraph.NoseRadius"/> falls back to the hull
/// radius, the blend degenerates to a constant, and a nose-first entry heats EXACTLY like a
/// belly-flop. The attitude stops deciding survival.</para>
///
/// <para><c>axial_drag_coefficient</c> — the only drag coefficient
/// <see cref="AerodynamicsModel.ComputeReentryDrag"/> reads from data. (The
/// <c>drag_coefficient</c> field present in every part JSON is not consulted by any
/// aerodynamic model; that is tracked separately.) With it absent the V3 stack ascended on
/// the generic 0.6 cylinder fallback rather than on its own geometry.</para>
/// </summary>
public sealed class StarshipAeroThermalDataContractTests
{
    private const string V3Variant = "starship_flight12_v3_2026.json";

    /// <summary>The value <c>ComputeReentryDrag</c> defaults to when no part declares one.</summary>
    private const double GenericCylinderFallbackCd = 0.6;

    [Fact]
    public void EveryV3FlightPartDeclaresAnAxialDragCoefficient()
    {
        foreach (string partId in LoadVariant(V3Variant).StackTopToBottom)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(PartPath(partId)));

            Assert.True(
                document.RootElement.TryGetProperty("axial_drag_coefficient", out var value)
                    && value.ValueKind == JsonValueKind.Number
                    && value.GetDouble() > 0.0,
                $"{partId}: must declare a positive 'axial_drag_coefficient'. Leaving it at "
                + "zero makes PartGraph.AxialDragCoefficient fall through to the generic "
                + $"{GenericCylinderFallbackCd} cylinder, so the vehicle does not ascend on "
                + "its own aerodynamics. Note that 'drag_coefficient' is a different field "
                + "and is read by no aerodynamic model.");
        }
    }

    [Fact]
    public void TheV3NoseSectionDeclaresAStagnationRadiusSharperThanItsHull()
    {
        var catalog = LoadPartCatalog();
        var variant = LoadVariant(V3Variant);
        string nosePartId = variant.StackTopToBottom[0];
        var definition = catalog[nosePartId];

        Assert.True(definition.NoseRadiusM > 0.0,
            $"{nosePartId}: the leading section must declare 'nose_radius_m'.");
        Assert.True(definition.NoseRadiusM < definition.DiameterM * 0.5,
            $"{nosePartId}: declared nose radius {definition.NoseRadiusM:F2} m is not sharper "
            + $"than its {definition.DiameterM * 0.5:F2} m hull radius, so the attitude blend "
            + "in ThermalModel.EffectiveNoseRadius has nothing to interpolate.");
    }

    /// <summary>
    /// The assembled stack — not a hand-built graph — must resolve a nose radius that keeps
    /// the Sutton-Graves attitude blend live, and the blend must be worth its complexity:
    /// q ∝ Rn^(−1/2) so a 3.0 m nose against a 4.5 m hull is √1.5 ≈ 1.22× hotter nose-on.
    /// </summary>
    [Fact]
    public void TheAssembledV3StackHeatsHarderNoseOnThanBroadside()
    {
        var vessel = BuildV3Stack();
        double hullRadius = vessel.MaximumDiameter * 0.5;

        Assert.Equal(9.0, vessel.MaximumDiameter, 9);
        Assert.True(vessel.NoseRadius < hullRadius,
            $"the assembled V3 stack resolved a {vessel.NoseRadius:F2} m nose radius against a "
            + $"{hullRadius:F2} m hull radius; the attitude-dependent heating blend is inert.");

        const double density = 1.0e-4;   // ~60 km, peak-entry order
        const double speed = 7_000.0;    // m/s, orbital return
        vessel.Orientation = Quaterniond.Identity;

        double noseOn = vessel.ComputeStagnationHeatFlux(
            density, new Vector3d(0.0, -speed, 0.0));
        double broadside = vessel.ComputeStagnationHeatFlux(
            density, new Vector3d(speed, 0.0, 0.0));

        Assert.True(noseOn / broadside >= 1.2,
            $"nose-on flux {noseOn:G6} W/m² is only {noseOn / broadside:F3}× the broadside "
            + $"{broadside:G6} W/m²; attitude must change entry heating materially.");
        Assert.Equal(
            System.Math.Sqrt(hullRadius / vessel.NoseRadius), noseOn / broadside, 6);
    }

    [Fact]
    public void TheAssembledV3StackUsesItsDeclaredAxialDragNotTheGenericFallback()
    {
        var catalog = LoadPartCatalog();
        var variant = LoadVariant(V3Variant);
        var graph = BuildV3Stack().Parts;
        double declaredNoseCd = catalog[variant.StackTopToBottom[0]].AxialDragCoefficient;

        Assert.NotEqual(GenericCylinderFallbackCd, graph.AxialDragCoefficient, 9);
        Assert.Equal(declaredNoseCd, graph.AxialDragCoefficient, 9);

        // The coefficient has to reach the force, not just the property. Axial flight must
        // use it; broadside must use the Newtonian blunt-body value instead.
        var axial = AerodynamicsModel.ComputeReentryDrag(
            1.0e-4,
            new Vector3d(0.0, -7_000.0, 0.0),
            Vector3d.Up,
            graph.VehicleLength,
            graph.MaximumDiameter,
            250.0,
            graph.AxialDragCoefficient);
        double axialArea = System.Math.PI * System.Math.Pow(graph.MaximumDiameter * 0.5, 2.0);
        double expected = 0.5 * 1.0e-4 * 7_000.0 * 7_000.0 * declaredNoseCd * axialArea;

        Assert.Equal(expected, axial.Magnitude, 6);
    }

    /// <summary>
    /// After staging the booster is its own vessel with its own root, so it stops inheriting
    /// the nose section's forebody coefficient and must carry the one for the blunt aft end
    /// it actually leads with on the way down.
    /// </summary>
    [Fact]
    public void ThePostStagingBoosterAndShipEachKeepTheirOwnAxialDrag()
    {
        var catalog = LoadPartCatalog();
        var variant = LoadVariant(V3Variant);
        var vessel = BuildV3Stack();

        double noseCd = catalog[variant.StackTopToBottom[0]].AxialDragCoefficient;
        double boosterCd = catalog[variant.StackTopToBottom[^1]].AxialDragCoefficient;

        var booster = vessel.Stage();
        Assert.NotNull(booster);

        Assert.Equal(boosterCd, booster!.Parts.AxialDragCoefficient, 9);
        Assert.Equal(noseCd, vessel.Parts.AxialDragCoefficient, 9);
        Assert.True(booster.Parts.AxialDragCoefficient > noseCd,
            $"the booster descends aft-first and must be blunter than the {noseCd:F2} forebody "
            + $"coefficient, but it resolved {booster.Parts.AxialDragCoefficient:F2}.");

        // The ship keeps the sharp nose it declared; the booster has no nose part and
        // correctly falls back to its hull radius.
        Assert.True(vessel.NoseRadius < vessel.MaximumDiameter * 0.5);
        Assert.Equal(booster.MaximumDiameter * 0.5, booster.NoseRadius, 9);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Vessel BuildV3Stack() =>
        LoadVariant(V3Variant).Build(LoadPartCatalog()).ToVessel("V3 aero-thermal contract");

    private static VehicleVariantDefinition LoadVariant(string fileName) =>
        VehicleVariantDefinition.LoadFromJson(
            Path.Combine(RepoRoot(), "data", "vehicles", fileName));

    private static PartCatalog LoadPartCatalog() =>
        PartCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "data", "parts"));

    private static string PartPath(string partId) =>
        Path.Combine(RepoRoot(), "data", "parts", $"{partId}.json");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root.");
    }
}
