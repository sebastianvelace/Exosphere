namespace Exosphere.Simulation.Visual;

/// <summary>Pure camera-fit contract shared by staging tests and the Godot chase camera.</summary>
public static class VehicleCameraFraming
{
    /// <summary>
    /// Deterministic early-ascent composition. VehicleFocus is zero for the opening
    /// tower shot and one once the camera has completed its move to the vehicle.
    /// CameraDistance is expressed in render units.
    /// </summary>
    public readonly record struct EarlyFlightCameraFrame(
        double VehicleFocus,
        double CameraDistance);

    /// <summary>
    /// Keep depth precision as an external camera pulls away. Distances are render
    /// units. Only the nearest one percent of the target distance is clipped;
    /// cockpit cameras retain their separate close-focus projection.
    /// </summary>
    public static double ExternalNearPlane(double targetDistanceRenderUnits, double minimumNear)
    {
        double floor = double.IsFinite(minimumNear) && minimumNear > 0 ? minimumNear : 0.1;
        if (!double.IsFinite(targetDistanceRenderUnits) || targetDistanceRenderUnits <= 0)
            return floor;
        return System.Math.Max(floor, System.Math.Min(1000.0, targetDistanceRenderUnits * 0.01));
    }

    /// <summary>
    /// Ground-anchored pad tracking distance. The opening shot stays at three
    /// vehicle heights through the early transition, then has a relative upper
    /// bound so no vehicle can collapse into a tiny subject at high altitude.
    /// </summary>
    public static double PadTrackingDistance(
        double vehicleHeightRenderUnits,
        double groundYRenderUnits)
    {
        if (!double.IsFinite(vehicleHeightRenderUnits)
            || !double.IsFinite(groundYRenderUnits)
            || vehicleHeightRenderUnits <= 0.0)
            return 40.0;
        double span = System.Math.Max(
            vehicleHeightRenderUnits - groundYRenderUnits,
            vehicleHeightRenderUnits);
        double minimum = vehicleHeightRenderUnits * 3.0;
        double maximum = vehicleHeightRenderUnits * 4.5;
        return System.Math.Clamp(span * 1.2, minimum, maximum);
    }

    /// <summary>
    /// Moves from a tower-context shot to a vehicle-focused tracking shot as a
    /// function of altitude alone. Smoothstep gives zero velocity at both ends,
    /// making the result deterministic and safe to hand over to the chase camera.
    /// </summary>
    public static EarlyFlightCameraFrame EarlyFlightFrame(
        double vehicleHeightRenderUnits,
        double groundYRenderUnits)
    {
        if (!double.IsFinite(vehicleHeightRenderUnits)
            || !double.IsFinite(groundYRenderUnits)
            || vehicleHeightRenderUnits <= 0.0)
            return new EarlyFlightCameraFrame(1.0, 40.0);

        double altitude = System.Math.Max(0.0, -groundYRenderUnits);
        double transitionStart = vehicleHeightRenderUnits * 0.25;
        double transitionEnd = vehicleHeightRenderUnits * 1.5;
        double linear = System.Math.Clamp(
            (altitude - transitionStart) / (transitionEnd - transitionStart),
            0.0,
            1.0);
        double vehicleFocus = linear * linear * (3.0 - 2.0 * linear);

        double contextDistance = PadTrackingDistance(
            vehicleHeightRenderUnits, groundYRenderUnits);
        double vehicleDistance = vehicleHeightRenderUnits * 1.65;
        double cameraDistance = contextDistance
            + (vehicleDistance - contextDistance) * vehicleFocus;

        return new EarlyFlightCameraFrame(vehicleFocus, cameraDistance);
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
