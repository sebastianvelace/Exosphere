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
    public double ThermalDamage   { get; set; } = 0.0;    // 0..1 progressive burn-through
    public bool   IsBroken        { get; set; }
    public bool   IsDeployed      { get; set; }
    public bool   IsStagingActive { get; set; } = true;
    public double ThermalRatio => Definition.HeatTolerance > 0.0
        ? Temperature / Definition.HeatTolerance
        : 0.0;
    public bool IsThermallyBurned => ThermalDamage >= 1.0;

    // ── Control del motor ─────────────────────────────────────────────────
    public double   ThrottleLevel { get; set; }          // [0, 1]
    public Vector3d GimbalOffset  { get; set; } = Vector3d.Zero;  // deflexión normalizada
    private double _activeEngineFraction = 1.0;

    /// <summary>
    /// Fraction of the engines represented by this aggregate part that are selected.
    /// Defaults to one (the complete cluster). EDL can select a discrete 1/2/3-engine
    /// centre cluster without pretending all six Raptors deep-throttle together.
    /// </summary>
    public double ActiveEngineFraction
    {
        get => _activeEngineFraction;
        set => _activeEngineFraction = System.Math.Clamp(value, 0.0, 1.0);
    }

    public int SelectedEngineCount => Definition.Category == PartCategory.Engine
        ? (int)System.Math.Round(System.Math.Max(1, Definition.EngineCount) * ActiveEngineFraction)
        : 0;

    public void SelectEngineCount(int count)
    {
        int total = System.Math.Max(1, Definition.EngineCount);
        ActiveEngineFraction = System.Math.Clamp(count, 0, total) / (double)total;
    }

    // ── Masa actual (seca + propelante) ───────────────────────────────────
    public double CurrentMass =>
        Definition.MassDry + LiquidFuel + Oxidizer + SolidFuel + Monopropellant;

    // ── Deep-throttle floor (Raptor 2 ≈ 40 %) ─────────────────────────────
    /// <summary>
    /// Returns <paramref name="requested"/> snapped UP to the engine's documented minimum
    /// throttle (<see cref="PartDefinition.MinThrottle"/>) — but only when it is genuinely
    /// firing: a request of (near) 0 is a deliberate shutdown and is left at 0, never floored.
    /// A real Raptor either runs at ≥40 % or is off; it does not hover at 12 %. Ascent and EDL
    /// opt into this; EDL combines the floor with discrete engine selection for lower thrust.
    /// </summary>
    public double ApplyThrottleFloor(double requested)
    {
        if (Definition.Category != PartCategory.Engine) return requested;
        double floor = Definition.MinThrottle;
        if (floor <= 0.0 || requested <= 1e-3) return requested;          // off stays off
        return System.Math.Clamp(System.Math.Max(requested, floor), 0.0, 1.0);
    }

    // ── Inicializar recursos al máximo de capacidad ───────────────────────
    public void ResetResources()
    {
        LiquidFuel     = Definition.FuelCapacityLF;
        Oxidizer       = Definition.FuelCapacityOx;
        SolidFuel      = Definition.FuelCapacitySolid;
        Monopropellant = Definition.FuelCapacityMono;
        ElectricCharge = Definition.ECCapacity;
    }

    // ── Startup / shutdown spool transient ────────────────────────────────
    // A real Raptor cannot step its thrust instantly: the turbopumps spin up over a fraction
    // of a second and the chamber pressure builds before full thrust. We model that with a
    // first-order ramp of ThrottleLevel toward a commanded value. Startup at ~2.0/s reaches
    // 100 % in ~0.5 s; shutdown uses a faster 5.0/s (~0.2 s) so cutoff does not keep injecting
    // landing impulse as long as chamber-pressure buildup. Callers that want an instant set
    // still just assign ThrottleLevel directly.
    public const double SpoolRate = 2.0;           // startup throttle units per second
    public const double ShutdownSpoolRate = 5.0;   // shutdown throttle units per second

    /// <summary>
    /// Advances <see cref="ThrottleLevel"/> toward <paramref name="commanded"/> at no more than
    /// the direction-specific spool rate over <paramref name="dt"/>. Returns the new level.
    /// </summary>
    public double SpoolToward(double commanded, double dt)
    {
        commanded = System.Math.Clamp(commanded, 0.0, 1.0);
        double delta   = commanded - ThrottleLevel;
        double rate = delta < 0.0 ? ShutdownSpoolRate : SpoolRate;
        double maxStep = rate * dt;
        if (System.Math.Abs(delta) <= maxStep) ThrottleLevel = commanded;
        else ThrottleLevel += System.Math.Sign(delta) * maxStep;
        return ThrottleLevel;
    }

    private const double SeaLevelPressurePa = 101_325.0;

    /// <summary>
    /// Pressure-corrected specific impulse (s): Isp_vac in vacuum, Isp_sl at Earth
    /// sea level, and linear extrapolation above one atmosphere. The extrapolation is
    /// intentionally not capped so dense-atmosphere back-pressure can cause flameout.
    /// </summary>
    public double GetIsp(double ambientPressure = 0.0)
    {
        double pf = System.Math.Max(0.0, ambientPressure / SeaLevelPressurePa);
        return System.Math.Max(0.0,
            Definition.IspVac + (Definition.IspSL - Definition.IspVac) * pf);
    }

    /// <summary>
    /// Current propellant mass flow (kg/s) this engine is drawing: ṁ = F(p)/(Isp(p)·g₀),
    /// using the pressure-corrected thrust at the present throttle. 0 when not firing.
    /// </summary>
    public double GetMassFlow(double ambientPressure = 0.0)
    {
        if (Definition.Category != PartCategory.Engine
            || IsBroken || !IsStagingActive || ThrottleLevel <= 0.0)
            return 0.0;
        double isp = GetIsp(ambientPressure);
        if (isp < 1.0) return 0.0;
        return GetThrustMagnitude(ambientPressure) / (isp * 9.80665);
    }

    /// <summary>
    /// Pressure-corrected thrust magnitude (N) for this engine at the given ambient
    /// pressure (Pa).  A rocket engine's thrust rises with altitude because the
    /// pressure term in the thrust equation falls:
    ///     F(p) = F_vac − (p / p₀) · (F_vac − F_sl)
    /// so F = F_sl at sea level (p = p₀) and F = F_vac in vacuum (p = 0).
    /// Falls back to vacuum thrust when no sea-level figure is provided.
    /// </summary>
    public double GetThrustMagnitude(double ambientPressure = 0.0)
        => GetFullThrottleThrustMagnitude(ambientPressure) * ThrottleLevel;

    /// <summary>Pressure-corrected thrust of the selected engines at 100% throttle (N).</summary>
    public double GetFullThrottleThrustMagnitude(double ambientPressure = 0.0)
        => GetRatedFullThrottleThrustMagnitude(ambientPressure) * ActiveEngineFraction;

    /// <summary>Pressure-corrected rated thrust of the complete represented cluster.</summary>
    public double GetRatedFullThrottleThrustMagnitude(double ambientPressure = 0.0)
    {
        double fVac = Definition.ThrustVac;
        double fSL  = Definition.ThrustSL > 0.0 ? Definition.ThrustSL : fVac;
        // Do not cap at one atmosphere. Dense worlds such as Venus impose far more
        // back-pressure than Earth, eventually reducing net nozzle thrust to zero.
        double pf   = System.Math.Max(0.0, ambientPressure / SeaLevelPressurePa);
        double f    = fVac - pf * (fVac - fSL);
        return System.Math.Max(0.0, f);
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
        double pressureFraction = System.Math.Max(0.0, ambientPressure / SeaLevelPressurePa);
        double isp = System.Math.Max(0.0,
            Definition.IspVac + (Definition.IspSL - Definition.IspVac) * pressureFraction);
        if (isp < 1.0) return false;

        // Flujo másico ṁ = F(p) / (Isp(p)·g₀), g₀ = 9.80665 m/s².
        // Se usa el empuje corregido por presión para que ṁ sea consistente
        // con el empuje realmente producido (GetThrustMagnitude).
        double thrust       = GetThrustMagnitude(ambientPressure);
        double massFlowRate = thrust / (isp * 9.80665);  // kg/s

        var fuelType = Definition.FuelTypeStr.ToLowerInvariant();

        if (fuelType.Contains("liquidfuel") || fuelType.Contains("liquid_fuel+oxidizer") || fuelType.Contains("liquidfuelandoxidizer"))
        {
            // Reparte ṁ entre LF y Ox según la proporción REALMENTE cargada en la pieza,
            // de modo que el O/F del motor (p. ej. 3.55 para Raptor) se respete y ambos
            // recursos se agoten juntos. (Antes se usaba un 9:11 fijo → O/F ≈ 1.22, erróneo.)
            // Esto deja este camino coherente con PartGraph.ConsumePropellant, la ruta
            // autoritativa que invoca Vessel.Tick.
            double total  = LiquidFuel + Oxidizer;
            double lfFrac = Definition.MixtureRatio > 0.0
                ? 1.0 / (1.0 + Definition.MixtureRatio)
                : total > 1e-9 ? LiquidFuel / total : 0.45;
            double lfRate = massFlowRate * lfFrac;
            double oxRate = massFlowRate * (1.0 - lfFrac);
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
