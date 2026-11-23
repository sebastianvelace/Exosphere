namespace Exosphere.Simulation.Flight;

/// <summary>Worst pin compression and load expected after an unpowered drop into the cradle.</summary>
public readonly record struct CatchImpactPrediction(double PeakCompressionM, double PeakPinLoadN);

/// <summary>
/// Predicts the impact transient after engine cutoff above or on the tower pins. It uses
/// the force law of <see cref="Physics.SurfaceContactSolver"/>, <c>F = max(0, k x + c ẋ)</c>
/// per pin. The symmetric form assumes every pin meets the cradle together; it understates
/// the peak of a tilted ship, whose first pin carries the drop until the ship rotates onto
/// the second, so guidance uses the rigid two-pin form (<see cref="CatchPinPair"/>). The
/// settled sample alone hides the first compression peak, which is the one that can reach
/// the travel limit.
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

    /// <summary>
    /// Same transient for a rigid ship landing on two pins at different heights: heave of the
    /// centre of mass plus rotation about the horizontal axis across the pin line, each pin
    /// with its own gap, closing speed and lever. Pin i moves down by <c>z + sᵢθ</c>; its
    /// upward load <c>Fᵢ</c> decelerates the heave (<c>m z̈ = m g − ΣFᵢ</c>) and rotates the pair
    /// (<c>I θ̈ = −Σ sᵢFᵢ</c>). The peak is the worse of the two pins.
    /// </summary>
    public static CatchImpactPrediction Predict(
        double massKg, double gravityMps2, double stiffnessPerPinNPerM, double dampingPerPinNsPerM,
        in CatchPinPair pair, double horizonS = 3.0)
    {
        if (!pair.IsDefined) throw new ArgumentException("Undefined pin pair.", nameof(pair));
        if (!(massKg > 0.0) || !double.IsFinite(massKg))
            throw new ArgumentOutOfRangeException(nameof(massKg));
        if (!(gravityMps2 >= 0.0) || !double.IsFinite(gravityMps2))
            throw new ArgumentOutOfRangeException(nameof(gravityMps2));
        if (!(stiffnessPerPinNPerM > 0.0) || !double.IsFinite(stiffnessPerPinNPerM))
            throw new ArgumentOutOfRangeException(nameof(stiffnessPerPinNPerM));
        if (!(dampingPerPinNsPerM >= 0.0) || !double.IsFinite(dampingPerPinNsPerM))
            throw new ArgumentOutOfRangeException(nameof(dampingPerPinNsPerM));

        double sA = pair.LeverAM, sB = pair.LeverBM;
        double inertia = pair.TransverseInertiaKgM2;
        double thetaRate = (pair.ClosingBMps - pair.ClosingAMps) / (sB - sA);
        double zRate = pair.ClosingAMps - sA * thetaRate;
        double z = 0.0, theta = 0.0;
        double peakX = System.Math.Max(0.0, System.Math.Max(-pair.GapAM, -pair.GapBM));
        double peakLoad = 0.0;
        int steps = (int)System.Math.Ceiling(horizonS / StepS);
        for (int i = 0; i < steps; i++)
        {
            double xA = z + sA * theta - pair.GapAM, xB = z + sB * theta - pair.GapBM;
            double fA = PinLoad(xA, zRate + sA * thetaRate, stiffnessPerPinNPerM, dampingPerPinNsPerM);
            double fB = PinLoad(xB, zRate + sB * thetaRate, stiffnessPerPinNPerM, dampingPerPinNsPerM);
            peakLoad = System.Math.Max(peakLoad, System.Math.Max(fA, fB));
            zRate += (gravityMps2 - (fA + fB) / massKg) * StepS;
            thetaRate += -(sA * fA + sB * fB) / inertia * StepS;
            z += zRate * StepS;
            theta += thetaRate * StepS;
            peakX = System.Math.Max(peakX, System.Math.Max(z + sA * theta - pair.GapAM, z + sB * theta - pair.GapBM));
        }

        return new CatchImpactPrediction(peakX, peakLoad);
    }

    private static double PinLoad(double x, double v, double k, double c) =>
        x > 0.0 ? System.Math.Max(0.0, k * x + c * v) : 0.0;
}
