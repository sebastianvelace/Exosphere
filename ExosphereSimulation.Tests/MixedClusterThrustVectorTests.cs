namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// R5d — <see cref="Part.GetThrustVector"/> must sum per-mount thrust vectors so a
/// mixed fixed/gimballed cluster does not dilute fixed engines' axial thrust by
/// averaging gimbal across the whole cluster.
/// </summary>
public sealed class MixedClusterThrustVectorTests
{
    private const double SeaLevelPressure = 101_325.0;
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void ShipMixedCluster_GetThrustVectorEqualsSumOfPerMountGeometry()
    {
        var ship = BuildSteadyShipEngines();
        DeflectGimballedMountsOnly(ship, gimbalZDeg: 8.0);

        var expected = SumGeometry(ship, SeaLevelPressure);
        var actual = ship.GetThrustVector(SeaLevelPressure);

        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
        Assert.Equal(expected.Z, actual.Z, 6);
    }

    [Fact]
    public void ShipMixedCluster_FixedEnginesKeepFullAxialContributionUnderSteer()
    {
        var ship = BuildSteadyShipEngines();
        DeflectGimballedMountsOnly(ship, gimbalZDeg: 10.0);

        double fixedThrustSum = 0.0;
        double fixedLateral = 0.0;
        var cluster = ship.Definition.ResolvedEngineCluster!;
        var geometry = ship.GetEngineInstanceThrustGeometry(SeaLevelPressure).ToList();
        for (int i = 0; i < ship.EngineStates.Count; i++)
        {
            if (cluster.Engines[i].Gimballed) continue;
            var f = geometry[i].ThrustVectorN;
            fixedThrustSum += f.Magnitude;
            fixedLateral += System.Math.Abs(f.X) + System.Math.Abs(f.Z);
            // Fixed mounts stay untilted: their full magnitude is axial (+Y).
            Assert.Equal(f.Magnitude, f.Y, 6);
        }

        Assert.Equal(0.0, fixedLateral, 6);
        double axial = ship.GetThrustVector(SeaLevelPressure).Y;
        Assert.True(axial + 1.0 >= fixedThrustSum,
            $"Fixed engines must keep axial thrust; axial={axial}, fixedSum={fixedThrustSum}");

        // Pre-R5d tilted the entire ΣT by the diluted average angle, so fixed engines
        // contributed lateral force they physically cannot. Net lateral must come only
        // from the gimballed half.
        double netLateral = System.Math.Abs(ship.GetThrustVector(SeaLevelPressure).Z);
        double gimballedLateral = 0.0;
        for (int i = 0; i < ship.EngineStates.Count; i++)
        {
            if (cluster.Engines[i].Gimballed)
                gimballedLateral += System.Math.Abs(geometry[i].ThrustVectorN.Z);
        }
        Assert.Equal(gimballedLateral, netLateral, 3);
    }

    [Fact]
    public void ShipMixedCluster_NetMagnitudeDropsBelowScalarSumWhenGimbalsDiverge()
    {
        var ship = BuildSteadyShipEngines();
        DeflectGimballedMountsOnly(ship, gimbalZDeg: 12.0);

        double scalar = ship.GetThrustMagnitude(SeaLevelPressure);
        double net = ship.GetThrustVector(SeaLevelPressure).Magnitude;

        Assert.True(net < scalar - 1.0,
            $"Divergent mount directions must lose net magnitude: |F|={net}, ΣT={scalar}");
        Assert.InRange(net / scalar, 0.90, 0.9999);
    }

    [Fact]
    public void UniformGimbalAcrossAllMounts_PreservesSingleTiltMagnitude()
    {
        var ship = BuildSteadyShipEngines();
        foreach (var state in ship.EngineStates)
        {
            if (state.ChamberPressureFraction <= 1e-3) continue;
            state.GimbalDeg = new Vector3d(0.0, 0.0, 6.0);
        }

        double scalar = ship.GetThrustMagnitude(SeaLevelPressure);
        var vector = ship.GetThrustVector(SeaLevelPressure);
        Assert.Equal(scalar, vector.Magnitude, 4);
        Assert.True(System.Math.Abs(vector.Z) > 1.0);
    }

    private static Part BuildSteadyShipEngines()
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(Root.FullName, "data", "parts"));
        var ship = new Part(catalog["starship_engines"], "ship-engines-r5d");
        ship.IsStagingActive = true;
        ship.ThrottleLevel = 1.0;
        ship.SelectEngineCount(6);
        for (int i = 0; i < 250; i++)
            ship.AdvanceEngineRuntime(1.0, 0.02);
        Assert.Equal(6, ship.EngineStates.Count(s => s.ChamberPressureFraction > 1e-3));
        Assert.Contains(false, ship.Definition.ResolvedEngineCluster!.Engines.Select(e => e.Gimballed));
        Assert.Contains(true, ship.Definition.ResolvedEngineCluster!.Engines.Select(e => e.Gimballed));
        return ship;
    }

    private static void DeflectGimballedMountsOnly(Part ship, double gimbalZDeg)
    {
        var cluster = ship.Definition.ResolvedEngineCluster!;
        for (int i = 0; i < ship.EngineStates.Count; i++)
        {
            ship.EngineStates[i].GimbalDeg = cluster.Engines[i].Gimballed
                ? new Vector3d(0.0, 0.0, gimbalZDeg)
                : Vector3d.Zero;
        }
    }

    private static Vector3d SumGeometry(Part ship, double pressure)
    {
        var sum = Vector3d.Zero;
        foreach (var (_, thrust) in ship.GetEngineInstanceThrustGeometry(pressure))
            sum += thrust;
        return sum;
    }

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
