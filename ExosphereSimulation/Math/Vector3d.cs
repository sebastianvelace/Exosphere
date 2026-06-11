namespace Exosphere.Simulation.Math;

public readonly struct Vector3d : IEquatable<Vector3d>
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public Vector3d(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    // Static properties
    public static Vector3d Zero    => new(0.0, 0.0, 0.0);
    public static Vector3d One     => new(1.0, 1.0, 1.0);
    public static Vector3d Up      => new(0.0, 1.0, 0.0);
    public static Vector3d Forward => new(0.0, 0.0, -1.0);
    public static Vector3d Right   => new(1.0, 0.0, 0.0);

    // Computed properties
    public double MagnitudeSquared => X * X + Y * Y + Z * Z;
    public double Magnitude        => System.Math.Sqrt(MagnitudeSquared);

    public Vector3d Normalized
    {
        get
        {
            double mag = Magnitude;
            if (mag < double.Epsilon) return Zero;
            return new Vector3d(X / mag, Y / mag, Z / mag);
        }
    }

    // Instance methods
    public double  Dot(Vector3d other)         => X * other.X + Y * other.Y + Z * other.Z;
    public double  DistanceTo(Vector3d other)  => (this - other).Magnitude;

    public Vector3d Cross(Vector3d other) => new(
        Y * other.Z - Z * other.Y,
        Z * other.X - X * other.Z,
        X * other.Y - Y * other.X);

    public Vector3d Lerp(Vector3d target, double t) =>
        new(X + (target.X - X) * t,
            Y + (target.Y - Y) * t,
            Z + (target.Z - Z) * t);

    // Arithmetic operators
    public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3d operator -(Vector3d v)             => new(-v.X, -v.Y, -v.Z);
    public static Vector3d operator *(Vector3d v, double s)   => new(v.X * s, v.Y * s, v.Z * s);
    public static Vector3d operator *(double s, Vector3d v)   => new(v.X * s, v.Y * s, v.Z * s);
    public static Vector3d operator /(Vector3d v, double s)   => new(v.X / s, v.Y / s, v.Z / s);

    // Equality operators
    public static bool operator ==(Vector3d a, Vector3d b) =>
        a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    public static bool operator !=(Vector3d a, Vector3d b) => !(a == b);

    public bool Equals(Vector3d other) => this == other;
    public override bool Equals(object? obj) => obj is Vector3d v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    // Implicit conversions with tuples
    public static implicit operator Vector3d((double x, double y, double z) t) => new(t.x, t.y, t.z);
    public static implicit operator (double x, double y, double z)(Vector3d v) => (v.X, v.Y, v.Z);

    public override string ToString() => $"({X:G6}, {Y:G6}, {Z:G6})";
}
