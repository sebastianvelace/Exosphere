namespace Exosphere.Simulation;

/// <summary>Low-rise VLA support building in local East/South metres.</summary>
public sealed record CampusBuildingSpec(
    string Id, string Use, double East, double South,
    double Width, double Depth, double Height);

/// <summary>
/// Reconstructed near-pad support campus. FAA sources establish facility categories and a
/// sub-30-foot support-building envelope; exact footprints/coordinates are visual estimates.
/// </summary>
public static class LaunchSupportCampusSpec
{
    public const double FaaLowRiseLimitM = 30.0 * 0.3048;

    public static IReadOnlyList<CampusBuildingSpec> StarbasePostDeluge { get; } =
    [
        new("support_a", "Maintenance and operations", -62,  74, 26, 14, 7.5),
        new("support_b", "Electrical workshop",       -32,  78, 20, 12, 6.0),
        new("emergency", "Emergency response garage", -65,  98, 18, 12, 6.5),
        new("gatehouse", "Security gatehouse",        -99, 112,  8,  5, 3.5),
        new("power_hall", "Site power plant",          -70, -58, 34, 18, 8.5),
        new("pump_house", "Fire water pump house",      68, -42, 12,  8, 5.0),
        new("desalination", "Water treatment",          94, -62, 28, 16, 7.0),
        new("lng_pretreat", "LNG pretreatment",        122,  46, 24, 12, 7.8),
        new("liquefier", "Methane liquefier",          138,  70, 28, 16, 8.8),
        new("tank_control", "Tank farm control",       116,  94, 14,  8, 5.0),
    ];

    public static IReadOnlyList<string> Validate(IReadOnlyList<CampusBuildingSpec> buildings)
    {
        var errors = new List<string>();
        foreach (var b in buildings)
        {
            double[] values = [b.East, b.South, b.Width, b.Depth, b.Height];
            if (values.Any(v => !double.IsFinite(v)) || b.Width <= 0 || b.Depth <= 0 || b.Height <= 0)
                errors.Add($"INVALID_DIMENSION:{b.Id}");
            if (b.Height >= FaaLowRiseLimitM) errors.Add($"FAA_HEIGHT_LIMIT:{b.Id}");
            if (System.Math.Sqrt(b.East * b.East + b.South * b.South) < 30.0)
                errors.Add($"PAD_HAZARD_CLEARANCE:{b.Id}");
        }

        for (int i = 0; i < buildings.Count; i++)
        for (int j = i + 1; j < buildings.Count; j++)
        {
            var a = buildings[i]; var b = buildings[j];
            bool overlap = System.Math.Abs(a.East - b.East) < (a.Width + b.Width) * 0.5
                && System.Math.Abs(a.South - b.South) < (a.Depth + b.Depth) * 0.5;
            if (overlap) errors.Add($"BUILDING_OVERLAP:{a.Id}:{b.Id}");
        }
        return errors.OrderBy(e => e, StringComparer.Ordinal).ToArray();
    }
}
