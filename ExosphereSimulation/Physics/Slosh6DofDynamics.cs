namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>One modal mass, with two lateral coordinates, taken from the locked liquid mass.</summary>
public readonly record struct Slosh6DofMode(double MassKg, Vector3d HingeBodyM, double LengthM);

/// <summary>Cartesian displacement and relative velocity on a pendulum's lower hemisphere.</summary>
public readonly record struct SloshModalState(double X, double Z, double XRate, double ZRate);
public readonly record struct SloshModalAcceleration(double X, double Z);
public readonly record struct SloshModalForce(double XN, double ZN);

/// <summary>One spherical mode with two physical degrees of freedom, without a pole singularity.</summary>
public readonly record struct SloshSphericalState(Vector3d DirectionBody, Vector3d DirectionRateBodyPerS);
public sealed record SloshSphericalAcceleration(Vector3d DatumAccelerationWorld,
    Vector3d AngularAccelerationBody, IReadOnlyList<Vector3d> DirectionAccelerationBodyPerS2);

/// <summary>External loads; applied torque is about the datum, gravity is a uniform field.</summary>
public readonly record struct Slosh6DofLoads(Vector3d AppliedForceWorld, Vector3d AppliedTorqueDatumBody,
    Vector3d GravityWorld, IReadOnlyList<SloshModalForce>? GeneralizedModalForces = null);

public sealed record Slosh6DofAcceleration(Vector3d DatumAccelerationWorld,
    Vector3d AngularAccelerationBody, IReadOnlyList<SloshModalAcceleration> ModalAccelerations);

public readonly record struct Slosh6DofInvariants(Vector3d LinearMomentumWorld,
    Vector3d AngularMomentumWorld, double KineticEnergyJ);

/// <summary>
/// Constant-fill Newton/Euler solve for a rigid carrier and spherical pendulum modes.
/// The locked mass, centroid and complete tensor already include all the liquid. Only
/// modal displacement corrections are added; a bob's mass is never added a second time.
/// This kernel has no resource funding, variable-fill flux or production activation policy.
/// </summary>
public sealed class Slosh6DofDynamics
{
    /// <summary>Roundoff budget per dimension for the diagonally scaled SPD solve.</summary>
    public const double NormalizedPivotBudgetPerDimension = 1.4210854715202004e-14;

    private readonly RigidBodyMassProperties _locked;
    private readonly Slosh6DofMode[] _modes;
    public int ModeCount => _modes.Length;

    public Slosh6DofDynamics(RigidBodyMassProperties locked, IEnumerable<Slosh6DofMode> modes)
    {
        _locked = locked;
        _modes = modes.ToArray();
        DynamicsInputs.RequireFinite(locked.CenterOfMassBody, nameof(locked));
        double mobileMass = 0;
        foreach (var mode in _modes)
        {
            if (!(mode.MassKg > 0) || !double.IsFinite(mode.MassKg)
                || !(mode.LengthM > 0) || !double.IsFinite(mode.LengthM))
                throw new ArgumentException("Modal mass and length must be finite and positive.", nameof(modes));
            DynamicsInputs.RequireFinite(mode.HingeBodyM, nameof(modes));
            mobileMass += mode.MassKg;
        }
        if (!(locked.Mass > mobileMass))
            throw new ArgumentException("Modal mass must leave a positive carrier mass.", nameof(modes));
        // Positive locked inertia alone does not establish a positive coupled kinetic energy.
        Evaluate(Quaterniond.Identity, Vector3d.Zero, new SloshModalState[_modes.Length], default);
    }

    public RigidBodyMassProperties MassProperties(IReadOnlyList<SloshModalState> states) => Sample(states).Mass;

    /// <summary>Angular momentum is about the fixed inertial origin supplied by the caller.</summary>
    public Slosh6DofInvariants Invariants(Vector3d datumPositionWorld, Vector3d datumVelocityWorld,
        Quaterniond orientation, Vector3d angularVelocityBody, IReadOnlyList<SloshModalState> states,
        Vector3d inertialOrigin)
    {
        return InvariantsSample(datumPositionWorld, datumVelocityWorld, orientation, angularVelocityBody,
            Sample(states), inertialOrigin);
    }

    private static Slosh6DofInvariants InvariantsSample(Vector3d datumPositionWorld, Vector3d datumVelocityWorld,
        Quaterniond orientation, Vector3d angularVelocityBody, SampleData sample, Vector3d inertialOrigin)
    {
        var q = Unit(orientation);
        DynamicsInputs.RequireFinite(datumPositionWorld, nameof(datumPositionWorld));
        DynamicsInputs.RequireFinite(datumVelocityWorld, nameof(datumVelocityWorld));
        DynamicsInputs.RequireFinite(angularVelocityBody, nameof(angularVelocityBody));
        DynamicsInputs.RequireFinite(inertialOrigin, nameof(inertialOrigin));
        var v = q.Inverse().Rotate(datumVelocityWorld);
        var w = angularVelocityBody;
        var linearBody = v * sample.Mass.Mass + w.Cross(sample.FirstMoment) + sample.RelativeLinear;
        var angularBody = sample.FirstMoment.Cross(v) + sample.InertiaDatum.Multiply(w) + sample.RelativeAngular;
        var p = q.Rotate(linearBody);
        double energy = 0.5 * sample.Mass.Mass * v.MagnitudeSquared
            + v.Dot(w.Cross(sample.FirstMoment) + sample.RelativeLinear)
            + 0.5 * w.Dot(sample.InertiaDatum.Multiply(w)) + w.Dot(sample.RelativeAngular)
            + sample.RelativeKineticEnergy;
        if (!double.IsFinite(energy)) throw new ArgumentException("Kinetic energy is not representable.");
        return new(p, (datumPositionWorld - inertialOrigin).Cross(p) + q.Rotate(angularBody), energy);
    }

    /// <summary>
    /// Solves absolute datum acceleration (not the derivative of body-frame datum velocity),
    /// body angular acceleration and every modal acceleration simultaneously. Internal
    /// generalized loads act only on modal coordinates; the solve supplies their reactions.
    /// </summary>
    public Slosh6DofAcceleration Evaluate(Quaterniond orientation, Vector3d angularVelocityBody,
        IReadOnlyList<SloshModalState> states, Slosh6DofLoads loads)
    {
        return EvaluateSample(orientation, angularVelocityBody, Sample(states), loads);
    }

    private Slosh6DofAcceleration EvaluateSample(Quaterniond orientation, Vector3d angularVelocityBody,
        SampleData sample, Slosh6DofLoads loads)
    {
        var q = Unit(orientation);
        DynamicsInputs.RequireFinite(angularVelocityBody, nameof(angularVelocityBody));
        DynamicsInputs.RequireFinite(loads.AppliedForceWorld, nameof(loads));
        DynamicsInputs.RequireFinite(loads.AppliedTorqueDatumBody, nameof(loads));
        DynamicsInputs.RequireFinite(loads.GravityWorld, nameof(loads));
        if (loads.GeneralizedModalForces is { } forces && forces.Count != ModeCount)
            throw new ArgumentException("One two-coordinate generalized force is required per mode.", nameof(loads));
        var w = angularVelocityBody;
        var g = q.Inverse().Rotate(loads.GravityWorld);
        var f = q.Inverse().Rotate(loads.AppliedForceWorld) + g * sample.Mass.Mass;
        var torque = loads.AppliedTorqueDatumBody + sample.FirstMoment.Cross(g);
        var linearBias = w.Cross(w.Cross(sample.FirstMoment)) + w.Cross(sample.RelativeLinear) * 2;
        var angularBias = w.Cross(sample.InertiaDatum.Multiply(w));
        var modes = new SloshKineticMode[ModeCount];
        var right = new SloshModalAcceleration[ModeCount];
        for (int i = 0; i < ModeCount; i++)
        {
            var k = sample.Kinematics[i];
            double m = _modes[i].MassKg;
            linearBias += k.Bias * m;
            angularBias += k.Position.Cross(w.Cross(k.Velocity) * 2 + k.Bias) * m;
            var relativeBias = w.Cross(w.Cross(k.Position)) + w.Cross(k.Velocity) * 2 + k.Bias;
            var generalized = loads.GeneralizedModalForces?[i] ?? default;
            right[i] = new(k.EX.Dot(g - relativeBias) + generalized.XN / m,
                k.EZ.Dot(g - relativeBias) + generalized.ZN / m);
            modes[i] = new(m, k.Position, k.EX, k.EZ);
        }
        return SloshKineticSystem.Solve(sample.Mass, modes, right, f - linearBias, torque - angularBias, q);
    }

    public RigidBodyMassProperties SphericalMassProperties(IReadOnlyList<SloshSphericalState> states) =>
        SampleSpherical(states).Mass;

    public Slosh6DofInvariants SphericalInvariants(Vector3d datumPositionWorld, Vector3d datumVelocityWorld,
        Quaterniond orientation, Vector3d angularVelocityBody, IReadOnlyList<SloshSphericalState> states,
        Vector3d inertialOrigin) => InvariantsSample(datumPositionWorld, datumVelocityWorld, orientation,
            angularVelocityBody, SampleSpherical(states), inertialOrigin);

    /// <summary>
    /// Globally parameterized spherical modes. Internal forces are physical body-frame
    /// forces on the bob; their equal-and-opposite carrier reactions come from the solve.
    /// Unit-direction/rate retraction is used at candidate stages, with no restoring force
    /// invented in free fall. The mathematical continuation is not a low-g liquid model.
    /// </summary>
    public SloshSphericalAcceleration EvaluateSpherical(Quaterniond orientation, Vector3d angularVelocityBody,
        IReadOnlyList<SloshSphericalState> states, Slosh6DofLoads loads,
        IReadOnlyList<Vector3d>? internalForcesBody = null)
    {
        if (loads.GeneralizedModalForces != null)
            throw new ArgumentException("Spherical modes require physical tangent forces, not chart-coordinate loads.", nameof(loads));
        if (internalForcesBody != null && internalForcesBody.Count != ModeCount)
            throw new ArgumentException("One internal force is required per mode.", nameof(internalForcesBody));
        var sample = SampleSpherical(states);
        var forces = new SloshModalForce[ModeCount];
        for (int i = 0; i < ModeCount; i++)
        {
            var force = internalForcesBody?[i] ?? Vector3d.Zero;
            DynamicsInputs.RequireFinite(force, nameof(internalForcesBody));
            var k = sample.Kinematics[i];
            forces[i] = new(k.EX.Dot(force), k.EZ.Dot(force));
        }
        var solved = EvaluateSample(orientation, angularVelocityBody, sample,
            loads with { GeneralizedModalForces = forces });
        var accelerations = new Vector3d[ModeCount];
        for (int i = 0; i < ModeCount; i++)
        {
            var k = sample.Kinematics[i]; var a = solved.ModalAccelerations[i];
            accelerations[i] = (k.EX * a.X + k.EZ * a.Z + k.Bias) / _modes[i].LengthM;
        }
        return new(solved.DatumAccelerationWorld, solved.AngularAccelerationBody, accelerations);
    }

    private SampleData SampleSpherical(IReadOnlyList<SloshSphericalState> states)
    {
        if (states.Count != ModeCount) throw new ArgumentException("Modal state count does not match the model.", nameof(states));
        var kinematics = new Kinematics[ModeCount];
        for (int i = 0; i < ModeCount; i++)
        {
            var mode = _modes[i]; var state = states[i];
            DynamicsInputs.RequireFinite(state.DirectionBody, nameof(states));
            DynamicsInputs.RequireFinite(state.DirectionRateBodyPerS, nameof(states));
            double norm = state.DirectionBody.Magnitude;
            if (!(norm > 0) || !double.IsFinite(norm)) throw new ArgumentException("Modal direction must be finite and nonzero.", nameof(states));
            var n = state.DirectionBody / norm;
            var rate = state.DirectionRateBodyPerS - n * n.Dot(state.DirectionRateBodyPerS);
            var velocity = rate * mode.LengthM;
            // Use a local orthonormal tangent basis only for the solve. The returned
            // vector acceleration is invariant to basis rotation or switching.
            var seed = System.Math.Abs(n.X) <= System.Math.Abs(n.Y) && System.Math.Abs(n.X) <= System.Math.Abs(n.Z)
                ? new Vector3d(1, 0, 0) : System.Math.Abs(n.Y) <= System.Math.Abs(n.Z)
                    ? Vector3d.Up : new Vector3d(0, 0, 1);
            var ex = n.Cross(seed).Normalized; var ez = n.Cross(ex);
            double rise = n.Y < 0 ? (n.X * n.X + n.Z * n.Z) / (1 - n.Y) : 1 + n.Y;
            var displacement = new Vector3d(n.X, rise, n.Z) * mode.LengthM;
            var position = mode.HingeBodyM + n * mode.LengthM;
            var bias = -n * (velocity.MagnitudeSquared / mode.LengthM);
            DynamicsInputs.RequireFinite(velocity, nameof(states)); DynamicsInputs.RequireFinite(bias, nameof(states));
            kinematics[i] = new(position, ex, ez, velocity, bias, displacement);
        }
        return AssembleSample(kinematics);
    }

    private SampleData Sample(IReadOnlyList<SloshModalState> states)
    {
        if (states.Count != ModeCount) throw new ArgumentException("Modal state count does not match the model.", nameof(states));
        var kinematics = new Kinematics[ModeCount];
        for (int i = 0; i < ModeCount; i++)
        {
            var mode = _modes[i]; var state = states[i];
            DynamicsInputs.RequireFinite(new Vector3d(state.X, state.Z, state.XRate), nameof(states));
            if (!double.IsFinite(state.ZRate)) throw new ArgumentException("Modal velocity must be finite.", nameof(states));
            double x = state.X / mode.LengthM, z = state.Z / mode.LengthM;
            double radiusSquared = x * x + z * z;
            if (!(radiusSquared < 1)) throw new ArgumentException("Pendulum state leaves its lower-hemisphere chart.", nameof(states));
            double root = System.Math.Sqrt(1 - radiusSquared), s = mode.LengthM * root;
            var displacement = new Vector3d(state.X, mode.LengthM * radiusSquared / (1 + root), state.Z);
            var equilibrium = mode.HingeBodyM - Vector3d.Up * mode.LengthM;
            var r = equilibrium + displacement;
            var ex = new Vector3d(1, state.X / s, 0); var ez = new Vector3d(0, state.Z / s, 1);
            var velocity = ex * state.XRate + ez * state.ZRate;
            double verticalRate = velocity.Y;
            var bias = Vector3d.Up * ((state.XRate * state.XRate + state.ZRate * state.ZRate
                + verticalRate * verticalRate) / s);
            DynamicsInputs.RequireFinite(r, nameof(states)); DynamicsInputs.RequireFinite(velocity, nameof(states));
            DynamicsInputs.RequireFinite(bias, nameof(states));
            kinematics[i] = new(r, ex, ez, velocity, bias, displacement);
        }
        return AssembleSample(kinematics);
    }

    private SampleData AssembleSample(Kinematics[] kinematics)
    {
        var firstDelta = Vector3d.Zero;
        var central = _locked.InertiaBody;
        var p = Vector3d.Zero; var h = Vector3d.Zero;
        double relativeEnergy = 0;
        for (int i = 0; i < ModeCount; i++)
        {
            var mode = _modes[i]; var k = kinematics[i];
            var displacement = k.Displacement; var r = k.Position; var velocity = k.Velocity;
            var equilibrium = mode.HingeBodyM - Vector3d.Up * mode.LengthM;
            firstDelta += displacement * mode.MassKg;
            central = Add(central, DeltaParallelAxis(equilibrium - _locked.CenterOfMassBody, displacement, mode.MassKg));
            p += velocity * mode.MassKg;
            h += r.Cross(velocity) * mode.MassKg;
            relativeEnergy += 0.5 * mode.MassKg * velocity.MagnitudeSquared;
        }
        var dc = firstDelta / _locked.Mass;
        central = Add(central, ParallelAxis(dc, -_locked.Mass));
        var mass = new RigidBodyMassProperties(_locked.Mass, _locked.CenterOfMassBody + dc, central);
        return new(mass, mass.CenterOfMassBody * mass.Mass,
            Add(central, ParallelAxis(mass.CenterOfMassBody, mass.Mass)), p, h, relativeEnergy, kinematics);
    }

    private static Quaterniond Unit(Quaterniond q)
    {
        if (!(q.NormSquared > 0) || !double.IsFinite(q.NormSquared))
            throw new ArgumentException("Orientation must be finite and nonzero.");
        return q.Normalize();
    }
    private static double Component(Vector3d v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
    internal static Matrix3x3d Add(Matrix3x3d a, Matrix3x3d b) => new(
        a.M11 + b.M11, a.M12 + b.M12, a.M13 + b.M13,
        a.M21 + b.M21, a.M22 + b.M22, a.M23 + b.M23,
        a.M31 + b.M31, a.M32 + b.M32, a.M33 + b.M33);
    internal static Matrix3x3d ParallelAxis(Vector3d r, double m) => new(
        m * (r.Y * r.Y + r.Z * r.Z), -m * r.X * r.Y, -m * r.X * r.Z,
        -m * r.X * r.Y, m * (r.X * r.X + r.Z * r.Z), -m * r.Y * r.Z,
        -m * r.X * r.Z, -m * r.Y * r.Z, m * (r.X * r.X + r.Y * r.Y));
    internal static Matrix3x3d DeltaParallelAxis(Vector3d r, Vector3d d, double m)
    {
        double xx = m * (2 * (r.Y * d.Y + r.Z * d.Z) + d.Y * d.Y + d.Z * d.Z);
        double yy = m * (2 * (r.X * d.X + r.Z * d.Z) + d.X * d.X + d.Z * d.Z);
        double zz = m * (2 * (r.X * d.X + r.Y * d.Y) + d.X * d.X + d.Y * d.Y);
        double xy = -m * (r.X * d.Y + d.X * r.Y + d.X * d.Y);
        double xz = -m * (r.X * d.Z + d.X * r.Z + d.X * d.Z);
        double yz = -m * (r.Y * d.Z + d.Y * r.Z + d.Y * d.Z);
        return new(xx, xy, xz, xy, yy, yz, xz, yz, zz);
    }
    private readonly record struct Kinematics(Vector3d Position, Vector3d EX, Vector3d EZ,
        Vector3d Velocity, Vector3d Bias, Vector3d Displacement);
    private sealed record SampleData(RigidBodyMassProperties Mass, Vector3d FirstMoment,
        Matrix3x3d InertiaDatum, Vector3d RelativeLinear, Vector3d RelativeAngular,
        double RelativeKineticEnergy, Kinematics[] Kinematics);
}
