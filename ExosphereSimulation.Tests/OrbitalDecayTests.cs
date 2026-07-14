namespace ExosphereSimulation.Tests;

using System.IO;
using Exosphere.Simulation;
using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// R7 acceptance — with the residual thermosphere, a 150 km circular orbit decays
/// slowly and monotonically under RK4 instead of being eternal.
/// </summary>
public sealed class OrbitalDecayTests
{
    [Fact]
    public void LowLeoOrbitDecaysSlowlyAndMonotonically()
    {
        var earth = LoadBody("earth");
        var bodies = new[] { earth };

        double r0 = earth.Radius + 150_000.0;
        double v0 = System.Math.Sqrt(earth.GM / r0);

        var vessel = new Vessel();
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "decay_probe",
            CategoryStr = "command",
            MassDry = 100_000.0,   // Starship-class landing mass (kg)
        }));
        vessel.Position = earth.Position + new Vector3d(r0, 0.0, 0.0);
        vessel.Velocity = new Vector3d(0.0, v0, 0.0);

        double period = 2.0 * System.Math.PI * System.Math.Sqrt(r0 * r0 * r0 / earth.GM);
        double dt = 5.0;
        int stepsPerOrbit = (int)System.Math.Round(period / dt);

        double SemiMajorAxis()
        {
            double r = (vessel.Position - earth.Position).Magnitude;
            double v = vessel.Velocity.Magnitude;
            return 1.0 / (2.0 / r - v * v / earth.GM);
        }

        double initialSma = SemiMajorAxis();
        double previousSma = initialSma;

        for (int orbit = 0; orbit < 3; orbit++)
        {
            for (int i = 0; i < stepsPerOrbit; i++)
            {
                (vessel.Position, vessel.Velocity) = RK4Integrator.StepPosVel(
                    vessel.Position,
                    vessel.Velocity,
                    (orbit * stepsPerOrbit + i) * dt,
                    dt,
                    (p, v, _) => vessel.ComputeNetAccelerationAt(p, v, bodies, earth));
            }

            double sma = SemiMajorAxis();
            Assert.True(sma < previousSma,
                $"orbit {orbit + 1}: SMA must shrink every orbit ({sma:F1} vs {previousSma:F1})");
            previousSma = sma;
        }

        double totalDecay = initialSma - previousSma;
        Assert.True(totalDecay > 10.0,
            $"decay over 3 orbits should be measurable (got {totalDecay:F2} m)");
        Assert.True(totalDecay < 20_000.0,
            $"decay over 3 orbits should be slow, not a brick (got {totalDecay:F0} m)");
    }

    [Fact]
    public void LowLeoDecaysUnderWarpInsteadOfFreezingOnRails()
    {
        var earth = LoadBody("earth");
        double r0 = earth.Radius + 150_000.0;
        double v0 = System.Math.Sqrt(earth.GM / r0);

        var vessel = new Vessel();
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "decay_probe_warp",
            CategoryStr = "command",
            MassDry = 100_000.0,
            DiameterM = 9.0,
            LengthM = 50.0,
        }));
        vessel.Position = earth.Position + new Vector3d(r0, 0.0, 0.0);
        vessel.Velocity = earth.Velocity + new Vector3d(0.0, v0, 0.0);
        vessel.Throttle = 0.0;
        vessel.IsOnRails = true;

        var universe = new Universe { TimeScale = 10.0, ActiveVessel = vessel };
        universe.AddBody(earth);
        universe.AddVessel(vessel);

        Assert.True(universe.RequiresOffRailsPhysics(vessel),
            "150 km LEO must stay off-rails so residual thermosphere drag applies under warp");

        double SemiMajorAxis()
        {
            double r = (vessel.Position - earth.Position).Magnitude;
            double v = (vessel.Velocity - earth.Velocity).Magnitude;
            return 1.0 / (2.0 / r - v * v / earth.GM);
        }

        double initialSma = SemiMajorAxis();
        // ~0.25 orbit of sim time. With force-sensitive warp the outer Tick already
        // substeps at MaxPhysicsStep, so keep this short for CI.
        double period = 2.0 * System.Math.PI * System.Math.Sqrt(r0 * r0 * r0 / earth.GM);
        double simSeconds = 0.25 * period;
        double wallDt = 0.02;
        int steps = (int)System.Math.Ceiling(simSeconds / (universe.TimeScale * wallDt));

        for (int i = 0; i < steps; i++)
            universe.Tick(wallDt);

        Assert.False(vessel.IsOnRails);
        double decay = initialSma - SemiMajorAxis();
        Assert.True(decay > 1.0,
            $"warp LEO must decay measurably (got {decay:F2} m over ~0.25 orbit)");
        Assert.True(decay < 20_000.0,
            $"warp LEO decay must remain gradual (got {decay:F0} m)");
    }

    [Fact]
    public void HighVacuumStayOnRailsWithoutSpuriousDecay()
    {
        var earth = LoadBody("earth");
        // Above ThermosphereTopAltitude (1e6 m) density is hard-zero.
        double r0 = earth.Radius + 1_200_000.0;
        double v0 = System.Math.Sqrt(earth.GM / r0);

        var vessel = new Vessel();
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "vacuum_probe",
            CategoryStr = "command",
            MassDry = 100_000.0,
            DiameterM = 9.0,
        }));
        vessel.Position = earth.Position + new Vector3d(r0, 0.0, 0.0);
        vessel.Velocity = earth.Velocity + new Vector3d(0.0, v0, 0.0);
        vessel.Throttle = 0.0;

        var universe = new Universe { TimeScale = 50.0, ActiveVessel = vessel };
        universe.AddBody(earth);
        universe.AddVessel(vessel);

        Assert.False(universe.RequiresOffRailsPhysics(vessel));

        double sma0 = 1.0 / (2.0 / r0 - v0 * v0 / earth.GM);
        for (int i = 0; i < 200; i++)
            universe.Tick(0.02);

        double r = (vessel.Position - earth.Position).Magnitude;
        double v = (vessel.Velocity - earth.Velocity).Magnitude;
        double sma1 = 1.0 / (2.0 / r - v * v / earth.GM);
        Assert.True(System.Math.Abs(sma1 - sma0) < 1.0,
            $"vacuum rails must not invent decay (Δa={sma1 - sma0:F3} m)");
    }

    private static CelestialBody LoadBody(string id) =>
        CelestialBody.LoadFromJson(Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));

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

        throw new System.InvalidOperationException("Could not locate repository root.");
    }
}
