namespace Exosphere.Simulation.Integrators;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>Adaptive attitude-only RK4 for a constant-mass coast on a prescribed orbit.</summary>
public static class CoastingAttitudeIntegrator
{
    // Per-second local truncation budgets; numerical accuracy controls, not physical damping.
    public const double ScaledOrientationErrorRatePerS = 1e-11;
    public const double AngularRateErrorRatePerS = 1e-11;
    public const double MaximumTrialRotationRad = 0.25;
    private const double StepDoublingDivisor = 15.0;
    private const double GrowthThreshold = 32.0;

    public static (Quaterniond Orientation, Vector3d AngularVelocityBody) Advance(
        Quaterniond orientation, Vector3d angularVelocityBody,
        RigidBodyMassProperties mass, double time, double dt,
        Func<Quaterniond, double, Vector3d> torqueBody)
    {
        ArgumentNullException.ThrowIfNull(torqueBody);
        if (!double.IsFinite(time) || !double.IsFinite(dt) || dt <= 0 || !double.IsFinite(time + dt))
            throw new ArgumentOutOfRangeException(nameof(dt));
        DynamicsInputs.RequireFinite(angularVelocityBody, nameof(angularVelocityBody));
        if (!double.IsFinite(orientation.NormSquared) || orientation.NormSquared <= 0)
            throw new ArgumentException("Orientation must be finite and nonzero.", nameof(orientation));
        if (!double.IsFinite(mass.Mass) || mass.Mass <= 0)
            throw new ArgumentException("Coasting mass must be finite and positive.", nameof(mass));
        DynamicsInputs.RequireFinite(mass.InertiaBody, nameof(mass));
        var state = new RigidBody6DofState(Vector3d.Zero, Vector3d.Zero, orientation, angularVelocityBody);
        double elapsed = 0;
        double rateMagnitude = angularVelocityBody.Magnitude;
        if (!double.IsFinite(rateMagnitude))
            throw new ArgumentException("Angular-rate magnitude must be finite.", nameof(angularVelocityBody));
        double step = System.Math.Min(dt, System.Math.Min(Universe.MaxCoastStep, MaximumTrialRotationRad / rateMagnitude));
        double maximumTrialRate = rateMagnitude;
        RigidBodyForces Loads(RigidBody6DofState candidate, double epoch)
        {
            maximumTrialRate = System.Math.Max(maximumTrialRate, candidate.AngularVelocityBody.Magnitude);
            var torque = torqueBody(candidate.Orientation, epoch);
            DynamicsInputs.RequireFinite(torque, nameof(torqueBody));
            return new(Vector3d.Zero, torque);
        }
        while (elapsed < dt)
        {
            double remaining = dt - elapsed;
            step = System.Math.Min(step, remaining);
            maximumTrialRate = state.AngularVelocityBody.Magnitude;
            var full = RigidBody6DofIntegrator.Step(state, time + elapsed, step, mass, Loads);
            var half = RigidBody6DofIntegrator.Step(state, time + elapsed, step * 0.5, mass, Loads);
            half = RigidBody6DofIntegrator.Step(half, time + elapsed + step * 0.5, step * 0.5, mass, Loads);
            double orientationError = QuaternionDistance(full.Orientation, half.Orientation) / StepDoublingDivisor;
            double angularError = (full.AngularVelocityBody - half.AngularVelocityBody).Magnitude / StepDoublingDivisor;
            double rateScale = System.Math.Max(1.0, System.Math.Max(full.AngularVelocityBody.Magnitude, half.AngularVelocityBody.Magnitude));
            // Error estimates can alias at enormous angular sweeps. Keep every trial
            // within the local RK regime independently of step-doubling agreement.
            maximumTrialRate = System.Math.Max(maximumTrialRate, half.AngularVelocityBody.Magnitude);
            // Scale orientation error by swept angle as well as elapsed time. Otherwise
            // very fast spins demand sub-roundoff error and shrink without useful progress.
            double orientationBudget = ScaledOrientationErrorRatePerS * step * System.Math.Max(1.0, maximumTrialRate);
            bool accept = maximumTrialRate * step <= MaximumTrialRotationRad
                && double.IsFinite(orientationError) && double.IsFinite(angularError)
                && orientationError <= orientationBudget
                && angularError <= AngularRateErrorRatePerS * rateScale * step;
            if (accept)
            {
                state = half;
                elapsed = step == remaining ? dt : elapsed + step;
                if (orientationError <= orientationBudget / GrowthThreshold
                    && angularError <= AngularRateErrorRatePerS * rateScale * step / GrowthThreshold)
                    step = System.Math.Min(step * 2.0, Universe.MaxCoastStep);
            }
            else
            {
                step *= 0.5;
                if (time + elapsed + step == time + elapsed || elapsed + step == elapsed)
                    throw new InvalidOperationException("Coasting attitude accuracy cannot be resolved at this epoch.");
            }
        }
        return (state.Orientation, state.AngularVelocityBody);
    }

    private static double QuaternionDistance(Quaterniond a, Quaterniond b)
    {
        double minus = (a.W-b.W)*(a.W-b.W) + (a.X-b.X)*(a.X-b.X)
            + (a.Y-b.Y)*(a.Y-b.Y) + (a.Z-b.Z)*(a.Z-b.Z);
        double plus = (a.W+b.W)*(a.W+b.W) + (a.X+b.X)*(a.X+b.X)
            + (a.Y+b.Y)*(a.Y+b.Y) + (a.Z+b.Z)*(a.Z+b.Z);
        return 2.0 * System.Math.Sqrt(System.Math.Min(minus, plus));
    }
}
