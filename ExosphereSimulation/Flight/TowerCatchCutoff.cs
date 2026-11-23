namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Physics;

/// <summary>Outcome of the engine-cutoff check above the tower pins.</summary>
public readonly record struct TowerCatchCutoffDecision(
    bool Commit, string Reason, double RequiredRangeM, CatchImpactPrediction Prediction);

/// <summary>
/// Decides when guidance may cut the engines for the drop onto the pins. The cutoff is
/// irreversible, so every check must hold for the whole unpowered drop: both pins stay
/// inside the cradle after the lateral drift until contact, the predicted first
/// compression peak stays inside the pin travel and load limits, and the body is not
/// rotating faster than the settle criterion accepts.
/// </summary>
public static class TowerCatchCutoff
{
    /// <summary>Highest pin clearance at which the drop may start (m).</summary>
    public const double MaxGapM = 0.25;
    public const double MaxVerticalSpeedMps = 0.65;
    public const double MaxLateralSpeedMps = 0.35;
    /// <summary>
    /// Sideways speed the closing arms accept. Pin friction (μ 0.6) decelerates a sliding ship at
    /// about 5.9 m/s², so 2.5 m/s stops in 0.4 s and 0.5 m, and the drift until contact is
    /// already charged against the rail slack. One Raptor holds the ship at 1.7–1.9 m/s of
    /// residual drift over the pins, which a 1.5 m/s limit rejected indefinitely.
    /// </summary>
    public const double MaxLateralSpeedOnRailsMps = 2.5;
    /// <summary>Pitch and yaw rate that would tilt the ship on its pins; matches the settle criterion.</summary>
    public const double MaxAngularRateRadPerS = 0.05;
    /// <summary>
    /// Roll rate about the vehicle axis the rails accept: a draining ship spins up in roll and one
    /// Raptor cannot stop it, but at the fittings 0.15 rad/s is only 0.7 m/s along the rail,
    /// which pin friction stops.
    /// </summary>
    public const double MaxRollRateRadPerS = 0.15;
    /// <summary>Fixed range margin for unmodelled drift and the integrator step (m).</summary>
    public const double RangeMarginM = 0.10;
    /// <summary>Share of the pin travel and load limits the predicted peak may use.</summary>
    public const double LimitUtilization = 0.90;

    /// <param name="angularRateRadPerS">Pitch and yaw rate, about axes across the vehicle.</param>
    /// <param name="rollRateRadPerS">Roll rate about the vehicle axis.</param>
    /// <param name="pinPair">Measured rigid pin pair; when defined the impact is predicted for the tilted ship.</param>
    /// <param name="verticalSpeedUpMps">Cradle-relative speed along the cradle normal, positive up.</param>
    public static TowerCatchCutoffDecision Decide(
        in TowerCatchPinGeometry geometry, IReadOnlyList<ContactPointDefinition> pins,
        double massKg, double gravityMps2, double verticalSpeedUpMps, double lateralSpeedMps,
        double angularRateRadPerS, double rollRateRadPerS = 0.0, CatchPinPair pinPair = default)
    {
        if (geometry.EnabledPins != 2)
            return Reject("two pins required");
        if (geometry.LastPinGapM > MaxGapM)
            return Reject("above release height");
        if (System.Math.Abs(verticalSpeedUpMps) > MaxVerticalSpeedMps)
            return Reject("vertical speed");
        if (lateralSpeedMps > (geometry.UsesRails ? MaxLateralSpeedOnRailsMps : MaxLateralSpeedMps))
            return Reject("lateral speed");
        if (angularRateRadPerS > MaxAngularRateRadPerS)
            return Reject("angular rate");
        if (System.Math.Abs(rollRateRadPerS) > MaxRollRateRadPerS)
            return Reject("roll rate");

        // Time until the highest pin reaches the cradle in free fall, then the drift over it.
        double gap = System.Math.Max(0.0, geometry.LastPinGapM);
        double closing = -verticalSpeedUpMps;
        double timeToContact = gravityMps2 > 0.0
            ? (-closing + System.Math.Sqrt(closing * closing + 2.0 * gravityMps2 * gap)) / gravityMps2
            : 0.0;
        double drift = RangeMarginM + lateralSpeedMps * timeToContact;
        double requiredRange = geometry.UsesRails ? drift : TowerCatchPinGeometry.CaptureRadiusM - drift;
        if (geometry.UsesRails ? geometry.RailSlackM < drift : geometry.FarthestPinRangeM > requiredRange)
            return new(false, "pin range", requiredRange, default);

        var pin = FirstEnabled(pins);
        // A measured pair carries the tilt: the first pin takes the drop until the ship rotates
        // onto the second, which a symmetric estimate from the lowest gap understates.
        var prediction = pinPair.IsDefined
            ? CatchImpactPredictor.Predict(massKg, gravityMps2, pin.SpringStiffnessNPerM, pin.DampingNsPerM, pinPair)
            : CatchImpactPredictor.Predict(massKg, gravityMps2, pin.SpringStiffnessNPerM,
                pin.DampingNsPerM, geometry.EnabledPins, geometry.FirstPinGapM, closing);
        if (pin.MaxCompressionM > 0.0
            && prediction.PeakCompressionM > LimitUtilization * pin.MaxCompressionM)
            return new(false, "predicted travel", requiredRange, prediction);
        if (pin.MaxLoadN > 0.0 && prediction.PeakPinLoadN > LimitUtilization * pin.MaxLoadN)
            return new(false, "predicted load", requiredRange, prediction);
        return new(true, "commit", requiredRange, prediction);

        static TowerCatchCutoffDecision Reject(string reason) => new(false, reason, double.NaN, default);
    }

    private static ContactPointDefinition FirstEnabled(IReadOnlyList<ContactPointDefinition> pins)
    {
        for (int i = 0; i < pins.Count; i++)
            if (pins[i].Enabled) return pins[i];
        throw new ArgumentException("No enabled catch pin.", nameof(pins));
    }
}
