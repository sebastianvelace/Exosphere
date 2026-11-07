namespace Exosphere.Simulation.Integrators;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>Rigid state at the construction datum, with modal displacement/rate states.</summary>
public sealed record Slosh6DofState(RigidBody6DofState Datum, IReadOnlyList<SloshDisplacementState> Modes);
public readonly record struct SloshTankParameters(CylindricalSloshParameters Parameters, Vector3d FloorBodyM,
    Quaterniond? TankToBody = null);
public sealed record Slosh6DofProfile(BodyMassSample Baseline, IReadOnlyList<SloshTankParameters> Tanks);
public sealed record Slosh6DofStage(Slosh6DofState State, Slosh6DofProfile Profile,
    SloshMassSnapshot Mass, IReadOnlyList<SloshTransportCorrection> Corrections);
public readonly record struct Slosh6DofStageLoads(Slosh6DofLoads Applied,
    OpenSystemTransportSample Feed = default, IReadOnlyList<Vector3d>? InternalModalAccelerationBody = null);

/// <summary>
/// RK4 of the coupled datum/attitude/modal system. Profiles and loads are pure candidate
/// samplers: resource funding, engine lifecycle, flow cuts and topology are owned by the
/// caller. Each smooth interval must end on its own left-hand profile, before any cut.
/// </summary>
public static class Slosh6DofIntegrator
{
    public static Slosh6DofState Step(Slosh6DofState state, double time, double dt,
        Func<double, Slosh6DofProfile> sampleProfile,
        Func<Slosh6DofStage, double, Slosh6DofStageLoads> evaluateLoads)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sampleProfile);
        ArgumentNullException.ThrowIfNull(evaluateLoads);
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (!(dt > 0) || !double.IsFinite(dt) || !double.IsFinite(time + dt))
            throw new ArgumentOutOfRangeException(nameof(dt));
        state = Sample(state, sampleProfile(time)).State;
        var k1 = Evaluate(state, time, sampleProfile, evaluateLoads);
        var k2 = Evaluate(Add(state, k1, dt / 2), time + dt / 2, sampleProfile, evaluateLoads);
        var k3 = Evaluate(Add(state, k2, dt / 2), time + dt / 2, sampleProfile, evaluateLoads);
        var k4 = Evaluate(Add(state, k3, dt), time + dt, sampleProfile, evaluateLoads);
        double scale = dt / 6;
        var modes = new SloshDisplacementState[state.Modes.Count];
        for (int i = 0; i < modes.Length; i++)
            modes[i] = new(state.Modes[i].DisplacementBodyM + (k1.Displacements[i] + 2 * k2.Displacements[i]
                    + 2 * k3.Displacements[i] + k4.Displacements[i]) * scale,
                state.Modes[i].RateBodyMps + (k1.Rates[i] + 2 * k2.Rates[i] + 2 * k3.Rates[i] + k4.Rates[i]) * scale);
        var d = state.Datum;
        var q = new Quaterniond(
            d.Orientation.W + (k1.Q.W + 2 * k2.Q.W + 2 * k3.Q.W + k4.Q.W) * scale,
            d.Orientation.X + (k1.Q.X + 2 * k2.Q.X + 2 * k3.Q.X + k4.Q.X) * scale,
            d.Orientation.Y + (k1.Q.Y + 2 * k2.Q.Y + 2 * k3.Q.Y + k4.Q.Y) * scale,
            d.Orientation.Z + (k1.Q.Z + 2 * k2.Q.Z + 2 * k3.Q.Z + k4.Q.Z) * scale).Normalize();
        var final = new Slosh6DofState(new(
            d.Position + (k1.Position + 2 * k2.Position + 2 * k3.Position + k4.Position) * scale,
            d.Velocity + (k1.Velocity + 2 * k2.Velocity + 2 * k3.Velocity + k4.Velocity) * scale,
            q, d.AngularVelocityBody + (k1.Angular + 2 * k2.Angular + 2 * k3.Angular + k4.Angular) * scale), modes);
        return Sample(final, sampleProfile(time + dt)).State;
    }

    public static Slosh6DofStage Sample(Slosh6DofState state, Slosh6DofProfile profile)
    {
        if (state.Modes.Count != profile.Tanks.Count)
            throw new ArgumentException("One state is required for every tank.", nameof(state));
        if (!(state.Datum.Orientation.NormSquared > 0) || !double.IsFinite(state.Datum.Orientation.NormSquared))
            throw new ArgumentException("Orientation must be finite and nonzero.", nameof(state));
        DynamicsInputs.RequireFinite(state.Datum.Position, nameof(state));
        DynamicsInputs.RequireFinite(state.Datum.Velocity, nameof(state));
        DynamicsInputs.RequireFinite(state.Datum.AngularVelocityBody, nameof(state));
        var modes = new SloshDisplacementState[state.Modes.Count];
        var corrections = new SloshTransportCorrection[modes.Length];
        for (int i = 0; i < modes.Length; i++)
        {
            var tank = profile.Tanks[i];
            modes[i] = state.Modes[i].Sample(tank.Parameters.CurvaturePerM).State;
            corrections[i] = SloshTransportCorrection.SampleDisplacement(tank.Parameters, tank.FloorBodyM, modes[i], tank.TankToBody);
        }
        var projected = new Slosh6DofState(new(state.Datum.Position, state.Datum.Velocity,
            state.Datum.Orientation.Normalize(), state.Datum.AngularVelocityBody), modes);
        return new(projected, profile, SloshVariableMassDynamics.MassProperties(profile.Baseline, corrections), corrections);
    }

    private static Derivative Evaluate(Slosh6DofState state, double time,
        Func<double, Slosh6DofProfile> sampleProfile,
        Func<Slosh6DofStage, double, Slosh6DofStageLoads> evaluateLoads)
    {
        var stage = Sample(state, sampleProfile(time));
        var loads = evaluateLoads(stage, time);
        var d = stage.State.Datum;
        var a = SloshVariableMassDynamics.Evaluate(stage.Profile.Baseline, loads.Feed, d.Orientation,
            d.AngularVelocityBody, stage.Corrections, loads.Applied, loads.InternalModalAccelerationBody);
        var displacements = new Vector3d[state.Modes.Count]; var rates = new Vector3d[state.Modes.Count];
        for (int i = 0; i < rates.Length; i++)
        {
            displacements[i] = stage.State.Modes[i].RateBodyMps;
            var correction = stage.Corrections[i];
            var tank = stage.Profile.Tanks[i]; var tankFrame = tank.TankToBody ?? Quaterniond.Identity;
            rates[i] = tankFrame.Inverse().Rotate(correction.AccelerationBiasBodyMps2
                + correction.TangentX * a.ModalAccelerations[i].X + correction.TangentZ * a.ModalAccelerations[i].Z)
                - Vector3d.Up * tank.Parameters.HeightAboveFloorM.Acceleration;
        }
        var q = d.Orientation * new Quaterniond(0, d.AngularVelocityBody.X,
            d.AngularVelocityBody.Y, d.AngularVelocityBody.Z);
        return new(d.Velocity, a.DatumAccelerationWorld, new(q.W / 2, q.X / 2, q.Y / 2, q.Z / 2),
            a.AngularAccelerationBody, displacements, rates);
    }

    private static Slosh6DofState Add(Slosh6DofState state, Derivative derivative, double scale)
    {
        var modes = new SloshDisplacementState[state.Modes.Count];
        for (int i = 0; i < modes.Length; i++) modes[i] = new(
            state.Modes[i].DisplacementBodyM + derivative.Displacements[i] * scale,
            state.Modes[i].RateBodyMps + derivative.Rates[i] * scale);
        var d = state.Datum; var q = derivative.Q;
        return new(new(d.Position + derivative.Position * scale, d.Velocity + derivative.Velocity * scale,
            new Quaterniond(d.Orientation.W + q.W * scale, d.Orientation.X + q.X * scale,
                d.Orientation.Y + q.Y * scale, d.Orientation.Z + q.Z * scale).Normalize(),
            d.AngularVelocityBody + derivative.Angular * scale), modes);
    }

    private sealed record Derivative(Vector3d Position, Vector3d Velocity, Quaterniond Q,
        Vector3d Angular, Vector3d[] Displacements, Vector3d[] Rates);
}
