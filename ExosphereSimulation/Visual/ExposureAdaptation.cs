namespace Exosphere.Simulation.Visual;

/// <summary>
/// Deterministic approximation of human luminance adaptation. Moving into a bright
/// scene contracts sensitivity quickly; recovering sensitivity in darkness takes
/// substantially longer. Exposure is expressed as Godot's linear pre-tonemap multiplier,
/// while adaptation is integrated in log-exposure space: equal ratios (photographic stops)
/// produce equal perceptual steps.
/// </summary>
public sealed class ExposureAdaptation
{
    // A 4.6-stop range is required between a bright cloud/plasma field and the darkest
    // useful exterior view.  The former 0.65 floor could only close down by 0.62 stop,
    // which forced physically linear atmospheric radiance to clip before tonemapping.
    public const double MinimumExposure = 0.25;
    public const double MaximumExposure = 6.0;
    public const double BrightAdaptationSeconds = 0.68;
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
        // TonemapExposure is a linear multiplier.  Mapping luminance through sqrt here
        // under-compensated highlights (2x luminance produced only 0.707x exposure).
        return ClampExposure(MiddleGreyLuminance / safeLuminance);
    }

    public double Update(double targetExposure, double deltaSeconds)
    {
        targetExposure = ClampExposure(targetExposure);
        if (deltaSeconds <= 0.0) return CurrentExposure;

        double timeConstant = targetExposure < CurrentExposure
            ? BrightAdaptationSeconds
            : DarkAdaptationSeconds;
        double blend = 1.0 - System.Math.Exp(-deltaSeconds / timeConstant);

        // Weber-like visual adaptation is ratio-sensitive.  Interpolating the multiplier
        // itself made a dark-adapted 6x eye remain around 1.7x a full 1.2 s after entering
        // daylight, blowing a physically linear cloud field white.  Exponential relaxation
        // in log space preserves frame-partition independence and responds symmetrically
        // to equal changes measured in stops.
        double logCurrent = System.Math.Log(CurrentExposure);
        double logTarget = System.Math.Log(targetExposure);
        CurrentExposure = System.Math.Exp(logCurrent + (logTarget - logCurrent) * blend);
        return CurrentExposure;
    }

    private static double ClampExposure(double value) => System.Math.Clamp(
        double.IsFinite(value) ? value : 1.0, MinimumExposure, MaximumExposure);
}
