namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// Phase 23 P6 QA gates. These checks intentionally stay in the pure simulation/test
/// project: no Godot node, generated playtest autoload, or production runtime is changed.
/// Visual acceptance remains a separate state-gated process exercised by the shell contract.
/// </summary>
public sealed class OptimizationPhase23QaTests
{
    [Theory]
    [InlineData("earth")]
    [InlineData("mars")]
    [InlineData("venus")]
    public void SpectralQaGateIsFiniteMonotonicAndUsesTheDeclaredPromotionBoundary(
        string bodyId)
    {
        var body = LoadBody(bodyId);
        var oracle = SpectralAtmosphereOracle.Build(
            body,
            maxOrder: SpectralAtmosphereOracle.ExperimentalOrder,
            sampleCount: 12);

        Assert.Equal(SpectralAtmosphereOracle.BandCount, oracle.BandCentersNm.Count);
        Assert.Equal("reconstructed", oracle.DataProvenance);

        double order2 = oracle.EnergyByOrder(2);
        double order3 = oracle.EnergyByOrder(3);
        double order4 = oracle.EnergyByOrder(4);
        double order5 = oracle.EnergyByOrder(5);
        Assert.True(double.IsFinite(order2) && double.IsFinite(order3)
            && double.IsFinite(order4) && double.IsFinite(order5));
        Assert.True(order2 >= 0.0 && order2 <= order3 && order3 <= order4 && order4 <= order5,
            $"non-monotonic scattering energy for {bodyId}: "
            + $"{order2}, {order3}, {order4}, {order5}");
        Assert.True(order5 - order4 <= order4 - order3 + 1e-12,
            $"order 5 is not converging for {bodyId}: "
            + $"delta34={order4 - order3}, delta45={order5 - order4}");

        double altitude = System.Math.Min(
            10_000.0,
            System.Math.Max(0.0, oracle.AtmosphereTopAltitude * 0.25));
        foreach (var radiance in new[]
        {
            oracle.Evaluate(altitude, System.Math.PI / 4.0, 1.0, 0.2),
            oracle.Evaluate(altitude, -0.01, 0.6, -0.2, solarVisibility: 0.5),
            oracle.Evaluate(altitude, System.Math.PI / 4.0, 1.0, 0.2, solarVisibility: 0.0),
        })
        {
            Assert.True(double.IsFinite(radiance.Energy) && radiance.Energy >= 0.0);
            Assert.All(radiance, value =>
                Assert.True(double.IsFinite(value) && value >= 0.0));
            var rgb = radiance.ToLinearRgb();
            Assert.True(double.IsFinite(rgb.X) && double.IsFinite(rgb.Y)
                && double.IsFinite(rgb.Z));
            Assert.True(rgb.X >= 0.0 && rgb.Y >= 0.0 && rgb.Z >= 0.0);
        }
    }

    [Fact]
    public void PostJumpQaGateClearsRigidBodyAndContactStateBeforeControlResumes()
    {
        var vessel = new Vessel("phase23-jump")
        {
            IsGroundHeld = true,
            IsOnRails = true,
            OrbitalState = new OrbitalElements
            {
                ReferenceBodyId = "earth",
                SemiMajorAxis = 7_000_000.0,
                Eccentricity = 0.02,
            },
            AngularVelocity = new Vector3d(0.3, -0.2, 0.1),
            PitchYawRoll = new Vector3d(0.8, -0.4, 0.2),
            Throttle = 1.0,
            IsAttemptingTowerCatch = true,
        };

        vessel.PrepareForTeleport();

        Assert.False(vessel.IsGroundHeld);
        Assert.False(vessel.IsSurfaceSettled);
        Assert.False(vessel.IsOnRails);
        Assert.Null(vessel.OrbitalState);
        Assert.Equal(Vector3d.Zero, vessel.AngularVelocity);
        Assert.Equal(Vector3d.Zero, vessel.PitchYawRoll);
        Assert.Equal(0.0, vessel.Throttle);
        Assert.False(vessel.IsAttemptingTowerCatch);
        Assert.False(vessel.IsCaught);
        Assert.Equal(0.0, vessel.CatchSettledDuration);
    }

    [Fact]
    public void CatchQaGateAcceptsSettledDualPinsAndRejectsLargeLateralMiss()
    {
        var (caughtUniverse, caughtVessel) = CreateCatchCase(lateralOffset: 0.0);
        for (int i = 0; i < 3_000 && !caughtVessel.IsCaught && !caughtVessel.IsDestroyed; i++)
            caughtUniverse.Tick(0.005);

        Assert.False(caughtVessel.IsDestroyed);
        Assert.True(caughtVessel.IsCaught);
        Assert.False(caughtVessel.IsSurfaceSettled);
        Assert.NotNull(caughtVessel.LastCatchContact);
        Assert.Equal(2, caughtVessel.LastCatchContact!.ContactCount);

        var missUniverse = CreateCatchCase(lateralOffset: 50.0).universe;
        var missVessel = missUniverse.ActiveVessel!;
        for (int i = 0; i < 3_000 && !missVessel.IsDestroyed; i++)
            missUniverse.Tick(0.005);

        Assert.False(missVessel.IsCaught);
    }

    [Fact]
    public void VisualQaSourcesExposeAllP6BoundariesAndGpuGateIsFailClosed()
    {
        string root = FindRepoRoot().FullName;
        string visual = File.ReadAllText(Path.Combine(root, "tools", "visual_playtest.sh"));
        string map = File.ReadAllText(Path.Combine(root, "scripts", "MapViewController.cs"));
        string bridge = File.ReadAllText(Path.Combine(root, "scripts", "SimulationBridge.cs"));
        string spectral = File.ReadAllText(Path.Combine(
            root, "tools", "SpectralValidation", "Program.cs"));
        string gpu = File.ReadAllText(Path.Combine(root, "tools", "perf", "texture_gpu_matrix.sh"));

        Assert.Contains("--ascent", visual);
        Assert.Contains("--edl", visual);
        Assert.Contains("--saturn", visual);
        Assert.Contains("--atmosphere", visual);
        Assert.Contains("--spectral", visual);
        Assert.Contains("--verify-only", visual);
        Assert.Contains("SUMMARY reason=CAUGHT", visual);
        Assert.Contains("CHECK tower_catch caught=True", visual);
        Assert.Contains("SUMMARY reason=SATURN_OK", visual);
        Assert.Contains("ATMOS_STATE", visual);
        Assert.Contains("SPECTRAL_ORACLE", visual);
        Assert.Contains("Key.J when Visible && _selectedTarget != null", map);
        Assert.Contains("CancelGuidanceForTeleport();", bridge);
        Assert.Contains("v.PrepareForTeleport();", bridge);
        Assert.Contains("SPECTRAL_SUMMARY", spectral);
        Assert.Contains("decision=order4-official-order5-diagnostic", spectral);
        Assert.Contains("physical_gpu_gate", gpu);
        Assert.Contains("software_renderer_observed", gpu);
        Assert.Contains("final_status=BLOCKED", gpu);
        Assert.Contains("never PASS hardware gate", gpu);
    }

    private static (Universe universe, Vessel vessel) CreateCatchCase(double lateralOffset)
    {
        var body = new CelestialBody
        {
            Id = "phase23-catch-body",
            Mass = 5.972e24,
            Radius = 6.371e6,
        };
        var nose = new Part(new PartDefinition
        {
            Id = "phase23-catch-nose",
            CategoryStr = "command",
            MassDry = 38_000.0,
            LengthM = 19.0,
            DiameterM = 9.0,
            CatchPinLateralOffsetM = 4.4,
            CatchPinRadiusM = 0.4,
        });
        var vessel = new Vessel("phase23-catch-vessel")
        {
            ReferenceBodyId = body.Id,
            SASEnabled = false,
        };
        vessel.Parts.SetRoot(nose);
        vessel.ConfigureCatchContactsFromParts();

        Vector3d up = Vector3d.Up;
        var cradle = body.Position + up * (body.Radius + 500.0);
        vessel.Position = cradle + up * 3.0;
        vessel.Velocity = up * -1.0;
        vessel.Orientation = Quaterniond.Identity;
        vessel.IsAttemptingTowerCatch = true;
        vessel.CatchTargetPositionWorld = cradle + Vector3d.Right * lateralOffset;
        vessel.CatchTargetUpWorld = up;
        vessel.CatchTargetVelocityWorld = Vector3d.Zero;

        var universe = new Universe { TimeScale = 1.0, ActiveVessel = vessel };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return (universe, vessel);
    }

    private static CelestialBody LoadBody(string id) => CelestialBody.LoadFromJson(
        Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
