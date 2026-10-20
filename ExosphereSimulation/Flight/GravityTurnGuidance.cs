namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// First-stage gravity turn: follow a short pitch-over program, then fly the atmosphere-
/// relative velocity at zero angle of attack and let gravity bend the trajectory. Holding
/// an open-loop elevation through the whole boost instead points thrust away from the
/// velocity and pays large steering losses. Pure function: returns an aim direction.
/// </summary>
public static class GravityTurnGuidance
{
    /// <param name="pitchProgram">Commanded elevation (degrees) during the pitch-over.</param>
    /// <param name="pitchOverEndSeconds">Time after liftoff at which the turn follows velocity.</param>
    public static Vector3d ComputeAim(Vessel vessel, CelestialBody body, Vector3d downrange,
        double elapsedSeconds, Func<double, double> pitchProgram, double pitchOverEndSeconds)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(pitchProgram);
        var up = body.GetGeodeticUp(vessel.Position);
        var programmed = AttitudeGuidance.AimFromElevation(
            up, downrange, pitchProgram(elapsedSeconds) * MathUtils.DEG_TO_RAD);
        if (elapsedSeconds < pitchOverEndSeconds) return programmed;

        var airVelocity = vessel.GetSurfaceVelocity(body);
        return airVelocity.MagnitudeSquared > 100.0 ? airVelocity.Normalized : programmed;
    }
}
