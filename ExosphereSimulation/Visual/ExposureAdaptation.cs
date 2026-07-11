namespace Exosphere.Simulation.Visual;

/// <summary>
/// Deterministic approximation of human luminance adaptation. Moving into a bright
/// scene contracts sensitivity quickly; recovering sensitivity in darkness takes
/// substantially longer. Exposure is expressed as Godot's linear pre-tonemap multiplier.
/// </summary>
public sealed class ExposureAdaptation
{
    public const double MinimumExposure = 0.65;
    public const double MaximumExposure = 6.0;
    public const double BrightAdaptationSeconds = 0.70;
    public const double DarkAdaptationSeconds = 9.0;
    public const double MiddleGreyLuminance = 0.18;

    public double CurrentExposure { get; private set; }

    public ExposureAdaptation(double initialExposure = 1.0)
    {
        CurrentExposure = ClampExposure(initialExposure);
    }

    /// <summary>Maps a relative scene-luminance estimate to a photographic exposure.</summary>
    public static double TargetForLuminance(double sceneLuminance)
    {
        double safeLuminance = System.Math.Max(sceneLuminance, 1e-6);
        return ClampExposure(System.Math.Sqrt(MiddleGreyLuminance / safeLuminance));
    }

    public double Update(double targetExposure, double deltaSeconds)
    {
        targetExposure = ClampExposure(targetExposure);
        if (deltaSeconds <= 0.0) return CurrentExposure;

        double timeConstant = targetExposure < CurrentExposure
            ? BrightAdaptationSeconds
            : DarkAdaptationSeconds;
        double blend = 1.0 - System.Math.Exp(-deltaSeconds / timeConstant);
        CurrentExposure += (targetExposure - CurrentExposure) * blend;
        return CurrentExposure;
    }

    private static double ClampExposure(double value) => System.Math.Clamp(
        double.IsFinite(value) ? value : 1.0, MinimumExposure, MaximumExposure);
}
