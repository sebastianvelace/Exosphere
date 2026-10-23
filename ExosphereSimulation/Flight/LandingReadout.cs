namespace Exosphere.Simulation.Flight;

/// <summary>Descent numbers a pilot lands by.</summary>
/// <param name="AltitudeM">Height above the surface (m).</param>
/// <param name="VerticalSpeedMps">Rate of climb (m/s); negative while descending.</param>
/// <param name="HorizontalSpeedMps">Surface-relative horizontal speed (m/s).</param>
/// <param name="TimeToImpactS">Unpowered fall time to the surface (s); +∞ when not falling to it.</param>
/// <param name="StoppingDistanceM">
/// Height a full-thrust burn needs to null the descent rate; +∞ when the engines cannot
/// out-pull gravity, NaN without engines.
/// </param>
/// <param name="BurnMarginM">Altitude left before that burn must start (m); negative = late.</param>
public readonly record struct LandingStatus(
    double AltitudeM,
    double VerticalSpeedMps,
    double HorizontalSpeedMps,
    double TimeToImpactS,
    double StoppingDistanceM,
    double BurnMarginM);

/// <summary>
/// Landing aids: ballistic time to impact under local gravity, and the full-thrust
/// "suicide burn" stopping height v²/(2(a − g)) from the vehicle's real maximum thrust
/// and mass. Vertical-only, like Kerbal Engineer's readout; drag is ignored, so on an
/// atmospheric body it is conservative.
/// </summary>
public static class LandingReadout
{
    public static LandingStatus Compute(Vessel vessel, CelestialBody body)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(body);
        var relative = vessel.Position - body.Position;
        var up = relative.Normalized;
        var surface = vessel.GetSurfaceVelocity(body);
        double altitude = vessel.GetAltitude(body);
        double vertical = surface.Dot(up);
        double horizontal = (surface - up * vertical).Magnitude;
        double gravity = body.GM / relative.MagnitudeSquared;

        // h + v t − ½ g t² = 0 → t = (v + √(v² + 2 g h)) / g
        double timeToImpact = altitude <= 0.0 ? 0.0
            : (vertical + System.Math.Sqrt(vertical * vertical + 2.0 * gravity * altitude)) / gravity;
        // A conic whose periapsis clears the surface is an orbit, not a fall.
        var orbit = OrbitalElements.FromStateVector(relative, vessel.Velocity - body.Velocity, body.GM, body.Id, 0.0);
        if (altitude > 0.0 && orbit.Periapsis > body.Radius) timeToImpact = double.PositiveInfinity;

        double thrust = vessel.GetMaximumThrust(body);
        double stopping = double.NaN;
        if (thrust > 0.0)
        {
            double net = thrust / System.Math.Max(1.0, vessel.TotalMass) - gravity;
            stopping = net > 0.0 ? vertical < 0.0 ? vertical * vertical / (2.0 * net) : 0.0
                : double.PositiveInfinity;
        }
        return new LandingStatus(altitude, vertical, horizontal, timeToImpact, stopping,
            double.IsFinite(stopping) ? altitude - stopping : double.NaN);
    }
}
