namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Integrators;

/// <summary>Instantaneous coupled impulse response with modal coordinates held continuous.</summary>
public static class SloshImpulseDynamics
{
    public static Slosh6DofState Apply(Slosh6DofStage stage, Vector3d impulseWorldNs,
        Vector3d angularImpulseDatumBodyNms)
    {
        DynamicsInputs.RequireFinite(impulseWorldNs, nameof(impulseWorldNs));
        DynamicsInputs.RequireFinite(angularImpulseDatumBodyNms, nameof(angularImpulseDatumBodyNms));
        var modes = stage.Corrections.Select(c => new SloshKineticMode(c.ModalMassKg,
            c.PositionBodyM, c.TangentX, c.TangentZ)).ToArray();
        var datum = stage.State.Datum;
        var change = SloshKineticSystem.Solve(stage.Mass.Properties, modes,
            new SloshModalAcceleration[modes.Length], datum.Orientation.Inverse().Rotate(impulseWorldNs),
            angularImpulseDatumBodyNms, datum.Orientation);
        var states = new SloshDisplacementState[modes.Length];
        for (int i = 0; i < states.Length; i++)
        {
            var modal = change.ModalAccelerations[i]; var correction = stage.Corrections[i];
            var tankFrame = stage.Profile.Tanks[i].TankToBody ?? Quaterniond.Identity;
            var rateChange = tankFrame.Inverse().Rotate(correction.TangentX * modal.X + correction.TangentZ * modal.Z);
            states[i] = stage.State.Modes[i] with { RateBodyMps = stage.State.Modes[i].RateBodyMps + rateChange };
        }
        return new(new(datum.Position, datum.Velocity + change.DatumAccelerationWorld,
            datum.Orientation, datum.AngularVelocityBody + change.AngularAccelerationBody), states);
    }
}
