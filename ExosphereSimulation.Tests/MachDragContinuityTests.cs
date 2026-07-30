namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Physics;
using Xunit;

/// <summary>
/// Guards the fix for a discontinuity at Mach 5: the old formula's supersonic branch
/// evaluated to 1.05 at M→5⁻ and then dropped to exactly 1.0 at M=5.0, a 4.8% step. The
/// fixed <see cref="AerodynamicsModel.GetMachDragMultiplier"/> ramps smoothly from 1.05
/// down to 1.0 over 5.0 ≤ mach &lt; 8.0 instead, and leaves every other branch untouched.
/// </summary>
public sealed class MachDragContinuityTests
{
    [Theory]
    [InlineData(0.8)]
    [InlineData(1.0)]
    [InlineData(1.2)]
    [InlineData(5.0)]
    [InlineData(8.0)]
    public void MachDragMultiplierIsContinuousAcrossEveryBreakpoint(double breakpoint)
    {
        double lo = AerodynamicsModel.GetMachDragMultiplier(breakpoint - 1e-9);
        double hi = AerodynamicsModel.GetMachDragMultiplier(breakpoint + 1e-9);

        Assert.True(System.Math.Abs(hi - lo) < 1e-6,
            $"discontinuity at mach {breakpoint}: lo={lo}, hi={hi}");
    }

    [Theory]
    [InlineData(8.0)]
    [InlineData(12.0)]
    [InlineData(20.0)]
    [InlineData(25.0)]
    [InlineData(40.0)]
    public void HypersonicRegimeIsMachIndependentAboveMachEight(double mach)
    {
        Assert.Equal(1.0, AerodynamicsModel.GetMachDragMultiplier(mach), 12);
    }

    [Fact]
    public void TransonicPeakIsTwiceTheSubsonicValue()
    {
        Assert.Equal(1.0, AerodynamicsModel.GetMachDragMultiplier(0.5), 12);
        Assert.Equal(2.0, AerodynamicsModel.GetMachDragMultiplier(1.1), 12);
    }

    [Fact]
    public void CombinedBroadsideHypersonicCdStaysBelowTheFlatPlateNewtonianLimit()
    {
        // ComputeReentryDrag's broadside cd is 1.5 (already the Newtonian blunt-body value).
        double cdBroadside = 1.5 * AerodynamicsModel.GetMachDragMultiplier(20.0);

        Assert.True(cdBroadside <= 2.0,
            $"combined broadside hypersonic Cd ({cdBroadside}) must not exceed the flat-plate Newtonian maximum of 2.0");
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.9)]
    [InlineData(1.1)]
    [InlineData(1.15)]
    [InlineData(2.0)]
    [InlineData(3.5)]
    [InlineData(4.9)]
    public void BelowMachFiveMultiplierIsUnchanged(double mach)
    {
        // Computed by hand from the ORIGINAL (pre-fix) formula, so this test would have
        // caught an accidental change to the untouched branches.
        double expected =
            mach < 0.8 ? 1.0
            : mach < 1.0 ? 1.0 + (mach - 0.8) * 5.0
            : mach < 1.2 ? 2.0
            : 2.0 - (mach - 1.2) * 0.25;

        Assert.Equal(expected, AerodynamicsModel.GetMachDragMultiplier(mach), 12);
    }
}
