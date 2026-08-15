namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class RK4AllocationRegressionTests
{
    [Fact]
    public void SixDofStepMatchesConstantAccelerationExactly()
    {
        var (position, velocity) = RK4Integrator.StepPosVel(
            Vector3d.Zero,
            Vector3d.Zero,
            0.0,
            2.0,
            static (_, _, _) => new Vector3d(1.0, 2.0, 3.0));

        Assert.Equal(new Vector3d(2.0, 4.0, 6.0), position);
        Assert.Equal(new Vector3d(2.0, 4.0, 6.0), velocity);
    }

    [Fact]
    public void SixDofStepDoesNotAllocatePerSubstep()
    {
        Func<Vector3d, Vector3d, double, Vector3d> acceleration =
            static (_, _, _) => Vector3d.Zero;
        var position = new Vector3d(7.0, -3.0, 2.0);
        var velocity = new Vector3d(1.0, 2.0, -1.0);

        for (int i = 0; i < 128; i++)
            (position, velocity) = RK4Integrator.StepPosVel(
                position, velocity, i * 0.02, 0.02, acceleration);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int samples = 2_000;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < samples; i++)
            (position, velocity) = RK4Integrator.StepPosVel(
                position, velocity, i * 0.02, 0.02, acceleration);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(double.IsFinite(position.X) && double.IsFinite(velocity.X));
        Assert.InRange(allocatedBytes / (double)samples, 0.0, 64.0);
    }
}
