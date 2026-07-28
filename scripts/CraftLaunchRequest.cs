namespace Exosphere.Game;

using Exosphere.Simulation.Construction;

public sealed class LaunchIntent
{
    public string Mode { get; init; } = "sandbox";
    public string? MissionId { get; init; }
    public string? VehicleVariantId { get; init; }
    public string LaunchSiteId { get; init; } = "starbase";
    public string FlightProfileId { get; init; } = "manual";
    public string? SaveSlot { get; init; }
    public CraftDocumentV2? Craft { get; init; }
}

/// <summary>One-shot scene transition payload; replaces implicit Starship/Starbase globals.</summary>
public static class CraftLaunchRequest
{
    private static LaunchIntent? _pending;

    public static void Set(LaunchIntent intent) =>
        _pending = intent ?? throw new ArgumentNullException(nameof(intent));

    public static void Set(VesselCraftDefinition craft) =>
        Set(new LaunchIntent { Craft = CraftDocumentMigration.FromLegacy(craft) });

    public static LaunchIntent? Pop()
    {
        var intent = _pending;
        _pending = null;
        return intent;
    }
}
