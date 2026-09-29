namespace ExosphereSimulation.Tests;

using System.IO;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

/// <summary>
/// Measures the corridor propagator against the production aerodynamic force model.
/// A passing vacuum time-to-ground is not evidence that an entry footprint is right.
/// </summary>
public sealed class EntryCorridorPropagationTests
{
    private const double EarthMu = 3.986004418e14;
    private const double EarthRadius = 6_371_000.0;

    [Fact]
    public void VacuumEntryConservesSpecificEnergy()
    {
        var dynamics = Synthetic(density: _ => 0.0, bankRadians: 0.0);
        var result = EntryCorridorPropagation.Propagate(
            altitudeM: 120_000.0,
            airspeedMps: 7_600.0,
            flightPathRad: -1.6 * MathUtils.DEG_TO_RAD,
            dynamics);

        double drift = System.Math.Abs(
            result.FinalSpecificEnergyJPerKg - result.InitialSpecificEnergyJPerKg);
        Assert.True(drift < 2.0e4,
            $"vacuum specific energy drifted {drift:F0} J/kg "
            + $"(initial {result.InitialSpecificEnergyJPerKg:F0}, "
            + $"final {result.FinalSpecificEnergyJPerKg:F0})");
        Assert.True(result.DurationS > 20.0);
        Assert.False(result.LeftAtmosphere);
    }

    [Fact]
    public void FirstStepDecelerationMatchesProductionDrag()
    {
        const double altitude = 50_000.0;
        const double speed = 3_000.0;
        const double gamma = -8.0 * MathUtils.DEG_TO_RAD;
        var dynamics = Synthetic(
            density: Altitude => 1.225 * System.Math.Exp(-Altitude / 7_500.0),
            bankRadians: 0.0,
            maxDurationS: EntryCorridorPropagation.IntegrationStepS,
            terminalAltitudeM: -1.0);

        double density = dynamics.DensityAtAltitude(altitude);
        var flow = Vector3d.Forward * speed;
        var axis = AerodynamicsModel.ComputeLiftUpEntryAxis(
            Vector3d.Up, Vector3d.Forward, dynamics.AngleOfAttackDegrees);
        double drag = AerodynamicsModel.ComputeReentryDrag(
            density,
            flow,
            axis,
            dynamics.VehicleLengthM,
            dynamics.VehicleDiameterM,
            dynamics.TemperatureAtAltitude(altitude),
            dynamics.AxialDragCoefficient).Magnitude;
        double radius = EarthRadius + altitude;
        double gravity = EarthMu / (radius * radius);
        double eulerDelta = (
            drag / dynamics.MassKg + gravity * System.Math.Sin(gamma))
            * EntryCorridorPropagation.IntegrationStepS;
        var result = EntryCorridorPropagation.Propagate(altitude, speed, gamma, dynamics);
        double lost = speed - result.FinalSpeedMps;

        Assert.Equal(EntryCorridorPropagation.IntegrationStepS, result.DurationS);
        Assert.InRange(lost, 0.8 * eulerDelta, 1.2 * eulerDelta);
    }

    [Fact]
    public void DragDissipatesSpecificEnergyOnTheRealEarthProfile()
    {
        var ship = BuildReturningStarship();
        var earth = LoadEarth();
        var dynamics = EntryCorridorPropagation.ForVessel(ship, earth);
        var result = EntryCorridorPropagation.Propagate(
            120_000.0,
            7_600.0,
            -1.6 * MathUtils.DEG_TO_RAD,
            dynamics);

        double lost = result.InitialSpecificEnergyJPerKg - result.FinalSpecificEnergyJPerKg;
        Assert.True(lost > 5.0e6,
            $"entry drag must remove orbital energy, lost only {lost:F0} J/kg "
            + $"over {result.DurationS:F0} s to {result.FinalAltitudeM:F0} m "
            + $"at {result.FinalSpeedMps:F0} m/s");
        double vacuumLead = 7_600.0 * System.Math.Cos(-1.6 * MathUtils.DEG_TO_RAD)
            * EntryCorridorGuidance.EstimateTimeToGround(
                120_000.0,
                -7_600.0 * System.Math.Sin(-1.6 * MathUtils.DEG_TO_RAD),
                9.45);
        Assert.True(System.Math.Abs(result.GroundRangeM - vacuumLead) > 100_000.0,
            $"energy-aware range {result.GroundRangeM:F0} m collapsed onto the vacuum lead "
            + $"{vacuumLead:F0} m");
    }

    [Fact]
    public void LiftUpExtendsRangeRelativeToLiftDown()
    {
        var up = PropagateBank(0.0);
        var down = PropagateBank(System.Math.PI);

        Assert.True(up.GroundRangeM > down.GroundRangeM + 20_000.0,
            $"lift-up range {up.GroundRangeM:F0} m must exceed lift-down "
            + $"{down.GroundRangeM:F0} m");
    }

    [Fact]
    public void BankSignChoosesCrossrangeSign()
    {
        var right = PropagateBank(0.6);
        var left = PropagateBank(-0.6);

        Assert.True(right.CrossRangeM > 1_000.0,
            $"positive bank must build positive crossrange, got {right.CrossRangeM:F0} m");
        Assert.True(left.CrossRangeM < -1_000.0,
            $"negative bank must build negative crossrange, got {left.CrossRangeM:F0} m");
    }

    [Fact]
    public void PredictUsesPropagatedEnergyInsteadOfTheVacuumLead()
    {
        var ship = BuildReturningStarship();
        var earth = LoadEarth();
        var dynamics = EntryCorridorPropagation.ForVessel(ship, earth);
        var prediction = EntryCorridorGuidance.Predict(
            targetOffsetWorld: Vector3d.Right * 400_000.0,
            vehicleSurfaceVelocity: Vector3d.Right * 7_600.0,
            targetSurfaceVelocity: Vector3d.Zero,
            bodyUp: Vector3d.Up,
            altitudeM: 120_000.0,
            downwardSpeedMps: 7_600.0 * System.Math.Sin(1.6 * MathUtils.DEG_TO_RAD),
            gravityMps2: 9.45,
            dynamics: dynamics);

        Assert.True(prediction.UsedEnergyPropagation);
        Assert.True(prediction.SpecificEnergyChangeJPerKg < -5.0e6,
            $"predicted entry did not dissipate energy, change="
            + $"{prediction.SpecificEnergyChangeJPerKg:F0} J/kg");
        Assert.NotEqual(0.0, prediction.FinalSpeedMps);
    }

    [Fact]
    public void SurfaceArcMarginRemainsShortOfTheTarget()
    {
        const double marginM = 80_000.0;
        double flightPath = -1.6 * MathUtils.DEG_TO_RAD;
        double speed = 7_600.0;
        var dynamics = Synthetic(density: _ => 0.0, bankRadians: 0.0, maxDurationS: 200.0);
        var flown = EntryCorridorPropagation.Propagate(120_000.0, speed, flightPath, dynamics);
        double targetAngle = flown.CentralAngleRad + marginM / dynamics.BodyRadiusM;
        var prediction = EntryCorridorGuidance.Predict(
            targetOffsetWorld: Vector3d.Right
                * (dynamics.BodyRadiusM * System.Math.Sin(targetAngle)),
            vehicleSurfaceVelocity: Vector3d.Right * (speed * System.Math.Cos(flightPath)),
            targetSurfaceVelocity: Vector3d.Zero,
            bodyUp: Vector3d.Up,
            altitudeM: 120_000.0,
            downwardSpeedMps: -speed * System.Math.Sin(flightPath),
            gravityMps2: 9.45,
            dynamics: dynamics);

        Assert.InRange(prediction.PredictedDownrangeM, 70_000.0, 90_000.0);
    }

    [Fact]
    public void EastwardRotationExtendsTheSameAirspeedFootprint()
    {
        System.Func<double, double> density = altitude =>
            1.225 * System.Math.Exp(-altitude / 7_500.0);
        const double latitude = 26.0 * MathUtils.DEG_TO_RAD;
        const double earthSpin = 7.292115e-5;
        var inertial = Synthetic(density, 0.0, maxDurationS: 900.0);
        var eastward = inertial with
        {
            PlanetAngularSpeedRadPerS = earthSpin,
            LatitudeRad = latitude,
            TrackEast = 1.0
        };
        double flightPath = -1.6 * MathUtils.DEG_TO_RAD;
        var withoutSpin = EntryCorridorPropagation.Propagate(
            120_000.0, 7_600.0, flightPath, inertial);
        var withSpin = EntryCorridorPropagation.Propagate(
            120_000.0, 7_600.0, flightPath, eastward);

        Assert.True(withSpin.GroundRangeM > withoutSpin.GroundRangeM + 50_000.0,
            $"eastward rotation must lengthen the footprint, spin {withSpin.GroundRangeM:F0} m "
            + $"versus inertial {withoutSpin.GroundRangeM:F0} m");
    }

    [Fact]
    public void BodyCentredEastwardEntryStaysBelowCircularAndDescends()
    {
        var earth = LoadEarth();
        var axis = earth.RotationAxis.Normalized;
        var equator = axis.Cross(Vector3d.Right);
        if (equator.MagnitudeSquared < 1e-6)
            equator = axis.Cross(Vector3d.Forward);
        equator = equator.Normalized;
        const double latitude = 26.0 * MathUtils.DEG_TO_RAD;
        var radial = (equator * System.Math.Cos(latitude) + axis * System.Math.Sin(latitude))
            .Normalized;
        const double altitude = 120_000.0;
        var position = earth.Position + radial * (earth.Radius + altitude);
        var east = earth.GetEastDirection(position);
        var state = EntryInterfaceState.Compose(
            earth,
            position,
            east,
            inertialSpeedMps: 7_600.0,
            inertialFlightPathRad: -1.6 * MathUtils.DEG_TO_RAD);

        double circular = EntryInterfaceState.CircularSpeedMps(earth, position);
        double rotationAlongTrack = earth.GetSurfaceVelocity(position).Dot(east);
        Assert.True(state.InertialSpeedMps < circular - 100.0,
            $"entry inertial speed {state.InertialSpeedMps:F0} must stay below circular {circular:F0}");
        Assert.True(state.InertialSpeedMps + rotationAlongTrack > circular,
            "stacking Earth rotation on a 7600 m/s eastward airspeed is the skip that failed the flight");
        Assert.True(state.AtmosphereRelativeSpeedMps < state.InertialSpeedMps - 200.0);

        var ship = BuildReturningStarship();
        ship.Position = position;
        ship.Velocity = earth.Velocity + state.InertialRelativeToBody;
        var dynamics = EntryCorridorPropagation.ForVessel(ship, earth);
        var result = EntryCorridorPropagation.Propagate(
            altitude,
            state.AtmosphereRelativeSpeedMps,
            state.AtmosphereRelativeFlightPathRad,
            dynamics);

        Assert.False(result.LeftAtmosphere);
        Assert.True(result.FinalAltitudeM < 60_000.0,
            $"body-centred entry must reach dense air, final altitude={result.FinalAltitudeM:F0} m "
            + $"speed={result.FinalSpeedMps:F0} gamma={result.FinalFlightPathRad * MathUtils.RAD_TO_DEG:F2} deg");
        Assert.True(result.FinalSpecificEnergyJPerKg < result.InitialSpecificEnergyJPerKg - 5.0e6);
    }

    private static EntryCorridorPropagation.Result PropagateBank(double bankRadians)
    {
        var dynamics = Synthetic(
            density: altitude => 0.4 * System.Math.Exp(-altitude / 7_500.0),
            bankRadians: bankRadians,
            maxDurationS: 180.0);
        return EntryCorridorPropagation.Propagate(
            altitudeM: 45_000.0,
            airspeedMps: 2_200.0,
            flightPathRad: -6.0 * MathUtils.DEG_TO_RAD,
            dynamics);
    }

    private static EntryCorridorPropagation.Dynamics Synthetic(
        System.Func<double, double> density,
        double bankRadians,
        double maxDurationS = EntryCorridorPropagation.DefaultMaxDurationS,
        double terminalAltitudeM = 0.0) =>
        new(
            MassKg: 170_000.0,
            VehicleLengthM: 50.0,
            VehicleDiameterM: 9.0,
            AxialDragCoefficient: 0.6,
            AngleOfAttackDegrees: AerodynamicsModel.NominalEntryAngleOfAttackDegrees,
            BankRadians: bankRadians,
            BodyRadiusM: EarthRadius,
            GravitationalParameter: EarthMu,
            DensityAtAltitude: density,
            TemperatureAtAltitude: _ => 250.0,
            TerminalAltitudeM: terminalAltitudeM,
            MaxDurationS: maxDurationS);

    private static Vessel BuildReturningStarship()
    {
        var vessel = new Vessel { Name = "CorridorPropagation" };
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "data", "parts"));
        var command = new Part(catalog["starship_command"]);
        var tank = new Part(catalog["starship_tank"]);
        var engines = new Part(catalog["starship_engines"]);
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddPart(tank);
        vessel.Parts.AddPart(engines);
        vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
        foreach (var part in vessel.Parts.Parts)
        {
            part.LiquidFuel *= 0.06;
            part.Oxidizer *= 0.06;
        }

        return vessel;
    }

    private static CelestialBody LoadEarth()
    {
        var universe = Universe.LoadFromDataDirectory(Path.Combine(RepoRoot(), "data"));
        return universe.GetBody("earth")!;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repository root");
    }
}
