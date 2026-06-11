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

    // ── Vector de empuje en espacio local de la pieza (+Y = arriba) ───────
    public Vector3d GetThrustVector()
    {
        if (Definition.Category != PartCategory.Engine
            || IsBroken || !IsStagingActive || ThrottleLevel <= 0.0)
            return Vector3d.Zero;

        double thrust = Definition.ThrustVac * ThrottleLevel;
        // Aplicar gimbal: deflexión en X y Z, el Y es el componente principal
        var dir = new Vector3d(
            GimbalOffset.X * System.Math.Sin(GimbalOffset.X),
            1.0,
            GimbalOffset.Z * System.Math.Sin(GimbalOffset.Z)).Normalized;
        return dir * thrust;
    }

    // ── Consumir propelante por dt segundos. Retorna false si se agota. ──
    public bool ConsumePropellant(double dt, double ambientPressure = 0.0)
    {
        if (Definition.Category != PartCategory.Engine
            || ThrottleLevel <= 0.0 || IsBroken || !IsStagingActive)
            return true;

        // ISP interpolado por presión (vac a sl)
        double pressureFraction = System.Math.Clamp(ambientPressure / 101325.0, 0.0, 1.0);
        double isp = Definition.IspVac + (Definition.IspSL - Definition.IspVac) * pressureFraction;
        if (isp < 1.0) return false;

        double thrust       = Definition.ThrustVac * ThrottleLevel;
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
