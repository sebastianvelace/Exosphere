namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

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
    /// <summary>Clearance between a caught vehicle's lowest point and the top of the launch
    /// mount (m). The carriage rides up the tower so the vehicle hangs above the mount.</summary>
    public double CatchTailClearanceM { get; init; }
    /// <summary>Highest carriage position, measured down from the top of the tower (m).</summary>
    public double ArmTopMarginM { get; init; }

    /// <summary>Default carriage clearance above the mount for a caught vehicle (m).</summary>
    public const double StarbaseCatchTailClearanceM = 5.0;
    /// <summary>Half the gap between the open arms, measured from the cradle centreline (m).</summary>
    public double ArmOpenHalfGapM { get; init; }
    /// <summary>Width of the catch rail on top of each arm (m).</summary>
    public double ArmRailWidthM { get; init; }
    /// <summary>Half-length of the catch zone along each arm, about the cradle (m).</summary>
    public double ArmCatchZoneHalfLengthM { get; init; }
    public const double StarbaseArmOpenHalfGapM = 10.0;
    public const double StarbaseArmRailWidthM = 1.5;

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
        CatchTailClearanceM = StarbaseCatchTailClearanceM,
        ArmTopMarginM = 15.0,
        ArmOpenHalfGapM = StarbaseArmOpenHalfGapM,
        ArmRailWidthM = StarbaseArmRailWidthM,
        ArmCatchZoneHalfLengthM = 9.5,
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var values = new[] { GradeElevation, VehicleInterfaceElevation, OlmOuterRadius,
            OlmInnerClearRadius, OlitEast, OlitNorth, OlitWidth, OlitHeight,
            LightningRodHeight, CommodityTankMaxHeight, ArmHeightM, ArmReachM, ArmGapM,
            CatchTailClearanceM, ArmTopMarginM, ArmOpenHalfGapM, ArmRailWidthM, ArmCatchZoneHalfLengthM };
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
        if (CatchTailClearanceM < 0.0 || ArmTopMarginM < 0.0
            || OlitHeight - ArmTopMarginM <= ArmHeightM)
            errors.Add("INVALID_CATCH_CARRIAGE");
        if (ArmOpenHalfGapM <= 0.0 || ArmRailWidthM <= 0.0 || ArmCatchZoneHalfLengthM <= 0.0
            || ArmCatchZoneHalfLengthM > 0.5 * ArmReachM)
            errors.Add("INVALID_CATCH_RAILS");
        return errors;
    }

    /// <summary>
    /// Carriage height above grade for a vehicle whose pin contact surface sits
    /// <paramref name="pinContactHeightAboveDatumM"/> above its lowest point. The arms ride
    /// up the tower until the vehicle hangs <see cref="CatchTailClearanceM"/> above the
    /// mount; a ship with its fittings under the forward flaps is caught far higher than
    /// the idle <see cref="ArmHeightM"/>. The tower top bounds the travel.
    /// </summary>
    public double GetCatchCarriageHeightM(double pinContactHeightAboveDatumM)
    {
        if (!double.IsFinite(pinContactHeightAboveDatumM) || pinContactHeightAboveDatumM < 0.0)
            throw new ArgumentOutOfRangeException(nameof(pinContactHeightAboveDatumM));
        double required = VehicleInterfaceElevation + CatchTailClearanceM + pinContactHeightAboveDatumM;
        return System.Math.Clamp(required, ArmHeightM, OlitHeight - ArmTopMarginM);
    }

    /// <summary>
    /// Inertial position of the catch cradle — the midpoint between the two chopstick arms,
    /// where a returning ship's catch pins must arrive. Derived from the pad's own local
    /// East/Up/North frame (<see cref="LaunchSite.GetLocalFrame(CelestialBody, double)"/>)
    /// so this stays consistent with every other pad-relative placement (vessel spawn,
    /// cameras) instead of introducing a second notion of "east" for the tower.
    /// </summary>
    public Vector3d GetCatchCradlePosition(LaunchSite site, CelestialBody body, double simulationTime) =>
        GetCatchCradlePosition(site, body, simulationTime, ArmHeightM);

    /// <summary>Cradle position with the carriage at <paramref name="carriageHeightM"/> above grade.</summary>
    public Vector3d GetCatchCradlePosition(
        LaunchSite site, CelestialBody body, double simulationTime, double carriageHeightM)
    {
        var pad = site.GetPosition(body, simulationTime);
        var frame = site.GetLocalFrame(body, simulationTime);
        double eastOffset = OlitEast + OlitWidth * 0.5 + ArmReachM * 0.5;
        return pad + frame.East * eastOffset + frame.North * OlitNorth + frame.Up * carriageHeightM;
    }

    /// <summary>The closing arm rails, running along the arms (east from the tower face).</summary>
    public CatchArmRails GetCatchArmRails(LaunchSite site, CelestialBody body, double simulationTime) =>
        new(site.GetLocalFrame(body, simulationTime).East, ArmCatchZoneHalfLengthM, ArmOpenHalfGapM, ArmRailWidthM);

    /// <summary>Cradle for a specific vessel: the carriage rides to that vessel's catch height.</summary>
    public Vector3d GetCatchCradlePosition(
        LaunchSite site, CelestialBody body, double simulationTime, Vessel vessel) =>
        GetCatchCradlePosition(site, body, simulationTime,
            vessel.HasCatchPins ? GetCatchCarriageHeightM(vessel.CatchPinContactHeightAboveDatumM) : ArmHeightM);
}
