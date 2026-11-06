namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// One-sided body-frame mass properties and their smooth-interval derivatives. Acceleration
/// excludes distributional impulses at changes of flow rate. Integration must split there.
/// Mass rates are signed (negative for loss), unlike positive outward exit fluxes.
/// </summary>
public readonly record struct BodyMassSample(
    double MassKg, double MassRateKgPerS,
    Vector3d CenterOfMassBodyM, Vector3d CenterOfMassRateBodyMps,
    Vector3d CenterOfMassAccelerationBodyMps2,
    Matrix3x3d InertiaBodyKgM2, Matrix3x3d InertiaRateBodyKgM2PerS)
{
    /// <summary>Requests positive-definite, invertible inertia for rigid-body rotation.</summary>
    public RigidBodyMassProperties ToRigidBodyMassProperties()
    {
        var inertia = InertiaBodyKgM2;
        DynamicsInputs.RequireFinite(CenterOfMassBodyM, nameof(CenterOfMassBodyM));
        DynamicsInputs.RequireFinite(inertia, nameof(InertiaBodyKgM2));
        double scale = System.Math.Max(inertia.M11, System.Math.Max(inertia.M22, inertia.M33));
        if (scale <= 0 || inertia.M12 != inertia.M21 || inertia.M13 != inertia.M31 || inertia.M23 != inertia.M32)
            throw new InvalidOperationException("Rotational inertia must be symmetric and positive definite.");
        var normalized = new Matrix3x3d(
            inertia.M11 / scale, inertia.M12 / scale, inertia.M13 / scale,
            inertia.M21 / scale, inertia.M22 / scale, inertia.M23 / scale,
            inertia.M31 / scale, inertia.M32 / scale, inertia.M33 / scale);
        if (normalized.M11 <= 0 || normalized.M11 * normalized.M22 - normalized.M12 * normalized.M12 <= 0
            || normalized.Determinant <= 0)
            throw new InvalidOperationException("Rotational inertia must be positive definite.");
        return new(MassKg, CenterOfMassBodyM, inertia);
    }
}

/// <summary>
/// Pure, allocation-free candidate sampling of immutable fixed-shape components. Mass,
/// centre, central inertia and derivatives all come from the same component masses.
/// This supplies no exhaust flux or relative-flow momentum and activates no mission model.
/// </summary>
public sealed class BodyMassDistribution
{
    public double DurationSeconds { get; }
    public IReadOnlyList<FixedShapeMassComponent> Components { get; }
    public IReadOnlyList<double> BoundariesSeconds { get; }
    private readonly FixedShapeMassComponent[] _components;

    public BodyMassDistribution(double durationSeconds, IEnumerable<FixedShapeMassComponent> components)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        ArgumentNullException.ThrowIfNull(components);
        _components = components.ToArray();
        if (_components.Length == 0)
            throw new ArgumentException("A distribution needs components.", nameof(components));
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var boundaries = new SortedSet<double> { 0, durationSeconds };
        foreach (var component in _components)
        {
            if (component == null || !identities.Add(component.Id))
                throw new ArgumentException("Component identities must be present and unique.", nameof(components));
            foreach (var knot in component.Knots)
            {
                if (knot.TimeSeconds > durationSeconds)
                    throw new ArgumentException("A mass knot lies outside the declared window.", nameof(components));
                boundaries.Add(knot.TimeSeconds);
            }
        }
        // Total mass is affine between the union of knots: checking all endpoints is
        // sufficient to rule out an empty or unrepresentable intermediate body.
        foreach (double time in boundaries)
        {
            double mass = 0;
            foreach (var component in _components) mass += component.SampleMass(time, MassBoundarySide.Right).Mass;
            if (!double.IsFinite(mass) || mass <= 0)
                throw new ArgumentException("Total mass must remain finite and positive.", nameof(components));
        }
        DurationSeconds = durationSeconds;
        Components = Array.AsReadOnly(_components);
        BoundariesSeconds = Array.AsReadOnly(boundaries.ToArray());
    }

    /// <summary>Right derivative internally; left derivative at the end of the full window.</summary>
    public BodyMassSample Sample(double timeSeconds) => Sample(timeSeconds,
        timeSeconds == DurationSeconds ? MassBoundarySide.Left : MassBoundarySide.Right);

    /// <summary>
    /// The caller selects the interval's side at an internal cut (left for its ending RK4
    /// stage, right for the next start). Derivatives outside the declared window are unknown.
    /// </summary>
    public BodyMassSample Sample(double timeSeconds, MassBoundarySide side)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds > DurationSeconds)
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        if (side is not (MassBoundarySide.Left or MassBoundarySide.Right))
            throw new ArgumentOutOfRangeException(nameof(side));
        if ((timeSeconds == 0 && side == MassBoundarySide.Left)
            || (timeSeconds == DurationSeconds && side == MassBoundarySide.Right))
            throw new ArgumentException("The requested derivative is outside the declared time window.", nameof(side));

        double mass = 0, massRate = 0;
        Vector3d anchor = default;
        bool anchored = false;
        foreach (var component in _components)
        {
            var sample = component.SampleMass(timeSeconds, side);
            mass += sample.Mass;
            massRate += sample.Rate;
            if (!anchored && sample.Mass > 0) { anchor = component.PositionBodyM; anchored = true; }
        }
        if (!double.IsFinite(mass) || mass <= 0 || !double.IsFinite(massRate))
            throw new InvalidOperationException("Candidate mass and mass rate must be representable.");

        // Work relative to one populated station and accumulate central tensors directly.
        // Subtracting m*P(c) from a large datum tensor loses small inertias far from origin.
        var centerRelative = Vector3d.Zero;
        foreach (var component in _components)
        {
            double componentMass = component.SampleMass(timeSeconds, side).Mass;
            if (componentMass > 0)
                centerRelative += (component.PositionBodyM - anchor) * (componentMass / mass);
        }
        var center = anchor + centerRelative;
        var centerRate = Vector3d.Zero;
        var inertia = new CentralTensorSum();
        var inertiaRate = new CentralTensorSum();
        foreach (var component in _components)
        {
            var sample = component.SampleMass(timeSeconds, side);
            if (sample.Mass == 0 && sample.Rate == 0) continue;
            var distance = (component.PositionBodyM - anchor) - centerRelative;
            if (sample.Rate != 0 && distance != Vector3d.Zero)
                centerRate += distance * (sample.Rate / mass);
            inertia.Add(component.SpecificInertiaBodyM2, distance, sample.Mass);
            // The terms from differentiating distance cancel: sum(m_i*(r_i-c)) = 0.
            inertiaRate.Add(component.SpecificInertiaBodyM2, distance, sample.Rate);
        }
        var centerAcceleration = centerRate == Vector3d.Zero ? Vector3d.Zero
            : centerRate * (-2 * (massRate / mass));
        var central = inertia.Matrix;
        var centralRate = inertiaRate.Matrix;
        DynamicsInputs.RequireFinite(center, nameof(center));
        DynamicsInputs.RequireFinite(centerRate, nameof(centerRate));
        DynamicsInputs.RequireFinite(centerAcceleration, nameof(centerAcceleration));
        DynamicsInputs.RequireFinite(central, nameof(central));
        DynamicsInputs.RequireFinite(centralRate, nameof(centralRate));
        return new(mass, massRate, center, centerRate, centerAcceleration, central, centralRate);
    }

    private struct CentralTensorSum
    {
        private double _xx, _xy, _xz, _yy, _yz, _zz;
        internal void Add(Matrix3x3d specific, Vector3d d, double weight)
        {
            if (weight == 0) return;
            _xx += weight * (specific.M11 + d.Y * d.Y + d.Z * d.Z);
            _yy += weight * (specific.M22 + d.X * d.X + d.Z * d.Z);
            _zz += weight * (specific.M33 + d.X * d.X + d.Y * d.Y);
            _xy += weight * (specific.M12 - d.X * d.Y);
            _xz += weight * (specific.M13 - d.X * d.Z);
            _yz += weight * (specific.M23 - d.Y * d.Z);
        }
        internal readonly Matrix3x3d Matrix => new(_xx, _xy, _xz, _xy, _yy, _yz, _xz, _yz, _zz);
    }
}
