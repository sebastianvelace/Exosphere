namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>A continuous mass profile has explicit knots at every change of rate.</summary>
public readonly record struct MassKnot(double TimeSeconds, double MassKg);

public enum MassBoundarySide { Left, Right }

/// <summary>
/// An immutable component at a fixed body-frame station with a fixed central inertia per kg
/// (m²). Piecewise-affine mass is an explicit caller-supplied schedule, not inferred from
/// whole-step debits. This does not model a changing free surface, slosh or internal piping.
/// </summary>
public sealed class FixedShapeMassComponent
{
    public string Id { get; }
    public Vector3d PositionBodyM { get; }
    public Matrix3x3d SpecificInertiaBodyM2 { get; }
    public IReadOnlyList<MassKnot> Knots { get; }
    private readonly MassKnot[] _knots;
    private readonly double[] _rates;

    public FixedShapeMassComponent(string id, Vector3d positionBodyM,
        Matrix3x3d specificInertiaBodyM2, IEnumerable<MassKnot> knots)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A component needs an identity.", nameof(id));
        DynamicsInputs.RequireFinite(positionBodyM, nameof(positionBodyM));
        RequirePhysicalInertia(specificInertiaBodyM2);
        ArgumentNullException.ThrowIfNull(knots);
        _knots = knots.ToArray();
        if (_knots.Length == 0 || _knots[0].TimeSeconds != 0)
            throw new ArgumentException("The first mass knot must be at step-local time zero.", nameof(knots));
        _rates = new double[_knots.Length - 1];
        for (int i = 0; i < _knots.Length; i++)
        {
            var knot = _knots[i];
            if (!double.IsFinite(knot.TimeSeconds) || !double.IsFinite(knot.MassKg)
                || knot.TimeSeconds < 0 || knot.MassKg < 0)
                throw new ArgumentException("Mass knots must be finite and nonnegative.", nameof(knots));
            if (i == 0) continue;
            if (knot.TimeSeconds <= _knots[i - 1].TimeSeconds)
                throw new ArgumentException("Mass knot times must strictly increase.", nameof(knots));
            double rate = (knot.MassKg - _knots[i - 1].MassKg)
                / (knot.TimeSeconds - _knots[i - 1].TimeSeconds);
            if (!double.IsFinite(rate) || (rate == 0 && knot.MassKg != _knots[i - 1].MassKg))
                throw new ArgumentException("Mass rates must be finite and representable.", nameof(knots));
            _rates[i - 1] = rate;
        }
        Id = id;
        PositionBodyM = positionBodyM;
        SpecificInertiaBodyM2 = specificInertiaBodyM2;
        Knots = Array.AsReadOnly(_knots);
    }

    // Exact knot masses avoid negative residue at exhaustion. Beyond the final knot the
    // component is constant for the rest of its distribution's declared time window.
    internal (double Mass, double Rate) SampleMass(double time, MassBoundarySide side)
    {
        int low = 0, high = _knots.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            double knotTime = _knots[middle].TimeSeconds;
            if (time < knotTime) high = middle - 1;
            else if (time > knotTime) low = middle + 1;
            else
            {
                int rateIndex = side == MassBoundarySide.Left ? middle - 1 : middle;
                return (_knots[middle].MassKg,
                    rateIndex >= 0 && rateIndex < _rates.Length ? _rates[rateIndex] : 0);
            }
        }
        if (low == _knots.Length) return (_knots[^1].MassKg, 0);
        var before = _knots[low - 1];
        var after = _knots[low];
        double rate = _rates[low - 1];
        // Anchor at the smaller mass: depletion near zero must not subtract two bulk
        // loads. Using the stored rate also avoids underflow of a tiny time fraction.
        double mass = before.MassKg <= after.MassKg
            ? before.MassKg + rate * (time - before.TimeSeconds)
            : after.MassKg - rate * (after.TimeSeconds - time);
        return (mass, rate);
    }

    private static void RequirePhysicalInertia(Matrix3x3d inertia)
    {
        DynamicsInputs.RequireFinite(inertia, nameof(inertia));
        if (inertia.M11 < 0 || inertia.M22 < 0 || inertia.M33 < 0)
            throw new ArgumentException("Central moments of inertia cannot be negative.", nameof(inertia));
        if (inertia.M12 != inertia.M21 || inertia.M13 != inertia.M31 || inertia.M23 != inertia.M32)
            throw new ArgumentException("Specific central inertia must be symmetric.", nameof(inertia));
        double scale = new[] { System.Math.Abs(inertia.M11), System.Math.Abs(inertia.M12),
            System.Math.Abs(inertia.M13), System.Math.Abs(inertia.M22), System.Math.Abs(inertia.M23),
            System.Math.Abs(inertia.M33) }.Max();
        if (scale == 0) return; // A point mass has zero specific central inertia.
        double a = inertia.M11 / scale, b = inertia.M22 / scale, c = inertia.M33 / scale;
        double halfTrace = (a + b + c) * 0.5;
        // The central second-moment matrix Q = tr(I)/2 * identity - I must be PSD.
        // This also enforces the principal-moment triangle inequalities. Scaling avoids
        // overflow; the allowance is solely roundoff in these dimensionless minors.
        double x = halfTrace - a, y = halfTrace - b, z = halfTrace - c;
        double xy = -inertia.M12 / scale, xz = -inertia.M13 / scale, yz = -inertia.M23 / scale;
        double roundoff = 32 * (double.BitIncrement(1.0) - 1.0);
        double determinant = x * (y * z - yz * yz) - xy * (xy * z - xz * yz)
            + xz * (xy * yz - xz * y);
        if (x < -roundoff || y < -roundoff || z < -roundoff
            || x * y - xy * xy < -roundoff || x * z - xz * xz < -roundoff
            || y * z - yz * yz < -roundoff || determinant < -roundoff)
            throw new ArgumentException("Specific central inertia must describe a nonnegative mass distribution.", nameof(inertia));
    }
}
