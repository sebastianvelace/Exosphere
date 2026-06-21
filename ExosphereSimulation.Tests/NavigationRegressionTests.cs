namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;
using Xunit;

public sealed class NavigationRegressionTests
{
    private const double SunGm = 1.32712440018e20;
    private const double Au = 1.495978707e11;

    [Fact]
    public void EarthToMarsHohmannHasExpectedSignAndFlightTime()
    {
        var plan = HohmannTransfer.Compute(SunGm, Au, 1.523679 * Au);

        Assert.True(plan.FirstBurnDeltaV > 0.0);
        Assert.True(plan.SecondBurnDeltaV > 0.0);
        Assert.InRange(plan.TimeOfFlight / 86400.0, 250.0, 270.0);
        Assert.InRange(plan.RequiredPhaseAngle, 0.0, 2.0 * System.Math.PI);
    }

    [Fact]
    public void EarthToVenusHohmannStartsWithRetrogradeBurn()
    {
        var plan = HohmannTransfer.Compute(SunGm, Au, 0.723332 * Au);

        Assert.True(plan.FirstBurnDeltaV < 0.0);
        Assert.True(plan.SecondBurnDeltaV < 0.0);
        Assert.InRange(plan.TimeOfFlight / 86400.0, 140.0, 160.0);
        Assert.InRange(plan.RequiredPhaseAngle, 0.0, 2.0 * System.Math.PI);
    }

    [Fact]
    public void HohmannRejectsDegenerateRadii()
    {
        Assert.Throws<ArgumentException>(() => HohmannTransfer.Compute(SunGm, Au, Au));
        Assert.Throws<ArgumentOutOfRangeException>(() => HohmannTransfer.Compute(SunGm, -1.0, Au));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Encounter prediction (forward propagation vs. a moving target body).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EncounterFindsClosestApproachWhenPathsCrossButMisses()
    {
        // Vessel on a Hohmann transfer ellipse from 1 AU out to 1.5 AU; the target rides a
        // circular 1.5 AU orbit but is phased so the vessel arrives at apoapsis with the
        // target far away → no SOI capture, only a finite closest approach.
        double rDepart = Au, rTarget = 1.5 * Au;
        var plan = HohmannTransfer.Compute(SunGm, rDepart, rTarget);

        var startPos = new Vector3d(rDepart, 0.0, 0.0);
        double vTransfer = System.Math.Sqrt(SunGm * (2.0 / rDepart - 1.0 / plan.TransferSemiMajorAxis));
        var startVel = new Vector3d(0.0, vTransfer, 0.0);   // prograde at periapsis
        var vesselOrbit = OrbitalElements.FromStateVector(startPos, startVel, SunGm, "sun", 0.0);

        // Target starts at +x (same side as the vessel's start) → vessel reaches apoapsis on
        // the −x axis but the target is nowhere near it. Circular rate ω = √(μ/r³).
        double omega = System.Math.Sqrt(SunGm / (rTarget * rTarget * rTarget));
        Vector3d Target(double t) =>
            new(rTarget * System.Math.Cos(omega * t), rTarget * System.Math.Sin(omega * t), 0.0);

        var result = TrajectoryPrediction.FindEncounter(
            vesselOrbit, SunGm, Target, targetSoiRadius: 5.77e8 /* Mars SOI */,
            startTime: 0.0, searchWindow: plan.TimeOfFlight * 1.05);

        Assert.True(double.IsFinite(result.ClosestApproachDistance));
        Assert.True(result.ClosestApproachDistance > 0.0);
        Assert.InRange(result.TimeOfClosestApproach, 0.0, plan.TimeOfFlight * 1.05);
    }

    [Fact]
    public void EncounterDetectsSoiEntryWhenTargetIsPhasedForArrival()
    {
        // Same transfer ellipse, but the target is phased so it sits at the vessel's apoapsis
        // (angle π) exactly at arrival (t = ToF) → SOI capture must trigger.
        double rDepart = Au, rTarget = 1.5 * Au;
        var plan = HohmannTransfer.Compute(SunGm, rDepart, rTarget);

        var startPos = new Vector3d(rDepart, 0.0, 0.0);
        double vTransfer = System.Math.Sqrt(SunGm * (2.0 / rDepart - 1.0 / plan.TransferSemiMajorAxis));
        var startVel = new Vector3d(0.0, vTransfer, 0.0);
        var vesselOrbit = OrbitalElements.FromStateVector(startPos, startVel, SunGm, "sun", 0.0);

        double omega = System.Math.Sqrt(SunGm / (rTarget * rTarget * rTarget));
        Vector3d Target(double t)
        {
            double ang = System.Math.PI - omega * (plan.TimeOfFlight - t);   // π at t = ToF
            return new Vector3d(rTarget * System.Math.Cos(ang), rTarget * System.Math.Sin(ang), 0.0);
        }

        var result = TrajectoryPrediction.FindEncounter(
            vesselOrbit, SunGm, Target, targetSoiRadius: 5.77e8,
            startTime: 0.0, searchWindow: plan.TimeOfFlight * 1.05);

        Assert.True(result.HasEncounter, "Phased rendezvous should enter the target SOI.");
        Assert.True(result.ClosestApproachDistance <= 5.77e8);
        Assert.True(double.IsFinite(result.TimeOfSoiEntry));
        Assert.InRange(result.TimeOfSoiEntry, 0.0, plan.TimeOfFlight * 1.05);
        Assert.True(result.TimeOfSoiEntry <= result.TimeOfClosestApproach + 1.0);
    }

    [Fact]
    public void EncounterReportsNoCaptureForAComfortablyDistantTarget()
    {
        // Vessel circular at 1 AU; target circular at 5 AU, never within a small SOI.
        var startPos = new Vector3d(Au, 0.0, 0.0);
        double vCirc = System.Math.Sqrt(SunGm / Au);
        var startVel = new Vector3d(0.0, vCirc, 0.0);
        var vesselOrbit = OrbitalElements.FromStateVector(startPos, startVel, SunGm, "sun", 0.0);

        double rTarget = 5.0 * Au;
        double omega = System.Math.Sqrt(SunGm / (rTarget * rTarget * rTarget));
        Vector3d Target(double t) =>
            new(rTarget * System.Math.Cos(omega * t), rTarget * System.Math.Sin(omega * t), 0.0);

        double period = 2.0 * System.Math.PI * System.Math.Sqrt(Au * Au * Au / SunGm);
        var result = TrajectoryPrediction.FindEncounter(
            vesselOrbit, SunGm, Target, targetSoiRadius: 5.77e8,
            startTime: 0.0, searchWindow: period);

        Assert.False(result.HasEncounter);
        Assert.True(double.IsPositiveInfinity(result.TimeOfSoiEntry));
        Assert.True(result.ClosestApproachDistance >= rTarget - Au - 1.0);
    }
}
