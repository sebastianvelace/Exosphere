namespace Exosphere.Game;

using Exosphere.Simulation.Construction;

public static class CraftLaunchRequest
{
    private static VesselCraftDefinition? _pendingCraft;

    public static void Set(VesselCraftDefinition craft)
    {
        _pendingCraft = craft;
    }

    public static VesselCraftDefinition? Pop()
    {
        var craft = _pendingCraft;
        _pendingCraft = null;
        return craft;
    }
}
