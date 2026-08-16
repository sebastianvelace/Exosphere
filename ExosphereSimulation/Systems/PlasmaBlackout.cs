namespace Exosphere.Simulation.Systems;

/// <summary>
/// Flight condition sampled once per tick and handed to <see cref="PlasmaBlackoutModel"/>.
/// All quantities are free-stream SI values already computed elsewhere in the simulation,
/// so the blackout model adds no new physics to the trajectory — it only reads it.
/// </summary>
public readonly record struct PlasmaBlackoutInput
{
    /// <summary>Free-stream convective heat flux (W/m²), i.e. Sutton-Graves stagnation flux.</summary>
    public double HeatFluxWm2 { get; init; }

    /// <summary>Local atmospheric density (kg/m³).</summary>
    public double DensityKgM3 { get; init; }

    /// <summary>Airspeed relative to the rotating atmosphere (m/s).</summary>
    public double AirspeedMs { get; init; }

    /// <summary>Vacuum / no-atmosphere condition: can never produce a blackout.</summary>
    public static PlasmaBlackoutInput None => default;
}

/// <summary>
/// Radio blackout caused by the ionised shock layer of a hypersonic atmospheric entry.
///
/// PHYSICS
/// -------
/// A vehicle entering at orbital speed drives a bow shock hot enough (≈7 000–11 000 K)
/// to thermally ionise the air behind it. The resulting free-electron sheath has a plasma
/// frequency f_p ≈ 8.98·10³·√(n_e) Hz (n_e in cm⁻³); when f_p exceeds the carrier frequency
/// the sheath reflects rather than transmits, and the vehicle goes silent. For S-band
/// (~2.2 GHz) that needs n_e ≈ 6·10¹⁰ cm⁻³.
///
/// Solving the Saha equation per frame would be both expensive and fragile, so this model
/// uses the three free-stream quantities the simulation already integrates, each standing
/// in for one physical precondition:
///
///  1. <b>Airspeed</b> — the ionisation switch. Post-shock temperature scales with v², and
///     air ionises appreciably only above ≈7 000 K, which needs an entry speed of roughly
///     5 km/s. This is the term that makes the model correctly say NO for a slow ballistic
///     hop: Mercury-Redstone reentered at ≈2.1 km/s and never lost the link, while orbital
///     Mercury (Friendship 7, ≈7.8 km/s) blacked out for about 4½ minutes.
///  2. <b>Density</b> — there must be a continuum shock layer at all. Above ≈90 km
///     (ρ ≲ 2·10⁻⁶ kg/m³) the flow is rarefied enough that no attached ionised sheath forms.
///  3. <b>Heat flux</b> — a single scalar that already folds √ρ and v³ together, so it tracks
///     the energy actually deposited in the shock layer and therefore the electron density.
///
/// THRESHOLDS AND THE BAND THEY TARGET
/// -----------------------------------
/// The engage set (v ≥ 5 000 m/s, ρ ≥ 2·10⁻⁶ kg/m³, q ≥ 1.0·10⁵ W/m²) first closes at about
/// 80–85 km for a 7.5 km/s entry with a blunt ≈4.5 m nose radius, and the clear set
/// (v &lt; 4 000 m/s or q &lt; 5·10⁴ W/m²) opens again at about 45–50 km, once aerodynamic
/// braking has taken the vehicle below ionisation speed. That is the ≈90→40 km, ≈4–6 minute
/// band reported for Gemini/Apollo/Shuttle orbital entries. A suborbital profile never
/// crosses the 5 km/s engage gate at any altitude, so it never blacks out.
///
/// HYSTERESIS
/// ----------
/// Separate engage and clear sets, plus a short entry dwell and a minimum hold time, so the
/// state cannot chatter when a quantity oscillates across a single threshold. The model is
/// pure state — it changes no force, coefficient or heat flux.
/// </summary>
public sealed class PlasmaBlackoutModel
{
    /// <summary>Airspeed at which shock-layer ionisation becomes radio-opaque (m/s).</summary>
    public const double EngageAirspeedMs = 5_000.0;

    /// <summary>Airspeed below which the sheath recombines and the link returns (m/s).</summary>
    public const double ClearAirspeedMs = 4_000.0;

    /// <summary>Minimum density for a continuum (non-rarefied) shock layer (kg/m³), ≈90 km.</summary>
    public const double EngageDensityKgM3 = 2.0e-6;

    /// <summary>Density below which the sheath cannot be sustained (kg/m³).</summary>
    public const double ClearDensityKgM3 = 5.0e-7;

    /// <summary>Convective flux standing in for the electron density needed at S-band (W/m²).</summary>
    public const double EngageHeatFluxWm2 = 1.0e5;

    /// <summary>Convective flux below which the sheath is transparent again (W/m²).</summary>
    public const double ClearHeatFluxWm2 = 5.0e4;

    /// <summary>Engage conditions must hold this long before the link is declared lost (s).</summary>
    public const double EngageDwellSeconds = 0.5;

    /// <summary>Once lost, the link stays lost at least this long (s).</summary>
    public const double MinimumHoldSeconds = 2.0;

    private double _engageDwell;

    /// <summary>True while the ionised sheath is blocking the radio link.</summary>
    public bool IsBlackedOut { get; private set; }

    /// <summary>Seconds spent inside the current blackout (0 when not blacked out).</summary>
    public double ElapsedSeconds { get; private set; }

    /// <summary>Longest blackout observed since the last <see cref="Reset"/> (s).</summary>
    public double LongestBlackoutSeconds { get; private set; }

    public PlasmaBlackoutState CaptureState() => new()
    {
        IsBlackedOut = IsBlackedOut,
        ElapsedSeconds = ElapsedSeconds,
        LongestBlackoutSeconds = LongestBlackoutSeconds,
        EngageDwellSeconds = _engageDwell,
    };

    public void RestoreState(PlasmaBlackoutState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        IsBlackedOut = state.IsBlackedOut;
        ElapsedSeconds = state.ElapsedSeconds;
        LongestBlackoutSeconds = state.LongestBlackoutSeconds;
        _engageDwell = state.EngageDwellSeconds;
    }

    /// <summary>Advances the blackout state by <paramref name="dt"/> seconds.</summary>
    public void Tick(double dt, in PlasmaBlackoutInput condition)
    {
        if (!double.IsFinite(dt) || dt < 0.0) dt = 0.0;

        if (IsBlackedOut)
        {
            ElapsedSeconds += dt;
            if (ElapsedSeconds > LongestBlackoutSeconds)
                LongestBlackoutSeconds = ElapsedSeconds;

            if (ElapsedSeconds >= MinimumHoldSeconds && MeetsClearCondition(condition))
            {
                IsBlackedOut = false;
                ElapsedSeconds = 0.0;
                _engageDwell = 0.0;
            }
            return;
        }

        if (MeetsEngageCondition(condition))
        {
            _engageDwell += dt;
            if (_engageDwell >= EngageDwellSeconds)
            {
                IsBlackedOut = true;
                ElapsedSeconds = 0.0;
            }
        }
        else
        {
            _engageDwell = 0.0;
        }
    }

    /// <summary>Clears all blackout state (new flight, load, or leaving the atmosphere).</summary>
    public void Reset()
    {
        IsBlackedOut = false;
        ElapsedSeconds = 0.0;
        LongestBlackoutSeconds = 0.0;
        _engageDwell = 0.0;
    }

    /// <summary>All three ionisation preconditions are met simultaneously.</summary>
    public static bool MeetsEngageCondition(in PlasmaBlackoutInput c) =>
        IsUsable(c)
        && c.AirspeedMs >= EngageAirspeedMs
        && c.DensityKgM3 >= EngageDensityKgM3
        && c.HeatFluxWm2 >= EngageHeatFluxWm2;

    /// <summary>Any one of the sustaining conditions has fallen away.</summary>
    public static bool MeetsClearCondition(in PlasmaBlackoutInput c) =>
        !IsUsable(c)
        || c.AirspeedMs < ClearAirspeedMs
        || c.DensityKgM3 < ClearDensityKgM3
        || c.HeatFluxWm2 < ClearHeatFluxWm2;

    private static bool IsUsable(in PlasmaBlackoutInput c) =>
        double.IsFinite(c.AirspeedMs)
        && double.IsFinite(c.DensityKgM3)
        && double.IsFinite(c.HeatFluxWm2);
}
