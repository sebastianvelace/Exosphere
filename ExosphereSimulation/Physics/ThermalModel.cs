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
    /// <summary>
    /// Whether <paramref name="part"/> carries a windward heat shield.
    /// Backed directly by the JSON <c>has_heat_shield</c> flag deserialised into
    /// <see cref="PartDefinition.HasHeatShield"/>.
    /// </summary>
    public static bool HasHeatShield(Part part) => part.Definition.HasHeatShield;

    // ── Heat flux ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Convective heat flux (W/m²) — Sutton-Graves stagnation-point approximation:
    /// q ≈ k·√(ρ/Rn)·v³. ρ in kg/m³, nose radius Rn in m, v in m/s.
    /// </summary>
    public static double ComputeHeatFlux(double density, double velocity, double noseRadius = 1.0)
    {
        if (density <= 0.0 || velocity <= 0.0 || noseRadius <= 0.0) return 0.0;
        const double k = 1.83e-4;
        return k * System.Math.Sqrt(density / noseRadius) * System.Math.Pow(velocity, 3);
    }

    // ── Temperature integration ────────────────────────────────────────────────

    /// <summary>Grey-body emissivity of the outer surface.</summary>
    public const double Emissivity = 0.9;

    /// <summary>Stefan-Boltzmann constant, W/(m²·K⁴).</summary>
    public const double StefanBoltzmann = 5.67e-8;

    /// <summary>Largest sub-step the stiff T⁴ radiation term stays stable at (s).</summary>
    private const double MaxSubStep = 0.02;

    /// <summary>Cap on sub-steps per call, so a long warp tick cannot stall the sim.</summary>
    private const int MaxSubSteps = 256;

    /// <summary>Radiative-equilibrium temperature (K) of a surface under <paramref name="flux"/>.</summary>
    public static double RadiativeEquilibrium(double flux) =>
        flux <= 0.0 ? 0.0 : System.Math.Pow(flux / (Emissivity * StefanBoltzmann), 0.25);

    /// <summary>
    /// Advances the two-node thermal state of a surface: an outer TPS skin that meets the
    /// plasma, and the load-bearing structure behind it.
    ///
    /// <para>Skin: absorbs the incident flux, radiates it back as a grey body, and leaks a
    /// little inward. Because its heat capacity is small, it climbs within seconds to the
    /// radiative equilibrium where re-radiation balances heating — around 1700 K at peak
    /// entry. That is the tiles doing their job, not the vehicle failing.</para>
    ///
    /// <para>Structure: over the fraction the shield covers, it only receives what conducts
    /// through the TPS — a few kW/m² out of hundreds. Over the fraction left bare (no shield,
    /// or the shield turned away from the flow) it takes the full flux on naked metal and
    /// climbs roughly twenty times faster. That difference is the whole point of a heat
    /// shield, and it is why attitude decides whether an entry is survivable.</para>
    /// </summary>
    /// <param name="skinTemp">Current TPS face temperature (K).</param>
    /// <param name="structureTemp">Current structure temperature (K).</param>
    /// <param name="flux">Incident convective flux (W/m²) in the free stream.</param>
    /// <param name="dt">Time to advance (s).</param>
    /// <param name="shieldedFraction">Fraction of the exposed area actually covered by TPS facing the flow, ∈ [0,1].</param>
    /// <param name="tpsCapacityPerArea">TPS heat capacity per area (J/(m²·K)).</param>
    /// <param name="tpsConductance">TPS→structure conductance (W/(m²·K)).</param>
    /// <param name="structureCapacityPerArea">Structure skin heat capacity per area (J/(m²·K)).</param>
    public static (double Skin, double Structure) StepTwoNode(
        double skinTemp,
        double structureTemp,
        double flux,
        double dt,
        double shieldedFraction,
        double tpsCapacityPerArea,
        double tpsConductance,
        double structureCapacityPerArea)
    {
        if (dt <= 0.0) return (skinTemp, structureTemp);

        double s    = System.Math.Clamp(shieldedFraction, 0.0, 1.0);
        double cTps = System.Math.Max(1.0, tpsCapacityPerArea);
        double cStr = System.Math.Max(1.0, structureCapacityPerArea);
        double u    = System.Math.Max(0.0, tpsConductance);

        int steps = (int)System.Math.Ceiling(dt / MaxSubStep);
        steps = System.Math.Clamp(steps, 1, MaxSubSteps);
        double h = dt / steps;

        double ts = skinTemp;
        double tb = structureTemp;

        for (int i = 0; i < steps; i++)
        {
            double radSkin = Emissivity * StefanBoltzmann * System.Math.Pow(ts, 4);
            double radStr  = Emissivity * StefanBoltzmann * System.Math.Pow(tb, 4);
            double inward  = u * (ts - tb);

            // The skin only exists where TPS covers; elsewhere the plasma meets bare structure.
            double skinPower = flux - radSkin - inward;
            double strPower  = s * inward + (1.0 - s) * (flux - radStr);

            ts = System.Math.Max(3.0, ts + skinPower / cTps * h);
            tb = System.Math.Max(3.0, tb + strPower  / cStr * h);
        }

        return (ts, tb);
    }

    /// <summary>
    /// Applies the free-stream <paramref name="heatFlux"/> to a part for <paramref name="dt"/>
    /// seconds through the two-node model, accumulating irreversible burn-through on the
    /// STRUCTURE. Returns true once the part has burned through.
    ///
    /// <paramref name="shieldedFraction"/> is how much of the exposed face is protected: the
    /// part's shield flag times how squarely that shield meets the flow.
    /// </summary>
    public static bool ApplyHeat(Part part, double heatFlux, double dt, double shieldedFraction)
    {
        var def = part.Definition;

        (part.SkinTemperature, part.Temperature) = StepTwoNode(
            part.SkinTemperature,
            part.Temperature,
            heatFlux,
            dt,
            def.HasHeatShield ? shieldedFraction : 0.0,
            def.TpsHeatCapacityPerArea,
            def.TpsConductance,
            def.StructureHeatCapacityPerArea);

        double ratio = part.ThermalRatio;
        if (ratio > 1.0)
        {
            double overLimit = ratio - 1.0;
            part.ThermalDamage = System.Math.Clamp(part.ThermalDamage + overLimit * dt * 0.5, 0.0, 1.0);
        }

        // Burn-through is loss of material and is irreversible. Cooling changes temperature,
        // not the amount of structure that has already charred or ablated away.
        return part.IsThermallyBurned;
    }

    /// <summary>
    /// Windward alignment ∈ [0,1] of a part's shield with the incoming flow.
    /// Starship re-enters belly-first: the heat shield is on the ventral face, modelled
    /// here as the vessel's local <c>-X</c> side. This matches the black tile geometry in
    /// VesselRenderer instead of treating the engine end as the heat shield.
    /// 1 ⇒ the shielded belly is squarely into the flow; 0 ⇒ the bare side leads.
    ///
    /// <paramref name="flowDirLocal"/> is the airflow direction expressed in the vessel's
    /// local frame (i.e. <c>orientation⁻¹ · surfaceVelocityDir</c>). The shield faces the
    /// flow when the direction of travel through the air points toward local <c>-X</c>.
    /// </summary>
    public static double WindwardFactor(Vector3d flowDirLocal)
    {
        if (flowDirLocal.Magnitude < 1e-9) return 0.0;
        double along = flowDirLocal.Normalized.Dot(-Vector3d.Right);
        return System.Math.Clamp(along, 0.0, 1.0);
    }
}
