namespace Exosphere.Game;

/// <summary>Launchable catalog entries and compatible home pads. Independent spacecraft
/// (LM and Agena) belong to their missions/VAB, rather than a standalone pad launch.</summary>
public sealed record MenuLauncher(string VariantFile, string SiteId, string SiteLabel);

public static class FlightMenuCatalog
{
    public static IReadOnlyList<MenuLauncher> Launchers { get; } = Array.AsReadOnly(new[]
    {
        new MenuLauncher("starship_flight12_v3_2026.json", "starbase_pad2", "STARBASE / PAD 2"),
        new MenuLauncher("starship_flight7_block2_2025.json", "starbase", "STARBASE / PAD 1"),
        new MenuLauncher("falcon9_block5_standard_2025.json", "kennedy", "KENNEDY / LC-39A"),
        new MenuLauncher("falcon9_block5_extended_2025.json", "kennedy", "KENNEDY / LC-39A"),
        new MenuLauncher("newglenn_7x2_public_2026.json", "cape_canaveral_lc36", "CAPE CANAVERAL / LC-36"),
        new MenuLauncher("mercury_redstone3_freedom7_1961.json", "cape_canaveral_lc5", "CAPE CANAVERAL / LC-5"),
        new MenuLauncher("mercury_atlas6_friendship7_1962.json", "cape_canaveral_lc14", "CAPE CANAVERAL / LC-14"),
        new MenuLauncher("gemini8_titan2_1966.json", "cape_canaveral_lc19", "CAPE CANAVERAL / LC-19"),
        new MenuLauncher("apollo8_saturn5_as503_1968.json", "kennedy", "KENNEDY / LC-39A"),
        new MenuLauncher("apollo11_saturn5_as506_1969.json", "kennedy", "KENNEDY / LC-39A"),
    });
}
