namespace Exosphere.Simulation;

/// <summary>
/// Bounded renderer binding derived from an atmosphere's optional aerosol climate state.
/// The optical coefficients in <see cref="AtmosphereOptics"/> remain the legacy baseline;
/// this binding converts the configured 550 nm AOD into a Mie multiplier and carries the
/// Ångström exponent and vertical scale height to the sky shader.
/// </summary>
public readonly record struct AtmosphereAerosolOptics(
    bool Enabled,
    double MieScale,
    double AngstromExponent,
    double ScaleHeightMeters,
    double OpticalDepth550,
    double LatitudeFactor,
    double AltitudeFactor,
    double TemporalFactor,
    double SeasonalFactor)
{
    /// <summary>Upper bound for a malformed or very clear baseline Mie coefficient.</summary>
    public const double MaximumMieScale = 1_000.0;

    /// <summary>Legacy path: shader uniforms resolve to their pre-climate behaviour.</summary>
    public static AtmosphereAerosolOptics Legacy => new(
        false, 1.0, 1.0, 1_500.0, 0.0, 1.0, 1.0, 1.0, 1.0);

    /// <summary>
    /// Derives a bounded binding at the observer's latitude/altitude and simulation time.
    /// The Mie coefficient is normalised against the existing visible-band vertical Mie
    /// optical depth, so an AOD is not silently added on top of the configured baseline.
    /// </summary>
    public static AtmosphereAerosolOptics Resolve(
        AtmosphereModel atmosphere,
        double latitudeDegrees,
        double altitudeMeters,
        double timeSeconds)
    {
        ArgumentNullException.ThrowIfNull(atmosphere);
        var state = atmosphere.AerosolClimate;
        if (state is null) return Legacy;

        state = state.Normalize();
        // MieScale is a sea-level normalisation. The shader applies the configured
        // climate scale height along every view/light sample, so including observer
        // altitude here would attenuate the same column twice.
        var sample = state.Sample(latitudeDegrees, 0.0, timeSeconds);
        double baseline = VisibleMieVerticalOpticalDepth(atmosphere.Optics);
        double scale = baseline > 1.0e-12
            ? sample.OpticalDepth / baseline
            : 1.0;
        if (sample.OpticalDepth <= 0.0 && baseline > 1.0e-12) scale = 0.0;

        return new AtmosphereAerosolOptics(
            true,
            ClampFinite(scale, 1.0, 0.0, MaximumMieScale),
            ClampFinite(sample.AngstromExponent, 1.0, 0.0, 4.0),
            ClampFinite(state.AltitudeScaleHeightMeters, 1_500.0, 1.0, 1_000_000.0),
            ClampFinite(sample.OpticalDepth, 0.0, 0.0, AerosolClimateState.MaximumAod550),
            sample.LatitudeFactor,
            state.AltitudeFactor(altitudeMeters),
            sample.TemporalFactor,
            sample.SeasonalFactor);
    }

    private static double VisibleMieVerticalOpticalDepth(AtmosphereOptics optics)
    {
        double scaleHeight = double.IsFinite(optics.MieScaleHeight)
            ? System.Math.Max(0.0, optics.MieScaleHeight)
            : 0.0;
        // The green band is the 550 nm anchor. If a custom atmosphere omits it, use a
        // finite mean of the configured RGB extinction instead of inventing an AOD.
        double anchor = optics.MieExtinction.Y;
        if (!double.IsFinite(anchor) || anchor <= 0.0)
        {
            anchor = (optics.MieExtinction.X + optics.MieExtinction.Y
                + optics.MieExtinction.Z) / 3.0;
        }
        return double.IsFinite(anchor) && anchor > 0.0 ? anchor * scaleHeight : 0.0;
    }

    private static double ClampFinite(
        double value, double fallback, double minimum, double maximum) =>
        !double.IsFinite(value) ? fallback : System.Math.Clamp(value, minimum, maximum);
}
