namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// The two catch fittings as a rigid pair over the cradle: each pin's gap and speed toward
/// the cradle, its lever along the horizontal pin line from the centre of mass, and the
/// transverse inertia that resists levelling the pair. A ship hovering on one off-axis
/// Raptor tilts its axis to keep the thrust vertical, so its pins reach the cradle at
/// different heights; the first pin then carries the drop until the ship rotates onto the
/// second, and a symmetric two-pin estimate understates the first compression peak.
/// </summary>
public readonly record struct CatchPinPair(
    double GapAM, double GapBM, double LeverAM, double LeverBM,
    double ClosingAMps, double ClosingBMps, double TransverseInertiaKgM2)
{
    /// <summary>True when the pair has a usable lever and inertia.</summary>
    public bool IsDefined => TransverseInertiaKgM2 > 0.0 && System.Math.Abs(LeverBM - LeverAM) > 1e-6;

    /// <summary>
    /// Measures the two enabled pins of <paramref name="ship"/> against a cradle at
    /// <paramref name="cradlePosition"/> moving at <paramref name="cradleVelocity"/>; undefined
    /// unless exactly two pins are enabled.
    /// </summary>
    public static CatchPinPair Measure(Vessel ship, Vector3d cradlePosition, Vector3d cradleVelocity, Vector3d cradleUp)
    {
        var up = cradleUp.Normalized;
        var pins = ship.CatchContactPoints;
        int a = -1, b = -1;
        for (int i = 0; i < pins.Count; i++)
        {
            if (!pins[i].Enabled) continue;
            if (a < 0) a = i;
            else if (b < 0) b = i;
            else return default;
        }
        if (b < 0) return default;

        var com = ship.CenterOfMass;
        var comVelocity = ship.CenterOfMassVelocity;
        var pinA = ship.Position + ship.Orientation.Rotate(pins[a].LocalPositionFromDatum);
        var pinB = ship.Position + ship.Orientation.Rotate(pins[b].LocalPositionFromDatum);
        var line = pinB - pinA;
        line -= up * line.Dot(up);
        if (line.MagnitudeSquared < 1e-12) return default;
        line = line.Normalized;

        double Gap(Vector3d pin, int index) => (pin - cradlePosition).Dot(up) - pins[index].ContactRadiusM;
        double Closing(Vector3d pin) =>
            -(comVelocity + ship.AngularVelocity.Cross(pin - com) - cradleVelocity).Dot(up);
        return new CatchPinPair(Gap(pinA, a), Gap(pinB, b),
            (pinA - com).Dot(line), (pinB - com).Dot(line),
            Closing(pinA), Closing(pinB), ship.Parts.TransverseMomentOfInertia);
    }
}
