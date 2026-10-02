namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>Estimated model-based flap trim for Flight 14's unpowered descent.</summary>
public static class Flight14AerodynamicAttitude
{
    /// <summary>
    /// Adds a bounded static-moment trim to ordinary attitude feedback. Evaluates the
    /// existing rotational force model without changing state or actuator coefficients.
    /// The legacy RCS floor is not inverted; aerodynamic blending and feedback retain
    /// low-q control, and the physical flap servos must still reach the request.
    /// </summary>
    public static Vector3d ComputeCommand(Vessel ship, CelestialBody body,
        Quaterniond reference, double trimOnsetPressurePa, double trimFullPressurePa)
    {
        var feedback = AttitudeGuidance.ComputeCommand(ship.Orientation, reference,
            ship.AngularVelocity, 2, 25);
        double authority = ControlAuthority.Evaluate(ship);
        double q = ship.GetDynamicPressure(body);
        if (body.Atmosphere == null || q <= trimOnsetPressurePa
            || authority <= 0 || !ship.HasFunctionalBodyFlaps()) return feedback;
        var velocity = ship.GetSurfaceVelocity(body);
        double altitude = ship.GetAltitude(body);
        double density = body.Atmosphere.GetDensity(altitude);
        double? centerOffset = null;
        foreach (var part in ship.Parts.PartList)
            if (part.Definition.AerodynamicCenterOffsetYM.HasValue)
            { centerOffset = part.Definition.AerodynamicCenterOffsetYM; break; }
        var disturbance = ship.Orientation.Inverse().Rotate(
            AerodynamicsModel.ComputeAttitudeAngularAcceleration(density, velocity,
                ship.Orientation.Rotate(Vector3d.Up), Vector3d.Zero,
                ship.VehicleLength, ship.MaximumDiameter, ship.Parts.TransverseMomentOfInertia,
                body.Atmosphere.GetTemperature(altitude), centerOffset));
        // Vessel scales flap commands by control health before the finite servo.
        // Include that factor once when converting nominal torque into a request.
        var unitFlap = ship.Orientation.Inverse().Rotate(
            AerodynamicsModel.ComputeFlapControlAngularAcceleration(density, velocity,
                ship.Orientation, new Vector3d(1, 1, 1), ship.VehicleLength,
                ship.MaximumDiameter, ship.Parts.TransverseMomentOfInertia)) * authority;
        double x = System.Math.Clamp((q-trimOnsetPressurePa)
            / (trimFullPressurePa-trimOnsetPressurePa), 0, 1);
        double blend = x*x*(3-2*x);
        // Physical local X=pitch, Z=yaw, Y=roll; semantic controls are pitch/yaw/roll.
        return new Vector3d(
            Saturate(feedback.X + blend*Trim(disturbance.X, unitFlap.X)),
            Saturate(feedback.Y + blend*Trim(disturbance.Z, unitFlap.Z)),
            Saturate(feedback.Z + blend*Trim(disturbance.Y, unitFlap.Y)));
    }

    private static double Trim(double disturbance, double authority) => authority > 1e-9
        ? System.Math.Clamp(-disturbance/authority, -1, 1) : 0;
    private static double Saturate(double command) => System.Math.Clamp(command, -1, 1);
}
