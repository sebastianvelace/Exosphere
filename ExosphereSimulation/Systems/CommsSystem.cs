namespace Exosphere.Simulation.Systems;

using Exosphere.Simulation.Math;

public class CommsSystem
{
    public bool   HasSignal           { get; private set; } = true;
    public double SignalStrength      { get; private set; } = 1.0;   // 0..1
    public double SignalDelaySeconds  { get; private set; } = 0.0;
    public bool   LossOfSignalAlert   { get; private set; }

    // Max relay distance for DSN (approximate)
    private const double MaxDSNRangeM = 3.0e11;  // ~2 AU
    private const double SpeedOfLight = 3e8;     // m/s

    public void Tick(double dt, Vector3d vesselPosition, Vector3d earthPosition,
                     IReadOnlyList<CelestialBody> bodies)
    {
        double distToEarth = (vesselPosition - earthPosition).Magnitude;
        double earthRadius = 0.0;
        foreach (var body in bodies)
        {
            if (body.Id == "earth") { earthRadius = body.Radius; break; }
        }

        // Check line-of-sight (simplified: check if any body blocks the path)
        bool los = CheckLineOfSight(vesselPosition, earthPosition, bodies);

        SignalDelaySeconds = MissionGeometry.SignalDelaySeconds(
            vesselPosition, earthPosition, SpeedOfLight, earthRadius);
        SignalStrength     = los ? System.Math.Clamp(1.0 - distToEarth / MaxDSNRangeM, 0.0, 1.0) : 0.0;
        HasSignal          = los && SignalStrength > 0.05;
        LossOfSignalAlert  = !HasSignal;
    }

    private static bool CheckLineOfSight(Vector3d from, Vector3d to, IReadOnlyList<CelestialBody> bodies)
    {
        var    dir  = to - from;
        double dist = dir.Magnitude;
        if (dist < 1.0) return true;
        var unit = dir / dist;

        foreach (var body in bodies)
        {
            if (body.Id == "earth" || body.Id == "sun") continue;
            var    toBody  = body.Position - from;
            double t       = toBody.Dot(unit);
            if (t < 0 || t > dist) continue;
            var    closest = from + unit * t;
            double d       = (closest - body.Position).Magnitude;
            if (d < body.Radius) return false;
        }
        return true;
    }
}
