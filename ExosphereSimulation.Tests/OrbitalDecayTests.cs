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
