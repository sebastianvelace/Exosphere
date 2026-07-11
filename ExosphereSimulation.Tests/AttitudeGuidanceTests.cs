namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class AttitudeGuidanceTests
{
    [Fact]
    public void QuaternionErrorMapsToSemanticYawForAYUpVehicle()
    {
        var desired = Quaterniond.FromAxisAngle(new Vector3d(0.0, 0.0, 1.0), 20.0 * MathUtils.DEG_TO_RAD);

        var command = AttitudeGuidance.ComputeCommand(
            Quaterniond.Identity, desired, Vector3d.Zero);

        Assert.True(command.Y > 0.0, $"Expected semantic yaw command, got {command}");
        Assert.True(System.Math.Abs(command.X) < 1e-12);
        Assert.True(System.Math.Abs(command.Z) < 1e-12);
    }

    [Fact]
    public void RollCanBeCommandedOrExplicitlySuppressed()
    {
        var desired = Quaterniond.FromAxisAngle(Vector3d.Up, 30.0 * MathUtils.DEG_TO_RAD);

        var full = AttitudeGuidance.ComputeCommand(
            Quaterniond.Identity, desired, Vector3d.Zero, allowRoll: true);
        var noRoll = AttitudeGuidance.ComputeCommand(
            Quaterniond.Identity, desired, Vector3d.Zero, allowRoll: false);

        Assert.True(full.Z > 0.0);
        Assert.Equal(0.0, noRoll.Z);
    }

    [Fact]
    public void ControllerDoesNotMutateOrSnapCurrentAttitude()
    {
        var current = Quaterniond.FromEuler(5.0, -3.0, 2.0);
        var before = current;
        var desired = Quaterniond.FromAxisAngle(Vector3d.Right, System.Math.PI * 0.5);

        var command = AttitudeGuidance.ComputeCommand(current, desired, Vector3d.Zero);

        Assert.Equal(before, current);
        Assert.True(command.Magnitude > 0.0);
        Assert.True(AttitudeGuidance.ErrorAngleRadians(current, desired) > 1.0);
    }

    [Fact]
    public void RateFeedbackOpposesExistingAngularVelocity()
    {
        var command = AttitudeGuidance.ComputeCommand(
            Quaterniond.Identity,
            Quaterniond.Identity,
            new Vector3d(0.2, -0.1, 0.3),
            dampingGain: 1.0,
            allowRoll: true);

        Assert.True(command.X < 0.0); // local X pitch rate
        Assert.True(command.Y < 0.0); // local Z yaw rate
        Assert.True(command.Z > 0.0); // local Y roll rate was negative
    }

    [Fact]
    public void AxisPointingIgnoresRollAndMapsTheShortestYaw()
    {
        var rolled = Quaterniond.FromAxisAngle(Vector3d.Up, 75.0 * MathUtils.DEG_TO_RAD);
        var alreadyPointed = AttitudeGuidance.ComputeAxisPointingCommand(
            rolled, Vector3d.Up, Vector3d.Up, Vector3d.Zero);
        var retro = AttitudeGuidance.ComputeAxisPointingCommand(
            Quaterniond.Identity, Vector3d.Up, -Vector3d.Right, Vector3d.Zero);

        Assert.True(alreadyPointed.Magnitude < 1e-12, "roll about the thrust axis is irrelevant");
        Assert.True(retro.Y > 0.0, $"+Y to -X requires positive local-Z yaw, got {retro}");
        Assert.Equal(0.0, retro.Z);
    }

    [Fact]
    public void AxisPointingConvergesThroughIntegratedAngularDynamics()
    {
        var orientation = Quaterniond.FromAxisAngle(-Vector3d.Forward, 20.0 * MathUtils.DEG_TO_RAD);
        var angularVelocity = Vector3d.Zero;
        var target = -Vector3d.Right;
        const double dt = 0.01;
        const double authority = 0.25;

        for (int i = 0; i < 2_000; i++)
        {
            var command = AttitudeGuidance.ComputeAxisPointingCommand(
                orientation, Vector3d.Up, target, angularVelocity, 2.6, 1.2);
            var localAcceleration = new Vector3d(command.X, 0.0, command.Y) * authority;
            angularVelocity += orientation.Rotate(localAcceleration) * dt;
            double rate = angularVelocity.Magnitude;
            if (rate > 0.35) angularVelocity *= 0.35 / rate;
            if (rate > 1e-12)
            {
                var dq = Quaterniond.FromAxisAngle(angularVelocity.Normalized, angularVelocity.Magnitude * dt);
                orientation = (dq * orientation).Normalize();
            }
        }

        double alignment = orientation.Rotate(Vector3d.Up).Normalized.Dot(target);
        Assert.True(alignment > 0.999, $"axis controller failed to converge: alignment={alignment:F6}");
        Assert.True(angularVelocity.Magnitude < 0.04,
            $"controller must settle instead of spinning through target: rate={angularVelocity.Magnitude:F6}");
    }
}
