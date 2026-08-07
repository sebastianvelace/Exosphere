namespace Exosphere.Simulation.Systems;

/// <summary>
/// Phase-dependent avionics and thermal-coupling loads shared by systems ticks.
/// Values are order-of-magnitude mission loads, not vehicle-specific budgets.
/// </summary>
public static class SystemsPhaseLoads
{
    /// <summary>Extra bus load (kW) beyond life-support for guidance/avionics by phase.</summary>
    public static double AvionicsExtraKw(SystemsMissionPhase phase) => phase switch
    {
        SystemsMissionPhase.Idle => 0.0,
        SystemsMissionPhase.Active => 0.0,
        SystemsMissionPhase.HighLoad => 1.5,
        SystemsMissionPhase.Entry => 2.0,
        SystemsMissionPhase.PeakHeating => 3.5,
        _ => 0.0,
    };

    /// <summary>
    /// Effective cabin-facing area (m²) that couples free-stream aero heat flux into
    /// the cabin thermal mass. Peak heating opens the largest leak path (windows,
    /// penetrations, imperfect TPS seal); idle/cruise stay nearly sealed.
    /// </summary>
    public static double ThermalCouplingAreaM2(SystemsMissionPhase phase) => phase switch
    {
        SystemsMissionPhase.PeakHeating => 4.0,
        SystemsMissionPhase.Entry => 2.0,
        SystemsMissionPhase.HighLoad => 0.8,
        SystemsMissionPhase.Active => 0.5,
        _ => 0.25,
    };
}
