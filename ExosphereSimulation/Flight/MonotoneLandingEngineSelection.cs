namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// Engine-count selection for a single continuous landing burn. The count only ever falls:
/// it picks the fewest engines, up to <paramref name="currentCount"/>, that keep the demand
/// within a throttle headroom of rated thrust and whose minimum-throttle gimbal moment covers
/// the predicted aerodynamic moment by a margin. A Raptor that shuts down is never relit,
/// which is how the flown burns step from three engines to two to one.
/// </summary>
public static class MonotoneLandingEngineSelection
{
    public static int Select(Vessel ship, CelestialBody body, Part engines, int currentCount,
        double force, double pressure, double gimbalRangeDeg, double throttleHeadroom,
        double controlAuthorityMargin)
    {
        int authorityCount = 1;
        engines.SelectEngineCount(1);
        double perEngineMinimumThrust = engines.GetFullThrottleThrustMagnitude(pressure)*engines.Definition.MinThrottle;
        if (ship.Parts.TryGetPartLocalPosition(engines, out var engineStation))
        {
            double arm = System.Math.Abs(ship.CenterOfMassOffsetLocal.Y - engineStation.Y);
            double perEngineMoment = perEngineMinimumThrust*System.Math.Sin(gimbalRangeDeg*MathUtils.DEG_TO_RAD)*arm;
            var aero = ship.PredictStaticAerodynamicTorqueBody(body);
            double transverse = System.Math.Sqrt(aero.X*aero.X + aero.Z*aero.Z);
            if (perEngineMoment > 0)
                authorityCount = (int)System.Math.Ceiling(controlAuthorityMargin*transverse/perEngineMoment);
        }
        int selected = currentCount;
        for (int count = 1; count <= currentCount; count++)
        {
            engines.SelectEngineCount(count);
            if (count >= authorityCount && force <= throttleHeadroom*engines.GetFullThrottleThrustMagnitude(pressure))
            { selected = count; break; }
        }
        engines.SelectEngineCount(selected);
        return selected;
    }
}
