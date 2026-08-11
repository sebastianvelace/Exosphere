namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>Physical coordinates shared by the spectral oracle and RGB LUT comparison.</summary>
public readonly record struct SpectralEvaluationCoordinate(
    double Altitude,
    double SolarElevationRadians,
    double ViewCosine,
    double ViewSunCosine,
    string Label = "");

/// <summary>Cost/resolution controls for the offline RGB-versus-spectral comparison.</summary>
public sealed record SpectralComparisonOptions
{
    public int OracleSampleCount { get; init; } = 24;
    public int LutWidth { get; init; } = 16;
    public int LutHeight { get; init; } = 12;
    public int LutIntegrationSteps { get; init; } = 16;
    public int LutSolarSamples { get; init; } = 12;
    public bool BuildAngularAtlas { get; init; } = true;
    public int AngularWidth { get; init; } = 8;
    public int AngularSolarHeight { get; init; } = 8;
    public int AngularViewHeight { get; init; } = 8;
    public int AngularMuWidth { get; init; } = 8;
    public int AngularOpticalDepthSamples { get; init; } = 12;
}

/// <summary>One coordinate's RGB and spectral comparison metrics.</summary>
public sealed record SpectralComparisonSample
{
    public required SpectralEvaluationCoordinate Coordinate { get; init; }
    public required Vector3d OracleLinearRgb { get; init; }
    public required Vector3d LutOrder2 { get; init; }
    public required Vector3d LutOrder3 { get; init; }
    public required Vector3d LutOrder4 { get; init; }
    public required Vector3d LutOrder5Experimental { get; init; }
    public Vector3d? AngularOrder4 { get; init; }
    public required double OracleEnergy { get; init; }
    public required double AbsoluteErrorOrder3 { get; init; }
    public required double AbsoluteErrorOrder4 { get; init; }
    public required double RelativeErrorOrder4 { get; init; }
    public required double ChromaticErrorOrder4 { get; init; }
    public required bool FiniteAndNonNegative { get; init; }
    public required bool OrderMonotonic { get; init; }

    public bool Order4ImprovesOnOrder3 => AbsoluteErrorOrder4 <= AbsoluteErrorOrder3;
}

/// <summary>Aggregated report produced by <see cref="SpectralAtmosphereComparator"/>.</summary>
public sealed class SpectralComparisonReport
{
    public required string BodyId { get; init; }
    public required string DataProvenance { get; init; }
    public required IReadOnlyList<SpectralComparisonSample> Samples { get; init; }
    public required double MeanAbsoluteErrorOrder3 { get; init; }
    public required double MeanAbsoluteErrorOrder4 { get; init; }
    public required double MeanRelativeErrorOrder4 { get; init; }
    public required double MeanChromaticErrorOrder4 { get; init; }
    public required bool AllFiniteAndNonNegative { get; init; }
    public required bool AllOrdersMonotonic { get; init; }
    public required bool Order4NotWorseThanOrder3 { get; init; }

    public SpectralComparisonSample? Find(string label) => Samples.FirstOrDefault(
        sample => string.Equals(sample.Coordinate.Label, label, StringComparison.Ordinal));

    public string ToCsv()
    {
        var lines = new List<string>
        {
            "body,label,altitude_m,solar_elevation_deg,view_cosine,view_sun_cosine,"
                + "oracle_r,oracle_g,oracle_b,lut2_r,lut2_g,lut2_b,lut3_r,lut3_g,lut3_b,"
                + "lut4_r,lut4_g,lut4_b,lut5_r,lut5_g,lut5_b,angular4_r,angular4_g,angular4_b,"
                + "oracle_energy,abs_error_order3,abs_error_order4,relative_error_order4,"
                + "chromatic_error_order4,finite_non_negative,order_monotonic",
        };
        foreach (var sample in Samples)
        {
            var angular = sample.AngularOrder4 ?? Vector3d.Zero;
            var c = sample.Coordinate;
            var values = new object[]
            {
                BodyId, Escape(sample.Coordinate.Label), c.Altitude,
                c.SolarElevationRadians * 180.0 / System.Math.PI,
                c.ViewCosine, c.ViewSunCosine,
                sample.OracleLinearRgb.X, sample.OracleLinearRgb.Y, sample.OracleLinearRgb.Z,
                sample.LutOrder2.X, sample.LutOrder2.Y, sample.LutOrder2.Z,
                sample.LutOrder3.X, sample.LutOrder3.Y, sample.LutOrder3.Z,
                sample.LutOrder4.X, sample.LutOrder4.Y, sample.LutOrder4.Z,
                sample.LutOrder5Experimental.X, sample.LutOrder5Experimental.Y,
                sample.LutOrder5Experimental.Z,
                angular.X, angular.Y, angular.Z,
                sample.OracleEnergy, sample.AbsoluteErrorOrder3,
                sample.AbsoluteErrorOrder4, sample.RelativeErrorOrder4,
                sample.ChromaticErrorOrder4,
                sample.FiniteAndNonNegative, sample.OrderMonotonic,
            };
            lines.Add(string.Join(',', values.Select(FormatCsv)));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string Escape(string value) =>
        value.Contains(',') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static string FormatCsv(object value) => value switch
    {
        bool boolean => boolean ? "1" : "0",
        double number => number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        _ => Escape(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""),
    };
}

/// <summary>
/// Offline comparator for the current global/atlas RGB LUTs against the nine-band CPU oracle.
/// It deliberately builds order 5 only in this diagnostic path; the game renderer remains on
/// its official order-four texture.
/// </summary>
public static class SpectralAtmosphereComparator
{
    public static SpectralComparisonReport Compare(
        CelestialBody body,
        IEnumerable<SpectralEvaluationCoordinate> coordinates,
        SpectralComparisonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(coordinates);
        if (body.Atmosphere is null)
            throw new ArgumentException("The body must provide an atmosphere.", nameof(body));
        return Compare(body.Id, body.Atmosphere, body.Radius, coordinates, options);
    }

    public static SpectralComparisonReport Compare(
        string bodyId,
        AtmosphereModel atmosphere,
        double planetRadius,
        IEnumerable<SpectralEvaluationCoordinate> coordinates,
        SpectralComparisonOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyId);
        ArgumentNullException.ThrowIfNull(atmosphere);
        ArgumentNullException.ThrowIfNull(coordinates);
        options ??= new SpectralComparisonOptions();
        ValidateOptions(options);

        var samples = coordinates.ToArray();
        if (samples.Length == 0)
            throw new ArgumentException("At least one comparison coordinate is required.", nameof(coordinates));

        var profile = new AtmosphereDensityProfile(atmosphere);
        var oracle = SpectralAtmosphereOracle.Build(
            profile, SpectralAtmosphereOracle.ExperimentalOrder,
            options.OracleSampleCount, planetRadius);
        var luts = new Dictionary<int, AtmosphereMultipleScatteringLut>();
        var angular = new Dictionary<int, AtmosphereAngularMultipleScatteringLut>();
        for (int order = 2; order <= SpectralAtmosphereOracle.ExperimentalOrder; order++)
        {
            var lut = AtmosphereMultipleScatteringLut.Build(
                profile, planetRadius, atmosphere.MaxAltitude,
                options.LutWidth, options.LutHeight,
                options.LutIntegrationSteps, options.LutSolarSamples, order);
            luts[order] = lut;
            if (options.BuildAngularAtlas)
            {
                angular[order] = AtmosphereAngularMultipleScatteringLut.Build(
                    profile, lut, planetRadius, atmosphere.MaxAltitude,
                    options.AngularWidth, options.AngularSolarHeight,
                    options.AngularViewHeight, options.AngularMuWidth,
                    options.AngularOpticalDepthSamples);
            }
        }

        var reportSamples = new List<SpectralComparisonSample>(samples.Length);
        foreach (var coordinate in samples)
        {
            double solarSin = System.Math.Sin(coordinate.SolarElevationRadians);
            var oracleRadiance = oracle.Evaluate(
                coordinate.Altitude,
                coordinate.SolarElevationRadians,
                coordinate.ViewCosine,
                coordinate.ViewSunCosine);
            var oracleRgb = oracleRadiance.ToLinearRgb();
            var lut2 = luts[2].Sample(coordinate.Altitude, solarSin);
            var lut3 = luts[3].Sample(coordinate.Altitude, solarSin);
            var lut4 = luts[4].Sample(coordinate.Altitude, solarSin);
            var lut5 = luts[5].Sample(coordinate.Altitude, solarSin);
            Vector3d? angular4 = options.BuildAngularAtlas
                ? angular[4].Sample(coordinate.Altitude, solarSin,
                    coordinate.ViewCosine, coordinate.ViewSunCosine)
                : null;

            bool finite = IsFiniteNonNegative(oracleRgb)
                && IsFiniteNonNegative(lut2)
                && IsFiniteNonNegative(lut3)
                && IsFiniteNonNegative(lut4)
                && IsFiniteNonNegative(lut5)
                && (!angular4.HasValue || IsFiniteNonNegative(angular4.Value));
            bool monotonic = IsMonotonic(lut2, lut3, lut4, lut5);
            reportSamples.Add(new SpectralComparisonSample
            {
                Coordinate = coordinate,
                OracleLinearRgb = oracleRgb,
                LutOrder2 = lut2,
                LutOrder3 = lut3,
                LutOrder4 = lut4,
                LutOrder5Experimental = lut5,
                AngularOrder4 = angular4,
                OracleEnergy = oracleRadiance.Energy,
                AbsoluteErrorOrder3 = MeanAbsoluteError(oracleRgb, lut3),
                AbsoluteErrorOrder4 = MeanAbsoluteError(oracleRgb, lut4),
                RelativeErrorOrder4 = MeanRelativeError(oracleRgb, lut4),
                ChromaticErrorOrder4 = ChromaticError(oracleRgb, lut4),
                FiniteAndNonNegative = finite,
                OrderMonotonic = monotonic,
            });
        }

        return new SpectralComparisonReport
        {
            BodyId = bodyId,
            DataProvenance = oracle.DataProvenance,
            Samples = reportSamples,
            MeanAbsoluteErrorOrder3 = reportSamples.Average(s => s.AbsoluteErrorOrder3),
            MeanAbsoluteErrorOrder4 = reportSamples.Average(s => s.AbsoluteErrorOrder4),
            MeanRelativeErrorOrder4 = reportSamples.Average(s => s.RelativeErrorOrder4),
            MeanChromaticErrorOrder4 = reportSamples.Average(s => s.ChromaticErrorOrder4),
            AllFiniteAndNonNegative = reportSamples.All(s => s.FiniteAndNonNegative),
            AllOrdersMonotonic = reportSamples.All(s => s.OrderMonotonic),
            Order4NotWorseThanOrder3 = reportSamples.Average(s => s.AbsoluteErrorOrder4)
                <= reportSamples.Average(s => s.AbsoluteErrorOrder3),
        };
    }

    private static double MeanAbsoluteError(Vector3d a, Vector3d b) =>
        (System.Math.Abs(a.X - b.X) + System.Math.Abs(a.Y - b.Y)
            + System.Math.Abs(a.Z - b.Z)) / 3.0;

    private static double MeanRelativeError(Vector3d a, Vector3d b) =>
        (Relative(a.X, b.X) + Relative(a.Y, b.Y) + Relative(a.Z, b.Z)) / 3.0;

    private static double Relative(double reference, double actual) =>
        System.Math.Abs(reference - actual) / System.Math.Max(System.Math.Abs(reference), 1e-8);

    private static double ChromaticError(Vector3d reference, Vector3d actual)
    {
        double referenceSum = reference.X + reference.Y + reference.Z;
        double actualSum = actual.X + actual.Y + actual.Z;
        if (referenceSum <= 1e-10 || actualSum <= 1e-10) return 0.0;
        return (System.Math.Abs(reference.X / referenceSum - actual.X / actualSum)
            + System.Math.Abs(reference.Y / referenceSum - actual.Y / actualSum)
            + System.Math.Abs(reference.Z / referenceSum - actual.Z / actualSum)) / 3.0;
    }

    private static bool IsMonotonic(params Vector3d[] values)
    {
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i].X + 1e-12 < values[i - 1].X
                || values[i].Y + 1e-12 < values[i - 1].Y
                || values[i].Z + 1e-12 < values[i - 1].Z)
                return false;
        }
        return true;
    }

    private static bool IsFiniteNonNegative(Vector3d value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z)
        && value.X >= 0.0 && value.Y >= 0.0 && value.Z >= 0.0;

    private static void ValidateOptions(SpectralComparisonOptions options)
    {
        if (options.OracleSampleCount < 8 || options.LutWidth < 2 || options.LutHeight < 2
            || options.LutIntegrationSteps < 4 || options.LutSolarSamples < 4)
            throw new ArgumentOutOfRangeException(nameof(options),
                "Comparison sample counts must be positive and large enough for Simpson integration.");
        if (options.BuildAngularAtlas && (options.AngularWidth < 2
            || options.AngularSolarHeight < 2 || options.AngularViewHeight < 2
            || options.AngularMuWidth < 2 || options.AngularOpticalDepthSamples < 8))
            throw new ArgumentOutOfRangeException(nameof(options),
                "Angular atlas dimensions are too small for comparison.");
    }
}
