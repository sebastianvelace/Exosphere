namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Propulsion;

public sealed class Falcon9Block5Tests
{
    [Fact]
    public void PublishedMerlinRatingsMatchMay2025Preset()
    {
        var catalog = LoadCatalog();

        Assert.Equal(845_000.0, catalog["engine_liquid_sl"].ThrustSL);
        Assert.Equal(845_000.0 * 9.0, catalog["merlin1d_cluster9_block5"].ThrustSL);
        Assert.Equal(981_000.0, catalog["merlin1d_vac_block5"].ThrustVac);
    }

    [Theory]
    [InlineData("falcon9_block5_standard_2025.json", "standard")]
    [InlineData("falcon9_block5_extended_2025.json", "extended")]
    public void DatedFalconVariantBuildsAFlightReadyStableStack(
        string fileName, string fairingVariant)
    {
        var variant = VehicleVariantDefinition.LoadFromJson(
            Path.Combine(Root.FullName, "data", "vehicles", fileName));
        var assembly = variant.Build(LoadCatalog());
        var metrics = assembly.ComputeMetrics();

        Assert.Equal(new DateOnly(2025, 5, 9), variant.AsOfDate);
        Assert.Equal(fairingVariant, variant.FairingVariant);
        Assert.Equal(7, assembly.Parts.Count);
        Assert.True(assembly.ValidateForLaunch().CanLaunch);
        Assert.InRange(metrics.WetMass, 550_000, 560_000);
        Assert.InRange(metrics.SeaLevelTwr, 1.35, 1.45);
        Assert.Equal(7_605_000, metrics.SeaLevelThrust);
        Assert.Equal(
            assembly.Parts.Select(p => p.InstanceId).Distinct().Count(),
            assembly.Parts.Count);
    }

    [Fact]
    public void FalconPerformanceFieldsHaveMandatoryProvenance()
    {
        var registry = DataProvenanceRegistry.LoadFromDirectory(
            Path.Combine(Root.FullName, "data", "provenance"));

        registry.RequireFields("merlin1d_cluster9_block5",
            "thrust_sl", "thrust_vac", "isp_sl", "isp_vac", "mass_dry");
        registry.RequireFields("merlin1d_vac_block5",
            "thrust_vac", "thrust_sl", "isp_vac", "mass_dry");
        registry.RequireFields("falcon9_first_stage_tank",
            "propellant_mass", "mass_dry");
        registry.RequireFields("falcon9_second_stage_tank",
            "propellant_mass", "mass_dry");
        registry.RequireFields("falcon9_fairing_standard", "envelope", "mass_dry");
        registry.RequireFields("falcon9_fairing_extended", "envelope", "mass_dry");

        var published = registry.Require("merlin1d_vac_block5", "thrust_vac");
        Assert.Equal(ProvenanceStatus.Published, published.Status);
        Assert.False(string.IsNullOrWhiteSpace(published.Uncertainty));
        Assert.Contains("2025", published.Source);
    }

    [Fact]
    public void PayloadDeploymentConservesMassCenterAndMomentumAndBecomesControllable()
    {
        var variant = VehicleVariantDefinition.LoadFromJson(
            Path.Combine(Root.FullName, "data", "vehicles",
                "falcon9_block5_standard_2025.json"));
        var carrier = variant.Build(LoadCatalog()).ToVessel("Falcon 9");
        carrier.Position = new Vector3d(100, 200, 300);
        carrier.Velocity = new Vector3d(10, 20, 30);
        var payloadPart = carrier.Parts.Root!;
        double originalMass = carrier.TotalMass;
        Vector3d originalCenter = carrier.Position;
        Vector3d originalMomentum = carrier.Velocity * originalMass;

        var payload = carrier.DeployPayload(
            payloadPart.InstanceId, "Payload Simulator", Vector3d.Up * 0.5);

        Assert.NotNull(payload);
        Assert.Equal(payloadPart.InstanceId, payload!.Parts.Root!.InstanceId);
        Assert.Equal(originalMass, carrier.TotalMass + payload.TotalMass, 8);
        Vector3d combinedCenter =
            (carrier.Position * carrier.TotalMass + payload.Position * payload.TotalMass)
            / originalMass;
        Vector3d combinedMomentum =
            carrier.Velocity * carrier.TotalMass + payload.Velocity * payload.TotalMass;
        Assert.True(combinedCenter.DistanceTo(originalCenter) < 1e-9);
        Assert.True(combinedMomentum.DistanceTo(originalMomentum) < 1e-6);

        var universe = new Universe();
        universe.AddVessel(carrier);
        universe.AddVessel(payload);
        Assert.True(universe.SetActiveVessel(payload.Id));
        Assert.Same(payload, universe.ActiveVessel);
    }

    [Fact]
    public void MerlinAcceptanceStandIsDeterministicAndPreservesThrustFlowIspIdentity()
    {
        var definition = LoadCatalog()["merlin1d_cluster9_block5"];
        var profile = EngineTestProfile.Merlin1DAcceptance();

        var first = EngineTestStand.Run(definition, profile, 0.02);
        var second = EngineTestStand.Run(definition, profile, 0.02);

        Assert.True(first.Passed);
        Assert.Equal(first.TotalImpulseNs, second.TotalImpulseNs);
        Assert.Equal(first.PropellantConsumedKg, second.PropellantConsumedKg);
        Assert.Equal(definition.ThrustSL, first.PeakThrustN, 6);
        Assert.Contains(first.Telemetry, t => t.Phase == EngineTestPhase.Preparation);
        Assert.Contains(first.Telemetry, t => t.Phase == EngineTestPhase.Chill);
        Assert.Contains(first.Telemetry, t => t.Phase == EngineTestPhase.Ignition);
        Assert.Contains(first.Telemetry, t => t.Phase == EngineTestPhase.Shutdown);
        Assert.Equal(
            definition.IspSL,
            first.TotalImpulseNs / (first.PropellantConsumedKg * 9.80665),
            8);
    }

    private static PartCatalog LoadCatalog() =>
        PartCatalog.LoadFromDirectory(Path.Combine(Root.FullName, "data", "parts"));

    private static DirectoryInfo Root
    {
        get
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln"))) return dir;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Repository root not found.");
        }
    }
}
