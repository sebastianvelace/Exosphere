namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// Translation reaction control: a per-axis command produces F = command × thrust along the
/// body axes, burns monopropellant at F / (Isp g0), stops when the propellant runs out, and
/// keeps the vessel off rails while it fires.
/// </summary>
public sealed class RcsTranslationTests
{
    private const double ThrustN = 890.0;
    private const double IspSeconds = 260.0;
    private const double MonopropellantKg = 50.0;

    [Fact]
    public void ForwardCommandGivesTheRocketEquationDeltaVAndPropellantFlow()
    {
        var (universe, vessel, _) = HighOrbit();
        var (coastUniverse, coasting, _) = HighOrbit();
        double massBefore = vessel.TotalMass;
        var nose = vessel.Orientation.Rotate(Vector3d.Up);

        vessel.TranslationCommand = new Vector3d(0.0, 1.0, 0.0);
        Assert.True(universe.RequiresOffRailsPhysics(vessel));
        for (int step = 0; step < 500; step++)
        {
            universe.Tick(0.02);
            coastUniverse.Tick(0.02);
        }

        double expectedFlow = ThrustN / (IspSeconds * 9.80665);
        Assert.Equal(massBefore - expectedFlow * 10.0, vessel.TotalMass, 3);
        double expectedDeltaV = IspSeconds * 9.80665 * System.Math.Log(massBefore / vessel.TotalMass);
        // Against an identical coasting twin, so orbital gravity cancels out.
        double gained = (vessel.Velocity - coasting.Velocity).Dot(nose);
        Assert.InRange(gained, expectedDeltaV * 0.99, expectedDeltaV * 1.01);
        Assert.False(vessel.RcsPropellantDepleted);
    }

    [Fact]
    public void ExhaustedMonopropellantStopsTheThrust()
    {
        var (universe, vessel, _) = HighOrbit();
        vessel.TranslationCommand = new Vector3d(1.0, 0.0, 0.0);
        double flow = ThrustN / (IspSeconds * 9.80665);
        int ticksToEmpty = (int)System.Math.Ceiling(MonopropellantKg / flow / 0.02) + 5;
        for (int step = 0; step < ticksToEmpty; step++)
            universe.Tick(0.02);

        Assert.True(vessel.RcsPropellantDepleted);
        Assert.Equal(Vector3d.Zero, vessel.GetRcsTranslationThrustLocal());
        Assert.True(vessel.Parts.TotalMonopropellant >= 0.0);
    }

    private static (Universe Universe, Vessel Vessel, CelestialBody Earth) HighOrbit()
    {
        var universe = Universe.LoadFromDataDirectory(SaturnVAscentFlight.DataDirectory());
        var earth = universe.GetBody("earth")!;
        var vessel = new Vessel("rcs-probe") { SASEnabled = false, ReferenceBodyId = earth.Id };
        var hull = new Part(new PartDefinition
        {
            Id = "rcs-probe-hull",
            CategoryStr = "command",
            MassDry = 3_000.0,
            LengthM = 3.0,
            DiameterM = 3.0,
            FuelCapacityMono = MonopropellantKg,
            RcsTranslationThrustN = ThrustN,
            RcsIsp = IspSeconds,
        });
        vessel.Parts.SetRoot(hull);
        vessel.Parts.AddPart(hull);
        double radius = earth.Radius + 2_000_000.0;
        vessel.Position = earth.Position + Vector3d.Right * radius;
        vessel.Velocity = earth.Velocity + new Vector3d(0.0, 0.0, -1.0) * System.Math.Sqrt(earth.GM / radius);
        // Nose along the orbital velocity, so the commanded Δv is along-track.
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, new Vector3d(0.0, 0.0, -1.0));
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;
        return (universe, vessel, earth);
    }
}
