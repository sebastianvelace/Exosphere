namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Parts;

/// <summary>
/// Structural / systems attitude authority after damage or breakup.
/// 1 = full, 0 = dead-stick (command gone or destroyed). Intermediate values mean
/// flaps-only or engines-without-flaps flight with reduced authority.
/// </summary>
public static class ControlAuthority
{
    public const double Full = 1.0;
    public const double EnginesOnly = 0.85;
    public const double FlapsOnly = 0.55;
    public const double CommandRemnant = 0.15;
    public const double None = 0.0;

    /// <summary>
    /// Hard control loss: no usable attitude authority. Autopilots and SAS must abort.
    /// </summary>
    public static bool IsLost(double factor) => factor <= 1e-6;

    /// <summary>
    /// Soft degradation: player may still fly, but cues / autopilot should be warned.
    /// </summary>
    public static bool IsDegraded(double factor) => factor > 1e-6 && factor < 0.99;

    public static double Evaluate(Vessel vessel)
    {
        if (vessel.IsDestroyed) return None;
        if (vessel.Parts.Parts.Count == 0) return None;

        bool hasCommand = false;
        bool hasFlaps = false;
        foreach (var part in vessel.Parts.Parts)
        {
            if (part.IsBroken) continue;
            if (part.Definition.Category == PartCategory.Command)
            {
                hasCommand = true;
                // Starship flaps live on the command section in this data model.
                if (part.Definition.Id == "starship_command")
                    hasFlaps = true;
            }
        }

        if (!hasCommand) return None;

        bool hasEngines = false;
        foreach (var _ in vessel.Parts.ActiveEngines)
        {
            hasEngines = true;
            break;
        }

        if (hasEngines && hasFlaps) return Full;
        if (hasEngines) return EnginesOnly;
        if (hasFlaps) return FlapsOnly;
        return CommandRemnant;
    }
}
