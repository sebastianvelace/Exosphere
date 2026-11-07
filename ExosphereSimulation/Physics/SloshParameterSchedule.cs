namespace Exosphere.Simulation.Physics;

/// <summary>A scalar value and its first two physical-time derivatives, all in SI units.</summary>
public readonly record struct SloshScalarJet(double Value, double Rate, double Acceleration);

/// <summary>
/// Smooth-interval cylindrical first-mode parameters. Curvature is inverse pendulum length;
/// unlike length itself, it has a finite zero-mass limit. Height is above the tank floor.
/// </summary>
public readonly record struct CylindricalSloshParameters(SloshScalarJet ModalMassKg,
    SloshScalarJet CurvaturePerM, SloshScalarJet HeightAboveFloorM);

public static partial class PropellantSlosh
{
    /// <summary>
    /// Samples the same SP-8009 coefficients and their analytic time derivatives from one
    /// funded store mass history. At a debit boundary the caller supplies the appropriate
    /// one-sided rate; no valve impulse, outflow or inventory mutation is performed here.
    /// The zero-mass left limit can have nonzero parameter rates until the flow is stopped.
    /// </summary>
    public static CylindricalSloshParameters SampleParameters(double radiusM, double linearDensityKgPerM,
        double massKg, double massRateKgPerS = 0, double massAccelerationKgPerS2 = 0)
    {
        if (!(radiusM > 0) || !double.IsFinite(radiusM)) throw new ArgumentOutOfRangeException(nameof(radiusM));
        if (!(linearDensityKgPerM > 0) || !double.IsFinite(linearDensityKgPerM))
            throw new ArgumentOutOfRangeException(nameof(linearDensityKgPerM));
        if (massKg < 0 || !double.IsFinite(massKg)) throw new ArgumentOutOfRangeException(nameof(massKg));
        if (!double.IsFinite(massRateKgPerS)) throw new ArgumentOutOfRangeException(nameof(massRateKgPerS));
        if (!double.IsFinite(massAccelerationKgPerS2)) throw new ArgumentOutOfRangeException(nameof(massAccelerationKgPerS2));
        const double xi = FirstModeRoot;
        double curvatureScale = xi / radiusM;
        double beta = curvatureScale / linearDensityKgPerM;
        double x = beta * massKg;
        if (!(beta > 0) || !double.IsFinite(beta) || !double.IsFinite(x))
            throw new ArgumentException("The dimensionless fill schedule is not representable.");
        double tanh = System.Math.Tanh(x), half = System.Math.Tanh(x / 2);
        double sechSquared = SechSquared(x), halfSechSquared = SechSquared(x / 2);
        double fractionLimit = 2 / (xi * xi - 1);
        double modal = x == 0 ? massKg * fractionLimit : massKg * (fractionLimit * (tanh / x));
        double modalDerivative = fractionLimit * sechSquared;
        double modalSecondDerivative = -2 * fractionLimit * beta * tanh * sechSquared;
        double curvature = curvatureScale * tanh;
        double curvatureDerivative = curvatureScale * beta * sechSquared;
        double curvatureSecondDerivative = -2 * curvatureScale * beta * beta * tanh * sechSquared;
        double depth = massKg / linearDensityKgPerM;
        if (massKg > 0 && (!(modal > 0) || !(curvature > 0)))
            throw new ArgumentException("Nonempty-store modal parameters are below the representable range.");
        double height = depth * OneMinusTanhOverX(x / 2);
        double heightDerivative = (half * half) / linearDensityKgPerM;
        double heightSecondDerivative = (beta / linearDensityKgPerM) * half * halfSechSquared;
        return new(Jet(modal, modalDerivative, modalSecondDerivative),
            Jet(curvature, curvatureDerivative, curvatureSecondDerivative),
            Jet(height, heightDerivative, heightSecondDerivative));

        SloshScalarJet Jet(double value, double derivative, double secondDerivative)
        {
            double rate = derivative * massRateKgPerS;
            double acceleration = (secondDerivative * massRateKgPerS) * massRateKgPerS
                + derivative * massAccelerationKgPerS2;
            if (!double.IsFinite(value) || !double.IsFinite(rate) || !double.IsFinite(acceleration))
                throw new ArgumentException("The slosh parameter derivatives are not representable.");
            return new(value, rate, acceleration);
        }
    }

    private static double SechSquared(double x)
    {
        // 1-tanh(x)^2 loses the still-representable derivative in a deep tank.
        double exponential = System.Math.Exp(-2 * System.Math.Abs(x));
        double denominator = 1 + exponential;
        return 4 * exponential / (denominator * denominator);
    }
}
