namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>
/// Frame-explicit diagnostics for orbital coast and atmospheric entry.
/// Orbital invariants use the body-centred inertial state; aerodynamic quantities use
/// velocity relative to the rotating atmosphere. Keeping those frames separate prevents
/// telemetry from hiding guidance or integration errors behind a mixed-frame speed.
/// </summary>
public readonly record struct EntryFlightState(
    double AltitudeM,
    double InertialSpeedMps,
    double AtmosphereRelativeSpeedMps,
    double DensityKgPerM3,
    double TemperatureK,
    double Mach,
    double DynamicPressurePa,
    double FlightPathAngleDegrees,
    double AngleOfAttackDegrees,
    double BankAngleDegrees,
    double PointMassSpecificOrbitalEnergyJPerKg,
    double SpecificAngularMomentumM2PerS,
    double DragForceN,
    double LiftForceN,
    double LiftToDragRatio,
    double AerodynamicLoadG,
    double StagnationHeatFluxWPerM2);

public static class EntryFlightDiagnostics
{
    private const double StandardGravity = 9.80665;
    private const double DirectionEpsilonSquared = 1e-12;

    /// <summary>
    /// Samples the vessel without mutating it. Undefined bank at zero lift or near-radial
    /// flight is reported as NaN rather than inventing a physically meaningless angle.
    /// </summary>
    public static EntryFlightState Evaluate(Vessel vessel, CelestialBody body)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(body);

        Vector3d relativePosition = vessel.Position - body.Position;
        Vector3d inertialVelocity = vessel.Velocity - body.Velocity;
        Vector3d atmosphereVelocity = vessel.GetSurfaceVelocity(body);
        Vector3d localUp = body.GetGeodeticUp(vessel.Position);
        Vector3d longitudinalAxis = vessel.Orientation.Rotate(Vector3d.Up).Normalized;

        double radius = relativePosition.Magnitude;
        double inertialSpeed = inertialVelocity.Magnitude;
        double airspeed = atmosphereVelocity.Magnitude;
        double altitude = body.GetAltitude(vessel.Position);
        double density = body.Atmosphere?.GetDensity(altitude) ?? 0.0;
        double temperature = body.Atmosphere?.GetTemperature(altitude) ?? 0.0;
        double mach = density > 0.0 && temperature > 0.0
            ? AerodynamicsModel.ComputeMach(airspeed, temperature)
            : double.NaN;
        double dynamicPressure = AerodynamicsModel.ComputeDynamicPressure(density, airspeed);

        double flightPathAngle = airspeed > 1e-6 && localUp.MagnitudeSquared > DirectionEpsilonSquared
            ? System.Math.Asin(System.Math.Clamp(
                atmosphereVelocity.Normalized.Dot(localUp.Normalized), -1.0, 1.0))
                * MathUtils.RAD_TO_DEG
            : double.NaN;
        double angleOfAttack = airspeed > 1e-6
            && longitudinalAxis.MagnitudeSquared > DirectionEpsilonSquared
            ? System.Math.Acos(System.Math.Clamp(
                longitudinalAxis.Dot(atmosphereVelocity.Normalized), -1.0, 1.0))
                * MathUtils.RAD_TO_DEG
            : double.NaN;

        Vector3d drag = Vector3d.Zero;
        Vector3d lift = Vector3d.Zero;
        if (density > 0.0 && airspeed > 1e-3)
        {
            drag = AerodynamicsModel.ComputeReentryDrag(
                density,
                atmosphereVelocity,
                longitudinalAxis,
                vessel.VehicleLength,
                vessel.MaximumDiameter,
                temperature,
                vessel.Parts.AxialDragCoefficient);
            lift = AerodynamicsModel.ComputeLift(
                density,
                atmosphereVelocity,
                longitudinalAxis,
                vessel.VehicleLength,
                vessel.MaximumDiameter);
        }

        double dragForce = drag.Magnitude;
        double liftForce = lift.Magnitude;
        double liftToDrag = dragForce > 1e-9 ? liftForce / dragForce : 0.0;
        double aerodynamicLoadG = vessel.TotalMass > 0.0
            ? (drag + lift).Magnitude / vessel.TotalMass / StandardGravity
            : 0.0;

        double bankAngle = ComputeBankAngleDegrees(
            atmosphereVelocity, localUp, lift);
        double specificEnergy = radius > 0.0 && body.GM > 0.0
            ? 0.5 * inertialSpeed * inertialSpeed - body.GM / radius
            : double.NaN;
        double angularMomentum = relativePosition.Cross(inertialVelocity).Magnitude;
        double heatFlux = vessel.ComputeStagnationHeatFlux(density, atmosphereVelocity);

        return new EntryFlightState(
            altitude,
            inertialSpeed,
            airspeed,
            density,
            temperature,
            mach,
            dynamicPressure,
            flightPathAngle,
            angleOfAttack,
            bankAngle,
            specificEnergy,
            angularMomentum,
            dragForce,
            liftForce,
            liftToDrag,
            aerodynamicLoadG,
            heatFlux);
    }

    private static double ComputeBankAngleDegrees(
        Vector3d atmosphereVelocity,
        Vector3d localUp,
        Vector3d lift)
    {
        if (atmosphereVelocity.MagnitudeSquared < DirectionEpsilonSquared
            || localUp.MagnitudeSquared < DirectionEpsilonSquared
            || lift.MagnitudeSquared < DirectionEpsilonSquared)
            return double.NaN;

        Vector3d flow = atmosphereVelocity.Normalized;
        Vector3d liftUp = localUp - flow * localUp.Dot(flow);
        Vector3d liftDirection = lift - flow * lift.Dot(flow);
        if (liftUp.MagnitudeSquared < DirectionEpsilonSquared
            || liftDirection.MagnitudeSquared < DirectionEpsilonSquared)
            return double.NaN;

        liftUp = liftUp.Normalized;
        liftDirection = liftDirection.Normalized;
        double sine = flow.Dot(liftUp.Cross(liftDirection));
        double cosine = System.Math.Clamp(liftUp.Dot(liftDirection), -1.0, 1.0);
        return System.Math.Atan2(sine, cosine) * MathUtils.RAD_TO_DEG;
    }
}
