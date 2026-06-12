namespace Exosphere.Simulation.Parts;

using Exosphere.Simulation.Math;

public class Part
{
    public string InstanceId { get; } = Guid.NewGuid().ToString();
    public PartDefinition Definition { get; }

    public Part(PartDefinition def)
    {
        Definition = def;
        ResetResources();
    }

    // ── Recursos actuales ─────────────────────────────────────────────────
    public double LiquidFuel      { get; set; }
    public double Oxidizer        { get; set; }
    public double SolidFuel       { get; set; }
    public double Monopropellant  { get; set; }
    public double ElectricCharge  { get; set; }

    // ── Estado físico ─────────────────────────────────────────────────────
    public double Temperature     { get; set; } = 290.0;  // K
    public bool   IsBroken        { get; set; }
    public bool   IsDeployed      { get; set; }
    public bool   IsStagingActive { get; set; } = true;

    // ── Control del motor ─────────────────────────────────────────────────
    public double   ThrottleLevel { get; set; }          // [0, 1]
    public Vector3d GimbalOffset  { get; set; } = Vector3d.Zero;  // deflexión normalizada

    // ── Masa actual (seca + propelante) ───────────────────────────────────
    public double CurrentMass =>
        Definition.MassDry + LiquidFuel + Oxidizer + SolidFuel + Monopropellant;

    // ── Inicializar recursos al máximo de capacidad ───────────────────────
    public void ResetResources()
    {
        LiquidFuel     = Definition.FuelCapacityLF;
        Oxidizer       = Definition.FuelCapacityOx;
        SolidFuel      = Definition.FuelCapacitySolid;
        Monopropellant = Definition.FuelCapacityMono;
        ElectricCharge = Definition.ECCapacity;
    }

    private const double SeaLevelPressurePa = 101_325.0;

    /// <summary>
    /// Pressure-corrected thrust magnitude (N) for this engine at the given ambient
    /// pressure (Pa).  A rocket engine's thrust rises with altitude because the
    /// pressure term in the thrust equation falls:
    ///     F(p) = F_vac − (p / p₀) · (F_vac − F_sl)
    /// so F = F_sl at sea level (p = p₀) and F = F_vac in vacuum (p = 0).
    /// Falls back to vacuum thrust when no sea-level figure is provided.
    /// </summary>
    public double GetThrustMagnitude(double ambientPressure = 0.0)
    {
        double fVac = Definition.ThrustVac;
        double fSL  = Definition.ThrustSL > 0.0 ? Definition.ThrustSL : fVac;
        double pf   = System.Math.Clamp(ambientPressure / SeaLevelPressurePa, 0.0, 1.0);
        double f    = fVac - pf * (fVac - fSL);
        return System.Math.Max(0.0, f) * ThrottleLevel;
    }

    // ── Vector de empuje en espacio local de la pieza (+Y = arriba) ───────
    // Overload sin presión: usa empuje de vacío (compatibilidad).
    public Vector3d GetThrustVector() => GetThrustVector(0.0);

    /// <summary>
    /// Thrust vector in the part's local frame (+Y = up), pressure-corrected and gimballed.
    /// </summary>
    public Vector3d GetThrustVector(double ambientPressure)
    {
        if (Definition.Category != PartCategory.Engine
            || IsBroken || !IsStagingActive || ThrottleLevel <= 0.0)
            return Vector3d.Zero;

        double thrust = GetThrustMagnitude(ambientPressure);
        if (thrust <= 0.0) return Vector3d.Zero;

        // Gimbal: GimbalOffset.{X,Z} ∈ [-1,1] is the normalized deflection of each axis.
        // The actual deflection angle is (offset · GimbalRange) in degrees; the thrust
        // direction is the unit vector tilted off +Y by that angle.
        double gimbalRad = Definition.GimbalRange * MathUtils.DEG_TO_RAD;
        double ax = System.Math.Clamp(GimbalOffset.X, -1.0, 1.0) * gimbalRad;
        double az = System.Math.Clamp(GimbalOffset.Z, -1.0, 1.0) * gimbalRad;
        var dir = new Vector3d(System.Math.Sin(ax), 1.0, System.Math.Sin(az)).Normalized;
        return dir * thrust;
    }

    // ── Consumir propelante por dt segundos. Retorna false si se agota. ──
    public bool ConsumePropellant(double dt, double ambientPressure = 0.0)
    {
        if (Definition.Category != PartCategory.Engine
            || ThrottleLevel <= 0.0 || IsBroken || !IsStagingActive)
            return true;

        // ISP interpolado por presión (vac ↔ sl).
        // pf = 0 en vacío (p=0) → Isp_vac;  pf = 1 a nivel del mar → Isp_sl.
        double pressureFraction = System.Math.Clamp(ambientPressure / SeaLevelPressurePa, 0.0, 1.0);
        double isp = Definition.IspVac + (Definition.IspSL - Definition.IspVac) * pressureFraction;
        if (isp < 1.0) return false;

        // Flujo másico ṁ = F(p) / (Isp(p)·g₀), g₀ = 9.80665 m/s².
        // Se usa el empuje corregido por presión para que ṁ sea consistente
        // con el empuje realmente producido (GetThrustMagnitude).
        double thrust       = GetThrustMagnitude(ambientPressure);
        double massFlowRate = thrust / (isp * 9.80665);  // kg/s

        var fuelType = Definition.FuelTypeStr.ToLowerInvariant();

        if (fuelType.Contains("liquidfuel") || fuelType.Contains("liquid_fuel+oxidizer") || fuelType.Contains("liquidfuelandoxidizer"))
        {
            // Ratio LF:Ox ≈ 9:11 por masa
            double lfRate = massFlowRate * (9.0 / 20.0);
            double oxRate = massFlowRate * (11.0 / 20.0);
            if (LiquidFuel < lfRate * dt || Oxidizer < oxRate * dt) return false;
            LiquidFuel -= lfRate * dt;
            Oxidizer   -= oxRate * dt;
        }
        else if (fuelType.Contains("solid"))
        {
            if (SolidFuel < massFlowRate * dt) return false;
            SolidFuel -= massFlowRate * dt;
        }
        else if (fuelType.Contains("mono"))
        {
            if (Monopropellant < massFlowRate * dt) return false;
            Monopropellant -= massFlowRate * dt;
        }
        return true;
    }
}
