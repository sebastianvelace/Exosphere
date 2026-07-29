namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;
using Xunit;

/// <summary>
/// Verifies <see cref="PartGraph.GetTotalTorque"/> and
/// <see cref="PartGraph.GetPitchYawRollAngularAcceleration"/>: genuine per-engine-mount
/// torque (τ = Σ r×F) built from each cluster mount's real 3D position instead of the
/// scalar-lever approximation the pre-existing pitch/yaw/roll methods still use.
/// </summary>
public sealed class EngineTorqueTests
{
    private const double SeaLevelPressure = 101_325.0;
    private static readonly DirectoryInfo Root = FindRepoRoot();

    // ── Booster fixture: root-only graph so CenterOfMass == Vector3d.Zero exactly,
    // making every mount's r == its raw PositionM (no CoM subtraction needed by hand). ──

    [Fact]
    public void NominalSymmetricCluster_ProducesZeroNetTorque()
    {
        var graph = BuildSteadyBoosterGraph(out _);

        var torque = graph.GetTotalTorque(SeaLevelPressure);

        // Every mount in the Flight 7 booster layout is either a member of an exact
        // negation pair (e.g. sh-middle-02 @ (1.4562,0,1.0580) / sh-middle-07 @
        // (-1.4562,0,-1.0580)) or, for the three centre engines, a trio whose positions
        // sum to exactly (0,0,0) (0 + -0.4763 + 0.4763 = 0; 0.55 + -0.275 + -0.275 = 0).
        // All 33 mounts share one engine model, so at zero gimbal every instance's thrust
        // is bit-identical: r×F + (-r)×F cancels exactly, and (Σr)×F = 0×F = 0 for the
        // centre trio. Net torque must be zero (to floating summation noise only).
        Assert.Equal(0.0, torque.X, 3);
        Assert.Equal(0.0, torque.Y, 3);
        Assert.Equal(0.0, torque.Z, 3);
    }

    [Fact]
    public void FailingOffCenterOuterMount_ProducesExactYawTorqueTowardOppositeSide()
    {
        var graph = BuildSteadyBoosterGraph(out var booster);
        double thrustBefore = booster.GetThrustMagnitude(SeaLevelPressure);
        double perEngineThrust = thrustBefore / 33.0;

        // sh-outer-01 is engine-cluster index 13 (3 centre + 10 middle mounts precede
        // it), positionM = [3.4, 0, 0] (data/engine_clusters/starship_flight7_superheavy_33.json).
        string instanceId = booster.EngineStates[13].InstanceId;
        Assert.True(booster.FailEngine(instanceId, "TEST_ASYMMETRIC"));

        var torque = graph.GetTotalTorque(SeaLevelPressure);

        // Before the failure the net torque is ~0 (previous test). Removing one engine
        // subtracts exactly its own r×F contribution from that zero sum, so the new total
        // equals -(r×F) for the failed mount alone:
        //   r = (3.4, 0, 0), F = (0, perEngineThrust, 0)
        //   r×F = (r.Y*F.Z - r.Z*F.Y, r.Z*F.X - r.X*F.Z, r.X*F.Y - r.Y*F.X)
        //       = (0*0-0*T, 0*0-3.4*0, 3.4*T-0*0) = (0, 0, 3.4*T)
        //   new total = (0,0,0) - (0,0,3.4*T) = (0, 0, -3.4*perEngineThrust)
        var r = new Vector3d(3.4, 0.0, 0.0);
        var f = new Vector3d(0.0, perEngineThrust, 0.0);
        var expected = -r.Cross(f);

        Assert.Equal(expected.X, torque.X, 3);
        Assert.Equal(expected.Y, torque.Y, 3);
        Assert.Equal(expected.Z, torque.Z, 3);
        // Dominant component: yaw (local Z), pushed negative — away from the failed
        // engine's +X side, exactly as a real engine-out asymmetry must behave.
        Assert.True(torque.Z < -1_000_000.0);
        Assert.Equal(0.0, torque.X, 3);
        Assert.Equal(0.0, torque.Y, 3);
    }

    [Fact]
    public void FailingDiametricallyOppositeOuterMounts_CancelToNearZeroTorque()
    {
        var graph = BuildSteadyBoosterGraph(out var booster);

        // sh-outer-01 @ (3.4,0,0) [index 13] and sh-outer-11 @ (-3.4,0,0) [index 23] are
        // an exact negation pair.
        Assert.True(booster.FailEngine(booster.EngineStates[13].InstanceId, "TEST_PAIR_A"));
        Assert.True(booster.FailEngine(booster.EngineStates[23].InstanceId, "TEST_PAIR_B"));

        var torque = graph.GetTotalTorque(SeaLevelPressure);

        // Removing a negation pair removes r×F and (-r)×F together, i.e. exactly zero net
        // change from the already-zero nominal torque — proving GetTotalTorque is a real
        // vector sum over independent mounts, not per-engine bookkeeping that would leave
        // a stale asymmetric residual.
        Assert.Equal(0.0, torque.X, 3);
        Assert.Equal(0.0, torque.Y, 3);
        Assert.Equal(0.0, torque.Z, 3);
    }

    [Fact]
    public void IsolatedGimbalDeflectionOnSingleGimballedMount_ProducesTorqueMatchingCrossProductSign()
    {
        var graph = BuildSteadyBoosterGraph(out var booster);
        double thrustBefore = booster.GetThrustMagnitude(SeaLevelPressure);
        double perEngineThrust = thrustBefore / 33.0;

        // sh-center-01, index 0, positionM = [0, 0, 0.55], gimballed = true. Deflect only
        // this instance's gimbal on X by 10 deg; leave every other instance untouched.
        var state = booster.EngineStates[0];
        state.GimbalDeg = new Vector3d(10.0, 0.0, 0.0);

        var torque = graph.GetTotalTorque(SeaLevelPressure);

        // Tilted direction (same formula GetThrustVector already uses):
        //   ax = 10deg in rad; dir = normalize(sin(ax), 1, 0)
        double ax = 10.0 * MathUtils.DEG_TO_RAD;
        var dir = new Vector3d(System.Math.Sin(ax), 1.0, 0.0).Normalized;
        var fNew = dir * perEngineThrust;
        var fOld = new Vector3d(0.0, perEngineThrust, 0.0);
        var r = new Vector3d(0.0, 0.0, 0.55);
        // Nominal (undeflected) net torque is ~0, so the new total equals just this one
        // instance's delta contribution: r x (Fnew - Fold).
        var expected = r.Cross(fNew - fOld);

        Assert.Equal(expected.X, torque.X, 3);
        Assert.Equal(expected.Y, torque.Y, 3);
        Assert.Equal(expected.Z, torque.Z, 3);
        // Sanity: this is a genuinely nonzero pitch+roll torque, not a no-op.
        Assert.True(System.Math.Abs(torque.X) > 1.0);
        Assert.True(System.Math.Abs(torque.Y) > 1.0);
    }

    [Fact]
    public void GetEngineInstanceThrustGeometry_TiltDirectionGeneralizesForNonVerticalMount()
    {
        var model = SyntheticModel();
        var definition = new PartDefinition
        {
            Id = "synthetic-side-engine",
            Name = "Synthetic Side Engine",
            CategoryStr = "engine",
            EngineModelId = model.Id,
            EngineCount = 1,
            ResolvedEngineModel = model,
            ResolvedEngineModels = new Dictionary<string, EngineModelDefinition>(
                StringComparer.Ordinal) { [model.Id] = model },
            ResolvedEngineCluster = new EngineClusterDefinition
            {
                Id = "synthetic-side-cluster",
                Name = "Synthetic Side Cluster",
                EngineModelId = model.Id,
                Engines =
                [
                    new EngineMountDefinition
                    {
                        InstanceId = "side-01",
                        PositionM = [1.0, 2.0, 3.0],
                        ThrustDirection = [1.0, 0.0, 0.0],
                        Gimballed = true,
                    },
                ],
                FeedNetwork = new FeedNetwork
                {
                    FuelResource = "fuel",
                    OxidizerResource = "oxidizer",
                    Branches = [new FeedBranch { EngineInstanceId = "side-01", MaximumFlowKgS = 100.0 }],
                },
            },
        };
        var part = new Part(definition, "synthetic-side-part");
        for (int i = 0; i < 150; i++)
            part.AdvanceEngineRuntime(1.0, 0.02);
        double thrust = part.GetThrustMagnitude(0.0);
        Assert.True(thrust > 0.0);

        var zeroGimbal = Assert.Single(part.GetEngineInstanceThrustGeometry(0.0));
        Assert.Equal(new Vector3d(1.0, 2.0, 3.0), zeroGimbal.PositionM);
        Assert.Equal(thrust, zeroGimbal.ThrustVectorN.X, 6);
        Assert.Equal(0.0, zeroGimbal.ThrustVectorN.Y, 6);
        Assert.Equal(0.0, zeroGimbal.ThrustVectorN.Z, 6);

        part.EngineStates[0].GimbalDeg = new Vector3d(15.0, 0.0, 0.0);
        var tilted = Assert.Single(part.GetEngineInstanceThrustGeometry(0.0));
        Assert.Equal(thrust, tilted.ThrustVectorN.Magnitude, 6);
        double dotWithBase = tilted.ThrustVectorN.Normalized.Dot(new Vector3d(1.0, 0.0, 0.0));
        Assert.True(dotWithBase < 1.0);
        Assert.True(dotWithBase > 0.9);
    }

    [Fact]
    public void RegressionSafety_ScalarPitchYawAndRollAuthorityUnchangedByNewTorqueApi()
    {
        // Pin/characterization test: proves the pre-existing scalar-lever methods were not
        // edited while adding the new geometric torque API. Values captured from an actual
        // run of this fixture.
        var graph = BuildSteadyBoosterGraph(out _);

        double pitchYaw = graph.GetPitchYawAngularAcceleration(SeaLevelPressure);
        double roll = graph.GetRollAngularAcceleration(SeaLevelPressure);

        Assert.Equal(PitchYawPinnedValue, pitchYaw, 6);
        Assert.Equal(RollPinnedValue, roll, 6);
    }

    // Filled in from an actual dotnet test run of RegressionSafety_* against this fixture;
    // see EngineTorqueTests build notes.
    private const double PitchYawPinnedValue = 0.22757019173609275;
    private const double RollPinnedValue = 0.7494644981175321;

    private static PartGraph BuildSteadyBoosterGraph(out Part booster)
    {
        var catalog = LoadPartCatalog();
        booster = new Part(catalog["super_heavy_booster"], "torque-test-booster");
        var graph = new PartGraph();
        graph.SetRoot(booster);
        for (int i = 0; i < 250; i++)
            booster.AdvanceEngineRuntime(1.0, 0.02);
        return graph;
    }

    private static EngineModelDefinition SyntheticModel() => new()
    {
        Id = "synthetic-side",
        Name = "Synthetic Side",
        Variant = "test",
        AsOfDate = new DateOnly(2026, 7, 28),
        Fuel = "fuel",
        Oxidizer = "oxidizer",
        MixtureRatioOxidizerToFuel = 2.0,
        RatedThrustSeaLevelN = 100.0,
        RatedThrustVacuumN = 140.0,
        SpecificImpulseSeaLevelS = 300.0,
        SpecificImpulseVacuumS = 320.0,
        MinimumThrottle = 0.0,
        MaximumThrottle = 1.0,
        StartupSeconds = 1.0,
        ShutdownSeconds = 1.0,
    };

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
