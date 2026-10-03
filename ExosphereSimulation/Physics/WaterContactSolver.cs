namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>Opt-in, calm-water terminal entry estimate. Geometry is relative to the visible skirt datum.</summary>
public sealed record WaterContactDefinition(double RadiusM, double CylinderHeightM,
    double LowestPointYM, double CenterOfMassYM, double DensityKgPerM3,
    double DragCoefficient, double WettingDepthM, double MinimumLatitudeDegrees,
    double MaximumLatitudeDegrees, double MinimumLongitudeDegrees, double MaximumLongitudeDegrees,
    double? SkirtDatumYM = null)
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
    double LowestPointAltitudeM, double EntrySpeedMps, double SubmergedVolumeM3,
    double HullLowestAltitudeM = double.PositiveInfinity,
    Vector3d BuoyancyCenterOffsetWorld = default,
    Vector3d DragForceWorld = default, Vector3d DragTorqueWorld = default,
    Vector3d WetHullStartOffsetWorld = default, Vector3d WetHullEndOffsetWorld = default,
    double WetMotionSpeedMps = 0);

/// <summary>
/// Calm-water sealed-cylinder estimate at arbitrary inclination. Circular sections are clipped
/// by the local mean sea plane; distributed cross-flow drag opposes each wetted section's motion.
/// The nozzle witness retains separate axial entry drag. No flooding, slamming or waves.
/// </summary>
public static class WaterContactSolver
{
    // Sixteen-point Gauss-Legendre quadrature. Split at the dry/full circular-section
    // boundaries, so a nearly vertical waterline cannot fall between fixed hull samples.
    private static readonly double[] Nodes = [
        -0.9894009349916499, -0.9445750230732326, -0.8656312023878318, -0.755404408355003,
        -0.6178762444026438, -0.4580167776572274, -0.2816035507792589, -0.09501250983763744,
        0.09501250983763744, 0.2816035507792589, 0.4580167776572274, 0.6178762444026438,
        0.755404408355003, 0.8656312023878318, 0.9445750230732326, 0.9894009349916499];
    private static readonly double[] Weights = [
        0.02715245941175409, 0.06225352393864789, 0.09515851168249278, 0.1246289712555339,
        0.1495959888165767, 0.1691565193950025, 0.1826034150449236, 0.1894506104550685,
        0.1894506104550685, 0.1826034150449236, 0.1691565193950025, 0.1495959888165767,
        0.1246289712555339, 0.09515851168249278, 0.06225352393864789, 0.02715245941175409];

    public static WaterContactWrench Evaluate(Vessel ship, CelestialBody body,
        WaterContactDefinition definition, Vector3d position, Vector3d velocity)
        => Evaluate(body, definition, position, velocity, ship.Orientation, ship.AngularVelocity,
            Vector3d.Up*definition.CenterOfMassYM);

    /// <summary>Pure stage evaluator; all kinematics belong to the same candidate state.</summary>
    public static WaterContactWrench Evaluate(CelestialBody body, WaterContactDefinition definition,
        Vector3d position, Vector3d velocity, Quaterniond orientation, Vector3d angularVelocityWorld,
        Vector3d centerOfMassFromSkirtBody)
    {
        var up = body.GetGeodeticUp(position);
        var axis = orientation.Rotate(Vector3d.Up);
        double vertical = System.Math.Clamp(axis.Dot(up), -1, 1);
        var radialUp = up-axis*vertical;
        double radialVertical = radialUp.Magnitude;
        var radialDirection = radialVertical > 1e-12 ? radialUp/radialVertical : Vector3d.Zero;
        double altitude = body.GetAltitude(position);
        double radius = definition.RadiusM, height = definition.CylinderHeightM;
        var nozzleOffset = axis*definition.LowestPointYM;
        var lowest = position+nozzleOffset;
        double gap = body.GetAltitude(lowest);
        double hullGap = altitude+System.Math.Min(0, height*vertical)-radius*radialVertical;
        var pointVelocity = RelativeVelocity(nozzleOffset);
        if (gap >= 0 && hullGap >= 0)
            return new(Vector3d.Zero, Vector3d.Zero, gap, pointVelocity.Magnitude, 0, hullGap);

        double area = System.Math.PI*radius*radius;
        var comOffset = orientation.Rotate(centerOfMassFromSkirtBody);
        double axialSpeed = pointVelocity.Dot(axis);
        double wetting = System.Math.Clamp(-gap/definition.WettingDepthM, 0, 1);
        var drag = axis*(-0.5*definition.DensityKgPerM3*definition.DragCoefficient
            *area*wetting*System.Math.Abs(axialSpeed)*axialSpeed);
        var dragTorque = (nozzleOffset-comOffset).Cross(drag);
        double volume = 0, wetSpeedMoment = 0;
        var firstMoment = Vector3d.Zero;
        Span<double> boundaries = stackalloc double[4];
        boundaries[0] = 0; boundaries[1] = height;
        int boundaryCount = 2;
        if (System.Math.Abs(vertical) > 1e-12)
        {
            boundaries[boundaryCount++] = System.Math.Clamp((-altitude-radius*radialVertical)/vertical, 0, height);
            boundaries[boundaryCount++] = System.Math.Clamp((-altitude+radius*radialVertical)/vertical, 0, height);
        }
        boundaries[..boundaryCount].Sort();
        for (int segment = 1; segment < boundaryCount; segment++)
        {
            double half = (boundaries[segment]-boundaries[segment-1])*0.5;
            double middle = (boundaries[segment]+boundaries[segment-1])*0.5;
            if (half <= 0) continue;
            for (int sample = 0; sample < Nodes.Length; sample++)
            {
                double y = middle+half*Nodes[sample];
                double sectionAltitude = altitude+y*vertical;
                double sectionArea, radialMoment;
                double cut = radialVertical > 1e-12 ? -sectionAltitude/radialVertical : radius;
                if (radialVertical < 1e-12)
                { sectionArea = sectionAltitude < 0 ? area : 0; radialMoment = 0; }
                else
                {
                    if (cut <= -radius) { sectionArea = 0; radialMoment = 0; }
                    else if (cut >= radius) { sectionArea = area; radialMoment = 0; }
                    else
                    {
                        double root = System.Math.Sqrt(System.Math.Max(0, radius*radius-cut*cut));
                        sectionArea = radius*radius*(System.Math.PI*0.5+System.Math.Asin(cut/radius))+cut*root;
                        radialMoment = -2.0/3*root*root*root;
                    }
                }
                if (sectionArea <= 0) continue;
                double lengthWeight = half*Weights[sample];
                double displaced = sectionArea*lengthWeight;
                var offset = axis*y+radialDirection*(radialMoment/sectionArea);
                volume += displaced; firstMoment += offset*displaced;
                // Project the wet circular segment onto the direction perpendicular
                // to cross-flow. A shallow horizontal hull has a much wider silhouette
                // than its wet volume fraction would suggest.
                var sectionVelocity = RelativeVelocity(offset);
                wetSpeedMoment += displaced*sectionVelocity.Magnitude;
                var crossFlow = sectionVelocity-axis*sectionVelocity.Dot(axis);
                double width = 2*radius;
                if (cut < radius && crossFlow.MagnitudeSquared > 1e-24)
                {
                    var side = axis.Cross(crossFlow.Normalized);
                    double k = System.Math.Clamp(side.Dot(radialDirection), -1, 1);
                    double edge = System.Math.Sqrt(System.Math.Max(0, (1-k*k)*(radius*radius-cut*cut)));
                    double maximum = radius*k <= cut ? radius : k*cut+edge;
                    double minimum = -radius*k <= cut ? -radius : k*cut-edge;
                    width = System.Math.Max(0, maximum-minimum);
                }
                var sectionDrag = crossFlow*(-0.5*definition.DensityKgPerM3*definition.DragCoefficient
                    *width*lengthWeight*crossFlow.Magnitude);
                drag += sectionDrag; dragTorque += (offset-comOffset).Cross(sectionDrag);
            }
        }
        var center = volume > 0 ? firstMoment/volume : Vector3d.Zero;
        var buoyancy = up*(definition.DensityKgPerM3*volume*body.GetGravityAt(position).Magnitude);
        return new(buoyancy+drag, (center-comOffset).Cross(buoyancy)+dragTorque,
            gap, pointVelocity.Magnitude, volume, hullGap, center, drag, dragTorque,
            axis*(vertical < -1e-12 ? System.Math.Clamp((-altitude+radius*radialVertical)/vertical, 0, height) : 0),
            axis*(vertical > 1e-12 ? System.Math.Clamp((-altitude+radius*radialVertical)/vertical, 0, height) : height),
            volume > 0 ? wetSpeedMoment/volume : pointVelocity.Magnitude);

        Vector3d RelativeVelocity(Vector3d offset) => velocity+angularVelocityWorld.Cross(offset)
            -body.Velocity-body.GetSurfaceVelocity(position+offset);
    }
}
