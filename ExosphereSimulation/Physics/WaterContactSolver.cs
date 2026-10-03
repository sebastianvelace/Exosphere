namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>Opt-in, calm-water terminal entry estimate. Geometry is relative to the visible skirt datum.</summary>
public sealed record WaterContactDefinition(double RadiusM, double CylinderHeightM,
    double LowestPointYM, double CenterOfMassYM, double DensityKgPerM3,
    double DragCoefficient, double WettingDepthM, double MinimumLatitudeDegrees,
    double MaximumLatitudeDegrees, double MinimumLongitudeDegrees, double MaximumLongitudeDegrees)
{
    public bool Covers(CelestialBody body, Vector3d position, double time)
    {
        if (body.Id != "earth") return false;
        body.GetGeodeticCoordinatesAtTime(position, time, out double lat, out double lon, out _);
        return lat >= MinimumLatitudeDegrees && lat <= MaximumLatitudeDegrees
            && lon >= MinimumLongitudeDegrees && lon <= MaximumLongitudeDegrees;
    }
}

public readonly record struct WaterContactWrench(Vector3d ForceWorld, Vector3d TorqueWorld,
    double LowestPointAltitudeM, double EntrySpeedMps, double SubmergedVolumeM3);

/// <summary>
/// First-order upright-cylinder water entry: Archimedes support and quadratic drag opposing
/// motion relative to rotating water. No rigid floor, surface anchoring or attitude snap.
/// Valid only for the bounded upright terminal phase; not a flooding/capsize solver or wave model.
/// </summary>
public static class WaterContactSolver
{
    public static WaterContactWrench Evaluate(Vessel ship, CelestialBody body,
        WaterContactDefinition definition, Vector3d position, Vector3d velocity)
    {
        var up = body.GetGeodeticUp(position);
        var axis = ship.Orientation.Rotate(Vector3d.Up);
        var lowest = position + axis*definition.LowestPointYM;
        double gap = body.GetAltitude(lowest);
        var pointVelocity = velocity + ship.AngularVelocity.Cross(axis*definition.LowestPointYM)
            - body.Velocity - body.GetSurfaceVelocity(lowest);
        if (gap >= 0) return new(Vector3d.Zero, Vector3d.Zero, gap, pointVelocity.Magnitude, 0);
        double upright = System.Math.Max(0.1, axis.Dot(up));
        double submergedHeight = System.Math.Clamp(-body.GetAltitude(position)/upright,
            0, definition.CylinderHeightM);
        double area = System.Math.PI*definition.RadiusM*definition.RadiusM;
        double volume = area*submergedHeight;
        var buoyancy = up*(definition.DensityKgPerM3*volume*body.GetGravityAt(position).Magnitude);
        double wetting = System.Math.Clamp(-gap/definition.WettingDepthM, 0, 1);
        var drag = pointVelocity*(-0.5*definition.DensityKgPerM3*definition.DragCoefficient
            *area*wetting*pointVelocity.Magnitude);
        var centerOfMass = position + axis*definition.CenterOfMassYM;
        var buoyancyCenter = position + axis*(submergedHeight*0.5);
        var torque = (lowest-centerOfMass).Cross(drag)
            + (buoyancyCenter-centerOfMass).Cross(buoyancy);
        return new(buoyancy+drag, torque, gap, pointVelocity.Magnitude, volume);
    }
}
