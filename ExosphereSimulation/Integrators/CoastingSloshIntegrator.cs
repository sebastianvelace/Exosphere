namespace Exosphere.Simulation.Integrators;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>
/// Adaptive constant-fill coupled coast on a separately prescribed centre-of-mass conic.
/// No settling force or artificial damping is added. The spherical analogue remains a
/// conservative mathematical continuation outside its settled-fluid validity domain.
/// </summary>
public static class CoastingSloshIntegrator
{
    public const double ScaledModalErrorRatePerS = 1e-11;
    public const double ScaledRoundoffBudget = 1.4210854715202004e-14;

    public static Slosh6DofState Advance(Slosh6DofState state, double time, double dt,
        Slosh6DofProfile profile, Func<Slosh6DofStage, double, Slosh6DofStageLoads> loads)
    {
        if (!double.IsFinite(time) || !(dt > 0) || !double.IsFinite(dt) || !double.IsFinite(time + dt))
            throw new ArgumentOutOfRangeException(nameof(dt));
        if (profile.Baseline.MassRateKgPerS != 0 || profile.Baseline.CenterOfMassRateBodyMps != Vector3d.Zero)
            throw new ArgumentException("A Kepler coast cannot contain funded mass flow.", nameof(profile));
        foreach (var tank in profile.Tanks)
            foreach (var jet in new[] { tank.Parameters.ModalMassKg, tank.Parameters.CurvaturePerM, tank.Parameters.HeightAboveFloorM })
                if (jet.Rate != 0 || jet.Acceleration != 0)
                    throw new ArgumentException("A Kepler coast needs constant modal coefficients.", nameof(profile));
        state = Slosh6DofIntegrator.Sample(state, profile).State;
        double TrialRate(Slosh6DofState candidate)
        {
            double rate = candidate.Datum.AngularVelocityBody.Magnitude;
            for (int i = 0; i < candidate.Modes.Count; i++)
                rate = System.Math.Max(rate, candidate.Datum.AngularVelocityBody.Magnitude
                    + profile.Tanks[i].Parameters.CurvaturePerM.Value * candidate.Modes[i].RateBodyMps.Magnitude);
            return rate;
        }
        double elapsed = 0, maximumRate = TrialRate(state);
        double step = System.Math.Min(dt, System.Math.Min(Universe.MaxCoastStep,
            CoastingAttitudeIntegrator.MaximumTrialRotationRad / maximumRate));
        Slosh6DofStageLoads Evaluate(Slosh6DofStage stage, double epoch)
        {
            maximumRate = System.Math.Max(maximumRate, TrialRate(stage.State));
            return loads(stage, epoch);
        }
        while (elapsed < dt)
        {
            double remaining = dt - elapsed; step = System.Math.Min(step, remaining);
            maximumRate = TrialRate(state);
            var full = Slosh6DofIntegrator.Step(state, time + elapsed, step, _ => profile, Evaluate);
            var half = Slosh6DofIntegrator.Step(state, time + elapsed, step / 2, _ => profile, Evaluate);
            half = Slosh6DofIntegrator.Step(half, time + elapsed + step / 2, step / 2, _ => profile, Evaluate);
            var a = full.Datum.Orientation; var b = half.Datum.Orientation;
            double minus = (a.W-b.W)*(a.W-b.W) + (a.X-b.X)*(a.X-b.X) + (a.Y-b.Y)*(a.Y-b.Y) + (a.Z-b.Z)*(a.Z-b.Z);
            double plus = (a.W+b.W)*(a.W+b.W) + (a.X+b.X)*(a.X+b.X) + (a.Y+b.Y)*(a.Y+b.Y) + (a.Z+b.Z)*(a.Z+b.Z);
            double orientationError = 2 * System.Math.Sqrt(System.Math.Min(minus, plus)) / 15;
            double orientationBudget = System.Math.Max(ScaledRoundoffBudget,
                CoastingAttitudeIntegrator.ScaledOrientationErrorRatePerS * step * System.Math.Max(1, maximumRate));
            double angularError = (full.Datum.AngularVelocityBody - half.Datum.AngularVelocityBody).Magnitude / 15;
            double angularBudget = System.Math.Max(ScaledRoundoffBudget,
                CoastingAttitudeIntegrator.AngularRateErrorRatePerS * step)
                * System.Math.Max(1, System.Math.Max(full.Datum.AngularVelocityBody.Magnitude, half.Datum.AngularVelocityBody.Magnitude));
            double modalRatio = 0;
            for (int i = 0; i < state.Modes.Count; i++)
            {
                var f = full.Modes[i]; var h = half.Modes[i];
                double positionScale = System.Math.Max(1, System.Math.Max(f.DisplacementBodyM.Magnitude, h.DisplacementBodyM.Magnitude));
                double speedScale = System.Math.Max(1, System.Math.Max(f.RateBodyMps.Magnitude, h.RateBodyMps.Magnitude));
                // Retractions of already constrained vectors differ at binary64 roundoff.
                // A tiny final interval must not demand sub-roundoff step-doubling agreement.
                double budget = System.Math.Max(ScaledRoundoffBudget, ScaledModalErrorRatePerS * step);
                modalRatio = System.Math.Max(modalRatio, (f.DisplacementBodyM - h.DisplacementBodyM).Magnitude
                    / (15 * positionScale * budget));
                modalRatio = System.Math.Max(modalRatio, (f.RateBodyMps - h.RateBodyMps).Magnitude
                    / (15 * speedScale * budget));
            }
            double ratio = System.Math.Max(modalRatio, System.Math.Max(orientationError / orientationBudget, angularError / angularBudget));
            if (double.IsFinite(ratio) && ratio <= 1 && maximumRate * step <= CoastingAttitudeIntegrator.MaximumTrialRotationRad)
            {
                state = half; elapsed = step == remaining ? dt : elapsed + step;
                if (ratio <= 1.0 / 32) step = System.Math.Min(step * 2, Universe.MaxCoastStep);
            }
            else
            {
                step /= 2;
                if (time + elapsed + step == time + elapsed || elapsed + step == elapsed)
                    throw new InvalidOperationException("Coupled coast accuracy cannot be resolved at this epoch.");
            }
        }
        return state;
    }
}
