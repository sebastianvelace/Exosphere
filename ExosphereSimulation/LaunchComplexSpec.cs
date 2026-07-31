namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>
/// Physical dimensions of a launch complex in metres. The renderer converts to Godot units
/// only at its boundary; simulation placement and geometry therefore share one datum.
/// </summary>
public sealed record LaunchComplexSpec
{
    public required string Id { get; init; }
    public required string LaunchSiteId { get; init; }
    public double GradeElevation { get; init; }
    public double VehicleInterfaceElevation { get; init; }
    public double OlmOuterRadius { get; init; }
    public double OlmInnerClearRadius { get; init; }
    public double OlitEast { get; init; }
    public double OlitNorth { get; init; }
    public double OlitWidth { get; init; }
    public double OlitHeight { get; init; }
    public double LightningRodHeight { get; init; }
    public int CommodityTankCount { get; init; }
    public double CommodityTankMaxHeight { get; init; }

    /// <summary>Height of the chopstick-arm carriage above grade (m).</summary>
    public double ArmHeightM { get; init; }
    /// <summary>Horizontal reach of one chopstick arm out from the tower face (m).</summary>
    public double ArmReachM { get; init; }
    /// <summary>Half-spacing between the two arm centres, either side of the tower's
    /// north-south axis (m).</summary>
    public double ArmGapM { get; init; }

    /// <summary>
    /// FAA 2022 PEA baseline for Boca Chica Pad A/B scale, combined with the operational
    /// water-cooled deflector configuration. Exact proprietary construction drawings are
    /// unavailable; radii/offsets not stated by FAA remain documented reconstruction values.
    /// </summary>
    public static LaunchComplexSpec StarbasePostDeluge { get; } = new()
    {
        Id = "starbase_post_deluge",
        LaunchSiteId = "starbase",
        GradeElevation = 0.0,
        VehicleInterfaceElevation = 65.0 * 0.3048,
        OlmOuterRadius = 10.5,
        OlmInnerClearRadius = 4.72,
        OlitEast = -35.0,
        OlitNorth = 0.0,
        OlitWidth = 12.0,
        OlitHeight = 480.0 * 0.3048,
        LightningRodHeight = 10.0 * 0.3048,
        CommodityTankCount = 15,
        CommodityTankMaxHeight = 100.0 * 0.3048,
        ArmHeightM = 55.0,
        ArmReachM = 30.0,
        ArmGapM = 6.25,
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var values = new[] { GradeElevation, VehicleInterfaceElevation, OlmOuterRadius,
            OlmInnerClearRadius, OlitEast, OlitNorth, OlitWidth, OlitHeight,
            LightningRodHeight, CommodityTankMaxHeight, ArmHeightM, ArmReachM, ArmGapM };
        if (values.Any(v => !double.IsFinite(v))) errors.Add("NON_FINITE_DIMENSION");
        if (VehicleInterfaceElevation <= GradeElevation) errors.Add("INVALID_INTERFACE_DATUM");
        if (OlmOuterRadius <= OlmInnerClearRadius || OlmInnerClearRadius <= 4.5)
            errors.Add("INVALID_OLM_CLEARANCE");
        if (OlitWidth <= 0.0 || OlitHeight <= 0.0) errors.Add("INVALID_OLIT");
        if (CommodityTankCount <= 0 || CommodityTankMaxHeight <= 0.0)
            errors.Add("INVALID_TANK_FARM");
        if (ArmHeightM <= GradeElevation || ArmHeightM >= OlitHeight)
            errors.Add("INVALID_ARM_HEIGHT");
        if (ArmReachM <= 0.0 || ArmGapM <= 0.0) errors.Add("INVALID_ARM_GEOMETRY");
        return errors;
    }

    /// <summary>
    /// Inertial position of the catch cradle — the midpoint between the two chopstick arms,
    /// where a returning ship's catch pins must arrive. Derived from the pad's own local
    /// East/Up/North frame (<see cref="LaunchSite.GetLocalFrame(CelestialBody, double)"/>)
    /// so this stays consistent with every other pad-relative placement (vessel spawn,
    /// cameras) instead of introducing a second notion of "east" for the tower.
    /// </summary>
    public Vector3d GetCatchCradlePosition(LaunchSite site, CelestialBody body, double simulationTime)
    {
        var pad = site.GetPosition(body, simulationTime);
        var frame = site.GetLocalFrame(body, simulationTime);
        double eastOffset = OlitEast + OlitWidth * 0.5 + ArmReachM * 0.5;
        return pad + frame.East * eastOffset + frame.North * OlitNorth + frame.Up * ArmHeightM;
    }
}
