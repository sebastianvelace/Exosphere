namespace Exosphere.Simulation.Flight;

/// <summary>
/// Published Gemini VIII events and a smooth Titan II guidance reconstruction.
/// The event schedule is historical; commands between events are calibrated.
/// </summary>
public static class Gemini8FlightProfile
{
    public const string Id = "gemini8-rendezvous-emergency-return";
    public const double StageOneCutoffSeconds = 154.61;
    public const double StageSeparationSeconds = 155.29;
    public const double StageTwoCutoffSeconds = 337.54;
    public const double SpacecraftSeparationSeconds = 365.66;
    public const double RendezvousCompleteSeconds = 21_480.0;
    public const double DockingSeconds = 23_596.0;
    public const double RigidizationSeconds = 23_602.0;
    public const double ThrusterAnomalySeconds = 25_226.7;
    public const double UndockingSeconds = 26_112.3;
    public const double ReentryControlActivationSeconds = 26_185.1;
    public const double ThrusterIsolationSeconds = 26_295.7;
    public const double ControlRecoveredSeconds = 26_730.0;
    public const double AdapterSeparationSeconds = 36_228.0;
    public const double RetrofireSeconds = 36_287.0;
    public const double DrogueDeploySeconds = 38_207.0;
    public const double MainDeploySeconds = 38_288.0;
    public const double SplashdownSeconds = 38_486.0;
    public const double HistoricalInsertionPerigeeM = 159_900.0;
    public const double HistoricalInsertionApogeeM = 271_900.0;
    public const double HistoricalTargetOrbitM = 298_730.0;

    /// <summary>Stage-one shutdown to separation (historical BECO T+154.61 s, separation T+155.29 s).</summary>
    public const double StagingDelaySeconds = StageSeparationSeconds - StageOneCutoffSeconds;

    /// <summary>
    /// Stage one is out once its engines deliver no thrust (propellant depletion, as BECO was),
    /// with the historical cutoff plus a margin as a backstop.
    /// </summary>
    public static bool IsStageOneDepleted(Vessel vessel, CelestialBody earth, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        return elapsedSeconds > 20.0
            && (vessel.ComputeThrust(earth).Magnitude < 1.0 || elapsedSeconds >= StageOneCutoffSeconds + 30.0);
    }

    /// <summary>
    /// Stage-two closed-loop insertion target. With the historical stage-one program and a
    /// propellant-depletion BECO, stage two reaches a ~145 x 152 km orbit at T+347 s (flight:
    /// 160 x 272 km at T+337.5 s); the model's ascent losses leave no margin for the higher arc.
    /// </summary>
    public const double InsertionAltitudeM = 150_000.0;

    /// <summary>Stage-two cutoff once the flown periapsis clears this altitude.</summary>
    public const double MinimumInsertionPeriapsisM = 140_000.0;

    /// <summary>Stage two has burned out: no thrust once its engine is well past start-up.</summary>
    public static bool IsStageTwoBurnedOut(Vessel vessel, CelestialBody earth, double secondsSinceBeco)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        return secondsSinceBeco > 20.0 && vessel.ComputeThrust(earth).Magnitude < 1.0;
    }

    /// <summary>Stage-two pointing damping for the live pitch/yaw authority (ζ ≈ 0.8, Kd floor 1.2).</summary>
    public static double StageTwoDampingGain(double pitchYawAuthorityRadPerS2) =>
        System.Math.Max(1.2, AttitudeGuidance.DampingGainFor(2.3, pitchYawAuthorityRadPerS2));
    public const double HistoricalPeakAngularRateDegPerS = 296.0;
    public const double DockedTumbleRateDegPerS = 20.0;

    /// <summary>
    /// Roll torque (N·m) of the OAMS thruster that stuck on. Calibrated to the post-undock
    /// rate growth, 20 → 296 °/s in 183 s, on the model's 4,405 kg·m² roll inertia; it is
    /// what one 25 lbf (111 N) attitude thruster gives on a ~1 m arm.
    /// </summary>
    public const double StuckThrusterTorqueNm = 116.0;

    /// <summary>Propellant (kg/s) that thruster burns: 111 N at the OAMS 260 s specific impulse.</summary>
    public const double StuckThrusterMassFlowKgPerS = 111.2 / (260.0 * 9.80665);

    /// <summary>Body rate (rad/s) below which control counts as recovered.</summary>
    public const double ControlRecoveredRateRadPerS = 0.2 * System.Math.PI / 180.0;

    public static double ElevationDegrees(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (elapsedSeconds <= 12.0) return 90.0;
        if (elapsedSeconds <= StageOneCutoffSeconds)
            return SmoothStep(
                90.0, 14.0,
                12.0, StageOneCutoffSeconds,
                elapsedSeconds);
        if (elapsedSeconds <= 220.0)
            return SmoothStep(
                14.0, 2.0,
                StageOneCutoffSeconds, 220.0,
                elapsedSeconds);
        return SmoothStep(
            2.0, -10.0,
            220.0, StageTwoCutoffSeconds,
            elapsedSeconds);
    }

    /// <summary>
    /// Calibrated reconstruction of the OAMS-8 failure telemetry. This is an
    /// externally forced rate, not a pilot-command envelope: the docked stack begins
    /// tumbling, Spacecraft 8 accelerates after undocking, then the reentry-control
    /// system arrests the motion after the failed circuit is isolated.
    /// </summary>
    public static double AnomalyAngularRateDegPerS(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (elapsedSeconds <= ThrusterAnomalySeconds) return 0.0;
        if (elapsedSeconds <= UndockingSeconds)
            return Linear(
                0.0,
                DockedTumbleRateDegPerS,
                ThrusterAnomalySeconds,
                UndockingSeconds,
                elapsedSeconds);
        if (elapsedSeconds <= ThrusterIsolationSeconds)
            return Linear(
                DockedTumbleRateDegPerS,
                HistoricalPeakAngularRateDegPerS,
                UndockingSeconds,
                ThrusterIsolationSeconds,
                elapsedSeconds);
        if (elapsedSeconds <= ControlRecoveredSeconds)
            return Linear(
                HistoricalPeakAngularRateDegPerS,
                0.0,
                ThrusterIsolationSeconds,
                ControlRecoveredSeconds,
                elapsedSeconds);
        return 0.0;
    }

    private static double Linear(
        double startValue,
        double endValue,
        double startTime,
        double endTime,
        double time)
    {
        double fraction = System.Math.Clamp(
            (time - startTime) / (endTime - startTime), 0.0, 1.0);
        return startValue + (endValue - startValue) * fraction;
    }

    private static double SmoothStep(
        double startValue,
        double endValue,
        double startTime,
        double endTime,
        double time)
    {
        double fraction = System.Math.Clamp(
            (time - startTime) / (endTime - startTime), 0.0, 1.0);
        double smooth = fraction * fraction * (3.0 - 2.0 * fraction);
        return startValue + (endValue - startValue) * smooth;
    }
}
