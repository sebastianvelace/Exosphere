namespace ExosphereSimulation.Tests;

using System.IO;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

/// <summary>
/// Sutton-Graves is a SPHERE stagnation-point correlation, but every caller flew it with a
/// single fixed hull radius regardless of attitude. These tests pin down
/// <see cref="ThermalModel.EffectiveNoseRadius"/>: it must reproduce the old hull-radius-only
/// behaviour exactly at broadside (cosAlpha = 0, the zero-regression guarantee), get hotter
/// nose/tail-on where the true nosecone curvature is sharper, and never do anything
/// discontinuous or physically nonsensical (radius exceeding the hull, going non-positive) in
/// between.
/// </summary>
public sealed class StagnationHeatingAttitudeTests
{
    [Fact]
    public void BroadsideNoseRadiusIsExactlyTheHullRadius()
    {
        Assert.Equal(4.5, ThermalModel.EffectiveNoseRadius(4.5, 3.0, 0.0), 12);
    }

    [Fact]
    public void NoseOnStagnationIsHotterThanBroadside()
    {
        const double density  = 0.01;
        const double velocity = 4_000.0;
        const double hullRadius = 4.5;
        const double noseRadius = 3.0;

        double rn0 = ThermalModel.EffectiveNoseRadius(hullRadius, noseRadius, 0.0);
        double rn1 = ThermalModel.EffectiveNoseRadius(hullRadius, noseRadius, 1.0);

        double q0 = ThermalModel.ComputeHeatFlux(density, velocity, rn0);
        double q1 = ThermalModel.ComputeHeatFlux(density, velocity, rn1);

        double expectedRatio = System.Math.Sqrt(hullRadius / noseRadius);
        double actualRatio = q1 / q0;

        Assert.True(q1 > q0, $"nose-on flux ({q1:F1}) must exceed broadside flux ({q0:F1})");
        Assert.Equal(expectedRatio, actualRatio, 9);
    }

    [Fact]
    public void EffectiveNoseRadiusIsContinuousAndMonotonicInCosAlpha()
    {
        const double hullRadius = 4.5;
        const double noseRadius = 3.0;
        const int steps = 200;

        double previous = ThermalModel.EffectiveNoseRadius(hullRadius, noseRadius, 0.0);
        for (int i = 1; i <= steps; i++)
        {
            double cosAlpha = i / (double)steps;
            double current = ThermalModel.EffectiveNoseRadius(hullRadius, noseRadius, cosAlpha);

            Assert.True(current <= previous + 1e-9,
                $"radius must shrink monotonically toward the nose as cosAlpha rises (prev {previous:F6} at step {i - 1}, current {current:F6} at step {i})");
            Assert.True(previous - current < 0.02,
                $"no sample-to-sample jump should exceed 0.02 m (prev {previous:F6}, current {current:F6})");

            previous = current;
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(-100.0)]
    [InlineData(2.0)]
    [InlineData(4.5)]
    [InlineData(100.0)]
    public void NoseRadiusNeverExceedsHullRadiusNorGoesNonPositive(double declaredNoseRadius)
    {
        const double hullRadius = 4.5;

        foreach (var cosAlpha in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            double effective = ThermalModel.EffectiveNoseRadius(hullRadius, declaredNoseRadius, cosAlpha);

            Assert.True(effective > 0.0,
                $"effective nose radius must stay positive (got {effective} for declared={declaredNoseRadius}, cosAlpha={cosAlpha})");
            Assert.True(effective <= hullRadius + 1e-9,
                $"effective nose radius must never exceed the hull radius (got {effective} for declared={declaredNoseRadius}, cosAlpha={cosAlpha})");
        }
    }

    [Fact]
    public void StarshipDeclaresAThreeMetreNoseAndANineMetreHull()
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "data", "parts"));

        var command = new Part(catalog["starship_command"]);
        var tank    = new Part(catalog["starship_tank"]);
        var engines = new Part(catalog["starship_engines"]);

        var vessel = new Vessel { Name = "NoseRadiusCatalogTest" };
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddPart(tank);
        vessel.Parts.AddPart(engines);
        vessel.Parts.AddJoint(new Joint(command, tank,    "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank,    engines, "bottom", "top"));

        Assert.Equal(9.0, vessel.MaximumDiameter, 9);
        Assert.Equal(3.0, vessel.NoseRadius, 9);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root from test working directory.");
    }
}
