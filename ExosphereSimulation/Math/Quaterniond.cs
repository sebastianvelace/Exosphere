namespace Exosphere.Simulation.Math;

public readonly struct Quaterniond : IEquatable<Quaterniond>
{
    public double W { get; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public Quaterniond(double w, double x, double y, double z)
    {
        W = w;
        X = x;
        Y = y;
        Z = z;
    }

    public static Quaterniond Identity => new(1.0, 0.0, 0.0, 0.0);

    // Quaternion multiplication (Hamilton product)
    public static Quaterniond operator *(Quaterniond a, Quaterniond b) => new(
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);

    public double NormSquared => W * W + X * X + Y * Y + Z * Z;
    public double Norm        => System.Math.Sqrt(NormSquared);

    /// <summary>Returns a normalized copy of this quaternion.</summary>
    public Quaterniond Normalize()
    {
        double n = Norm;
        if (n < double.Epsilon) return Identity;
        return new Quaterniond(W / n, X / n, Y / n, Z / n);
    }

    /// <summary>Returns the inverse (conjugate / norm²) of this quaternion.</summary>
    public Quaterniond Inverse()
    {
        double ns = NormSquared;
        if (ns < double.Epsilon) return Identity;
        return new Quaterniond(W / ns, -X / ns, -Y / ns, -Z / ns);
    }

    /// <summary>Rotates a vector by this quaternion: q * v * q⁻¹.</summary>
    public Vector3d Rotate(Vector3d v)
    {
        // Efficient sandwich product
        // t = 2 * (q.xyz cross v)
        double tx = 2.0 * (Y * v.Z - Z * v.Y);
        double ty = 2.0 * (Z * v.X - X * v.Z);
        double tz = 2.0 * (X * v.Y - Y * v.X);

        return new Vector3d(
            v.X + W * tx + (Y * tz - Z * ty),
            v.Y + W * ty + (Z * tx - X * tz),
            v.Z + W * tz + (X * ty - Y * tx));
    }

    /// <summary>Creates a quaternion from an axis and angle in radians.</summary>
    public static Quaterniond FromAxisAngle(Vector3d axis, double angleRad)
    {
        Vector3d normAxis = axis.Normalized;
        double half = angleRad * 0.5;
        double s    = System.Math.Sin(half);
        return new Quaterniond(
            System.Math.Cos(half),
            normAxis.X * s,
            normAxis.Y * s,
            normAxis.Z * s);
    }

    /// <summary>
    /// Creates a quaternion from Euler angles in degrees (intrinsic ZYX / yaw-pitch-roll).
    /// Application order: roll (Z) → pitch (X) → yaw (Y).
    /// </summary>
    public static Quaterniond FromEuler(double pitchDeg, double yawDeg, double rollDeg)
    {
        double p = pitchDeg * MathUtils.DEG_TO_RAD * 0.5;
        double y = yawDeg   * MathUtils.DEG_TO_RAD * 0.5;
        double r = rollDeg  * MathUtils.DEG_TO_RAD * 0.5;

        double cp = System.Math.Cos(p);  double sp = System.Math.Sin(p);
        double cy = System.Math.Cos(y);  double sy = System.Math.Sin(y);
        double cr = System.Math.Cos(r);  double sr = System.Math.Sin(r);

        // Yaw * Pitch * Roll
        return new Quaterniond(
            cy * cp * cr + sy * sp * sr,
            cy * sp * cr + sy * cp * sr,
            sy * cp * cr - cy * sp * sr,
            cy * cp * sr - sy * sp * cr);
    }

    /// <summary>Returns (pitch, yaw, roll) in degrees (intrinsic ZYX decomposition).</summary>
    public (double pitch, double yaw, double roll) ToEuler()
    {
        // pitch (X-axis rotation)
        double sinPCosR = 2.0 * (W * X + Y * Z);
        double cosPCosR = 1.0 - 2.0 * (X * X + Y * Y);
        double pitch = System.Math.Atan2(sinPCosR, cosPCosR) * MathUtils.RAD_TO_DEG;

        // yaw (Y-axis rotation)
        double sinY = 2.0 * (W * Y - Z * X);
        sinY = System.Math.Clamp(sinY, -1.0, 1.0);
        double yaw = System.Math.Asin(sinY) * MathUtils.RAD_TO_DEG;

        // roll (Z-axis rotation)
        double sinRCosP = 2.0 * (W * Z + X * Y);
        double cosRCosP = 1.0 - 2.0 * (Y * Y + Z * Z);
        double roll = System.Math.Atan2(sinRCosP, cosRCosP) * MathUtils.RAD_TO_DEG;

        return (pitch, yaw, roll);
    }

    /// <summary>Spherical linear interpolation between this and <paramref name="other"/>.</summary>
    public Quaterniond Slerp(Quaterniond other, double t)
    {
        Quaterniond a = Normalize();
        Quaterniond b = other.Normalize();

        double dot = a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        // Ensure shortest arc
        if (dot < 0.0)
        {
            b   = new Quaterniond(-b.W, -b.X, -b.Y, -b.Z);
            dot = -dot;
        }

        // If very close, fall back to linear interpolation
        if (dot > 0.9995)
        {
            return new Quaterniond(
                a.W + t * (b.W - a.W),
                a.X + t * (b.X - a.X),
                a.Y + t * (b.Y - a.Y),
                a.Z + t * (b.Z - a.Z)).Normalize();
        }

        double theta0  = System.Math.Acos(dot);
        double theta   = theta0 * t;
        double sinT0   = System.Math.Sin(theta0);
        double sinT    = System.Math.Sin(theta);
        double coeff0  = System.Math.Cos(theta) - dot * sinT / sinT0;
        double coeff1  = sinT / sinT0;

        return new Quaterniond(
            coeff0 * a.W + coeff1 * b.W,
            coeff0 * a.X + coeff1 * b.X,
            coeff0 * a.Y + coeff1 * b.Y,
            coeff0 * a.Z + coeff1 * b.Z).Normalize();
    }

    public bool Equals(Quaterniond other) => W == other.W && X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Quaterniond q && Equals(q);
    public override int GetHashCode() => HashCode.Combine(W, X, Y, Z);

    public static bool operator ==(Quaterniond a, Quaterniond b) => a.Equals(b);
    public static bool operator !=(Quaterniond a, Quaterniond b) => !a.Equals(b);

    public override string ToString() => $"({W:G6}, {X:G6}i, {Y:G6}j, {Z:G6}k)";
}
