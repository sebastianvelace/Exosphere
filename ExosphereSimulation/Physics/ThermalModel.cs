namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// Aerodynamic (convective) heating model for atmospheric re-entry.
/// Computes a convective heat flux from local density and airspeed, integrates
/// each part's temperature against its radiative cooling, and resolves how much
/// of that flux a windward heat shield deflects.
///
/// Modelo de calentamiento por reentrada: flujo convectivo a partir de densidad
/// y velocidad, integración de temperatura con enfriamiento radiativo, y cuánto
/// flujo desvía el escudo térmico orientado al flujo.
/// </summary>
public static class ThermalModel
{
    // ── Heat-shield resolution (sin tocar PartDefinition, propiedad de A) ─────
    //
    // `has_heat_shield` vive en data/parts/*.json pero PartDefinition (de Agente A)
    // todavía no lo deserializa, así que no podemos leerlo directamente desde el
    // sim. Hasta que A exponga una propiedad `HasHeatShield`, identificamos una
    // pieza con escudo por un proxy estable y data-driven sobre los campos que SÍ
    // están cargados: una sección de mando (categoría Command) con una tolerancia
    // térmica reforzada. Esto cubre `starship_command` (2800 K) y
    // `command_pod_mk1` (2400 K, con `has_heat_shield:true`) sin marcar como
    // escudo los motores de alta tolerancia (categoría Engine).
    //
    // `has_heat_shield` lives in data/parts/*.json, but PartDefinition (Agent A's
    // file) does not deserialise it yet, so we cannot read it directly. Until A
    // exposes a `HasHeatShield` property we detect a shielded part with a stable,
    // data-driven proxy over the fields that ARE loaded.

    /// <summary>Heat tolerance (K) at/above which a command part is treated as heat-shielded.</summary>
    public const double HeatShieldToleranceThreshold = 2400.0;

    /// <summary>Minimum flux fraction a perfectly oriented heat shield lets through (ablative/radiative residual).</summary>
    public const double ShieldedFluxFloor = 0.08;

    /// <summary>
    /// Whether <paramref name="part"/> carries a windward heat shield.
    /// Proxy over loaded <see cref="PartDefinition"/> fields until Agent A exposes
    /// the JSON <c>has_heat_shield</c> flag; swap the body for that flag then.
    /// </summary>
    public static bool HasHeatShield(Part part) =>
        part.Definition.Category == PartCategory.Command
        && part.Definition.HeatTolerance >= HeatShieldToleranceThreshold;

    // ── Heat flux ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Convective heat flux (W/m²) — simplified Detra–Kemp–Riddell stagnation-point
    /// model:  q ≈ k · √ρ · v³.  ρ in kg/m³, v in m/s.
    /// </summary>
    public static double ComputeHeatFlux(double density, double velocity)
    {
        if (density <= 0.0 || velocity <= 0.0) return 0.0;
        const double k = 1.83e-4;
        return k * System.Math.Sqrt(density) * System.Math.Pow(velocity, 3);
    }

    /// <summary>
    /// Fraction of the free-stream heat flux that actually reaches a part, given its
    /// shield state and how well that shield faces the flow.
    ///
    /// <para><paramref name="windwardFactor"/> ∈ [0,1] is the alignment of the shield's
    /// windward face with the incoming flow: 1 = shield squarely into the flow
    /// (belly-flop on the heat-shield side), 0 = shield turned away (the bare,
    /// unprotected side meets the plasma).</para>
    ///
    /// An oriented shield deflects most of the flux (down to <see cref="ShieldedFluxFloor"/>);
    /// a shield turned away offers no protection. Parts without a shield always take
    /// the full flux.
    /// </summary>
    public static double EffectiveFluxFactor(bool hasShield, double windwardFactor)
    {
        if (!hasShield) return 1.0;
        double w = System.Math.Clamp(windwardFactor, 0.0, 1.0);
        // Shield squarely into the flow → ShieldedFluxFloor; turned away → 1.0 (no help).
        return 1.0 - (1.0 - ShieldedFluxFloor) * w;
    }

    // ── Temperature integration ────────────────────────────────────────────────

    /// <summary>
    /// Integrates one part's temperature (K) for <paramref name="dt"/> seconds under
    /// an incident <paramref name="heatFlux"/> (W/m²), balancing convective heating
    /// against grey-body radiative cooling. Floors at 3 K so it never goes unphysical.
    /// </summary>
    public static double UpdateTemperature(
        double currentTemp,
        double heatFlux,
        double dt,
        double partMass = 100.0)
    {
        const double specificHeat    = 800.0;   // J/(kg·K)
        const double emissivity      = 0.9;
        const double stefanBoltzmann = 5.67e-8; // W/(m²·K⁴)
        const double surfaceArea     = 1.0;     // m² (approx)

        double thermalMass = System.Math.Max(1.0, partMass) * specificHeat;
        double radiation   = emissivity * stefanBoltzmann
                             * System.Math.Pow(currentTemp, 4) * surfaceArea;
        double netPower    = heatFlux * surfaceArea - radiation;
        double dTemp       = (netPower / thermalMass) * dt;

        return System.Math.Max(3.0, currentTemp + dTemp);
    }

    /// <summary>
    /// Applies an incident heat flux to a part for <paramref name="dt"/> seconds,
    /// accumulating <see cref="Part.Temperature"/>. Returns true if the part is now
    /// hotter than its <see cref="PartDefinition.HeatTolerance"/>.
    /// The flux is the value already reaching the part (shield attenuation, if any,
    /// must be applied by the caller via <see cref="EffectiveFluxFactor"/>).
    /// </summary>
    public static bool ApplyHeat(Part part, double heatFlux, double dt)
    {
        part.Temperature = UpdateTemperature(part.Temperature, heatFlux, dt, part.CurrentMass);
        return part.Temperature > part.Definition.HeatTolerance;
    }

    /// <summary>
    /// Windward alignment ∈ [0,1] of a part's shield with the incoming flow.
    /// Starship re-enters belly-first: the heat shield is on the ventral face, modelled
    /// here as the vessel's local <c>-Y</c> (down) side meeting the airflow broadside.
    /// 1 ⇒ the shielded belly is squarely into the flow; 0 ⇒ the bare side leads.
    ///
    /// <paramref name="flowDirLocal"/> is the airflow direction expressed in the vessel's
    /// local frame (i.e. <c>orientation⁻¹ · surfaceVelocityDir</c>). The shield faces the
    /// flow when that direction points toward local <c>+Y</c> (air coming "up" into the
    /// down-facing belly).
    /// </summary>
    public static double WindwardFactor(Vector3d flowDirLocal)
    {
        if (flowDirLocal.Magnitude < 1e-9) return 0.0;
        double along = flowDirLocal.Normalized.Dot(Vector3d.Up);
        return System.Math.Clamp(along, 0.0, 1.0);
    }
}
