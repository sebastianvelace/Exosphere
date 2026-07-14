namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>
/// Finite ray segment through a spherical atmosphere, expressed as distances along a normalised
/// ray. The segment ends at the solid surface when the planet occludes the far atmosphere.
/// </summary>
public readonly record struct AtmosphereRaySegment(
    double StartDistance,
    double EndDistance,
    double ClosestApproachDistance,
    bool HitsSurface)
{
    public double Length => EndDistance - StartDistance;
}

/// <summary>
/// Pure spherical geometry shared by CPU verification and atmospheric ray marchers. Keeping this
/// outside rendering code makes horizon/tangent behaviour testable at true planetary scale.
/// Coordinates are relative to the planet centre; radii and returned distances use the same unit.
/// </summary>
public static class AtmosphereRaySampling
{
    private const double RelativeEpsilon = 1e-12;

    /// <summary>
    /// Clips a forward ray against the outer atmosphere and the opaque planet. Returns
    /// <see langword="null"/> when the ray never traverses atmosphere in front of the observer.
    /// An origin inside the solid planet is invalid and also returns null.
    /// </summary>
    public static AtmosphereRaySegment? IntersectShell(
        Vector3d originFromPlanetCentre,
        Vector3d rayDirection,
        double planetRadius,
        double atmosphereTopRadius)
    {
        if (!IsFinite(originFromPlanetCentre) || !IsFinite(rayDirection)
            || !double.IsFinite(planetRadius) || !double.IsFinite(atmosphereTopRadius)
            || planetRadius <= 0.0 || atmosphereTopRadius <= planetRadius)
            return null;

        double directionMagnitude = rayDirection.Magnitude;
        if (!double.IsFinite(directionMagnitude) || directionMagnitude <= 0.0)
            return null;
        Vector3d direction = rayDirection / directionMagnitude;

        double originRadius = originFromPlanetCentre.Magnitude;
        double tolerance = atmosphereTopRadius * RelativeEpsilon;
        if (originRadius < planetRadius - tolerance)
            return null;
        // At the solid boundary an inward ray enters rock immediately; the zero-distance
        // surface root is real occlusion, unlike an outward or exactly tangent ray.
        if (System.Math.Abs(originRadius - planetRadius) <= tolerance
            && originFromPlanetCentre.Dot(direction) < -tolerance)
            return null;

        if (!TrySphereRoots(originFromPlanetCentre, direction, atmosphereTopRadius,
                out double outerNear, out double outerFar)
            || outerFar <= tolerance)
            return null;

        double start = System.Math.Max(0.0, outerNear);
        double end = outerFar;
        bool hitsSurface = false;

        if (TrySphereRoots(originFromPlanetCentre, direction, planetRadius,
                out double surfaceNear, out double surfaceFar))
        {
            // A root at the observer (standing on the surface and looking up/tangent) must not
            // occlude the sky. Pick the first root strictly ahead of the clipped segment.
            double surface = surfaceNear > start + tolerance
                ? surfaceNear
                : surfaceFar > start + tolerance
                    ? surfaceFar
                    : double.PositiveInfinity;
            if (surface < end)
            {
                end = surface;
                hitsSurface = true;
            }
        }

        if (!double.IsFinite(start) || !double.IsFinite(end) || end <= start + tolerance)
            return null;

        double closest = System.Math.Clamp(
            -originFromPlanetCentre.Dot(direction), start, end);
        return new AtmosphereRaySegment(start, end, closest, hitsSurface);
    }

    /// <summary>
    /// Returns the centre of sample <paramref name="index"/> using a quadratic distribution
    /// concentrated around the ray's closest approach to the planet, where exponential density
    /// changes fastest and limb scattering is generated.
    /// </summary>
    public static double TangentBiasedSampleDistance(
        AtmosphereRaySegment segment, int index, int sampleCount)
    {
        if (sampleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (index < 0 || index >= sampleCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (!double.IsFinite(segment.StartDistance) || !double.IsFinite(segment.EndDistance)
            || segment.EndDistance <= segment.StartDistance)
            throw new ArgumentException("Atmosphere ray segment must be finite and non-empty.",
                nameof(segment));

        double start = segment.StartDistance;
        double end = segment.EndDistance;
        double pivot = System.Math.Clamp(segment.ClosestApproachDistance, start, end);
        double u = (index + 0.5) / sampleCount;
        double edgeTolerance = (end - start) * RelativeEpsilon;

        if (pivot <= start + edgeTolerance)
            return start + (end - start) * u * u;
        if (pivot >= end - edgeTolerance)
        {
            double oneMinusU = 1.0 - u;
            return end - (end - start) * oneMinusU * oneMinusU;
        }

        if (u < 0.5)
        {
            double distanceFromPivot = 1.0 - 2.0 * u;
            return pivot - (pivot - start) * distanceFromPivot * distanceFromPivot;
        }

        double distanceFromPivotRight = 2.0 * u - 1.0;
        return pivot + (end - pivot) * distanceFromPivotRight * distanceFromPivotRight;
    }

    private static bool TrySphereRoots(Vector3d origin, Vector3d unitDirection, double radius,
        out double near, out double far)
    {
        double projected = origin.Dot(unitDirection);
        double discriminant = projected * projected
            - (origin.MagnitudeSquared - radius * radius);
        double numericalTolerance = radius * radius * RelativeEpsilon;
        if (discriminant < -numericalTolerance)
        {
            near = far = 0.0;
            return false;
        }

        double root = System.Math.Sqrt(System.Math.Max(0.0, discriminant));
        near = -projected - root;
        far = -projected + root;
        return true;
    }

    private static bool IsFinite(Vector3d value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
}
