namespace Exosphere.Simulation.Flight;

/// <summary>Worst pin compression and load expected after an unpowered drop into the cradle.</summary>
public readonly record struct CatchImpactPrediction(double PeakCompressionM, double PeakPinLoadN);

/// <summary>
/// Predicts the impact transient after engine cutoff above or on the tower pins. It uses
/// the force law of <see cref="Physics.SurfaceContactSolver"/>, <c>F = max(0, k x + c ẋ)</c>
/// per pin, for a symmetric load across the pins. Feeding the gap of the lowest pin makes
/// the estimate conservative for a slightly tilted vehicle. The settled sample alone hides
/// the first compression peak, which is the one that can reach the travel limit.
/// </summary>
public static class CatchImpactPredictor
{
    private const double StepS = 5.0e-4;

    /// <param name="gapM">Clearance of the lowest pin above its cradle; negative while compressed.</param>
    /// <param name="closingSpeedMps">Speed toward the cradle along its normal; negative when rising.</param>
    public static CatchImpactPrediction Predict(
        double massKg, double gravityMps2, double stiffnessPerPinNPerM, double dampingPerPinNsPerM,
        int pinCount, double gapM, double closingSpeedMps, double horizonS = 3.0)
    {
        if (!(massKg > 0.0) || !double.IsFinite(massKg))
            throw new ArgumentOutOfRangeException(nameof(massKg));
        if (!(gravityMps2 >= 0.0) || !double.IsFinite(gravityMps2))
            throw new ArgumentOutOfRangeException(nameof(gravityMps2));
        if (!(stiffnessPerPinNPerM > 0.0) || !double.IsFinite(stiffnessPerPinNPerM))
            throw new ArgumentOutOfRangeException(nameof(stiffnessPerPinNPerM));
        if (!(dampingPerPinNsPerM >= 0.0) || !double.IsFinite(dampingPerPinNsPerM))
            throw new ArgumentOutOfRangeException(nameof(dampingPerPinNsPerM));
        if (pinCount <= 0) throw new ArgumentOutOfRangeException(nameof(pinCount));
        if (!double.IsFinite(gapM)) throw new ArgumentOutOfRangeException(nameof(gapM));
        if (!double.IsFinite(closingSpeedMps)) throw new ArgumentOutOfRangeException(nameof(closingSpeedMps));

        double x = -gapM;
        double v = closingSpeedMps;
        double peakX = System.Math.Max(0.0, x);
        double peakLoad = PinLoad(x, v, stiffnessPerPinNPerM, dampingPerPinNsPerM);
        int steps = (int)System.Math.Ceiling(horizonS / StepS);
        for (int i = 0; i < steps; i++)
        {
            double load = PinLoad(x, v, stiffnessPerPinNPerM, dampingPerPinNsPerM);
            v += (gravityMps2 - pinCount * load / massKg) * StepS;
            x += v * StepS;
            peakX = System.Math.Max(peakX, x);
            peakLoad = System.Math.Max(peakLoad, PinLoad(x, v, stiffnessPerPinNPerM, dampingPerPinNsPerM));
        }

        return new CatchImpactPrediction(peakX, peakLoad);
    }

    private static double PinLoad(double x, double v, double k, double c) =>
        x > 0.0 ? System.Math.Max(0.0, k * x + c * v) : 0.0;
}
