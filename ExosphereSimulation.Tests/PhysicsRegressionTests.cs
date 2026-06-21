namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class PhysicsRegressionTests
{
    private const double G0 = 9.80665;
    private const double SeaLevelPressure = 101_325.0;

    [Fact]
    public void GravityAtEarthRadiusMatchesGmOverRSquared()
    {
        var earth = LoadBody("earth");
        var pos = earth.Position + new Vector3d(earth.Radius, 0.0, 0.0);

        var gravity = earth.GetGravityAt(pos);
        double expected = earth.GM / (earth.Radius * earth.Radius);

        AssertWithinRelative(expected, gravity.Magnitude, 1e-12);
        Assert.True(gravity.X < 0.0);
        AssertWithinAbsolute(0.0, gravity.Y, 1e-12);
        AssertWithinAbsolute(0.0, gravity.Z, 1e-12);
    }

    [Fact]
    public void Rk4CircularLeoConservesRadiusAndSpecificEnergy()
    {
        var earth = LoadBody("earth");
        double r0 = earth.Radius + 400_000.0;
        double v0 = System.Math.Sqrt(earth.GM / r0);
        double initialEnergy = SpecificOrbitalEnergy(r0, v0, earth.GM);

        var pos = new Vector3d(r0, 0.0, 0.0);
        var vel = new Vector3d(0.0, v0, 0.0);
        double period = 2.0 * System.Math.PI * System.Math.Sqrt(r0 * r0 * r0 / earth.GM);
        double dt = 10.0;
        int steps = (int)System.Math.Round(period * 3.0 / dt);

        for (int i = 0; i < steps; i++)
        {
            (pos, vel) = RK4Integrator.StepPosVel(
                pos,
                vel,
                i * dt,
                dt,
                (p, _, _) => p.Normalized * (-earth.GM / p.MagnitudeSquared));
        }

        double finalEnergy = SpecificOrbitalEnergy(pos.Magnitude, vel.Magnitude, earth.GM);
        AssertWithinAbsolute(r0, pos.Magnitude, 10.0);
        AssertWithinRelative(initialEnergy, finalEnergy, 1e-9);
    }

    [Fact]
    public void KeplerRoundTripPreservesEllipticOrbitShape()
    {
        var earth = LoadBody("earth");
        double a = earth.Radius + 2_000_000.0;
        double e = 0.18;
        double nu = 37.0 * MathUtils.DEG_TO_RAD;
        double p = a * (1.0 - e * e);
        double r = p / (1.0 + e * System.Math.Cos(nu));

        var pos = new Vector3d(r * System.Math.Cos(nu), r * System.Math.Sin(nu), 0.0);
        var vel = new Vector3d(
            -System.Math.Sqrt(earth.GM / p) * System.Math.Sin(nu),
            System.Math.Sqrt(earth.GM / p) * (e + System.Math.Cos(nu)),
            0.0);

        var elements = OrbitalElements.FromStateVector(pos, vel, earth.GM, earth.Id, epoch: 0.0);
        double period = 2.0 * System.Math.PI * System.Math.Sqrt(a * a * a / earth.GM);
        var (nextPos, nextVel) = elements.GetStateAtTime(period * 0.37, earth.GM);
        var propagated = OrbitalElements.FromStateVector(nextPos, nextVel, earth.GM, earth.Id, period * 0.37);

        Assert.False(elements.IsRadial);
        AssertWithinRelative(a, elements.SemiMajorAxis, 1e-10);
        AssertWithinRelative(e, elements.Eccentricity, 1e-10);
        AssertWithinRelative(elements.SemiMajorAxis, propagated.SemiMajorAxis, 1e-10);
        AssertWithinRelative(elements.Eccentricity, propagated.Eccentricity, 1e-10);
    }

    [Fact]
    public void RadialAndSuborbitalClassificationCatchesVerticalImpact()
    {
        var earth = LoadBody("earth");
        var radial = OrbitalElements.FromStateVector(
            new Vector3d(earth.Radius + 100_000.0, 0.0, 0.0),
            new Vector3d(1_200.0, 0.0, 0.0),
            earth.GM,
            earth.Id,
            epoch: 0.0);

        double r = earth.Radius + 400_000.0;
        double circularSpeed = System.Math.Sqrt(earth.GM / r);
        var validLeo = OrbitalElements.FromStateVector(
            new Vector3d(r, 0.0, 0.0),
            new Vector3d(0.0, circularSpeed, 0.0),
            earth.GM,
            earth.Id,
            epoch: 0.0);

        Assert.True(radial.IsRadial);
        Assert.True(radial.IsSuborbital(earth.Radius));
        Assert.False(validLeo.IsRadial);
        Assert.False(validLeo.IsSuborbital(earth.Radius));
    }

    [Fact]
    public void OnRailsSuborbitalPeriapsisDestroysVesselInsteadOfTunneling()
    {
        var earth = LoadBody("earth");
        var universe = new Universe { TimeScale = 10_000.0 };
        universe.AddBody(earth);

        double ra = earth.Radius + 200_000.0;
        double rp = earth.Radius - 20_000.0;
        double a = (ra + rp) * 0.5;
        double velocityAtApoapsis = System.Math.Sqrt(earth.GM * (2.0 / ra - 1.0 / a));

        var vessel = new Vessel
        {
            Position = new Vector3d(ra, 0.0, 0.0),
            Velocity = new Vector3d(0.0, velocityAtApoapsis, 0.0),
            IsOnRails = true,
            ReferenceBodyId = earth.Id,
        };
        universe.AddVessel(vessel);

        // A suborbital ellipse is NOT destroyed the instant its periapsis dips below the radius
        // (it is still at apoapsis, 200 km up) — it coasts down on rails and is destroyed only
        // when it actually reaches the surface. Propagate until it impacts, and verify it never
        // tunnels through to the far side in the meantime (the R16 "exits through the planet" bug).
        double minAltSeen = double.MaxValue;
        for (int i = 0; i < 8 && !vessel.IsDestroyed; i++)   // up to 8000 s of sim time
        {
            universe.Tick(0.1);
            minAltSeen = System.Math.Min(minAltSeen, earth.GetAltitude(vessel.Position));
        }

        Assert.True(vessel.IsDestroyed);
        Assert.Equal(VesselDestructionCause.GroundImpact, vessel.DestructionCause);
        Assert.False(vessel.IsOnRails);
        Assert.True(vessel.CrashImpactSpeed > 1_000.0);
        Assert.True(earth.GetAltitude(vessel.Position) >= 0.0);
        // Never tunnelled: it never appeared deep below the surface while coasting on rails.
        Assert.True(minAltSeen > -1_000.0, $"Vessel tunnelled to {minAltSeen:F0} m before impact.");
    }

    [Theory]
    [InlineData("super_heavy_booster")]
    [InlineData("starship_engines")]
    public void EngineThrustMassFlowAndIspAreSelfConsistentAtSeaLevelAndVacuum(string partId)
    {
        var engine = new Part(LoadPart(partId)) { ThrottleLevel = 1.0 };

        AssertEngineEquation(engine, 0.0);
        AssertEngineEquation(engine, SeaLevelPressure);
    }

    [Fact]
    public void HeatShieldProtectsOnlyWhenWindwardFaceMeetsFlow()
    {
        var shielded = new Part(new PartDefinition
        {
            Id = "shielded_command",
            CategoryStr = "command",
            MassDry = 100.0,
            HeatTolerance = 1_000.0,
            HasHeatShield = true,
        })
        {
            Temperature = 1_500.0,
        };
        var graph = new PartGraph();
        graph.SetRoot(shielded);

        double protectedRatio = StressSolver.WorstHeatRatio(graph, Vector3d.Up);
        double exposedRatio = StressSolver.WorstHeatRatio(graph, -Vector3d.Up);

        Assert.True(protectedRatio < 1.0);
        Assert.True(exposedRatio > 1.0);
    }

    [Fact]
    public void ReentryHeatingScalesWithSqrtDensityAndVelocityCubed()
    {
        double baseline = ThermalModel.ComputeHeatFlux(0.01, 2_000.0);
        double densityScaled = ThermalModel.ComputeHeatFlux(0.04, 2_000.0);
        double velocityScaled = ThermalModel.ComputeHeatFlux(0.01, 4_000.0);

        AssertWithinRelative(baseline * 2.0, densityScaled, 1e-12);
        AssertWithinRelative(baseline * 8.0, velocityScaled, 1e-12);
    }

    [Fact]
    public void NominalBellyFirstShieldedReentrySurvivesButWrongOrientationBurnsThrough()
    {
        var protectedGraph = new PartGraph();
        protectedGraph.SetRoot(new Part(new PartDefinition
        {
            Id = "shielded",
            CategoryStr = "command",
            MassDry = 100.0,
            HeatTolerance = 2_800.0,
            HasHeatShield = true,
        }));

        var wrongWayGraph = new PartGraph();
        wrongWayGraph.SetRoot(new Part(new PartDefinition
        {
            Id = "wrong_way",
            CategoryStr = "command",
            MassDry = 100.0,
            HeatTolerance = 2_800.0,
            HasHeatShield = true,
        }));

        const double severeFlux = 50_000_000.0;
        var protectedBurned = StressSolver.ApplyThermalLoads(protectedGraph, severeFlux, 10.0, Vector3d.Up);
        var wrongWayBurned = StressSolver.ApplyThermalLoads(wrongWayGraph, severeFlux, 10.0, -Vector3d.Up);

        Assert.Empty(protectedBurned);
        Assert.True(protectedGraph.Parts[0].ThermalDamage < 1.0);
        Assert.Single(wrongWayBurned);
        Assert.True(wrongWayGraph.Parts[0].IsThermallyBurned);
    }

    [Fact]
    public void ReentryWithoutHeatShieldBurnsThrough()
    {
        var graph = new PartGraph();
        graph.SetRoot(new Part(new PartDefinition
        {
            Id = "bare",
            CategoryStr = "command",
            MassDry = 100.0,
            HeatTolerance = 1_200.0,
            HasHeatShield = false,
        }));

        var burned = StressSolver.ApplyThermalLoads(graph, 50_000_000.0, 10.0, Vector3d.Up);

        Assert.Single(burned);
        Assert.True(graph.Parts[0].IsBroken);
        Assert.True(graph.Parts[0].ThermalDamage >= 1.0);
    }

    [Fact]
    public void AerodynamicDragIsBroadsideDominantAndScalesWithDynamicPressure()
    {
        double density = 0.02;
        double speed = 2_500.0;
        var axial = AerodynamicsModel.ComputeReentryDrag(
            density,
            Vector3d.Up * speed,
            Vector3d.Up,
            partCount: 5,
            temperature: 220.0);
        var broadside = AerodynamicsModel.ComputeReentryDrag(
            density,
            Vector3d.Right * speed,
            Vector3d.Up,
            partCount: 5,
            temperature: 220.0);

        var baseline = AerodynamicsModel.ComputeDrag(density, Vector3d.Right * speed, 1.2, 20.0);
        var doubledDensity = AerodynamicsModel.ComputeDrag(density * 2.0, Vector3d.Right * speed, 1.2, 20.0);
        var doubledSpeed = AerodynamicsModel.ComputeDrag(density, Vector3d.Right * speed * 2.0, 1.2, 20.0);

        Assert.True(broadside.Magnitude > axial.Magnitude * 4.0);
        AssertWithinRelative(baseline.Magnitude * 2.0, doubledDensity.Magnitude, 1e-12);
        AssertWithinRelative(baseline.Magnitude * 4.0, doubledSpeed.Magnitude, 1e-12);
    }

    [Fact]
    public void DominantBodyUsesSmallestContainingSphereOfInfluence()
    {
        var universe = Universe.LoadFromDataDirectory(FindRepoRoot().FullName + "/data");
        var sun = universe.GetBody("sun")!;
        var earth = universe.GetBody("earth")!;
        var moon = universe.GetBody("moon")!;

        Assert.Equal(earth, universe.GetDominantBody(earth.Position + new Vector3d(earth.Radius + 400_000.0, 0.0, 0.0)));
        Assert.Equal(moon, universe.GetDominantBody(moon.Position + new Vector3d(moon.Radius + 50_000.0, 0.0, 0.0)));
        Assert.Equal(sun, universe.GetDominantBody(sun.Position + new Vector3d(sun.Radius + 10_000_000.0, 0.0, 0.0)));
    }

    private static void AssertEngineEquation(Part engine, double pressure)
    {
        double thrust = engine.GetThrustMagnitude(pressure);
        double massFlow = engine.GetMassFlow(pressure);
        double isp = engine.GetIsp(pressure);

        Assert.True(thrust > 0.0);
        Assert.True(massFlow > 0.0);
        Assert.True(isp > 0.0);
        AssertWithinRelative(thrust, massFlow * isp * G0, 1e-12);
    }

    private static double SpecificOrbitalEnergy(double radius, double speed, double gm) =>
        0.5 * speed * speed - gm / radius;

    private static CelestialBody LoadBody(string id) =>
        CelestialBody.LoadFromJson(Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));

    private static PartDefinition LoadPart(string id) =>
        PartDefinition.LoadFromJson(Path.Combine(FindRepoRoot().FullName, "data", "parts", $"{id}.json"));

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
            {
                return dir;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static void AssertWithinRelative(double expected, double actual, double tolerance)
    {
        double scale = System.Math.Max(1.0, System.Math.Abs(expected));
        Assert.True(
            System.Math.Abs(expected - actual) <= scale * tolerance,
            $"Expected {actual:R} to be within {tolerance:P6} of {expected:R}.");
    }

    private static void AssertWithinAbsolute(double expected, double actual, double tolerance)
    {
        Assert.True(
            System.Math.Abs(expected - actual) <= tolerance,
            $"Expected {actual:R} to be within {tolerance:R} of {expected:R}.");
    }
}
