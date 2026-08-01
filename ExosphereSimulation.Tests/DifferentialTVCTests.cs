namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// R5b — verifies <see cref="PartGraph.SolveDifferentialGimbal"/> commands each live engine
/// instance's own gimbal (via <see cref="Propulsion.EngineInstanceState.GimbalCommandOverride"/>)
/// toward a requested torque, instead of <c>Vessel.Tick</c>'s pre-R5b behaviour of mirroring one
/// shared deflection to every mount of a part. Uses the same steady 33-engine booster fixture as
/// <see cref="EngineTorqueTests"/> so the nominal-cluster symmetry argument there also applies
/// here. Convergence is driven through the real servo (<c>AdvanceEngineRuntime</c> →
/// <c>Part.AdvanceGimbal</c>), not by poking <c>GimbalDeg</c> directly, so these tests exercise
/// the exact pipeline <c>Vessel.Tick</c> uses.
/// </summary>
public sealed class DifferentialTVCTests
{
    private const double SeaLevelPressure = 101_325.0;
    private static readonly DirectoryInfo Root = FindRepoRoot();

    // Pitch/yaw gimbal authority scales with the vertical lever arm between the engine ring
    // and the vessel's centre of mass (see PartGraph.GetPitchYawAngularAcceleration's own
    // Math.Abs(engineY - comY) lever). BuildSteadyBoosterGraph, like EngineTorqueTests' fixture
    // it mirrors, is a single isolated engine part with no tank/payload stacked above it, so its
    // own CenterOfMass sits right at the engine ring (r.y ≈ 0 for every mount) — deliberately
    // representative of *this part's* own mass distribution, not a full assembled vessel. Roll
    // authority, which scales with the ring's *radial* spread (r.x/r.z, ~3.4 m here) instead of
    // a vertical offset, is exactly what this fixture's geometry does represent well, so the
    // torque-producing tests below command roll — which also happens to be the axis R5b closes
    // a real capability gap on: pre-R5b, GimbalOffset never carried a roll (Y) component at all
    // (Vessel.cs mapped only command.X/command.Z into GimbalOffset.X/Z), so roll authority came
    // exclusively from the idealized GetRollAngularAcceleration scalar, never from an actual
    // differential gimbal command.

    [Fact]
    public void SymmetricCluster_PureRollTorqueCommand_ConvergesOnAxisWithLittleCrosstalk()
    {
        var graph = BuildSteadyBoosterGraph(out var booster);

        graph.SolveDifferentialGimbal(new Vector3d(0.0, 500_000.0, 0.0), SeaLevelPressure);
        ConvergeGimbals(booster);

        var torque = graph.GetTotalTorque(SeaLevelPressure);

        Assert.True(torque.Y > 50_000.0,
            $"expected a real roll torque from differential gimbal, got {torque.Y}");
        // Off-axis crosstalk should be small relative to the commanded axis for a nominally
        // symmetric cluster — the differential solve should not smuggle in pitch/yaw nobody
        // asked for just to satisfy a pure-roll demand.
        Assert.True(System.Math.Abs(torque.X) < 0.1 * torque.Y,
            $"unexpected pitch crosstalk: pitch={torque.X}, roll={torque.Y}");
        Assert.True(System.Math.Abs(torque.Z) < 0.1 * torque.Y,
            $"unexpected yaw crosstalk: yaw={torque.Z}, roll={torque.Y}");
    }

    [Fact]
    public void EngineOut_StillProducesBestEffortAlignedTorque_NotSilentFailure()
    {
        var graph = BuildSteadyBoosterGraph(out var booster);
        // sh-outer-01 @ (3.4,0,0), index 13 (see EngineTorqueTests) — remove one contributor.
        Assert.True(booster.FailEngine(booster.EngineStates[13].InstanceId, "TEST_TVC_ENGINE_OUT"));

        graph.SolveDifferentialGimbal(new Vector3d(0.0, 500_000.0, 0.0), SeaLevelPressure);
        ConvergeGimbals(booster);

        var torque = graph.GetTotalTorque(SeaLevelPressure);

        // Losing one of 33 engines should not collapse authority to zero or flip its sign —
        // the surviving cluster still commands a real, correctly-signed roll torque.
        Assert.True(torque.Y > 40_000.0,
            $"engine-out should still yield most of the commanded torque, got {torque.Y}");
    }

    [Fact]
    public void ZeroDesiredTorque_IsANoOp()
    {
        var graph = BuildSteadyBoosterGraph(out var booster);

        graph.SolveDifferentialGimbal(Vector3d.Zero, SeaLevelPressure);
        ConvergeGimbals(booster);

        var torque = graph.GetTotalTorque(SeaLevelPressure);

        Assert.Equal(0.0, torque.X, 3);
        Assert.Equal(0.0, torque.Y, 3);
        Assert.Equal(0.0, torque.Z, 3);
    }

    [Fact]
    public void NoActiveEngines_DoesNotThrow()
    {
        var graph = new PartGraph();
        // Empty graph: SolveDifferentialGimbal must be a safe no-op, not a NullReferenceException.
        graph.SolveDifferentialGimbal(new Vector3d(1.0, 1.0, 1.0), SeaLevelPressure);
    }

    private static void ConvergeGimbals(Part booster)
    {
        for (int i = 0; i < 400; i++)
            booster.AdvanceEngineRuntime(1.0, 0.02);
    }

    private static PartGraph BuildSteadyBoosterGraph(out Part booster)
    {
        var catalog = LoadPartCatalog();
        booster = new Part(catalog["super_heavy_booster"], "tvc-test-booster");
        var graph = new PartGraph();
        graph.SetRoot(booster);
        for (int i = 0; i < 250; i++)
            booster.AdvanceEngineRuntime(1.0, 0.02);
        return graph;
    }

    private static PartCatalog LoadPartCatalog() =>
        PartCatalog.LoadFromDirectory(Path.Combine(Root.FullName, "data", "parts"));

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
