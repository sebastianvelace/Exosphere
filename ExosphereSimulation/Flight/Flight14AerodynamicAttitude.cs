namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>Estimated model-based flap trim for Flight 14's unpowered descent.</summary>
public static class Flight14AerodynamicAttitude
{
    /// <summary>
    /// Adds a bounded static-moment trim to ordinary attitude feedback. Evaluates the
    /// existing rotational force model without changing state or actuator coefficients.
    /// Aerodynamic blending and declared jets retain low-q control, and the physical
    /// flap servos must still reach the request.
    /// </summary>
    public static Vector3d ComputeCommand(Vessel ship, CelestialBody body,
        Quaterniond reference, double trimOnsetPressurePa, double trimFullPressurePa)
    {
        // The command is a fraction of the ship's whole authority (flaps, jets, gimbals).
        var authority3 = ship.FlightControlInvertsAerodynamics ? ship.GetAttitudeAuthorityBody(body) : Vector3d.Zero;
        var feedback = ship.FlightControlInvertsAerodynamics && authority3.X > 1e-9
                && authority3.Y > 1e-9 && authority3.Z > 1e-9
            ? ScheduledFeedback(ship.Orientation, reference, ship.AngularVelocity, authority3,
                ship.GetSurfaceVelocity(body).Magnitude)
            : AttitudeGuidance.ComputeCommand(ship.Orientation, reference, ship.AngularVelocity, 2, 25);
        double authority = ControlAuthority.Evaluate(ship);
        double q = ship.GetDynamicPressure(body);
        // Flight control already inverts the predicted moment of the distributed model
        // (hull and physical flaps); a second, aggregate-model trim would double it.
        if (body.Atmosphere == null || q <= trimOnsetPressurePa
            || authority <= 0 || !ship.HasFunctionalBodyFlaps()
            || ship.FlightControlInvertsAerodynamics) return feedback;
        var velocity = ship.GetSurfaceVelocity(body);
        double altitude = ship.GetAltitude(body);
        double density = body.Atmosphere.GetDensity(altitude);
        double? centerOffset = ship.Parts.AerodynamicCenterOffsetYM;
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

    /// <summary>Closed-loop attitude bandwidth (rad/s) and damping ratio on physical flaps.</summary>
    public const double ScheduledNaturalFrequencyRadPerS = 0.3;
    public const double ScheduledDampingRatio = 0.8;

    // Pitch/yaw share flap travel with the large static trim moment. Keep their loop
    // below the servo/trim bandwidth; transverse saturation otherwise excites roll
    // through the coupled plate allocation during hypersonic bank reversals.
    public const double TransverseNaturalFrequencyRadPerS = 0.15;
    public const double TransverseDampingRatio = 1.0;
    public const double TransverseSlowLoopOnsetSpeedMps = 500.0;
    public const double TransverseSlowLoopFullSpeedMps = 1_000.0;

    /// <summary>
    /// Gain-scheduled feedback: the angular acceleration ωn²·error − 2ζωn·rate on each axis,
    /// normalised by that axis's full authority (flaps, RCS jets and gimbals), so the loop keeps
    /// its per-axis bandwidth and damping across changing aerodynamic authority. The
    /// slower hypersonic transverse loop returns smoothly to the terminal trim loop.
    /// </summary>
    private static Vector3d ScheduledFeedback(Quaterniond current, Quaterniond desired,
        Vector3d angularVelocityWorld, Vector3d authorityBody, double airspeedMps)
    {
        var q = (desired * current.Inverse()).Normalize();
        if (q.W < 0.0) q = new Quaterniond(-q.W, -q.X, -q.Y, -q.Z);
        double w = System.Math.Clamp(q.W, -1.0, 1.0);
        double angle = 2.0 * System.Math.Acos(w);
        double sinHalf = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - w * w));
        var error = Vector3d.Zero;
        if (angle > 1e-6 && sinHalf > 1e-8)
            error = current.Inverse().Rotate(new Vector3d(q.X, q.Y, q.Z) / sinHalf) * angle;
        var rate = current.Inverse().Rotate(angularVelocityWorld);
        double kp = ScheduledNaturalFrequencyRadPerS * ScheduledNaturalFrequencyRadPerS;
        double kd = 2.0 * ScheduledDampingRatio * ScheduledNaturalFrequencyRadPerS;
        double blend = System.Math.Clamp((airspeedMps - TransverseSlowLoopOnsetSpeedMps)
            / (TransverseSlowLoopFullSpeedMps - TransverseSlowLoopOnsetSpeedMps), 0.0, 1.0);
        double transverseFrequency = ScheduledNaturalFrequencyRadPerS
            + blend * (TransverseNaturalFrequencyRadPerS - ScheduledNaturalFrequencyRadPerS);
        double transverseDamping = ScheduledDampingRatio
            + blend * (TransverseDampingRatio - ScheduledDampingRatio);
        double transverseKp = transverseFrequency * transverseFrequency;
        double transverseKd = 2.0 * transverseDamping * transverseFrequency;
        var acceleration = new Vector3d(
            error.X * transverseKp - rate.X * transverseKd,
            error.Y * kp - rate.Y * kd,
            error.Z * transverseKp - rate.Z * transverseKd);
        // Body X = pitch, Z = yaw, Y = roll; semantic controls are pitch/yaw/roll.
        return new Vector3d(
            System.Math.Clamp(acceleration.X / authorityBody.X, -1.0, 1.0),
            System.Math.Clamp(acceleration.Z / authorityBody.Z, -1.0, 1.0),
            System.Math.Clamp(acceleration.Y / authorityBody.Y, -1.0, 1.0));
    }

    private static double Trim(double disturbance, double authority) => authority > 1e-9
        ? System.Math.Clamp(-disturbance/authority, -1, 1) : 0;
    private static double Saturate(double command) => System.Math.Clamp(command, -1, 1);
}
