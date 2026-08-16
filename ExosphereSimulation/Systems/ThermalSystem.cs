namespace Exosphere.Simulation.Systems;

public class ThermalSystem
{
    public double TemperatureK  { get; private set; } = 293.0;  // 20°C inicial
    public double MinSafeTemp   => 253.0;   // -20°C
    public double MaxSafeTemp   => 353.0;   // 80°C

    public bool HotAlert  { get; private set; }
    public bool ColdAlert { get; private set; }

    private const double SpaceBgTemp   = 3.0;     // K fondo cósmico
    private const double SolarHeatFlux = 1361.0;  // W/m² a 1 AU
    private const double VehicleArea   = 200.0;   // m² área expuesta
    private const double Emissivity    = 0.85;
    private const double SolarAbsorb   = 0.25;
    private const double ThermalMass   = 50000.0; // J/K
    private const double Boltzmann     = 5.67e-8;

    /// <summary>
    /// Fraction of free-stream aero heat flux that leaks past TPS into the cabin
    /// thermal mass. Real vehicles are far better sealed; this is a gameplay dial
    /// so peak heating can drive HotAlert without requiring multi-hour soaks.
    /// </summary>
    private const double AeroCabinLeakFraction = 0.015;

    // The interest adapter may ask for a deadline between committed system ticks. Keep
    // the inputs that produced the current thermal sample so the projection uses the
    // same balance equation as Tick without advancing or mutating the system.
    private bool _hasLastSample;
    private double _lastSolarVisibility;
    private bool _lastInAtmosphere;
    private double _lastAtmosphericTemp;
    private double _lastAeroHeatFluxWm2;
    private SystemsMissionPhase _lastPhase;

    public ThermalState CaptureState() => new()
    {
        TemperatureK = TemperatureK,
        HasLastSample = _hasLastSample,
        LastSolarVisibility = _lastSolarVisibility,
        LastInAtmosphere = _lastInAtmosphere,
        LastAtmosphericTemp = _lastAtmosphericTemp,
        LastAeroHeatFluxWm2 = _lastAeroHeatFluxWm2,
        LastPhase = _lastPhase,
    };

    public void RestoreState(ThermalState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        TemperatureK = state.TemperatureK;
        _hasLastSample = state.HasLastSample;
        _lastSolarVisibility = state.LastSolarVisibility;
        _lastInAtmosphere = state.LastInAtmosphere;
        _lastAtmosphericTemp = state.LastAtmosphericTemp;
        _lastAeroHeatFluxWm2 = state.LastAeroHeatFluxWm2;
        _lastPhase = state.LastPhase;
        HotAlert = TemperatureK > MaxSafeTemp;
        ColdAlert = TemperatureK < MinSafeTemp;
    }

    public void Reset() => RestoreState(new ThermalState());

    public void Tick(double dt, bool inEclipse, bool inAtmosphere, double atmosphericTemp)
        => Tick(dt, inEclipse ? 0.0 : 1.0, inAtmosphere, atmosphericTemp);

    public void Tick(double dt, double solarVisibility, bool inAtmosphere, double atmosphericTemp)
        => Tick(dt, solarVisibility, inAtmosphere, atmosphericTemp, aeroHeatFluxWm2: 0.0);

    public void Tick(double dt, double solarVisibility, bool inAtmosphere, double atmosphericTemp,
        double aeroHeatFluxWm2, SystemsMissionPhase phase = SystemsMissionPhase.Active)
    {
        _lastSolarVisibility = solarVisibility;
        _lastInAtmosphere = inAtmosphere;
        _lastAtmosphericTemp = atmosphericTemp;
        _lastAeroHeatFluxWm2 = aeroHeatFluxWm2;
        _lastPhase = phase;
        _hasLastSample = true;

        double netHeat = ComputeNetHeatWatts(
            TemperatureK, solarVisibility, inAtmosphere, atmosphericTemp,
            aeroHeatFluxWm2, phase);
        TemperatureK = System.Math.Max(SpaceBgTemp, TemperatureK + netHeat * dt / ThermalMass);

        HotAlert  = TemperatureK > MaxSafeTemp;
        ColdAlert = TemperatureK < MinSafeTemp;
    }

    /// <summary>
    /// Projects the next thermal alert from the last committed heat-balance sample.
    /// This is a read-only, local linear estimate: atmosphere, solar visibility and
    /// aero heating may change before the deadline, so callers must recompute it after
    /// the next committed systems tick. A missing or non-finite sample returns null.
    /// </summary>
    public double? GetNextAlertDeadlineSeconds()
    {
        if (!_hasLastSample || !double.IsFinite(TemperatureK))
            return null;

        if (TemperatureK <= MinSafeTemp || TemperatureK >= MaxSafeTemp)
            return 0.0;

        double rateKPerSecond = ComputeNetHeatWatts(
            TemperatureK, _lastSolarVisibility, _lastInAtmosphere,
            _lastAtmosphericTemp, _lastAeroHeatFluxWm2, _lastPhase) / ThermalMass;
        if (!double.IsFinite(rateKPerSecond) || rateKPerSecond == 0.0)
            return null;

        double seconds = rateKPerSecond > 0.0
            ? (MaxSafeTemp - TemperatureK) / rateKPerSecond
            : (TemperatureK - MinSafeTemp) / -rateKPerSecond;
        return double.IsFinite(seconds) && seconds >= 0.0 ? seconds : null;
    }

    public double TempCelsius    => TemperatureK - 273.15;
    public double ThermalFraction => System.Math.Clamp(
        (TemperatureK - MinSafeTemp) / (MaxSafeTemp - MinSafeTemp), 0.0, 1.0);

    private static double ComputeNetHeatWatts(
        double temperatureK,
        double solarVisibility,
        bool inAtmosphere,
        double atmosphericTemp,
        double aeroHeatFluxWm2,
        SystemsMissionPhase phase)
    {
        double solarIn = System.Math.Clamp(solarVisibility, 0.0, 1.0)
            * SolarHeatFlux * SolarAbsorb * VehicleArea * 0.5;

        // Radiation to space.
        double radOut = Emissivity * Boltzmann * VehicleArea * System.Math.Pow(temperatureK, 4);

        // In atmosphere: convective exchange.
        double convective = inAtmosphere
            ? (atmosphericTemp - temperatureK) * 200.0
            : 0.0;

        // TPS leak: free-stream stagnation flux × coupling area × tiny fraction.
        double couplingArea = SystemsPhaseLoads.ThermalCouplingAreaM2(phase);
        double aeroIn = System.Math.Max(0.0, aeroHeatFluxWm2)
            * couplingArea * AeroCabinLeakFraction;

        return solarIn - radOut + convective + aeroIn;
    }
}
