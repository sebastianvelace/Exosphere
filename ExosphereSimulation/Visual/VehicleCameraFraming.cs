namespace Exosphere.Simulation.Visual;

/// <summary>Pure camera-fit contract shared by staging tests and the Godot chase camera.</summary>
public static class VehicleCameraFraming
{
    public static double PadTrackingDistance(
        double vehicleHeightRenderUnits,
        double groundYRenderUnits)
    {
        if (!double.IsFinite(vehicleHeightRenderUnits)
            || !double.IsFinite(groundYRenderUnits)
            || vehicleHeightRenderUnits <= 0.0)
            return 28.0;
        double span = System.Math.Max(
            vehicleHeightRenderUnits - groundYRenderUnits,
            vehicleHeightRenderUnits);
        double minimum = vehicleHeightRenderUnits < 20.0
            ? System.Math.Max(12.0, vehicleHeightRenderUnits * 1.7)
            : vehicleHeightRenderUnits * 2.2;
        return System.Math.Clamp(span * 1.55, minimum, 850.0);
    }

    public static double MinimumOrbitDistance(
        double vehicleLengthM, double vehicleDiameterM, double verticalFovDegrees,
        double metresPerRenderUnit = 2.8, double margin = 1.25)
    {
        if (!double.IsFinite(vehicleLengthM) || !double.IsFinite(vehicleDiameterM)
            || vehicleLengthM <= 0.0 || vehicleDiameterM <= 0.0
            || verticalFovDegrees is <= 1.0 or >= 179.0 || metresPerRenderUnit <= 0.0)
            return 5.0;
        double halfLengthUnits = vehicleLengthM / (2.0 * metresPerRenderUnit);
        double radiusUnits = vehicleDiameterM / (2.0 * metresPerRenderUnit);
        double boundingRadius = System.Math.Sqrt(
            halfLengthUnits * halfLengthUnits + radiusUnits * radiusUnits);
        double halfFov = verticalFovDegrees * System.Math.PI / 360.0;
        return boundingRadius / System.Math.Tan(halfFov) * System.Math.Max(margin, 1.0);
    }
}
