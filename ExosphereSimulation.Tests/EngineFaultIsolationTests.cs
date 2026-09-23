namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Propulsion;

public sealed class EngineFaultIsolationTests
{
    [Fact]
    public void OneWeakEngineIsolatedFromPeerThrustTelemetry()
    {
        var rows = new[]
        {
            Row("engine-0", 1.0, 1.0, 1_000_000.0),
            Row("engine-1", 1.0, 1.0, 1_000_000.0),
            Row("engine-2", 1.0, 0.0, 0.0),
            Row("engine-3", 1.0, 1.0, 1_000_000.0),
        };

        var result = EngineFaultIsolation.Infer(rows);

        Assert.True(result.FaultDetected);
        Assert.Contains("engine-2", result.SuspectEngineIds);
        Assert.DoesNotContain("engine-0", result.SuspectEngineIds);
        Assert.True(result.Confidence > 0.8);
    }

    [Fact]
    public void HealthyClusterDoesNotSelfDiagnose()
    {
        var rows = new[]
        {
            Row("engine-0", 1.0, 0.96, 960_000.0),
            Row("engine-1", 1.0, 1.0, 1_000_000.0),
            Row("engine-2", 1.0, 0.98, 980_000.0),
        };

        var result = EngineFaultIsolation.Infer(rows);

        Assert.False(result.FaultDetected);
        Assert.Empty(result.SuspectEngineIds);
    }

    [Fact]
    public void SingleEngineHasNoPeerBaseline()
    {
        var result = EngineFaultIsolation.Infer(
            new[] { Row("only-engine", 1.0, 0.0, 0.0) });

        Assert.False(result.FaultDetected);
        Assert.Equal(0.0, result.Confidence);
    }

    [Fact]
    public void MissingEngineProducesGeometricTorqueResidual()
    {
        var rows = new[]
        {
            Row("engine-0", 1.0, 1.0, 1_000_000.0),
            Row("engine-1", 1.0, 1.0, 1_000_000.0),
            Row("engine-2", 1.0, 0.0, 0.0),
        };
        var geometry = new[]
        {
            new EngineFaultIsolationGeometry(
                "engine-0", new(1.0, 0.0, 0.0), Exosphere.Simulation.Math.Vector3d.Up),
            new EngineFaultIsolationGeometry(
                "engine-1", new(-1.0, 0.0, 0.0), Exosphere.Simulation.Math.Vector3d.Up),
            new EngineFaultIsolationGeometry(
                "engine-2", new(0.0, 0.0, 1.0), Exosphere.Simulation.Math.Vector3d.Up),
        };

        var result = EngineFaultIsolation.Infer(
            rows,
            geometry,
            Exosphere.Simulation.Math.Vector3d.Zero);

        Assert.True(result.FaultDetected);
        Assert.Contains("engine-2", result.SuspectEngineIds);
        Assert.True(result.TorqueResidualNm.Magnitude > 0.0);
        Assert.True(
            result.TorqueResidualRatio >= EngineFaultIsolation.DefaultMinimumTorqueResidualRatio);
    }

    private static EngineTelemetry Row(
        string id, double commanded, double actual, double thrust) => new(
            id,
            EngineLifecycleState.Running,
            commanded,
            actual,
            thrust,
            thrust / 300_000.0,
            actual,
            3.5,
            Exosphere.Simulation.Math.Vector3d.Zero,
            900.0,
            4_000.0,
            1,
            1,
            null);
}
