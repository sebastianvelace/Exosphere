namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// An unpowered off-rails stack with declared liquid tanks coasts through the propellant-slosh
/// integrator. Its specific energy about the centre of mass, including the J2 potential, must
/// not depend on the time warp. Regression: that integrator rebuilt each RK4 stage on the
/// reference body's epoch-dated position while the fields were sampled on the bodies' frozen
/// end-of-step positions, which offset Earth's gravity by v_Earth·(dt − τ) and drained
/// −1.7 kJ/kg (warp 1), −14 kJ/kg (warp 10) and −48 kJ/kg (warp 100) over 900 s of AS-506
/// parking-orbit coast. The undated (Kepler) world also accelerated Earth's frame by the Sun
/// alone, so the vessel felt the Moon's full pull instead of its tide: −0.19 kJ/kg here.
/// </summary>
public sealed class SloshCoastEnergyConservationTests
{
    private const double AltitudeM = 300_000.0;
    private const double InclinationDeg = 32.5;
    private const double CoastSeconds = 900.0;

    // Fixed before running: residual-thermosphere drag at 300 km costs ~0.02 kJ/kg on this
    // stack and Sun/Moon tides relative to Earth a few J/kg; the slosh-frame defect cost ≥ 1.7
    // kJ/kg and the untidal lunar pull 0.19 kJ/kg.
    private const double EnergyBandJPerKg = 100.0;

    [Theory]
    [InlineData(1.0)]
    [InlineData(10.0)]
    [InlineData(100.0)]
    public void UnpoweredLeoCoastConservesJ2InclusiveEnergyAtEveryWarp(double warp)
    {
        var (universe, vessel, earth) = ParkingStack();
        double massKg = vessel.TotalMass;
        double startEnergy = SpecificEnergy(vessel, earth);
        double start = universe.CurrentTime;

        universe.TimeScale = warp;
        bool sloshIntegrated = false;
        while (universe.CurrentTime - start < CoastSeconds)
        {
            universe.Tick(1.0 / 60.0);
            sloshIntegrated |= vessel.LastSloshStatus is "conservative-unsettled" or "axial-first-mode";
            Assert.False(vessel.IsOnRails);
        }

        Assert.True(sloshIntegrated, $"Coast did not use the slosh integrator: {vessel.LastSloshStatus}");
        Assert.Equal(massKg, vessel.TotalMass, 6);
        Assert.InRange(SpecificEnergy(vessel, earth) - startEnergy, -EnergyBandJPerKg, EnergyBandJPerKg);
    }

    /// <summary>E = v²/2 − μ/r·(1 − J2 (R/r)² P2(sin φ)) of the centre of mass relative to Earth.</summary>
    private static double SpecificEnergy(Vessel vessel, CelestialBody earth)
    {
        var r = vessel.CenterOfMass - earth.Position;
        var v = vessel.CenterOfMassVelocity - earth.Velocity;
        double radius = r.Magnitude;
        double sinLatitude = r.Dot(earth.RotationAxis) / radius;
        double p2 = 0.5 * (3.0 * sinLatitude * sinLatitude - 1.0);
        double ratio = earth.EquatorialRadius / radius;
        return 0.5 * v.Dot(v) - earth.GM / radius * (1.0 - earth.J2 * ratio * ratio * p2);
    }

    /// <summary>AS-506 S-IVB, IU, LM and CSM with S-IVB propellant aboard, in a circular orbit.</summary>
    private static (Universe Universe, Vessel Vessel, CelestialBody Earth) ParkingStack()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var vessel = VehicleVariantDefinition.LoadFromJson(Path.Combine(root, "vehicles",
            "apollo11_saturn5_as506_1969.json")).Build(catalog).ToVessel("Saturn V", "saturn-v");
        var universe = Universe.LoadFromDataDirectory(root);
        universe.SetCurrentTime(0.0);
        var earth = universe.GetBody("earth")!;
        Assert.NotNull(vessel.Stage()); // S-IC
        Assert.NotNull(vessel.Stage()); // S-II
        Assert.Contains(vessel.Parts.Parts, p => p.Definition.HasVehicleRole("sivb_tank"));
        Assert.DoesNotContain(vessel.Parts.Parts, p => p.Definition.HasVehicleRole("sii_engine_cluster"));

        var axis = earth.RotationAxis;
        var seed = System.Math.Abs(axis.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        var radial = seed.Cross(axis).Normalized;
        double inclination = InclinationDeg * MathUtils.DEG_TO_RAD;
        var along = (axis.Cross(radial) * System.Math.Cos(inclination)
            + axis * System.Math.Sin(inclination)).Normalized;
        double radius = earth.EquatorialRadius + AltitudeM;

        vessel.Throttle = 0.0;
        vessel.SASEnabled = false;
        vessel.ReferenceBodyId = earth.Id;
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, along);
        vessel.Position = earth.Position + radial * radius;
        vessel.Velocity = earth.Velocity + along * System.Math.Sqrt(earth.GM / radius);
        // The post-insertion tumble seen on the AS-506 probe, about a transverse axis.
        vessel.AngularVelocity = vessel.Orientation.Rotate(new Vector3d(0.01, 0.0, 0.005));
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;
        return (universe, vessel, earth);
    }
}
