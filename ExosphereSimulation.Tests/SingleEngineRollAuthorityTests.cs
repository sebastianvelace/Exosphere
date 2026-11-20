namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class SingleEngineRollAuthorityTests
{
    /// <summary>
    /// One lit Raptor of the ring has two gimbal axes and cannot hold roll as well as pitch and
    /// yaw, so the thrusters must take roll. The envelope used to credit the gimbal with roll
    /// whenever the cluster declared two or more engines, which kept the jets off and left the
    /// Flight 15 ship rolling at 0.064 rad/s over the pins.
    /// </summary>
    [Fact]
    public void ThrustersStopTheRollOnASingleEngine()
    {
        var bench = TowerCatchBurnTests.Bench.Create(upstreamM: 380.0, glideMps: 0.0, crossrangeM: 0.0);
        foreach (var part in bench.Ship.Parts.Parts.Where(p => p.Definition.RcsThrusters.Count > 0))
            part.RcsAttitudeEnabled = true;
        for (int i = 0; i < 4000 && (bench.Burn.EngineSteps.Count == 0 || bench.Burn.EngineSteps[^1].EngineCount != 1); i++)
            bench.Step(0.02);
        Assert.Equal(1, bench.Burn.EngineSteps[^1].EngineCount);

        var axis = bench.Ship.Orientation.Rotate(Vector3d.Up).Normalized;
        bench.Ship.AngularVelocity += axis * 0.064;
        double monoBefore = bench.Ship.Parts.Parts.Sum(p => p.Monopropellant);
        for (int i = 0; i < 250; i++) bench.Step(0.02);   // 5 s

        double roll = bench.Ship.AngularVelocity.Dot(bench.Ship.Orientation.Rotate(Vector3d.Up).Normalized);
        Assert.True(bench.Ship.Parts.Parts.Sum(p => p.Monopropellant) < monoBefore,
            $"the thrusters never fired: mono={monoBefore:F0} thrusters={bench.Ship.Parts.GetRcsThrusters().Count} "
            + $"torque={bench.Ship.RcsThrusterTorqueBody} cmd={bench.Ship.PitchYawRoll}");
        Assert.True(System.Math.Abs(roll) < 0.02, $"roll still {roll:F3} rad/s after 5 s");
    }
}
