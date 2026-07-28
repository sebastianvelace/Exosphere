namespace ExosphereSimulation.Tests;

using System.IO;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// C2 acceptance — orbit → impulsive deorbit → atmospheric entry kinematics
/// without the Godot reentry-demonstration teleport.
/// </summary>
public sealed class DeorbitToEntryTests
{
    private const double LeoAltitudeM = 400_000.0;
    private const double TargetPeAltitudeM = 80_000.0;

    [Fact]
    public void ComputeRetroDeltaV_For400kmLeoTargeting80kmPe_MatchesVisVivaBand()
    {
        var earth = LoadBody("earth");
        double r = earth.Radius + LeoAltitudeM;
        double rp = earth.Radius + TargetPeAltitudeM;

        double dv = DeorbitPlanner.ComputeRetroDeltaV(
            earth.GM, r, rp, earth.Radius, earth.Atmosphere!.MaxAltitude);

        // Circular LEO vis-viva check: Δv = √(μ/r) − √(μ(2/r − 1/a)), a = (r+rp)/2.
        double a = 0.5 * (r + rp);
        double expected = System.Math.Sqrt(earth.GM / r)
            - System.Math.Sqrt(earth.GM * (2.0 / r - 1.0 / a));

        Assert.InRange(dv, 50.0, 200.0);
        Assert.True(System.Math.Abs(dv - expected) < 1e-6,
            $"Δv must match vis-viva (got {dv:R}, expected {expected:R})");
    }

    [Fact]
    public void ClampKeepsTargetPeBetweenSafetyFloorAndAtmosphereTop()
    {
        var earth = LoadBody("earth");
        double floor = earth.Radius + DeorbitPlanner.SafetyFloorAboveSurfaceM;
        double ceiling = earth.Radius + earth.Atmosphere!.MaxAltitude;

        Assert.Equal(floor,
            DeorbitPlanner.ClampTargetPeriapsisRadius(earth.Radius, earth.Radius, earth.Atmosphere.MaxAltitude));
        Assert.Equal(ceiling,
            DeorbitPlanner.ClampTargetPeriapsisRadius(
                earth.Radius + 500_000.0, earth.Radius, earth.Atmosphere.MaxAltitude));
        Assert.InRange(
            DeorbitPlanner.ClampTargetPeriapsisRadius(
                earth.Radius + TargetPeAltitudeM, earth.Radius, earth.Atmosphere.MaxAltitude),
            floor, ceiling);
    }

    [Fact]
    public void ImpulsiveDeorbitFrom400kmLeo_ReachesAtmosphereWithEntrySpeedGate()
    {
        var earth = LoadBody("earth");
        double r0 = earth.Radius + LeoAltitudeM;
        double v0 = System.Math.Sqrt(earth.GM / r0);
        double rpTarget = earth.Radius + TargetPeAltitudeM;

        double dv = DeorbitPlanner.ComputeRetroDeltaV(
            earth.GM, r0, rpTarget, earth.Radius, earth.Atmosphere!.MaxAltitude);
        Assert.True(dv > 50.0);

        var vessel = new Vessel { Name = "DeorbitProbe" };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "deorbit_probe",
            CategoryStr = "command",
            MassDry = 50_000.0,
            DiameterM = 9.0,
            LengthM = 50.0,
        }));
        vessel.Position = earth.Position + new Vector3d(r0, 0.0, 0.0);
        // Circular LEO, +Y prograde. Impulsive retro reduces speed along −prograde.
        vessel.Velocity = earth.Velocity + new Vector3d(0.0, v0 - dv, 0.0);
        vessel.Throttle = 0.0;
        vessel.IsOnRails = false;
        vessel.ReferenceBodyId = earth.Id;

        var universe = new Universe { TimeScale = 50.0, ActiveVessel = vessel };
        universe.AddBody(earth);
        universe.AddVessel(vessel);

        // Post-burn ellipse: periapsis must dip into the aerodynamically significant column.
        var elems = OrbitalElements.FromStateVector(
            vessel.Position - earth.Position,
            vessel.Velocity - earth.Velocity,
            earth.GM, earth.Id, universe.CurrentTime);
        Assert.True(elems.Periapsis < earth.Radius + earth.Atmosphere.MaxAltitude,
            $"Pe {elems.Periapsis - earth.Radius:F0} m must be below atmo top " +
            $"{earth.Atmosphere.MaxAltitude:F0} m");
        Assert.InRange(elems.Periapsis - earth.Radius, 20_000.0, earth.Atmosphere.MaxAltitude);

        // Propagate apo → peri half-ellipse (warped). Look for EDL EntrySpeed gate
        // kinematics: descending in atmosphere with surface speed > 1200 m/s.
        double a = 0.5 * ((vessel.Position - earth.Position).Magnitude + elems.Periapsis);
        double halfPeriod = System.Math.PI * System.Math.Sqrt(a * a * a / earth.GM);
        double wallDt = 0.05;
        int maxSteps = (int)System.Math.Ceiling(halfPeriod * 1.4 / (universe.TimeScale * wallDt)) + 50;

        bool sawEntryGate = false;
        for (int i = 0; i < maxSteps && !vessel.IsDestroyed; i++)
        {
            universe.Tick(wallDt);

            double alt = earth.GetAltitude(vessel.Position);
            Vector3d up = (vessel.Position - earth.Position).Normalized;
            Vector3d surfVel = vessel.GetSurfaceVelocity(earth);
            double vUp = surfVel.Dot(up);
            double speed = surfVel.Magnitude;

            bool descending = vUp < -20.0;
            bool inAtmo = alt < earth.Atmosphere.MaxAltitude * 1.05;
            if (descending && inAtmo && speed > 1_200.0)
            {
                sawEntryGate = true;
                break;
            }
        }

        Assert.False(vessel.IsDestroyed,
            "deorbit→entry probe must not impact before the entry-speed gate");
        Assert.True(sawEntryGate,
            "after deorbit coast the vessel must be descending in atmosphere with " +
            "surface speed > 1200 m/s (EDL EntrySpeed gate), without teleport demo");
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
