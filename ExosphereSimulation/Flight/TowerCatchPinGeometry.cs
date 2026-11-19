namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>Contact geometry measured in the same inertial cradle plane as the pin solver.</summary>
public readonly record struct TowerCatchPinGeometry(
    int EnabledPins, double FirstPinGapM, double LastPinGapM, double FarthestPinRangeM)
{
    public const double CaptureRadiusM = 5.0;

    public bool BothPinsFit => EnabledPins == 2 && FarthestPinRangeM <= CaptureRadiusM;

    public static TowerCatchPinGeometry Measure(IReadOnlyList<ContactPointDefinition> pins,
        Vector3d datumFromCradle, Quaterniond orientation, Vector3d cradleUp)
    {
        var up = cradleUp.Normalized;
        if (up.MagnitudeSquared < 0.5) return default;

        int count = 0;
        double firstGap = double.PositiveInfinity;
        double lastGap = double.NegativeInfinity;
        double farthestRange = 0.0;
        for (int i = 0; i < pins.Count; i++)
        {
            var pin = pins[i];
            if (!pin.Enabled) continue;
            var offset = datumFromCradle + orientation.Rotate(pin.LocalPositionFromDatum);
            double height = offset.Dot(up);
            double gap = height - pin.ContactRadiusM;
            firstGap = System.Math.Min(firstGap, gap);
            lastGap = System.Math.Max(lastGap, gap);
            farthestRange = System.Math.Max(farthestRange, (offset - up * height).Magnitude);
            count++;
        }

        return count == 0 ? default : new(count, firstGap, lastGap, farthestRange);
    }
}
