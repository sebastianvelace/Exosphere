namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>A body flap placed in the vessel frame (skirt-datum origin), angles in radians.</summary>
public readonly record struct BodyFlapGeometry(
    Part Owner,
    Vector3d HingeLocal,
    int Side,
    double LengthM,
    double SpanM,
    double MinDeflectionRad,
    double MaxDeflectionRad,
    double NeutralDeflectionRad,
    double RateRadPerS)
{
    public double AreaM2 => LengthM * SpanM;

    /// <summary>Span/area aspect ratio of the plate (span²/area = span/length).</summary>
    public double AspectRatio => LengthM > 0.0 ? SpanM / LengthM : 0.0;

    public static BodyFlapGeometry From(Part owner, Vector3d partCentre, BodyFlapDef flap)
    {
        int side = flap.Side >= 0 ? 1 : -1;
        double radius = flap.HingeRadiusM > 0.0 ? flap.HingeRadiusM : 0.5 * owner.Definition.DiameterM;
        double min = flap.MinDeflectionDeg * MathUtils.DEG_TO_RAD;
        double max = System.Math.Max(min, flap.MaxDeflectionDeg * MathUtils.DEG_TO_RAD);
        return new BodyFlapGeometry(
            owner,
            new Vector3d(partCentre.X, partCentre.Y + flap.HingeYM, partCentre.Z + side * radius),
            side,
            System.Math.Max(0.0, flap.LengthM),
            System.Math.Max(0.0, flap.SpanM),
            min,
            max,
            System.Math.Clamp(flap.NeutralDeflectionDeg * MathUtils.DEG_TO_RAD, min, max),
            System.Math.Max(0.0, flap.RateDegPerS * MathUtils.DEG_TO_RAD));
    }

    /// <summary>Unit vector from the hinge to the tip at <paramref name="deflection"/>.</summary>
    public Vector3d SpanDirection(double deflection) =>
        new(System.Math.Cos(deflection), 0.0, Side * System.Math.Sin(deflection));

    /// <summary>Plate normal (perpendicular to the span direction and the hull axis).</summary>
    public Vector3d Normal(double deflection) =>
        new(Side * System.Math.Sin(deflection), 0.0, -System.Math.Cos(deflection));

    public Vector3d CentroidLocal(double deflection) =>
        HingeLocal + SpanDirection(deflection) * (0.5 * SpanM);
}

/// <summary>
/// Hinged flat-plate control surfaces (Starship's four body flaps). Each plate sees the local
/// flow v + ω×r at its centroid and carries a normal force
///
///   N = ½ρ|v|² · S · C_N(α, M),
///
/// against the side the flow strikes. C_N is the low-aspect-ratio plate law
/// (π/2)·AR·sinα·cosα + 1.2·sin²α below Mach 0.8 (attached lift plus Hoerner's broadside
/// plate) and modified Newtonian Cp,max(M)·sin²α above Mach 1.5 (Rayleigh-pitot stagnation
/// pressure), blended across the transonic band. A flap folded against the leeward hull lies
/// along a belly-first flow and carries no load; hull shadowing of a partly folded flap is
/// not modelled.
/// </summary>
public static class BodyFlapAerodynamics
{
    /// <summary>Broadside normal-force coefficient of a low-aspect-ratio plate (Hoerner).</summary>
    public const double SubsonicBroadsideCoefficient = 1.2;

    public const double SubsonicMachLimit = 0.8;
    public const double NewtonianMachOnset = 1.5;
    private const double HeatCapacityRatio = 1.4;

    public static AeroWrench Evaluate(
        IReadOnlyList<BodyFlapGeometry> flaps,
        IReadOnlyList<double> deflections,
        Quaterniond orientation,
        Vector3d angularVelocityWorld,
        Vector3d centerOfMassLocal,
        Vector3d airVelocityAtCenterOfMass,
        double density,
        double temperature,
        double soundSpeedConstant = AerodynamicsModel.EarthAirSoundSpeedConstant)
    {
        if (flaps.Count == 0 || density <= 0.0) return default;
        var inverse = orientation.Inverse();
        var airBody = inverse.Rotate(airVelocityAtCenterOfMass);
        var omegaBody = inverse.Rotate(angularVelocityWorld);
        var forceBody = Vector3d.Zero;
        var torqueBody = Vector3d.Zero;
        for (int i = 0; i < flaps.Count; i++)
        {
            var flap = flaps[i];
            if (flap.Owner.IsBroken || flap.AreaM2 <= 0.0) continue;
            double deflection = i < deflections.Count ? deflections[i] : flap.NeutralDeflectionRad;
            var arm = flap.CentroidLocal(deflection) - centerOfMassLocal;
            var force = PlateForce(flap, deflection, airBody + omegaBody.Cross(arm), density, temperature,
                soundSpeedConstant);
            forceBody += force;
            torqueBody += arm.Cross(force);
        }
        return new AeroWrench(orientation.Rotate(forceBody), torqueBody);
    }

    /// <summary>Force (body frame) on one plate moving at <paramref name="localVelocityBody"/> through still air.</summary>
    public static Vector3d PlateForce(BodyFlapGeometry flap, double deflection, Vector3d localVelocityBody,
        double density, double temperature, double soundSpeedConstant = AerodynamicsModel.EarthAirSoundSpeedConstant)
    {
        double speedSquared = localVelocityBody.MagnitudeSquared;
        if (speedSquared < 1e-12) return Vector3d.Zero;
        double speed = System.Math.Sqrt(speedSquared);
        var normal = flap.Normal(deflection);
        double normalSpeed = localVelocityBody.Dot(normal);
        double sine = System.Math.Min(1.0, System.Math.Abs(normalSpeed) / speed);
        if (sine < 1e-12) return Vector3d.Zero;
        double cosine = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - sine * sine));
        double mach = AerodynamicsModel.ComputeMach(speed, temperature, soundSpeedConstant);
        double coefficient = NormalForceCoefficient(sine, cosine, flap.AspectRatio, mach);
        // The plate moving into the air along +n is pushed back along −n.
        return normal * (-System.Math.Sign(normalSpeed) * 0.5 * density * speedSquared * flap.AreaM2 * coefficient);
    }

    public static double NormalForceCoefficient(double sine, double cosine, double aspectRatio, double mach)
    {
        double subsonic = 0.5 * System.Math.PI * aspectRatio * sine * cosine
            + SubsonicBroadsideCoefficient * sine * sine;
        if (mach <= SubsonicMachLimit) return subsonic;
        double newtonian = StagnationPressureCoefficient(System.Math.Max(mach, NewtonianMachOnset)) * sine * sine;
        if (mach >= NewtonianMachOnset) return newtonian;
        double x = (mach - SubsonicMachLimit) / (NewtonianMachOnset - SubsonicMachLimit);
        double blend = x * x * (3.0 - 2.0 * x);
        return subsonic + (newtonian - subsonic) * blend;
    }

    /// <summary>
    /// Stagnation pressure coefficient behind a normal shock (Rayleigh pitot), the
    /// modified-Newtonian Cp,max; it tends to 1.839 for γ = 1.4 as M → ∞.
    /// </summary>
    public static double StagnationPressureCoefficient(double mach)
    {
        const double g = HeatCapacityRatio;
        double m2 = mach * mach;
        double ratio = System.Math.Pow((g + 1.0) * (g + 1.0) * m2 / (4.0 * g * m2 - 2.0 * (g - 1.0)), g / (g - 1.0))
            * (1.0 - g + 2.0 * g * m2) / (g + 1.0);
        return 2.0 / (g * m2) * (ratio - 1.0);
    }

    /// <summary>Solves the symmetric 3×3 system [[a,b,c],[b,d,e],[c,e,f]]·x = rhs; zero when singular.</summary>
    public static Vector3d SolveSymmetric(double a, double b, double c, double d, double e, double f, Vector3d rhs)
    {
        double det = a * (d * f - e * e) - b * (b * f - e * c) + c * (b * e - d * c);
        if (System.Math.Abs(det) < 1e-300) return Vector3d.Zero;
        double rx = rhs.X, ry = rhs.Y, rz = rhs.Z;
        double x = (rx * (d * f - e * e) - b * (ry * f - e * rz) + c * (ry * e - d * rz)) / det;
        double y = (a * (ry * f - e * rz) - rx * (b * f - e * c) + c * (b * rz - ry * c)) / det;
        double z = (a * (d * rz - ry * e) - b * (b * rz - ry * c) + rx * (b * e - d * c)) / det;
        return new Vector3d(x, y, z);
    }
}
