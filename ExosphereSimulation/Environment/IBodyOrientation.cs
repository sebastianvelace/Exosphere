namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Math;

/// <summary>
/// Orientation of a body's rotating frame in inertial space (environment plan E0.3). Every
/// spin quantity of <see cref="CelestialBody"/> (axis, angular speed, the rotation angle that
/// places surface points at a time) comes from here, so surface velocity, latitude, east and
/// dated site positions cannot drift apart. <see cref="FixedAxisRotation"/> is today's model;
/// IERS Earth orientation and the DE440 lunar principal axes (plan E1-E2) replace it per body.
/// </summary>
public interface IBodyOrientation
{
    /// <summary>Short identifier recorded in saves and telemetry, e.g. "fixed-axis".</summary>
    string Id { get; }

    /// <summary>Unit spin axis (north pole) in the inertial frame.</summary>
    Vector3d SpinAxis(CelestialBody body);

    /// <summary>Signed spin rate (rad/s); negative is retrograde, zero is non-rotating.</summary>
    double AngularSpeed(CelestialBody body);

    /// <summary>
    /// Rotation of the body-fixed frame about <see cref="SpinAxis"/> at
    /// <paramref name="time"/> (rad, seconds since J2000), relative to its frame at epoch zero.
    /// </summary>
    double RotationAngleAt(CelestialBody body, double time);
}

/// <summary>
/// Uniform rotation about a fixed axis: the J2000 ecliptic normal tilted by
/// <see cref="CelestialBody.AxialTilt"/> toward +Y, at the sidereal rate
/// 2π / <see cref="CelestialBody.RotationalPeriod"/>, with the prime meridian at the
/// simulation's convention at t = 0 (not a dated Greenwich phase). For Earth this gives the
/// J2000 celestial pole and the real seasonal solar declination; other bodies share the tilt
/// azimuth, so their obliquity is exact and their pole longitude approximate. Bit for bit the
/// model the simulation has always used.
/// </summary>
public sealed class FixedAxisRotation : IBodyOrientation
{
    public static readonly FixedAxisRotation Instance = new();

    public string Id => "fixed-axis";

    public Vector3d SpinAxis(CelestialBody body)
    {
        double tiltRad = body.AxialTilt * MathUtils.DEG_TO_RAD;
        return new Vector3d(0.0, System.Math.Sin(tiltRad), System.Math.Cos(tiltRad)).Normalized;
    }

    public double AngularSpeed(CelestialBody body) =>
        System.Math.Abs(body.RotationalPeriod) < 1.0 ? 0.0 : 2.0 * System.Math.PI / body.RotationalPeriod;

    public double RotationAngleAt(CelestialBody body, double time) => AngularSpeed(body) * time;
}
