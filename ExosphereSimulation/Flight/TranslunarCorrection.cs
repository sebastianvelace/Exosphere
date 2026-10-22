namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;

/// <summary>
/// Translunar midcourse correction aimed at a perilune altitude, not at a stored point. The
/// Earth-centred Lambert arc to a lunar B-plane offset is re-solved from the flown state and
/// the offset is secant-iterated until the patched-conic encounter (with lunar focusing at
/// sphere-of-influence entry) predicts the target perilune. Aiming at the point the TLI plan
/// estimated left Apollo 8 at a 211 km perilune instead of 111 km.
/// </summary>
public static class TranslunarCorrection
{
    /// <summary>
    /// The trim (Apollo MCC-4) is flown inside the Moon's sphere of influence once perilune is
    /// this close, as a <see cref="PeriapsisCorrection"/> on the Moon-centred hyperbola: there
    /// the Earth-centred patched conic no longer predicts the approach (it left 72 km, not 111).
    /// </summary>
    public const double TrimLeadTimeSeconds = 6.0 * 3_600.0;

    /// <summary>True once the inside-SOI trim is due.</summary>
    public static bool IsTrimDue(Universe universe, Vessel vessel, CelestialBody moon)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(moon);
        if (universe.GetDominantBody(vessel.Position) != moon) return false;
        double timeToPerilune = PeriapsisCaptureBurn.TimeToPeriapsis(vessel.Position - moon.Position,
            vessel.Velocity - moon.Velocity, moon.GM);
        return double.IsFinite(timeToPerilune) && timeToPerilune <= TrimLeadTimeSeconds;
    }

    /// <summary>The inside-SOI trim burn to the target perilune radius.</summary>
    public static LambertGuidedBurn CreateTrim(Vessel vessel, CelestialBody moon, double epoch,
        double targetPeriluneRadius) =>
        PeriapsisCorrection.Create(vessel, moon, epoch, targetPeriluneRadius, cutoffDeltaVMps: 0.02);

    /// <param name="aimPosition">The current aim point (Earth-relative at <paramref name="arrivalTime"/>);
    /// it fixes which side of the Moon the trajectory passes and the starting offset.</param>
    public static LambertGuidedBurn Create(Vessel vessel, CelestialBody earth, CelestialBody moon,
        double epoch, double arrivalTime, Vector3d aimPosition, double targetPeriluneRadius,
        double cutoffDeltaVMps = 0.1, double maximumBurnSeconds = 300.0)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(earth);
        ArgumentNullException.ThrowIfNull(moon);
        var ephemeris = moon.OrbitalElements
            ?? throw new InvalidOperationException("The translunar correction needs the lunar ephemeris.");
        double timeOfFlight = arrivalTime - epoch;
        if (!(timeOfFlight > 0.0))
            throw new InvalidOperationException("The planned lunar arrival is already past.");

        var position = vessel.Position - earth.Position;
        var velocity = vessel.Velocity - earth.Velocity;
        var normal = position.Cross(velocity);
        var (moonPosition, moonVelocity) = ephemeris.GetStateAtTime(arrivalTime, earth.GM);
        var centre = LambertSolver.Solve(earth.GM, position, moonPosition, timeOfFlight, normal);
        var bAxis = (centre.ArrivalVelocity - moonVelocity).Normalized
            .Cross(moonPosition.Cross(moonVelocity).Normalized).Normalized;
        double offset = (aimPosition - moonPosition).Dot(bAxis);
        if (System.Math.Abs(offset) < 1.0) offset = 2.0 * targetPeriluneRadius;

        double PredictedPerilune(double signedOffset)
        {
            var arc = LambertSolver.Solve(earth.GM, position, moonPosition + bAxis * signedOffset,
                timeOfFlight, normal);
            var conic = OrbitalElements.FromStateVector(position, arc.DepartureVelocity, earth.GM,
                earth.Id, epoch);
            var encounter = LunarTransferPlanner.AnalyzeEncounter(earth.GM, moon.GM, moon.Radius,
                moon.SphereOfInfluence, conic, ephemeris, epoch, 1.2 * timeOfFlight);
            return encounter.HasEncounter ? encounter.PredictedPeriapsisRadius : double.NaN;
        }

        double previousOffset = double.NaN, previousError = double.NaN;
        for (int iteration = 0; iteration < 12; iteration++)
        {
            double error = PredictedPerilune(offset) - targetPeriluneRadius;
            if (!double.IsFinite(error) || System.Math.Abs(error) < 200.0) break;
            double next = double.IsNaN(previousError) || System.Math.Abs(error - previousError) < 1e-6
                ? offset - System.Math.Sign(offset) * error
                : offset - error * (offset - previousOffset) / (error - previousError);
            previousOffset = offset;
            previousError = error;
            // Stay on the planned side of the Moon.
            offset = System.Math.Sign(previousOffset) * System.Math.Max(0.2 * targetPeriluneRadius,
                System.Math.Abs(next));
        }
        return new LambertGuidedBurn(moonPosition + bAxis * offset, arrivalTime, epoch, normal,
            cutoffDeltaVMps, maximumBurnSeconds);
    }
}
