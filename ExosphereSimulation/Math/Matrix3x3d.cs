namespace Exosphere.Simulation.Math;

/// <summary>
/// A small immutable 3x3 matrix for double-precision rigid-body dynamics.
/// Values are stored in row-major order and vectors are treated as columns.
/// </summary>
public readonly struct Matrix3x3d : IEquatable<Matrix3x3d>
{
    public double M11 { get; }
    public double M12 { get; }
    public double M13 { get; }
    public double M21 { get; }
    public double M22 { get; }
    public double M23 { get; }
    public double M31 { get; }
    public double M32 { get; }
    public double M33 { get; }

    public Matrix3x3d(
        double m11, double m12, double m13,
        double m21, double m22, double m23,
        double m31, double m32, double m33)
    {
        M11 = m11;
        M12 = m12;
        M13 = m13;
        M21 = m21;
        M22 = m22;
        M23 = m23;
        M31 = m31;
        M32 = m32;
        M33 = m33;
    }

    public static Matrix3x3d Identity => new(
        1.0, 0.0, 0.0,
        0.0, 1.0, 0.0,
        0.0, 0.0, 1.0);

    public static Matrix3x3d Diagonal(double x, double y, double z) => new(
        x, 0.0, 0.0,
        0.0, y, 0.0,
        0.0, 0.0, z);

    public Vector3d Multiply(Vector3d vector) => new(
        M11 * vector.X + M12 * vector.Y + M13 * vector.Z,
        M21 * vector.X + M22 * vector.Y + M23 * vector.Z,
        M31 * vector.X + M32 * vector.Y + M33 * vector.Z);

    public double Determinant =>
        M11 * (M22 * M33 - M23 * M32)
        - M12 * (M21 * M33 - M23 * M31)
        + M13 * (M21 * M32 - M22 * M31);

    public Matrix3x3d Inverse()
    {
        double determinant = Determinant;
        if (!double.IsFinite(determinant) || System.Math.Abs(determinant) < 1e-24)
            throw new InvalidOperationException("Cannot invert a singular 3x3 matrix.");

        double inv = 1.0 / determinant;
        return new Matrix3x3d(
            (M22 * M33 - M23 * M32) * inv,
            (M13 * M32 - M12 * M33) * inv,
            (M12 * M23 - M13 * M22) * inv,
            (M23 * M31 - M21 * M33) * inv,
            (M11 * M33 - M13 * M31) * inv,
            (M13 * M21 - M11 * M23) * inv,
            (M21 * M32 - M22 * M31) * inv,
            (M12 * M31 - M11 * M32) * inv,
            (M11 * M22 - M12 * M21) * inv);
    }

    public bool Equals(Matrix3x3d other) =>
        M11 == other.M11 && M12 == other.M12 && M13 == other.M13
        && M21 == other.M21 && M22 == other.M22 && M23 == other.M23
        && M31 == other.M31 && M32 == other.M32 && M33 == other.M33;

    public override bool Equals(object? obj) => obj is Matrix3x3d other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        HashCode.Combine(M11, M12, M13, M21),
        HashCode.Combine(M22, M23, M31, M32),
        M33);

    public static bool operator ==(Matrix3x3d left, Matrix3x3d right) => left.Equals(right);
    public static bool operator !=(Matrix3x3d left, Matrix3x3d right) => !left.Equals(right);
}
