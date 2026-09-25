namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public sealed class EntryFlightDiagnosticsTests
{
    private const double EarthRadius = 6_371_000.0;
    private const double EarthGm = 3.986004418e14;

    [Fact]
    public void CircularOrbitReportsExpectedPointMassInvariants()
    {
        CelestialBody body = BuildBody(withAtmosphere: false);
        double radius = EarthRadius + 400_000.0;
        double circularSpeed = System.Math.Sqrt(EarthGm / radius);
        Vessel vessel = BuildVessel(
            position: new Vector3d(radius, 0.0, 0.0),
            velocity: new Vector3d(0.0, 0.0, circularSpeed));

        EntryFlightState state = EntryFlightDiagnostics.Evaluate(vessel, body);

        Assert.Equal(-EarthGm / (2.0 * radius),
            state.PointMassSpecificOrbitalEnergyJPerKg, 5);
        Assert.Equal(radius * circularSpeed, state.SpecificAngularMomentumM2PerS, 3);
        Assert.Equal(circularSpeed, state.InertialSpeedMps, 6);
        Assert.True(double.IsNaN(state.Mach));
        Assert.Equal(0.0, state.DynamicPressurePa);
        Assert.Equal(0.0, state.StagnationHeatFluxWPerM2);
    }

    [Fact]
    public void EntryAnglesAndLoadsUseAtmosphereRelativeVelocity()
    {
        CelestialBody body = BuildBody(withAtmosphere: true);
        double radius = EarthRadius + 10_000.0;
        double speed = 1_000.0;
        double descent = -100.0;
        Vessel vessel = BuildVessel(
            position: new Vector3d(radius, 0.0, 0.0),
            velocity: new Vector3d(descent, 0.0, speed));
        Vector3d atmosphereVelocity = vessel.GetSurfaceVelocity(body);
        vessel.Orientation = Quaterniond.FromTo(
            Vector3d.Up,
            Aerodynamics.PhysicsAxis(atmosphereVelocity, 70.0));

        EntryFlightState state = EntryFlightDiagnostics.Evaluate(vessel, body);
        double expectedFpa = System.Math.Asin(
            atmosphereVelocity.Normalized.Dot(Vector3d.Right)) * MathUtils.RAD_TO_DEG;

        Assert.Equal(expectedFpa, state.FlightPathAngleDegrees, 8);
        Assert.InRange(state.AngleOfAttackDegrees, 69.999999, 70.000001);
        Assert.True(state.Mach > 2.0);
        Assert.True(state.DynamicPressurePa > 0.0);
        Assert.True(state.DragForceN > 0.0);
        Assert.True(state.LiftForceN > 0.0);
        Assert.True(state.AerodynamicLoadG > 0.0);
        Assert.True(state.StagnationHeatFluxWPerM2 > 0.0);
    }

    [Fact]
    public void UndefinedDirectionsFailOpenAsNaNWithoutInfiniteTelemetry()
    {
        CelestialBody body = BuildBody(withAtmosphere: true);
        Vector3d position = new(EarthRadius + 1_000.0, 0.0, 0.0);
        Vessel vessel = BuildVessel(
            position,
            body.Velocity + body.GetSurfaceVelocity(position));

        EntryFlightState state = EntryFlightDiagnostics.Evaluate(vessel, body);

        Assert.True(double.IsNaN(state.FlightPathAngleDegrees));
        Assert.True(double.IsNaN(state.AngleOfAttackDegrees));
        Assert.True(double.IsNaN(state.BankAngleDegrees));
        Assert.Equal(0.0, state.DynamicPressurePa);
        Assert.Equal(0.0, state.AerodynamicLoadG);
    }

    private static CelestialBody BuildBody(bool withAtmosphere) => new()
    {
        Id = "diagnostic-earth",
        Name = "Diagnostic Earth",
        Radius = EarthRadius,
        GM = EarthGm,
        RotationalPeriod = 86_164.0905,
        Atmosphere = withAtmosphere
            ? new AtmosphereModel
            {
                SeaLevelPressure = 101_325.0,
                SeaLevelDensity = 1.225,
                SeaLevelTemperature = 288.15,
                ScaleHeight = 8_500.0,
                MaxAltitude = 140_000.0,
            }
            : null,
    };

    private static Vessel BuildVessel(Vector3d position, Vector3d velocity)
    {
        var vessel = new Vessel
        {
            Position = position,
            Velocity = velocity,
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "diagnostic-body",
            Name = "Diagnostic body",
            CategoryStr = "structure",
            MassDry = 100_000.0,
            LengthM = 50.0,
            DiameterM = 9.0,
            AxialDragCoefficient = 0.6,
        }));
        return vessel;
    }

    private static class Aerodynamics
    {
        public static Vector3d PhysicsAxis(Vector3d velocity, double angleOfAttackDegrees)
        {
            Vector3d flow = velocity.Normalized;
            Vector3d liftUp = Vector3d.Right - flow * Vector3d.Right.Dot(flow);
            double angle = angleOfAttackDegrees * MathUtils.DEG_TO_RAD;
            return (flow * System.Math.Cos(angle)
                + liftUp.Normalized * System.Math.Sin(angle)).Normalized;
        }
    }
}
