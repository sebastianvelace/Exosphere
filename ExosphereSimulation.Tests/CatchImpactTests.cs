namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class CatchImpactTests
{
    private const double Gravity = 3.986e14 / (6.371e6 + 500.0) / (6.371e6 + 500.0);

    [Fact]
    public void UndampedDropFromRestOvershootsToTwiceTheStaticDeflection()
    {
        var p = CatchImpactPredictor.Predict(200_000.0, 9.81, 2.5e6, 0.0, 2, gapM: 0.0, closingSpeedMps: 0.0);

        double staticDeflection = 200_000.0 * 9.81 / (2 * 2.5e6);
        Assert.Equal(2.0 * staticDeflection, p.PeakCompressionM, 3);
        Assert.Equal(1.0, p.PeakPinLoadN / (2.5e6 * 2.0 * staticDeflection), 3);
    }

    [Theory]
    [InlineData(225_000.0, 2.5e6, 0.25, 0.65)]
    [InlineData(208_000.0, 2.5e6, 0.0, 0.40)]
    [InlineData(300_000.0, 4.2e6, 0.10, 0.40)]
    public void PredictionMatchesTheContactSolverDrop(double mass, double k, double gap, double closing)
    {
        var (universe, vessel) = CreateDrop(mass, k, gap, closing);
        double peakCompression = 0.0;
        for (int i = 0; i < 300; i++)
        {
            universe.Tick(0.002);
            if (vessel.LastCatchContact is { } c && c.Points.Count > 0)
                peakCompression = System.Math.Max(peakCompression, c.Points.Max(p => p.PenetrationM));
        }
        var pin = vessel.CatchContactPoints[0];

        var predicted = CatchImpactPredictor.Predict(mass, Gravity, k, pin.DampingNsPerM, 2, gap, closing);

        // The one-axis model omits the small pitch the off-centre pins induce: it stays
        // within 3 % of the rigid-body solver, inside the 10 % margin the cutoff keeps.
        Assert.InRange(predicted.PeakCompressionM / peakCompression, 0.97, 1.03);
        Assert.Equal(peakCompression, vessel.CatchPeakCompressionM, 6);
    }

    [Fact]
    public void RigidPairWithLevelPinsMatchesTheSymmetricPrediction()
    {
        var pair = new CatchPinPair(0.10, 0.10, -4.4, 4.4, 0.4, 0.4, 5.0e6);

        var rigid = CatchImpactPredictor.Predict(220_000.0, 9.81, 4.2e6, 1.0e5, pair);
        var symmetric = CatchImpactPredictor.Predict(220_000.0, 9.81, 4.2e6, 1.0e5, 2, 0.10, 0.4);

        Assert.Equal(symmetric.PeakCompressionM, rigid.PeakCompressionM, 6);
        Assert.Equal(symmetric.PeakPinLoadN, rigid.PeakPinLoadN, 0);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(-1.5)]
    public void TiltedDropMatchesTheContactSolverWhereTheSymmetricFormUnderstatesIt(double tiltDegrees)
    {
        const double mass = 220_000.0, k = 4.2e6, closing = 0.4;
        var (universe, vessel) = CreateDrop(mass, k, 0.0, closing, tiltDegrees);
        var cradle = vessel.CatchTargetPositionWorld;
        var pair = CatchPinPair.Measure(vessel, cradle, Vector3d.Zero, Vector3d.Up);
        var pin = vessel.CatchContactPoints[0];
        double lowestGap = System.Math.Min(pair.GapAM, pair.GapBM);
        double peakCompression = 0.0;
        for (int i = 0; i < 600; i++)
        {
            universe.Tick(0.002);
            if (vessel.LastCatchContact is { } c && c.Points.Count > 0)
                peakCompression = System.Math.Max(peakCompression, c.Points.Max(p => p.PenetrationM));
        }

        var rigid = CatchImpactPredictor.Predict(mass, Gravity, k, pin.DampingNsPerM, pair);
        var symmetric = CatchImpactPredictor.Predict(mass, Gravity, k, pin.DampingNsPerM, 2, lowestGap, closing);

        Assert.True(System.Math.Abs(pair.GapBM - pair.GapAM) > 0.1, "the tilt must stagger the pins");
        Assert.InRange(rigid.PeakCompressionM / peakCompression, 0.97, 1.05);
        Assert.True(symmetric.PeakCompressionM < 0.95 * peakCompression,
            $"symmetric {symmetric.PeakCompressionM:F3} m vs solver {peakCompression:F3} m");
    }

    [Fact]
    public void CutoffWaitsWhenTheDropWouldReachThePinTravelLimit()
    {
        var pins = Pins(2.5e6);
        var geometry = new TowerCatchPinGeometry(2, 0.25, 0.25, 4.4);

        var decision = TowerCatchCutoff.Decide(geometry, pins, 225_000.0, 9.81, -0.65, 0.0, 0.0);

        Assert.False(decision.Commit);
        Assert.Equal("predicted travel", decision.Reason);
        Assert.True(decision.Prediction.PeakCompressionM > 0.9 * pins[0].MaxCompressionM);
    }

    [Fact]
    public void CutoffAcceptsTheSlowRollOfADrainingShip()
    {
        var geometry = new TowerCatchPinGeometry(2, 0.02, 0.03, 4.45);

        var decision = TowerCatchCutoff.Decide(geometry, Pins(2.5e6), 210_000.0, 9.81, -0.40, 0.10, 0.01, 0.06);

        Assert.True(decision.Commit, decision.Reason);
    }

    [Fact]
    public void CutoffCommitsAtFirstContactForASlowCentredApproach()
    {
        var pins = Pins(2.5e6);
        var geometry = new TowerCatchPinGeometry(2, 0.02, 0.03, 4.45);

        var decision = TowerCatchCutoff.Decide(geometry, pins, 210_000.0, 9.81, -0.40, 0.10, 0.01);

        Assert.True(decision.Commit, decision.Reason);
        Assert.True(decision.Prediction.PeakCompressionM <= 0.9 * pins[0].MaxCompressionM);
    }

    [Theory]
    [InlineData(4.95, 0.0, 0.0, "pin range")]
    [InlineData(4.85, 0.35, 0.0, "pin range")]
    [InlineData(4.45, 0.0, 0.08, "angular rate")]
    [InlineData(4.45, 0.50, 0.0, "lateral speed")]
    [InlineData(4.45, 0.0, 0.0, "roll rate", 0.2)]
    public void CutoffRejectsAnApproachThatCannotFinishInsideTheCradle(
        double range, double lateralSpeed, double angularRate, string reason, double rollRate = 0.0)
    {
        var geometry = new TowerCatchPinGeometry(2, 0.20, 0.25, range);

        var decision = TowerCatchCutoff.Decide(geometry, Pins(2.5e6), 150_000.0, 9.81, -0.2,
            lateralSpeed, angularRate, rollRate);

        Assert.False(decision.Commit);
        Assert.Equal(reason, decision.Reason);
    }

    [Theory]
    [InlineData("starship_v3_command_flight12.json", 225_000.0)]
    [InlineData("super_heavy_v3_booster_flight12.json", 300_000.0)]
    [InlineData("super_heavy_booster.json", 300_000.0)]
    public void DeclaredPinStiffnessSettlesItsCatchEnvelopeInsideTheLimits(string file, double massKg)
    {
        var definition = PartDefinition.LoadFromJson(Path.Combine(RepoRoot(), "data", "parts", file));
        var (universe, vessel) = CreateDrop(massKg, definition.CatchPinSpringStiffnessNPerM, 0.0, 0.40);
        for (int i = 0; i < 300 && !vessel.IsCaught; i++) universe.Tick(0.02);

        Assert.True(vessel.IsCaught);
        Assert.False(vessel.CatchLimitExceeded);
        Assert.True(vessel.CatchPeakCompressionM <= 0.9 * vessel.CatchContactPoints[0].MaxCompressionM);
    }

    private static ContactPointDefinition[] Pins(double k)
    {
        var (_, vessel) = CreateDrop(200_000.0, k, 0.0, 0.0);
        return vessel.CatchContactPoints.ToArray();
    }

    private static (Universe universe, Vessel vessel) CreateDrop(double massKg, double k, double gap, double closing,
        double tiltDegrees = 0.0)
    {
        var body = new CelestialBody { Id = "catch-drop-body", Mass = 5.972e24, Radius = 6.371e6, GM = 3.986e14 };
        var vessel = new Vessel { ReferenceBodyId = body.Id, SASEnabled = false };
        var nose = new Part(new PartDefinition
        {
            Id = "catch-drop-nose", CategoryStr = "command", MassDry = massKg, LengthM = 19.0, DiameterM = 9.0,
            CatchPinLateralOffsetM = 4.4, CatchPinOffsetYM = 0.0, CatchPinRadiusM = 0.4,
            CatchPinSpringStiffnessNPerM = k,
        });
        vessel.Parts.SetRoot(nose);
        vessel.Parts.AddPart(nose);
        vessel.ConfigureCatchContactsFromParts();
        var up = Vector3d.Up;
        var cradle = body.Position + up * (body.Radius + 500.0);
        // Tilt about the axis across the pin line, so the pins (body ±Z) reach the cradle
        // at different heights; the lowest pin starts at the requested gap.
        vessel.Orientation = Quaterniond.FromAxisAngle(Vector3d.Right, tiltDegrees * System.Math.PI / 180.0);
        double lowest = vessel.CatchContactPoints.Min(p =>
            vessel.Orientation.Rotate(p.LocalPositionFromDatum).Dot(up) - p.ContactRadiusM);
        vessel.Position = cradle + up * (gap - lowest);
        vessel.Velocity = -up * closing;
        vessel.IsAttemptingTowerCatch = true;
        vessel.CatchTargetPositionWorld = cradle;
        vessel.CatchTargetUpWorld = up;
        vessel.CatchTargetVelocityWorld = Vector3d.Zero;
        var universe = new Universe { TimeScale = 1.0, ActiveVessel = vessel };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return (universe, vessel);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
