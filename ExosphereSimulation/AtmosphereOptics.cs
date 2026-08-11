namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>
/// Visible-spectrum optical profile for a planetary atmosphere. Scattering and absorption
/// coefficients are RGB-band extinction coefficients at the surface in m⁻¹. Scale heights
/// describe the exponential vertical distributions used by the renderer and diagnostics.
/// </summary>
public sealed record AtmosphereOptics
{
    public Vector3d RayleighScattering { get; init; } = Vector3d.Zero;
    public Vector3d MieScattering { get; init; } = Vector3d.Zero;
    public Vector3d MieAbsorption { get; init; } = Vector3d.Zero;
    public Vector3d OzoneAbsorption { get; init; } = Vector3d.Zero;
    public double RayleighScaleHeight { get; init; } = 8_000.0;
    public double MieScaleHeight { get; init; } = 1_200.0;
    public double OzoneCenterAltitude { get; init; } = 25_000.0;
    public double OzoneHalfWidth { get; init; } = 15_000.0;
    /// <summary>
    /// Visible upper-atmosphere chemiluminescence at the layer peak, in relative radiance
    /// emitted per metre.  It is intentionally independent of solar scattering: airglow
    /// remains present on the night limb after direct sunlight has vanished.
    /// </summary>
    public Vector3d AirglowEmission { get; init; } = Vector3d.Zero;
    public double AirglowCenterAltitude { get; init; } = 97_000.0;
    public double AirglowScaleHeight { get; init; } = 6_000.0;
    public double MieAnisotropy { get; init; } = 0.80;
    public double SunIlluminanceScale { get; init; } = 20.0;
    /// <summary>
    /// Surface refractivity (n − 1) at the reference pressure/temperature of the profile.
    /// It is dimensionless; Earth air at sea level is approximately 2.77e−4.
    /// </summary>
    public double SurfaceRefractivity { get; init; } = 2.77e-4;
    /// <summary>Scale height used by the bounded visible-horizon refraction approximation.</summary>
    public double RefractiveScaleHeight { get; init; } = 8_000.0;
    /// <summary>Bounded isotropic second-order fill used by the realtime sky integrator.</summary>
    public double LowOrderDiffuseStrength { get; init; } = 0.25;
    public double CloudBaseAltitude { get; init; } = 0.0;
    public double CloudTopAltitude { get; init; } = 0.0;
    public double CloudExtinction { get; init; } = 0.0;
    public double CloudCoverage { get; init; } = 0.0;
    public double CloudWindRadiansPerSecond { get; init; } = 0.0;

    /// <summary>
    /// Approximate upward displacement of a geometric horizon ray in radians.  The expression
    /// follows the exponential spherical-gradient integral and is deliberately capped: it is
    /// used for the apparent solar disc, not as a substitute for a full refractive ray tracer.
    /// </summary>
    public double HorizonRefractionRadians(double altitude, double planetRadius)
    {
        if (!double.IsFinite(altitude) || !double.IsFinite(planetRadius)
            || !double.IsFinite(SurfaceRefractivity) || !double.IsFinite(RefractiveScaleHeight)
            || planetRadius <= 0.0 || SurfaceRefractivity <= 0.0
            || RefractiveScaleHeight <= 0.0)
            return 0.0;

        double density = System.Math.Exp(-System.Math.Max(0.0, altitude)
            / RefractiveScaleHeight);
        double sphericalGradient = System.Math.Sqrt(
            2.0 * System.Math.PI * planetRadius / RefractiveScaleHeight);
        // The 0.5 factor accounts for the finite temperature/lapse profile versus
        // the isothermal exponential envelope used by the optical profile.
        return System.Math.Clamp(
            0.5 * SurfaceRefractivity * density * sphericalGradient,
            0.0, 0.035);
    }

    public bool HasCloudLayer => AreFinite(CloudBaseAltitude, CloudTopAltitude,
            CloudExtinction, CloudCoverage, CloudWindRadiansPerSecond)
        && CloudBaseAltitude >= 0.0 && CloudTopAltitude > CloudBaseAltitude
        && CloudExtinction > 0.0 && CloudCoverage is > 0.0 and <= 1.0;

    /// <summary>Normalised vertical density envelope with soft cloud-base and anvil fades.</summary>
    public double CloudVerticalDensity(double altitude)
    {
        if (!HasCloudLayer || altitude <= CloudBaseAltitude || altitude >= CloudTopAltitude)
            return 0.0;
        double fraction = (altitude - CloudBaseAltitude) /
            (CloudTopAltitude - CloudBaseAltitude);
        double baseFade = Smoothstep(0.0, 0.12, fraction);
        double topFade = 1.0 - Smoothstep(0.72, 1.0, fraction);
        return baseFade * topFade;
    }

    /// <summary>
    /// Converts a normalised weather-map sample into cloud occupancy. Coverage controls the
    /// occupied fraction for a uniformly distributed map; the soft edge avoids binary clouds.
    /// </summary>
    public double CloudWeatherDensity(double weatherSample)
    {
        if (!HasCloudLayer || !double.IsFinite(weatherSample)) return 0.0;
        double threshold = 1.0 - CloudCoverage;
        return Smoothstep(threshold - 0.12, threshold + 0.10,
            System.Math.Clamp(weatherSample, 0.0, 1.0));
    }

    public double CloudLocalExtinction(double altitude, double weatherSample) =>
        CloudExtinction * CloudVerticalDensity(altitude) * CloudWeatherDensity(weatherSample);

    /// <summary>
    /// Integrates the vertical cloud optical depth for a weather-map sample.  This is the
    /// CPU oracle for the renderer's Beer–Lambert cloud transport: the shader may add
    /// horizontal billow detail, but it must preserve this same non-negative, finite
    /// vertical envelope and weather monotonicity.
    /// </summary>
    public double CloudVerticalOpticalDepth(double weatherSample, int sampleCount = 32)
    {
        if (!HasCloudLayer || !double.IsFinite(weatherSample)) return 0.0;

        int n = System.Math.Max(8, sampleCount);
        if ((n & 1) != 0) n++;
        double span = CloudTopAltitude - CloudBaseAltitude;
        double step = span / n;
        double integral = 0.0;
        for (int i = 0; i <= n; i++)
        {
            double altitude = CloudBaseAltitude + step * i;
            int weight = i == 0 || i == n ? 1 : (i % 2 == 0 ? 2 : 4);
            integral += weight * CloudLocalExtinction(altitude, weatherSample);
        }
        return integral * step / 3.0;
    }

    public Vector3d MieExtinction => MieScattering + MieAbsorption;
    public bool IsEnabled => RayleighScattering.MagnitudeSquared > 0.0
        || MieExtinction.MagnitudeSquared > 0.0;

    public double RayleighDensity(double altitude) => ExponentialDensity(
        altitude, RayleighScaleHeight);

    public double MieDensity(double altitude) => ExponentialDensity(
        altitude, MieScaleHeight);

    /// <summary>
    /// Ozone layer density proxy: triangular distribution centred in the stratosphere.
    /// It integrates to <c>OzoneHalfWidth</c>, so the coefficient remains a local m⁻¹ value.
    /// </summary>
    public double OzoneDensity(double altitude)
    {
        if (OzoneHalfWidth <= 0.0) return 0.0;
        return System.Math.Max(0.0,
            1.0 - System.Math.Abs(altitude - OzoneCenterAltitude) / OzoneHalfWidth);
    }

    /// <summary>Gaussian mesospheric/lower-thermospheric airglow layer.</summary>
    public double AirglowDensity(double altitude)
    {
        if (AirglowScaleHeight <= 0.0 || !double.IsFinite(altitude)) return 0.0;
        double z = (altitude - AirglowCenterAltitude) / AirglowScaleHeight;
        return System.Math.Exp(-0.5 * z * z);
    }

    /// <summary>Vertical optical depth from altitude to space for the RGB bands.</summary>
    public Vector3d VerticalOpticalDepth(double altitude)
    {
        altitude = System.Math.Max(0.0, altitude);
        double rayleighColumn = RayleighScaleHeight > 0.0
            ? RayleighScaleHeight * System.Math.Exp(-altitude / RayleighScaleHeight)
            : 0.0;
        double mieColumn = MieScaleHeight > 0.0
            ? MieScaleHeight * System.Math.Exp(-altitude / MieScaleHeight)
            : 0.0;
        double ozoneColumn = OzoneColumnAbove(altitude);
        return RayleighScattering * rayleighColumn
            + MieExtinction * mieColumn
            + OzoneAbsorption * ozoneColumn;
    }

    public Vector3d VerticalTransmittance(double altitude)
    {
        var tau = VerticalOpticalDepth(altitude);
        return new Vector3d(
            System.Math.Exp(-tau.X),
            System.Math.Exp(-tau.Y),
            System.Math.Exp(-tau.Z));
    }

    /// <summary>
    /// Direct-sun RGB transmittance through a plane-parallel optical column. The Kasten–Young
    /// relative air-mass expression remains stable close to the horizon, unlike 1/cos(z).
    /// Below the geometric horizon the direct beam is zero (twilight remains sky scattering).
    /// </summary>
    public Vector3d DirectSolarTransmittance(double altitude, double sunElevationSin)
        => DirectSolarTransmittance(altitude, sunElevationSin, 6_371_000.0, 1_000_000.0);

    /// <summary>
    /// Direct-sun RGB transmittance through a spherical atmosphere. Unlike the familiar
    /// plane-parallel air-mass approximation, this follows the finite ray from the
    /// observer to the outer atmospheric sphere. That matters at sunrise/sunset, from
    /// high-altitude aircraft and in thin atmospheres where a flat column can overstate
    /// the path by orders of magnitude.
    /// </summary>
    public Vector3d DirectSolarTransmittance(
        double altitude,
        double sunElevationSin,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount = 48)
    {
        // A non-finite observation must never turn into full, unattenuated sunlight.
        // This is especially important for the game-layer exposure controller during
        // scene/bootstrap frames, when a body position may not be initialised yet.
        if (!double.IsFinite(altitude) || !double.IsFinite(sunElevationSin))
            return Vector3d.Zero;
        if (!double.IsFinite(planetRadius) || planetRadius <= 0.0
            || !double.IsFinite(atmosphereTopAltitude) || atmosphereTopAltitude <= 0.0)
        {
            return DirectSolarTransmittance(altitude, sunElevationSin);
        }

        if (SurfaceRefractivity > 0.0 && RefractiveScaleHeight > 0.0)
        {
            // The input is the geometric solar elevation.  Refraction changes the
            // apparent elevation before the ray enters the atmosphere; solving that
            // angle is what makes a sun a few tenths of a degree below the geometric
            // horizon visible instead of hard-clipping to black.
            if (!TrySolveRefractedSolarElevation(
                    altitude, sunElevationSin, planetRadius, atmosphereTopAltitude,
                    out double apparentElevationSin, sampleCount))
                return Vector3d.Zero;

            var refractedTau = OpticalDepthAlongRefractedPath(
                altitude, apparentElevationSin,
                planetRadius, atmosphereTopAltitude, sampleCount);
            return new Vector3d(
                System.Math.Exp(-refractedTau.X),
                System.Math.Exp(-refractedTau.Y),
                System.Math.Exp(-refractedTau.Z));
        }

        if (sunElevationSin <= 0.0) return Vector3d.Zero;
        var tau = OpticalDepthAlongRay(
            altitude, System.Math.Clamp(sunElevationSin, 0.0, 1.0),
            planetRadius, atmosphereTopAltitude, sampleCount);
        return new Vector3d(
            System.Math.Exp(-tau.X),
            System.Math.Exp(-tau.Y),
            System.Math.Exp(-tau.Z));
    }

    /// <summary>
    /// Profile-aware direct solar transport used by the renderer's thermodynamic LUTs.
    /// The ray solver and extinction integral both use the exact thermodynamic profile,
    /// including a valid two-branch duct path when a dense atmosphere bends a source below
    /// the geometric horizon.
    /// </summary>
    public Vector3d DirectSolarTransmittance(
        AtmosphereDensityProfile profile,
        double altitude,
        double sunElevationSin,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount = 48)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!double.IsFinite(altitude) || !double.IsFinite(sunElevationSin)
            || !double.IsFinite(planetRadius) || !double.IsFinite(atmosphereTopAltitude)
            || planetRadius <= 0.0 || atmosphereTopAltitude <= 0.0)
            return Vector3d.Zero;

        // Away from the solar limb the refractive angular correction is sub-pixel for
        // the LUT domain, while solving the full monotonic/ducted inverse would require
        // dozens of nested radial integrations per texel.  Keep the exact profile ray
        // integral (so extinction remains thermodynamic) and reserve the inverse solver
        // for the only region where refraction changes visibility: the final two degrees
        // around the geometric horizon and all sub-horizon rays.
        double geometricElevation = System.Math.Asin(
            System.Math.Clamp(sunElevationSin, -1.0, 1.0));
        if (geometricElevation > 0.035)
        {
            var fastTau = OpticalDepthAlongRefractedRay(
                profile, altitude, System.Math.Sin(geometricElevation),
                planetRadius, atmosphereTopAltitude, sampleCount);
            return new Vector3d(
                System.Math.Exp(-fastTau.X),
                System.Math.Exp(-fastTau.Y),
                System.Math.Exp(-fastTau.Z));
        }

        if (!TrySolveRefractedSolarElevation(
                profile, altitude, sunElevationSin, planetRadius,
                atmosphereTopAltitude, out double apparentElevationSin, sampleCount))
            return Vector3d.Zero;

        var tau = OpticalDepthAlongRefractedPath(
            altitude, apparentElevationSin, planetRadius,
            atmosphereTopAltitude, sampleCount, profile);
        return new Vector3d(
            System.Math.Exp(-tau.X),
            System.Math.Exp(-tau.Y),
            System.Math.Exp(-tau.Z));
    }

    /// <summary>
    /// Solves the apparent solar elevation for a spherical refractive atmosphere.
    ///
    /// A ray leaves the observer at the apparent angle, accumulates the angular
    /// displacement <c>∫ p/(r√((nr)²−p²)) dr</c> through the atmosphere, and then
    /// continues as a vacuum ray.  Bisection is deterministic and monotonic for the
    /// ordinary terrestrial profile.  If a dense profile has a refractive minimum, the
    /// solver switches to the explicit two-branch tangent path when the observer is above
    /// it; impossible roots still return no beam rather than fabricating energy. Negative
    /// geometric elevations are accepted while the refracted path clears the planet.
    /// </summary>
    public bool TrySolveRefractedSolarElevation(
        double altitude,
        double geometricElevationSin,
        double planetRadius,
        double atmosphereTopAltitude,
        out double apparentElevationSin,
        int sampleCount = 48)
        => TrySolveRefractedSolarElevationCore(
            altitude, geometricElevationSin, planetRadius, atmosphereTopAltitude,
            out apparentElevationSin, sampleCount, profile: null);

    /// <summary>
    /// Solves the apparent solar elevation using molecular refractivity sampled from a
    /// thermodynamic profile.  Unlike the legacy overload, the refractive invariant,
    /// duct search and angular integral all see the same P/T-derived index of refraction.
    /// </summary>
    public bool TrySolveRefractedSolarElevation(
        AtmosphereDensityProfile profile,
        double altitude,
        double geometricElevationSin,
        double planetRadius,
        double atmosphereTopAltitude,
        out double apparentElevationSin,
        int sampleCount = 48)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return TrySolveRefractedSolarElevationCore(
            altitude, geometricElevationSin, planetRadius, atmosphereTopAltitude,
            out apparentElevationSin, sampleCount, profile);
    }

    private bool TrySolveRefractedSolarElevationCore(
        double altitude,
        double geometricElevationSin,
        double planetRadius,
        double atmosphereTopAltitude,
        out double apparentElevationSin,
        int sampleCount,
        AtmosphereDensityProfile? profile)
    {
        apparentElevationSin = 0.0;
        if (!double.IsFinite(altitude) || !double.IsFinite(geometricElevationSin)
            || !double.IsFinite(planetRadius) || !double.IsFinite(atmosphereTopAltitude)
            || !double.IsFinite(SurfaceRefractivity) || !double.IsFinite(RefractiveScaleHeight)
            || planetRadius <= 0.0 || atmosphereTopAltitude <= 0.0)
            return false;
        if (SurfaceRefractivity <= 0.0 || RefractiveScaleHeight <= 0.0)
        {
            if (geometricElevationSin <= 0.0) return false;
            apparentElevationSin = System.Math.Clamp(geometricElevationSin, 0.0, 1.0);
            return true;
        }

        double geometricElevation = System.Math.Asin(
            System.Math.Clamp(geometricElevationSin, -1.0, 1.0));
        if (geometricElevation >= System.Math.PI * 0.5 - 1e-8)
        {
            apparentElevationSin = 1.0;
            return true;
        }

        // A horizon ray is the lower end of the ordinary monotonic branch.  Its
        // asymptotic elevation is negative by the amount of the real horizon lift.
        if (!TryRefractedAsymptoticElevation(
                altitude, 0.0, planetRadius, atmosphereTopAltitude,
                out double geometricHorizon, sampleCount, profile))
        {
            if (geometricElevation <= 0.0)
                return TrySolveRefractedTwoBranchSolarElevation(
                    altitude, geometricElevation, planetRadius, atmosphereTopAltitude,
                    out apparentElevationSin, sampleCount, profile);
            apparentElevationSin = System.Math.Sin(geometricElevation);
            return true;
        }
        if (geometricElevation < geometricHorizon - 1e-6)
        {
            return TrySolveRefractedTwoBranchSolarElevation(
                altitude, geometricElevation, planetRadius, atmosphereTopAltitude,
                out apparentElevationSin, sampleCount, profile);
        }

        double high = System.Math.PI * 0.5 - 1e-7;
        if (!TryRefractedAsymptoticElevation(
                altitude, high, planetRadius, atmosphereTopAltitude,
                out double highGeometric, sampleCount, profile))
            return false;
        if (geometricElevation >= highGeometric)
        {
            apparentElevationSin = System.Math.Sin(high);
            return true;
        }

        // f(apparent) = geometric is monotonic on the non-ducted branch.
        double low = 0.0;
        for (int iteration = 0; iteration < 36; iteration++)
        {
            double middle = 0.5 * (low + high);
            if (!TryRefractedAsymptoticElevation(
                    altitude, middle, planetRadius, atmosphereTopAltitude,
                    out double middleGeometric, sampleCount, profile))
                return false;
            if (middleGeometric < geometricElevation)
                low = middle;
            else
                high = middle;
        }

        apparentElevationSin = System.Math.Sin(0.5 * (low + high));
        return true;
    }

    private bool TrySolveRefractedTwoBranchSolarElevation(
        double altitude,
        double geometricElevation,
        double planetRadius,
        double atmosphereTopAltitude,
        out double apparentElevationSin,
        int sampleCount,
        AtmosphereDensityProfile? profile = null)
    {
        apparentElevationSin = 0.0;
        if (!TryFindRefractiveMinimum(
                altitude, planetRadius, atmosphereTopAltitude,
                out double minimumRadius, out double minimumProduct, profile))
            return false;

        double r0 = planetRadius + System.Math.Max(0.0, altitude);
        double observerProduct = RefractiveIndex(altitude, profile) * r0;
        if (minimumRadius >= r0 - 1.0 || minimumProduct >= observerProduct)
            return false;

        // The downward branch exists between the tangent at the refractive minimum
        // and a nearly horizontal local ray. Both endpoints are offset slightly so
        // Simpson integration never samples a non-integrable double root.
        double criticalMagnitude = System.Math.Acos(System.Math.Clamp(
            minimumProduct / observerProduct, 0.0, 1.0));
        if (criticalMagnitude <= 1e-5) return false;
        double low = -criticalMagnitude + 1e-5;
        double high = -1e-5;
        if (!TryRefractedTwoBranchAsymptoticElevation(
                altitude, low, planetRadius, atmosphereTopAltitude,
                out double lowGeometric, sampleCount, profile)
            || !TryRefractedTwoBranchAsymptoticElevation(
                altitude, high, planetRadius, atmosphereTopAltitude,
                out double highGeometric, sampleCount, profile))
            return false;

        double minimumGeometric = System.Math.Min(lowGeometric, highGeometric);
        double maximumGeometric = System.Math.Max(lowGeometric, highGeometric);
        if (geometricElevation < minimumGeometric - 1e-5
            || geometricElevation > maximumGeometric + 1e-5)
            return false;

        double fa = lowGeometric;
        double fb = highGeometric;
        for (int iteration = 0; iteration < 36; iteration++)
        {
            double middle = 0.5 * (low + high);
            if (!TryRefractedTwoBranchAsymptoticElevation(
                    altitude, middle, planetRadius, atmosphereTopAltitude,
                    out double middleGeometric, sampleCount, profile))
                return false;

            if (fa < fb)
            {
                if (middleGeometric < geometricElevation)
                {
                    low = middle;
                    fa = middleGeometric;
                }
                else
                {
                    high = middle;
                    fb = middleGeometric;
                }
            }
            else
            {
                if (middleGeometric > geometricElevation)
                {
                    low = middle;
                    fa = middleGeometric;
                }
                else
                {
                    high = middle;
                    fb = middleGeometric;
                }
            }
        }

        apparentElevationSin = System.Math.Sin(0.5 * (low + high));
        return true;
    }

    private bool TryFindRefractiveMinimum(
        double altitude,
        double planetRadius,
        double atmosphereTopAltitude,
        out double minimumRadius,
        out double minimumProduct,
        AtmosphereDensityProfile? profile = null)
    {
        minimumRadius = planetRadius;
        minimumProduct = double.PositiveInfinity;
        double top = planetRadius + atmosphereTopAltitude;
        int samples = 256;
        for (int i = 0; i <= samples; i++)
        {
            double radius = planetRadius
                + atmosphereTopAltitude * i / samples;
            double product = RefractiveIndex(radius - planetRadius, profile) * radius;
            if (product < minimumProduct)
            {
                minimumProduct = product;
                minimumRadius = radius;
            }
        }

        double observerRadius = planetRadius + System.Math.Max(0.0, altitude);
        return double.IsFinite(minimumProduct)
            && minimumRadius > planetRadius + 1.0
            && minimumRadius < observerRadius - 1.0
            && minimumRadius < top;
    }

    private bool TryRefractedTwoBranchAsymptoticElevation(
        double altitude,
        double apparentElevation,
        double planetRadius,
        double atmosphereTopAltitude,
        out double geometricElevation,
        int sampleCount,
        AtmosphereDensityProfile? profile = null)
    {
        geometricElevation = 0.0;
        if (apparentElevation >= 0.0
            || !TryFindRefractiveMinimum(
                altitude, planetRadius, atmosphereTopAltitude,
                out double minimumRadius, out double minimumProduct, profile))
            return false;

        double r0 = planetRadius + System.Math.Max(0.0, altitude);
        double rTop = planetRadius + atmosphereTopAltitude;
        double p = RefractiveIndex(altitude, profile) * r0 * System.Math.Cos(apparentElevation);
        if (p <= minimumProduct || p >= RefractiveIndex(altitude, profile) * r0)
            return false;
        if (!TryFindTurnRadius(
                minimumRadius, r0, planetRadius, p, out double turnRadius, profile))
            return false;

        if (!TryIntegrateRefractedAngularSegment(
                turnRadius, r0, planetRadius, p, sampleCount,
                profile,
                out double inwardAngular)
            || !TryIntegrateRefractedAngularSegment(
                turnRadius, rTop, planetRadius, p, sampleCount,
                profile,
                out double outwardAngular))
            return false;

        double nTop = RefractiveIndex(atmosphereTopAltitude, profile);
        double vacuumTail = System.Math.Asin(System.Math.Clamp(
            p / System.Math.Max(nTop * rTop, 1.0), 0.0, 1.0));
        geometricElevation = System.Math.PI * 0.5
            - inwardAngular - outwardAngular - vacuumTail;
        return double.IsFinite(geometricElevation);
    }

    private bool TryFindTurnRadius(
        double lowerRadius,
        double upperRadius,
        double planetRadius,
        double invariant,
        out double turnRadius,
        AtmosphereDensityProfile? profile = null)
    {
        turnRadius = lowerRadius;
        double lowerProduct = RefractiveIndex(lowerRadius - planetRadius, profile) * lowerRadius;
        double upperProduct = RefractiveIndex(upperRadius - planetRadius, profile) * upperRadius;
        if (lowerProduct > invariant || upperProduct < invariant) return false;

        double low = lowerRadius;
        double high = upperRadius;
        for (int i = 0; i < 64; i++)
        {
            double middle = 0.5 * (low + high);
            double product = RefractiveIndex(middle - planetRadius, profile) * middle;
            if (product < invariant) low = middle;
            else high = middle;
        }
        turnRadius = 0.5 * (low + high);
        return true;
    }

    private bool TryRefractedAsymptoticElevation(
        double altitude,
        double apparentElevation,
        double planetRadius,
        double atmosphereTopAltitude,
        out double geometricElevation,
        int sampleCount,
        AtmosphereDensityProfile? profile = null)
    {
        geometricElevation = 0.0;
        altitude = System.Math.Max(0.0, altitude);
        double r0 = planetRadius + altitude;
        double rTop = planetRadius + atmosphereTopAltitude;
        double span = rTop - r0;
        if (span <= 0.0) return false;

        double n0 = RefractiveIndex(altitude, profile);
        double p = n0 * r0 * System.Math.Cos(
            System.Math.Clamp(apparentElevation, 0.0, System.Math.PI * 0.5));
        if (!TryIntegrateRefractedAngularPath(
                altitude, planetRadius, rTop, p, sampleCount, profile,
                out double angularDisplacement))
            return false;

        double nTop = RefractiveIndex(atmosphereTopAltitude, profile);
        double vacuumTail = System.Math.Asin(System.Math.Clamp(
            p / System.Math.Max(nTop * rTop, 1.0), 0.0, 1.0));
        geometricElevation = System.Math.PI * 0.5
            - angularDisplacement - vacuumTail;
        return double.IsFinite(geometricElevation);
    }

    /// <summary>
    /// Integrates the angular ray displacement in the radial coordinate.  The preflight
    /// scan catches refractive ducts (where n·r has a local minimum below the invariant)
    /// instead of clamping an imaginary square root and creating non-physical energy.
    /// </summary>
    private bool TryIntegrateRefractedAngularPath(
        double altitude,
        double planetRadius,
        double atmosphereTopRadius,
        double invariant,
        int sampleCount,
        AtmosphereDensityProfile? profile,
        out double integral)
    {
        integral = 0.0;
        double r0 = planetRadius + System.Math.Max(0.0, altitude);
        double span = atmosphereTopRadius - r0;
        if (span <= 0.0) return false;

        int scanSteps = System.Math.Max(32, sampleCount * 2);
        for (int i = 0; i <= scanSteps; i++)
        {
            double u = (double)i / scanSteps;
            double radius = r0 + span * u * u;
            double product = RefractiveIndex(radius - planetRadius, profile) * radius;
            if (product + 1e-5 < invariant)
                return false;
        }

        int n = System.Math.Max(8, sampleCount);
        if ((n & 1) != 0) n++;
        double step = 1.0 / n;
        for (int i = 0; i <= n; i++)
        {
            double u = i * step;
            double radius = r0 + span * u * u;
            double drDu = 2.0 * span * u;
            double product = RefractiveIndex(radius - planetRadius, profile) * radius;
            double delta = product * product - invariant * invariant;
            double angularDu;
            if (i == 0 && delta <= 1e-8)
            {
                // The grazing endpoint is an integrable square-root singularity.
                // Evaluate its finite transformed limit instead of dropping it to
                // zero, which otherwise biases the horizon bending by several percent.
                double derivative = RefractiveProductDerivative(
                    radius, radius - planetRadius, profile);
                if (derivative <= 0.0)
                    return false;
                angularDu = 2.0 * invariant * span
                    / (radius * System.Math.Sqrt(2.0 * invariant * derivative * span));
            }
            else
            {
                if (delta <= 0.0) return false;
                angularDu = invariant * drDu
                    / (radius * System.Math.Sqrt(delta));
            }

            int weight = i == 0 || i == n ? 1 : (i % 2 == 0 ? 2 : 4);
            integral += weight * angularDu;
        }

        integral *= step / 3.0;
        return double.IsFinite(integral) && integral >= 0.0;
    }

    private bool TryIntegrateRefractedAngularSegment(
        double startRadius,
        double endRadius,
        double planetRadius,
        double invariant,
        int sampleCount,
        AtmosphereDensityProfile? profile,
        out double integral)
    {
        integral = 0.0;
        double span = endRadius - startRadius;
        if (span <= 0.0) return false;

        int scanSteps = System.Math.Max(32, sampleCount * 2);
        for (int i = 0; i <= scanSteps; i++)
        {
            double u = (double)i / scanSteps;
            double radius = startRadius + span * u * u;
            double product = RefractiveIndex(radius - planetRadius, profile) * radius;
            if (product + 1e-5 < invariant) return false;
        }

        int n = System.Math.Max(8, sampleCount);
        if ((n & 1) != 0) n++;
        double step = 1.0 / n;
        for (int i = 0; i <= n; i++)
        {
            double u = i * step;
            double radius = startRadius + span * u * u;
            double drDu = 2.0 * span * u;
            double product = RefractiveIndex(radius - planetRadius, profile) * radius;
            double delta = product * product - invariant * invariant;
            double angularDu;
            if (i == 0 && delta <= 1e-8)
            {
                double derivative = RefractiveProductDerivative(
                    radius, radius - planetRadius, profile);
                if (derivative <= 0.0) return false;
                angularDu = 2.0 * invariant * span
                    / (radius * System.Math.Sqrt(2.0 * invariant * derivative * span));
            }
            else
            {
                if (delta <= 0.0) return false;
                angularDu = invariant * drDu
                    / (radius * System.Math.Sqrt(delta));
            }

            int weight = i == 0 || i == n ? 1 : (i % 2 == 0 ? 2 : 4);
            integral += weight * angularDu;
        }

        integral *= step / 3.0;
        return double.IsFinite(integral) && integral >= 0.0;
    }

    private double RefractiveProductDerivative(
        double radius,
        double altitude,
        AtmosphereDensityProfile? profile = null)
    {
        if (profile is not null)
        {
            double top = profile.TopAltitude;
            double h = System.Math.Clamp(
                System.Math.Max(0.5, top * 1e-5), 0.5, 25.0);
            double lowerAltitude = System.Math.Max(0.0, altitude - h);
            double upperAltitude = System.Math.Min(top, altitude + h);
            if (upperAltitude <= lowerAltitude)
                return 1.0;
            double lowerRadius = radius - (altitude - lowerAltitude);
            double upperRadius = radius + (upperAltitude - altitude);
            double lowerProduct = RefractiveIndex(lowerAltitude, profile) * lowerRadius;
            double upperProduct = RefractiveIndex(upperAltitude, profile) * upperRadius;
            return (upperProduct - lowerProduct) / (upperAltitude - lowerAltitude);
        }

        double refractivity = SurfaceRefractivity * System.Math.Exp(
            -System.Math.Max(0.0, altitude) / RefractiveScaleHeight);
        return 1.0 + refractivity
            - radius * refractivity / RefractiveScaleHeight;
    }

    /// <summary>
    /// Integrates extinction along a ray that leaves an observer at <paramref name="altitude"/>
    /// with <paramref name="cosZenith"/> equal to the ray's dot product with local up.
    /// Simpson integration is intentionally deterministic so the same optical profile is
    /// used by tests, exposure control and offline diagnostics.
    /// </summary>
    public Vector3d OpticalDepthAlongRay(
        double altitude,
        double cosZenith,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount = 48)
        => OpticalDepthAlongRayCore(
            altitude, cosZenith, planetRadius, atmosphereTopAltitude,
            sampleCount, SampleLegacyDensity);

    /// <summary>
    /// Integrates extinction using the supplied thermodynamic density profile.
    /// This overload keeps offline LUTs and realtime transport on the same source
    /// profile instead of silently falling back to the legacy exponential envelope.
    /// </summary>
    public Vector3d OpticalDepthAlongRay(
        AtmosphereDensityProfile profile,
        double altitude,
        double cosZenith,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount = 48)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return OpticalDepthAlongRayCore(
            altitude, cosZenith, planetRadius, atmosphereTopAltitude,
            sampleCount, profile.Sample);
    }

    private Vector3d OpticalDepthAlongRayCore(
        double altitude,
        double cosZenith,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount,
        Func<double, Vector3d> densitySampler)
    {
        if (!double.IsFinite(altitude) || !double.IsFinite(cosZenith)
            || !double.IsFinite(planetRadius) || !double.IsFinite(atmosphereTopAltitude)
            || planetRadius <= 0.0 || atmosphereTopAltitude <= 0.0)
            return Vector3d.Zero;

        altitude = System.Math.Max(0.0, altitude);
        cosZenith = System.Math.Clamp(cosZenith, -1.0, 1.0);
        int n = System.Math.Max(8, sampleCount);
        if ((n & 1) != 0) n++;

        double observerRadius = planetRadius + altitude;
        double outerRadius = planetRadius + atmosphereTopAltitude;
        double b = observerRadius * cosZenith;
        double discriminant = b * b - (observerRadius * observerRadius
            - outerRadius * outerRadius);
        if (discriminant <= 0.0) return Vector3d.Zero;

        double distanceToOuter = -b + System.Math.Sqrt(discriminant);
        if (distanceToOuter <= 0.0) return Vector3d.Zero;

        // A sun below the geometric horizon has no direct beam. The caller may still
        // render twilight/multiple scattering; this method is specifically direct light.
        if (cosZenith <= 0.0) return Vector3d.Zero;

        Vector3d integral = Vector3d.Zero;
        double step = distanceToOuter / n;
        for (int i = 0; i <= n; i++)
        {
            double distance = step * i;
            double radius = System.Math.Sqrt(observerRadius * observerRadius
                + distance * distance + 2.0 * observerRadius * cosZenith * distance);
            double localAltitude = radius - planetRadius;
            Vector3d density = densitySampler(localAltitude);
            Vector3d sample = RayleighScattering * density.X
                + MieExtinction * density.Y
                + OzoneAbsorption * density.Z;
            int weight = i == 0 || i == n ? 1 : (i % 2 == 0 ? 2 : 4);
            integral += sample * weight;
        }

        return integral * (step / 3.0);
    }

    private Vector3d SampleLegacyDensity(double altitude)
        => new(RayleighDensity(altitude), MieDensity(altitude), OzoneDensity(altitude));

    /// <summary>
    /// Dispatches the refracted optical path selected by the apparent solar elevation.
    /// Positive elevations use the direct outbound branch; negative elevations may use a
    /// valid two-branch duct path when the observer is above a refractive minimum.
    /// </summary>
    private Vector3d OpticalDepthAlongRefractedPath(
        double altitude,
        double apparentElevationSin,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount,
        AtmosphereDensityProfile? profile = null)
    {
        if (apparentElevationSin >= 0.0)
        {
            return profile is null
                ? OpticalDepthAlongRefractedRay(
                    altitude, apparentElevationSin,
                    planetRadius, atmosphereTopAltitude, sampleCount)
                : OpticalDepthAlongRefractedRay(
                    profile, altitude, apparentElevationSin,
                    planetRadius, atmosphereTopAltitude, sampleCount);
        }
        return profile is null
            ? OpticalDepthAlongRefractedTwoBranch(
                altitude, apparentElevationSin,
                planetRadius, atmosphereTopAltitude, sampleCount)
            : OpticalDepthAlongRefractedTwoBranch(
                profile, altitude, apparentElevationSin,
                planetRadius, atmosphereTopAltitude, sampleCount);
    }

    /// <summary>
    /// Integrates extinction along the outward branch of a refracted spherical ray.
    /// Snell's invariant <c>p = n r sin(zenith)</c> is held constant and the radial
    /// integral uses <c>ds/dr = 1 / sqrt(1 - (p/(nr))²)</c>.  The transformed variable
    /// <c>r = r₀ + (Rtop-r₀)u²</c> resolves the dense lower atmosphere and the grazing
    /// singularity deterministically.  Sub-horizon source directions are first mapped
    /// to an apparent outbound elevation by <see cref="TrySolveRefractedSolarElevation"/>.
    /// </summary>
    public Vector3d OpticalDepthAlongRefractedRay(
        double altitude,
        double cosZenith,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount = 48)
        => OpticalDepthAlongRefractedRayCore(
            altitude, cosZenith, planetRadius, atmosphereTopAltitude,
            sampleCount, profile: null);

    /// <summary>Profile-aware counterpart of <see cref="OpticalDepthAlongRefractedRay(double, double, double, double, int)"/>.</summary>
    public Vector3d OpticalDepthAlongRefractedRay(
        AtmosphereDensityProfile profile,
        double altitude,
        double cosZenith,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount = 48)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return OpticalDepthAlongRefractedRayCore(
            altitude, cosZenith, planetRadius, atmosphereTopAltitude,
            sampleCount, profile);
    }

    private Vector3d OpticalDepthAlongRefractedRayCore(
        double altitude,
        double cosZenith,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount,
        AtmosphereDensityProfile? profile)
    {
        if (!double.IsFinite(altitude) || !double.IsFinite(cosZenith)
            || !double.IsFinite(planetRadius) || !double.IsFinite(atmosphereTopAltitude)
            || !double.IsFinite(SurfaceRefractivity) || !double.IsFinite(RefractiveScaleHeight)
            || planetRadius <= 0.0 || atmosphereTopAltitude <= 0.0
            || SurfaceRefractivity <= 0.0 || RefractiveScaleHeight <= 0.0
            || cosZenith <= 0.0)
        {
            return profile is null
                ? OpticalDepthAlongRay(
                    altitude, cosZenith, planetRadius, atmosphereTopAltitude, sampleCount)
                : OpticalDepthAlongRay(
                    profile, altitude, cosZenith,
                    planetRadius, atmosphereTopAltitude, sampleCount);
        }

        altitude = System.Math.Max(0.0, altitude);
        cosZenith = System.Math.Clamp(cosZenith, 0.0, 1.0);
        int n = System.Math.Max(8, sampleCount);
        if ((n & 1) != 0) n++;

        double r0 = planetRadius + altitude;
        double rTop = planetRadius + atmosphereTopAltitude;
        double span = rTop - r0;
        if (span <= 0.0) return Vector3d.Zero;

        double n0 = RefractiveIndex(altitude, profile);
        double p = n0 * r0 * System.Math.Sqrt(
            System.Math.Max(0.0, 1.0 - cosZenith * cosZenith));
        Vector3d integral = Vector3d.Zero;
        double step = 1.0 / n;
        for (int i = 0; i <= n; i++)
        {
            double u = i * step;
            double radius = r0 + span * u * u;
            double localAltitude = radius - planetRadius;
            double dnDu = 2.0 * span * u;
            double refractiveRadius = RefractiveIndex(localAltitude, profile) * radius;
            double ratio = p / System.Math.Max(refractiveRadius, 1.0);
            if (ratio > 1.0 + 1e-8) return Vector3d.Zero;
            double radial = System.Math.Sqrt(System.Math.Max(1e-12, 1.0 - ratio * ratio));
            double dsDu = dnDu / radial;
            Vector3d density = profile?.Sample(localAltitude)
                ?? SampleLegacyDensity(localAltitude);
            Vector3d sample = RayleighScattering * density.X
                + MieExtinction * density.Y
                + OzoneAbsorption * density.Z;
            int weight = i == 0 || i == n ? 1 : (i % 2 == 0 ? 2 : 4);
            integral += sample * (weight * dsDu);
        }

        return integral * (step / 3.0);
    }

    /// <summary>
    /// Integrates a valid two-branch refracted path for an observer above a refractive
    /// minimum.  The ray initially travels downward, turns where <c>n(r)r = p</c>, then
    /// climbs through the outer atmosphere.  This is the branch that dense Venus-like
    /// profiles need for a source below the geometric horizon; a ray that would intersect
    /// the ground returns zero instead of being treated as direct sunlight.
    /// </summary>
    public Vector3d OpticalDepthAlongRefractedTwoBranch(
        double altitude,
        double apparentElevationSin,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount = 48)
        => OpticalDepthAlongRefractedTwoBranchCore(
            altitude, apparentElevationSin, planetRadius, atmosphereTopAltitude,
            sampleCount, profile: null);

    /// <summary>Profile-aware counterpart for dense-atmosphere duct paths.</summary>
    public Vector3d OpticalDepthAlongRefractedTwoBranch(
        AtmosphereDensityProfile profile,
        double altitude,
        double apparentElevationSin,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount = 48)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return OpticalDepthAlongRefractedTwoBranchCore(
            altitude, apparentElevationSin, planetRadius, atmosphereTopAltitude,
            sampleCount, profile);
    }

    private Vector3d OpticalDepthAlongRefractedTwoBranchCore(
        double altitude,
        double apparentElevationSin,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount,
        AtmosphereDensityProfile? profile)
    {
        if (!double.IsFinite(altitude) || !double.IsFinite(apparentElevationSin)
            || !double.IsFinite(planetRadius) || !double.IsFinite(atmosphereTopAltitude)
            || !double.IsFinite(SurfaceRefractivity) || !double.IsFinite(RefractiveScaleHeight)
            || planetRadius <= 0.0 || atmosphereTopAltitude <= 0.0
            || SurfaceRefractivity <= 0.0 || RefractiveScaleHeight <= 0.0
            || apparentElevationSin >= 0.0)
            return Vector3d.Zero;

        if (!TryFindRefractiveMinimum(
                altitude, planetRadius, atmosphereTopAltitude,
                out double minimumRadius, out double minimumProduct, profile))
            return Vector3d.Zero;
        double r0 = planetRadius + System.Math.Max(0.0, altitude);
        double rTop = planetRadius + atmosphereTopAltitude;
        double p = RefractiveIndex(altitude, profile) * r0 * System.Math.Sqrt(
            System.Math.Max(0.0, 1.0 - apparentElevationSin * apparentElevationSin));
        if (p <= minimumProduct
            || p >= RefractiveIndex(altitude, profile) * r0
            || !TryFindTurnRadius(minimumRadius, r0, planetRadius, p,
                out double turnRadius, profile))
            return Vector3d.Zero;

        var inward = IntegrateRefractedExtinctionSegment(
            turnRadius, r0, planetRadius, p, sampleCount, profile);
        var outward = IntegrateRefractedExtinctionSegment(
            turnRadius, rTop, planetRadius, p, sampleCount, profile);
        return inward + outward;
    }

    private Vector3d IntegrateRefractedExtinctionSegment(
        double startRadius,
        double endRadius,
        double planetRadius,
        double invariant,
        int sampleCount,
        AtmosphereDensityProfile? profile)
    {
        double span = endRadius - startRadius;
        if (span <= 0.0) return Vector3d.Zero;
        int n = System.Math.Max(8, sampleCount);
        if ((n & 1) != 0) n++;
        double step = 1.0 / n;
        Vector3d integral = Vector3d.Zero;
        for (int i = 0; i <= n; i++)
        {
            double u = i * step;
            double radius = startRadius + span * u * u;
            double localAltitude = radius - planetRadius;
            double drDu = 2.0 * span * u;
            double product = RefractiveIndex(localAltitude, profile) * radius;
            double ratio = invariant / System.Math.Max(product, 1.0);
            if (ratio > 1.0 + 1e-8) return Vector3d.Zero;

            double dsDu;
            double delta = product * product - invariant * invariant;
            if (i == 0 && delta <= 1e-8)
            {
                double derivative = RefractiveProductDerivative(
                    radius, localAltitude, profile);
                if (derivative <= 0.0) return Vector3d.Zero;
                dsDu = 2.0 * span / System.Math.Sqrt(
                    2.0 * derivative * span / System.Math.Max(invariant, 1.0));
            }
            else
            {
                dsDu = drDu / System.Math.Sqrt(
                    System.Math.Max(1e-12, 1.0 - ratio * ratio));
            }

            var density = profile?.Sample(localAltitude)
                ?? SampleLegacyDensity(localAltitude);
            var extinction = RayleighScattering * density.X
                + MieExtinction * density.Y
                + OzoneAbsorption * density.Z;
            int weight = i == 0 || i == n ? 1 : (i % 2 == 0 ? 2 : 4);
            integral += extinction * (weight * dsDu);
        }

        return integral * (step / 3.0);
    }

    /// <summary>
    /// Local second-order source approximation. Light removed from the direct solar beam is
    /// redistributed isotropically, bounded per colour band by the single-scattering albedo.
    /// Solid planetary shadow is an explicit zero: an opaque planet is not a scattering event.
    /// </summary>
    public Vector3d LowOrderDiffuseSource(
        Vector3d density, Vector3d solarTransmittance, bool planetOccluded = false)
    {
        if (planetOccluded || LowOrderDiffuseStrength <= 0.0) return Vector3d.Zero;
        var scattering = RayleighScattering * density.X + MieScattering * density.Y;
        var ext = scattering + MieAbsorption * density.Y + OzoneAbsorption * density.Z;
        const double invFourPi = 1.0 / (4.0 * System.Math.PI);
        return new Vector3d(
            DiffuseBand(scattering.X, ext.X, solarTransmittance.X),
            DiffuseBand(scattering.Y, ext.Y, solarTransmittance.Y),
            DiffuseBand(scattering.Z, ext.Z, solarTransmittance.Z)) *
            (LowOrderDiffuseStrength * invFourPi);
    }

    private static double DiffuseBand(double scattering, double extinction, double solarT)
    {
        if (scattering <= 0.0 || extinction <= 0.0) return 0.0;
        double albedo = System.Math.Clamp(scattering / extinction, 0.0, 1.0);
        double removed = System.Math.Clamp(1.0 - solarT, 0.0, 1.0);
        return scattering * albedo * removed;
    }

    private static double Smoothstep(double low, double high, double value)
    {
        double t = System.Math.Clamp((value - low) / (high - low), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    private static bool AreFinite(params double[] values) =>
        values.All(double.IsFinite);

    private double OzoneColumnAbove(double altitude)
    {
        if (OzoneHalfWidth <= 0.0) return 0.0;
        double low = OzoneCenterAltitude - OzoneHalfWidth;
        double high = OzoneCenterAltitude + OzoneHalfWidth;
        if (altitude >= high) return 0.0;
        if (altitude <= low) return OzoneHalfWidth;

        if (altitude < OzoneCenterAltitude)
        {
            double x = altitude - low;
            double risingArea = OzoneHalfWidth * 0.5
                - x * x / (2.0 * OzoneHalfWidth);
            return risingArea + OzoneHalfWidth * 0.5;
        }

        double remaining = high - altitude;
        return remaining * remaining / (2.0 * OzoneHalfWidth);
    }

    private static double ExponentialDensity(double altitude, double scaleHeight) =>
        scaleHeight > 0.0
            ? System.Math.Exp(-System.Math.Max(0.0, altitude) / scaleHeight)
            : 0.0;

    private double RefractiveIndex(
        double altitude,
        AtmosphereDensityProfile? profile = null) =>
        profile is null
            ? 1.0 + SurfaceRefractivity * System.Math.Exp(
                -System.Math.Max(0.0, altitude) / RefractiveScaleHeight)
            : 1.0 + profile.Refractivity(altitude);
}
