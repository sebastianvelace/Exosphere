namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Xunit;

public sealed class TowerCatchTerminalGuidanceTests
{
    [Theory]
    [InlineData(800.0, 90.0)]
    [InlineData(300.0, 40.0)]
    [InlineData(60.0, 15.0)]
    public void GateBrakingReachesTheGateSpeedAtTheGateInTheGateTime(double clearance, double descent)
    {
        var definition = TowerCatchBurnTests.Bench.Definition();
        double gate = definition.TerminalProfileClearanceM;
        double gateSpeed = definition.TargetDescentSpeedMps(gate);
        double deceleration = TowerCatchTerminalGuidance.GateDecelerationMps2(definition, clearance, descent);
        double expectedTime = TowerCatchTerminalGuidance.TimeToGateS(definition, clearance, descent);

        // Integrate the uniform deceleration from the state to the gate.
        double h = clearance, d = descent, t = 0.0;
        const double dt = 1e-4;
        while (h > gate) { d -= deceleration * dt; h -= d * dt; t += dt; }

        Assert.Equal(gateSpeed, d, 1);
        Assert.Equal(expectedTime, t, 2);
    }

    [Fact]
    public void BelowTheGateOrSlowerThanItNoGateBrakingIsAsked()
    {
        var definition = TowerCatchBurnTests.Bench.Definition();
        double gate = definition.TerminalProfileClearanceM;
        Assert.Equal(0.0, TowerCatchTerminalGuidance.GateDecelerationMps2(definition, gate - 1.0, 20.0));
        Assert.Equal(0.0, TowerCatchTerminalGuidance.GateDecelerationMps2(definition, 200.0, 1.0));
        Assert.Equal(0.0, TowerCatchTerminalGuidance.TimeToGateS(definition, gate - 1.0, 20.0));
        Assert.Equal(double.PositiveInfinity, TowerCatchTerminalGuidance.TimeToGateS(definition, 200.0, -50.0));
    }
}
