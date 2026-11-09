namespace Exosphere.Simulation;

/// <summary>
/// Comparisons between absolute simulation epochs (seconds from J2000, TDB on the dated path).
/// A dated 1969 mission runs at |t| ≈ 9.6e8 s, where a double resolves only ~1.2e-7 s, so a fixed
/// absolute tolerance that is generous near J2000 rejects contiguous epochs there. The tolerance
/// is the larger of a fixed floor and 16 units in the last place of the epoch (~1.9 µs in 1969),
/// still far below any physics substep.
/// </summary>
public static class SimulationClock
{
    public static double EpochTolerance(double epoch, double floor = 1e-7) =>
        System.Math.Max(floor, 16.0 * (System.Math.BitIncrement(System.Math.Abs(epoch)) - System.Math.Abs(epoch)));

    public static bool SameEpoch(double a, double b, double floor = 1e-7) =>
        System.Math.Abs(a - b) <= EpochTolerance(System.Math.Max(System.Math.Abs(a), System.Math.Abs(b)), floor);
}
