namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Parts;

public sealed class SaturnVAscentScheduleTests
{
    [Theory]
    [InlineData(0.0, 40_000.0)]
    [InlineData(40_000.0, 0.0)]
    public void ExhaustedConstituentCutsOffEvenAboveTheAggregateResidual(double fuel, double oxidizer)
    {
        var vessel = Tank(fuel, oxidizer);

        Assert.True(SaturnVAscentSchedule.SicPropellantKg(vessel)
            > SaturnVAscentSchedule.As506.SicCutoffResidualKg);
        Assert.True(SaturnVAscentSchedule.As506.IsSicCutoff(vessel, 160.0));
    }

    [Fact]
    public void FundedHeaderFeedKeepsTheBurnAvailableAfterMainTanksEmpty()
    {
        var vessel = Tank(0.0, 0.0, headerFuel: 10_000.0, headerOxidizer: 30_000.0);

        Assert.False(SaturnVAscentSchedule.As506.IsSicCutoff(vessel, 160.0));
    }

    [Fact]
    public void NonemptyStoresRetainTheResidualAndClockPolicies()
    {
        Assert.False(SaturnVAscentSchedule.As506.IsSicCutoff(Tank(25_000, 25_000), 160.0));
        Assert.True(SaturnVAscentSchedule.As506.IsSicCutoff(Tank(10_000, 20_000), 160.0));
        Assert.True(SaturnVAscentSchedule.As506.IsSicCutoff(Tank(25_000, 25_000),
            SaturnVAscentSchedule.As506.SicOutboardCutoffSeconds + 15.0));
    }

    [Fact]
    public void ClockOnlyScheduleKeepsItsExistingCutoff()
    {
        var empty = Tank(0, 0);
        Assert.False(SaturnVAscentSchedule.As503.IsSicCutoff(empty, 20.0));
        Assert.True(SaturnVAscentSchedule.As503.IsSicCutoff(empty,
            SaturnVAscentSchedule.As503.SicOutboardCutoffSeconds));
    }

    [Fact]
    public void RealAs506StagesAfterFeedExhaustionWithoutWaitingForTheGuard()
    {
        double exhaustion = double.NaN, separation = double.NaN;
        var schedule = SaturnVAscentSchedule.As506;
        SaturnVAscentFlight.Fly(variantFile: "apollo11_saturn5_as506_1969.json",
            untilElapsedSeconds: 180.0, afterTick: (vessel, elapsed, _) =>
            {
                var tank = vessel.Parts.Parts.SingleOrDefault(p => p.Definition.HasVehicleRole("sic_tank"));
                if (tank == null)
                {
                    if (double.IsNaN(separation)) separation = elapsed;
                    return;
                }
                if (double.IsNaN(exhaustion) && tank.LiquidFuel + tank.HeaderLiquidFuel == 0.0)
                {
                    exhaustion = elapsed;
                    // Reproduces the trapped residual rather than an ordinary threshold cutoff.
                    Assert.True(SaturnVAscentSchedule.SicPropellantKg(vessel) > schedule.SicCutoffResidualKg);
                }
            });

        Assert.True(double.IsFinite(exhaustion));
        Assert.InRange(separation - exhaustion, schedule.SicSeparationDelaySeconds,
            schedule.SicSeparationDelaySeconds + 0.04);
    }

    private static Vessel Tank(double fuel, double oxidizer, double headerFuel = 0.0, double headerOxidizer = 0.0)
    {
        var vessel = new Vessel();
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "sic-feed", VehicleRole = "sic_tank", MassDry = 1.0,
            FuelCapacityLF = fuel, FuelCapacityOx = oxidizer,
            HeaderTanks =
            [
                new() { Id = "fuel-line", Propellant = "liquid_fuel", CapacityKg = headerFuel },
                new() { Id = "oxidizer-line", Propellant = "oxidizer", CapacityKg = headerOxidizer },
            ],
        }));
        return vessel;
    }
}
