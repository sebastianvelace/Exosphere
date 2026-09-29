namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>
/// Reduced-order point-mass entry used by corridor guidance. It integrates altitude,
/// airspeed, flight-path angle and bank on a spherical planet, with drag and lift taken
/// from <see cref="AerodynamicsModel"/> at the commanded angle of attack. It is not a
/// second aerodynamic model and it does not replace the vessel integrator.
/// </summary>
public static class EntryCorridorPropagation
{
    public const double IntegrationStepS = 0.5;
    public const double DefaultMaxDurationS = 1_800.0;
    public const double MinimumSpeedMps = 80.0;

    /// <summary>
    /// Vehicle and planet data for one prediction. Density and temperature delegates
    /// must be the same atmosphere the force integrator samples.
    /// </summary>
    public readonly record struct Dynamics(
        double MassKg,
        double VehicleLengthM,
        double VehicleDiameterM,
        double AxialDragCoefficient,
        double AngleOfAttackDegrees,
        double BankRadians,
        double BodyRadiusM,
        double GravitationalParameter,
        System.Func<double, double> DensityAtAltitude,
        System.Func<double, double> TemperatureAtAltitude,
        double TerminalAltitudeM = 0.0,
        double MaxDurationS = DefaultMaxDurationS,
        double PlanetAngularSpeedRadPerS = 0.0,
        double LatitudeRad = 0.0,
        double TrackEast = 0.0,
        double CrossEast = 0.0);

    public readonly record struct Result(
        double DurationS,
        double GroundRangeM,
        double CrossRangeM,
        double CentralAngleRad,
        double CrossAngleRad,
        double FinalAltitudeM,
        double FinalSpeedMps,
        double FinalFlightPathRad,
        double InitialSpecificEnergyJPerKg,
        double FinalSpecificEnergyJPerKg,
        bool LeftAtmosphere);

    public static Dynamics ForVessel(
        Vessel vessel,
        CelestialBody body,
        double bankRadians = 0.0)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(body);
        var atmosphere = body.Atmosphere;
        (double trackEast, double crossEast) = TrackBasis(vessel, body);
        return new Dynamics(
            MassKg: System.Math.Max(1.0, vessel.Parts.TotalMass),
            VehicleLengthM: System.Math.Max(1.0, vessel.VehicleLength),
            VehicleDiameterM: System.Math.Max(0.1, vessel.MaximumDiameter),
            AxialDragCoefficient: vessel.Parts.AxialDragCoefficient,
            AngleOfAttackDegrees: AerodynamicsModel.NominalEntryAngleOfAttackDegrees,
            BankRadians: bankRadians,
            BodyRadiusM: System.Math.Max(1.0, body.Radius),
            GravitationalParameter: body.GM,
            DensityAtAltitude: altitude => atmosphere?.GetDensity(altitude) ?? 0.0,
            TemperatureAtAltitude: altitude =>
            {
                double temperature = atmosphere?.GetTemperature(altitude) ?? 1.0;
                return temperature > 1.0 ? temperature : 1.0;
            },
            PlanetAngularSpeedRadPerS: body.AngularSpeed,
            LatitudeRad: body.GetLatitude(vessel.Position) * MathUtils.DEG_TO_RAD,
            TrackEast: trackEast,
            CrossEast: crossEast);
    }

    private static (double TrackEast, double CrossEast) TrackBasis(
        Vessel vessel,
        CelestialBody body)
    {
        var up = body.GetGeodeticUp(vessel.Position);
        var east = body.GetEastDirection(vessel.Position);
        var horizontal = vessel.GetSurfaceVelocity(body);
        horizontal -= up * horizontal.Dot(up);
        var track = horizontal.Magnitude > 1.0
            ? horizontal / horizontal.Magnitude
            : (east.Magnitude > 1e-9 ? east : up.Cross(body.RotationAxis));
        if (track.MagnitudeSquared < 1e-12)
            return (0.0, 0.0);
        track = track.Normalized;
        var cross = up.Cross(track);
        if (east.MagnitudeSquared < 1e-12)
            return (0.0, 0.0);
        return (track.Dot(east), cross.Dot(east));
    }

    /// <summary>
    /// Integrates a spherical-planet entry. Bank zero points the modeled lift fully away
    /// from the planet. Airspeed is planet-relative. The curvature term uses inertial
    /// speed, airspeed plus the local eastward rotation, so an eastward return is not
    /// treated as a slower suborbital entry. Horizontal Coriolis is the heading rate
    /// −2ω sin φ, with latitude held at the value captured for this prediction.
    /// Specific energy uses the planet-relative speed: a zero-density, non-rotating run
    /// must conserve it, and a dense run must lose it to drag.
    /// </summary>
    public static Result Propagate(
        double altitudeM,
        double airspeedMps,
        double flightPathRad,
        Dynamics dynamics)
    {
        double h = System.Math.Max(0.0, altitudeM);
        double v = System.Math.Max(0.0, airspeedMps);
        double gamma = flightPathRad;
        double heading = 0.0;
        double along = 0.0;
        double cross = 0.0;
        double time = 0.0;
        double radius = dynamics.BodyRadiusM + h;
        double initialEnergy = SpecificEnergy(v, radius, dynamics.GravitationalParameter);
        bool leftAtmosphere = false;
        double step = IntegrationStepS;

        while (time < dynamics.MaxDurationS
            && h > dynamics.TerminalAltitudeM
            && v > MinimumSpeedMps)
        {
            var start = new State(h, v, gamma, heading, along, cross);
            var k1 = Evaluate(start, dynamics);
            var predicted = Advance(start, k1, step);
            var k2 = Evaluate(predicted, dynamics);
            var next = Advance(start, Average(k1, k2), step);

            h = System.Math.Max(0.0, next.AltitudeM);
            v = System.Math.Max(0.0, next.SpeedMps);
            gamma = next.FlightPathRad;
            heading = next.HeadingRad;
            along = next.AlongTrackM;
            cross = next.CrossTrackM;
            time += step;

            if (h > altitudeM + 20_000.0 && gamma > 0.0)
                leftAtmosphere = true;
            if (!double.IsFinite(h) || !double.IsFinite(v) || !double.IsFinite(gamma))
                break;
        }

        double finalRadius = dynamics.BodyRadiusM + h;
        double surfaceRadius = System.Math.Max(1.0, dynamics.BodyRadiusM);
        return new Result(
            time,
            surfaceRadius * along,
            surfaceRadius * cross,
            along,
            cross,
            h,
            v,
            gamma,
            initialEnergy,
            SpecificEnergy(v, finalRadius, dynamics.GravitationalParameter),
            leftAtmosphere);
    }

    private readonly record struct State(
        double AltitudeM,
        double SpeedMps,
        double FlightPathRad,
        double HeadingRad,
        double AlongTrackM,
        double CrossTrackM);

    private readonly record struct Derivative(
        double AltitudeRate,
        double SpeedRate,
        double FlightPathRate,
        double HeadingRate,
        double AlongTrackRate,
        double CrossTrackRate);

    private static State Advance(State state, Derivative rate, double dt) =>
        new(
            state.AltitudeM + rate.AltitudeRate * dt,
            state.SpeedMps + rate.SpeedRate * dt,
            state.FlightPathRad + rate.FlightPathRate * dt,
            state.HeadingRad + rate.HeadingRate * dt,
            state.AlongTrackM + rate.AlongTrackRate * dt,
            state.CrossTrackM + rate.CrossTrackRate * dt);

    private static Derivative Average(Derivative a, Derivative b) =>
        new(
            0.5 * (a.AltitudeRate + b.AltitudeRate),
            0.5 * (a.SpeedRate + b.SpeedRate),
            0.5 * (a.FlightPathRate + b.FlightPathRate),
            0.5 * (a.HeadingRate + b.HeadingRate),
            0.5 * (a.AlongTrackRate + b.AlongTrackRate),
            0.5 * (a.CrossTrackRate + b.CrossTrackRate));

    private static Derivative Evaluate(State state, Dynamics dynamics)
    {
        double radius = System.Math.Max(1.0, dynamics.BodyRadiusM + state.AltitudeM);
        double mu = System.Math.Max(0.0, dynamics.GravitationalParameter);
        double gravity = mu / (radius * radius);
        double speed = System.Math.Max(0.0, state.SpeedMps);
        (double drag, double lift) = SampleForces(dynamics, state.AltitudeM, speed);
        double mass = System.Math.Max(1.0, dynamics.MassKg);
        double dragAccel = drag / mass;
        double liftAccel = lift / mass;
        double bank = dynamics.BankRadians;
        double verticalLift = liftAccel * System.Math.Cos(bank);
        double lateralLift = liftAccel * System.Math.Sin(bank);
        double safeSpeed = System.Math.Max(speed, 1.0);
        double horizontalSpeed = speed * System.Math.Cos(state.FlightPathRad);
        double eastComponent = dynamics.TrackEast * System.Math.Cos(state.HeadingRad)
            + dynamics.CrossEast * System.Math.Sin(state.HeadingRad);
        double rotationalEast = dynamics.PlanetAngularSpeedRadPerS
            * radius
            * System.Math.Cos(dynamics.LatitudeRad);
        double verticalSpeed = speed * System.Math.Sin(state.FlightPathRad);
        double inertialHorizontal = horizontalSpeed + rotationalEast * eastComponent;
        double inertialSpeed = System.Math.Sqrt(
            inertialHorizontal * inertialHorizontal + verticalSpeed * verticalSpeed);
        double safeInertial = System.Math.Max(inertialSpeed, 1.0);
        // (v_inertial²/r) already contains the eastward Eötvös term. The remaining
        // horizontal Coriolis changes heading to the right in the northern hemisphere.
        double coriolisHeading = -2.0
            * dynamics.PlanetAngularSpeedRadPerS
            * System.Math.Sin(dynamics.LatitudeRad);

        return new Derivative(
            AltitudeRate: verticalSpeed,
            SpeedRate: -dragAccel - gravity * System.Math.Sin(state.FlightPathRad),
            FlightPathRate: verticalLift / safeSpeed
                + (inertialSpeed / radius - gravity / safeInertial)
                    * System.Math.Cos(state.FlightPathRad),
            HeadingRate: lateralLift / System.Math.Max(System.Math.Abs(horizontalSpeed), 1.0)
                + coriolisHeading,
            AlongTrackRate: horizontalSpeed * System.Math.Cos(state.HeadingRad) / radius,
            CrossTrackRate: horizontalSpeed * System.Math.Sin(state.HeadingRad) / radius);
    }

    /// <summary>
    /// Samples the production drag and lift at the commanded angle of attack. The
    /// arbitrary flow axis only builds the force plane; the propagator consumes magnitudes.
    /// </summary>
    private static (double DragN, double LiftN) SampleForces(
        Dynamics dynamics,
        double altitudeM,
        double speedMps)
    {
        if (speedMps < 1e-3)
            return (0.0, 0.0);

        double density = dynamics.DensityAtAltitude(altitudeM);
        if (density <= 0.0)
            return (0.0, 0.0);

        double temperature = dynamics.TemperatureAtAltitude(altitudeM);
        var flow = Vector3d.Forward * speedMps;
        var axis = AerodynamicsModel.ComputeLiftUpEntryAxis(
            Vector3d.Up,
            Vector3d.Forward,
            dynamics.AngleOfAttackDegrees);
        double drag = AerodynamicsModel.ComputeReentryDrag(
            density,
            flow,
            axis,
            dynamics.VehicleLengthM,
            dynamics.VehicleDiameterM,
            temperature,
            dynamics.AxialDragCoefficient).Magnitude;
        double lift = AerodynamicsModel.ComputeLift(
            density,
            flow,
            axis,
            dynamics.VehicleLengthM,
            dynamics.VehicleDiameterM).Magnitude;
        return (drag, lift);
    }

    private static double SpecificEnergy(double speedMps, double radiusM, double mu)
    {
        if (radiusM <= 0.0 || mu <= 0.0 || !double.IsFinite(speedMps))
            return double.NaN;
        return 0.5 * speedMps * speedMps - mu / radiusM;
    }
}
