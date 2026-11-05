namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// SAS is a rate controller acting through the vehicle's actuators. It must not remove
/// angular momentum faster than the declared thrusters allow, and without thrusters (or
/// lit engines) it removes none: there is no generic reaction-control floor.
/// </summary>
[Trait("FidelityGate", "F3")]
[Trait("GateKind", "analytic")]
public sealed class StabilityAugmentationAuthorityTests
{
    private const double ThrusterN = 1_000.0;
    private const double LeverM = 20.0;
    private const double IspS = 70.0;
    private const double MassKg = 100_000.0;
    private const double LengthM = 50.0;

    [Fact]
    public void SasCannotStopAFastTumbleFasterThanItsThrusters()
    {
        var (universe, vessel) = TumblingVesselInVacuum(0.3);
        double monopropellant = vessel.Parts.TotalMonopropellant;
        double inertia = vessel.Parts.TransverseMomentOfInertia;
        for (int step = 0; step < 50; step++)
            universe.Tick(0.02);

        // Two 1 kN thrusters 20 m from the centre of mass, one per end, for one second.
        double removed = 2.0 * ThrusterN * LeverM / inertia;
        Assert.Equal(0.3 - removed, vessel.AngularVelocity.Magnitude, 4);
        double burned = 2.0 * ThrusterN / (IspS * 9.80665);
        Assert.Equal(burned, monopropellant - vessel.Parts.TotalMonopropellant, 3);
    }

    [Fact]
    public void WithoutThrustersSasCannotDampAnUnpoweredTumble()
    {
        var (universe, vessel) = TumblingVesselInVacuum(0.3, thrusters: false);
        for (int step = 0; step < 50; step++)
            universe.Tick(0.02);

        Assert.Equal(0.3, vessel.AngularVelocity.Magnitude, 9);
    }

    [Fact]
    public void SasStillSettlesASmallResidualRate()
    {
        var (universe, vessel) = TumblingVesselInVacuum(0.002);
        for (int step = 0; step < 50 * 3; step++)
            universe.Tick(0.02);

        Assert.True(vessel.AngularVelocity.Magnitude < 1e-5,
            $"Residual rate {vessel.AngularVelocity.Magnitude:E3} rad/s was not damped.");
    }

    [Fact]
    public void WithoutSasAnUnpoweredTumbleConservesItsRate()
    {
        var (universe, vessel) = TumblingVesselInVacuum(0.3);
        vessel.SASEnabled = false;
        for (int step = 0; step < 50; step++)
            universe.Tick(0.02);

        Assert.Equal(0.3, vessel.AngularVelocity.Magnitude, 9);
    }

    private static (Universe Universe, Vessel Vessel) TumblingVesselInVacuum(double rate, bool thrusters = true)
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        // Isolate actuator authority from the environmental tidal moment.
        universe.GravityGradientEnabled = false;
        var earth = universe.GetBody("earth")!;
        var vessel = new Vessel("sas-tumbler") { SASEnabled = true, ReferenceBodyId = earth.Id };
        var command = new Part(new PartDefinition
        {
            Id = "sas-test-command",
            CategoryStr = "command",
            MassDry = MassKg,
            LengthM = LengthM,
            DiameterM = 9.0,
            FuelCapacityMono = thrusters ? 500.0 : 0.0,
            // Lateral pairs at both ends: pure pitch and yaw couples.
            RcsThrusters = thrusters
                ?
                [
                    Lateral("nose_px", LeverM, 1, 0), Lateral("nose_nx", LeverM, -1, 0),
                    Lateral("nose_pz", LeverM, 0, 1), Lateral("nose_nz", LeverM, 0, -1),
                    Lateral("tail_px", -LeverM, 1, 0), Lateral("tail_nx", -LeverM, -1, 0),
                    Lateral("tail_pz", -LeverM, 0, 1), Lateral("tail_nz", -LeverM, 0, -1),
                ]
                : [],
        });
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddPart(command);
        double radius = earth.Radius + 2_000_000.0;
        vessel.Position = earth.Position + Vector3d.Right * radius;
        vessel.Velocity = earth.Velocity + new Vector3d(0.0, 0.0, -1.0) * System.Math.Sqrt(earth.GM / radius);
        vessel.AngularVelocity = new Vector3d(rate, 0.0, 0.0);
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;
        return (universe, vessel);
    }

    private static RcsThrusterDef Lateral(string id, double y, double x, double z) => new()
    {
        Id = id, Position = [0.0, y, 0.0], Direction = [x, 0.0, z], ThrustN = ThrusterN, IspS = IspS,
    };

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
