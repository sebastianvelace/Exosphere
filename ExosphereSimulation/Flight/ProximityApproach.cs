namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// Proximity operations to a docking: braking toward a waypoint on the target port's axis
/// with a closing speed that shrinks with range, then a final axial approach at docking speed
/// with the lateral offset nulled. Translation reaction control flies it; the attitude loop
/// keeps the chaser's port facing the target's. Only commands are written.
/// </summary>
public sealed class ProximityApproach
{
    public const double WaypointDistanceM = 30.0;
    public const double FinalClosingSpeedMps = 0.12;

    /// <summary>Share of the translation deceleration the braking profile plans to use.</summary>
    public const double BrakingFraction = 0.8;

    /// <summary>
    /// Range (m) at which proximity operations take over from the intercept arc: where braking
    /// the planned arrival speed at <see cref="BrakingFraction"/> of the thrusters' deceleration
    /// ends at the target, and at least 3 km. An intercept arriving at 63 m/s on 0.24 m/s² of
    /// OAMS needs ~10 km, so a fixed 3 km hand-over sailed past; handing over on the current
    /// relative speed instead started 80 km out, where the arc still curves and the controller
    /// burned propellant fighting orbital dynamics.
    /// </summary>
    public static double HandoverRangeM(Vessel chaser, double arrivalSpeedMps)
    {
        ArgumentNullException.ThrowIfNull(chaser);
        double acceleration = BrakingFraction * chaser.Parts.RcsTranslation.ThrustN
            / System.Math.Max(1.0, chaser.TranslationalMassKg);
        return acceleration > 0.0
            ? System.Math.Max(3_000.0, arrivalSpeedMps * arrivalSpeedMps / (2.0 * acceleration))
            : 3_000.0;
    }

    public bool IsOnFinalApproach { get; private set; }
    public double PortDistanceM { get; private set; } = double.NaN;

    /// <summary>Writes translation and attitude commands to <paramref name="chaser"/>.</summary>
    public void Advance(Vessel chaser, Part chaserPort, Vessel target, Part targetPort, CelestialBody body)
    {
        ArgumentNullException.ThrowIfNull(chaser);
        ArgumentNullException.ThrowIfNull(target);
        if (!Universe.TryGetDockingFrame(chaser, chaserPort, out var chaserPosition, out _, out var chaserVelocity)
            || !Universe.TryGetDockingFrame(target, targetPort, out var targetPosition, out var targetAxis,
                out var targetVelocity))
        {
            chaser.TranslationCommand = Vector3d.Zero;
            return;
        }
        var offset = chaserPosition - targetPosition;
        var relativeVelocity = chaserVelocity - targetVelocity;
        PortDistanceM = offset.Magnitude;
        var waypoint = targetAxis * WaypointDistanceM;

        Vector3d desired;
        if (!IsOnFinalApproach)
        {
            var error = offset - waypoint;
            double distance = error.Magnitude;
            // Closing speed the thrusters can still stop at the braking fraction, √(2·f·a·d),
            // and no faster than covering the remaining distance in 20 s.
            double acceleration = chaser.Parts.RcsTranslation.ThrustN / System.Math.Max(1.0, chaser.TranslationalMassKg);
            double speed = System.Math.Max(0.1, System.Math.Min(
                System.Math.Sqrt(2.0 * BrakingFraction * acceleration * distance), distance / 20.0));
            desired = distance > 1e-6 ? -error / distance * speed : Vector3d.Zero;
            if (distance < 1.0 && relativeVelocity.Magnitude < 0.05) IsOnFinalApproach = true;
        }
        else
        {
            var lateral = offset - targetAxis * offset.Dot(targetAxis);
            desired = -targetAxis * FinalClosingSpeedMps - lateral / 10.0;
        }
        chaser.TranslationCommand = TranslationLambertBurn.TranslationCommandFor(
            chaser, desired - relativeVelocity, 2.0);

        // While braking from range the port faces the waypoint, so the closing speed is shed on
        // the forward-firing thrusters alone (braking on three axes at once cost ~1.7x the
        // propellant). Near the waypoint that line of sight is the docking axis itself.
        var toWaypoint = waypoint - offset;
        var pointing = !IsOnFinalApproach && toWaypoint.Magnitude > 50.0
            ? toWaypoint.Normalized
            : -targetAxis;
        var portAxisLocal = chaserPort.Definition.DockingAxisLocal is { Length: 3 } axis
            ? new Vector3d(axis[0], axis[1], axis[2]).Normalized
            : Vector3d.Up;
        chaser.SASEnabled = false;
        chaser.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(chaser.Orientation, portAxisLocal,
            pointing, chaser.AngularVelocity - target.AngularVelocity, proportionalGain: 2.3,
            dampingGain: AttitudeGuidance.DampingGainFor(2.3, chaser.GetPitchYawAngularAuthority(body)));
    }
}
