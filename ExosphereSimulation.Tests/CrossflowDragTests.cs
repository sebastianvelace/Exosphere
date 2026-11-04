namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Physics;
using Xunit;

/// <summary>
/// Crossflow drag against the published curves of Jorgensen, NASA TR R-474 (1977), and the
/// USSA-1976 Sutherland viscosity of air. Analytic gates of fidelity phase F1 (the crossflow
/// term of the distributed aerodynamics).
/// </summary>
[Trait("FidelityGate", "F1")]
[Trait("GateKind", "analytic")]
public sealed class CrossflowDragTests
{
    [Fact]
    public void SutherlandViscosityMatchesTheStandardAtmosphereTable()
    {
        // USSA-1976 table: 1.7894e-5 Pa s at sea level (288.15 K), 1.4216e-5 at 216.65 K.
        var air = new AtmosphereModel();
        Assert.Equal(1.7894e-5, air.GetDynamicViscosityAtTemperature(288.15), 8);
        Assert.Equal(1.4216e-5, air.GetDynamicViscosityAtTemperature(216.65), 8);
    }

    [Fact]
    public void SubcriticalCylinderIsWieselsbergersValue()
    {
        Assert.Equal(1.20, CrossflowDrag.SectionCoefficient(0.1, 1.0e5), 6);
    }

    [Fact]
    public void DragCrisisDropsAndTranscriticalFlowRecovers()
    {
        double subcritical = CrossflowDrag.SectionCoefficient(0.1, 1.5e5);
        double crisis = CrossflowDrag.SectionCoefficient(0.1, 6.0e5);
        double transcritical = CrossflowDrag.SectionCoefficient(0.1, 5.0e7);
        Assert.InRange(crisis, 0.25, 0.32);
        Assert.InRange(transcritical, 0.55, 0.65);
        Assert.True(crisis < transcritical && transcritical < subcritical);
    }

    [Fact]
    public void CompressibilityRemovesTheReynoldsDependence()
    {
        // Fig. 1 critical-Re data: by M_n 0.5 the cylinder is back on the subcritical curve.
        Assert.Equal(CrossflowDrag.SectionCoefficient(0.5, 1.0e5), CrossflowDrag.SectionCoefficient(0.5, 1.0e7), 9);
        Assert.Equal(1.98, CrossflowDrag.SectionCoefficient(0.97, 1.0e7), 6);
    }

    [Fact]
    public void HypersonicCrossflowApproachesModifiedNewtonian()
    {
        // Modified Newtonian broadside cylinder: C_dn = (2/3)·Cp,stag (fig. 1 theory line).
        double newtonian = 2.0 / 3.0 * BodyFlapAerodynamics.StagnationPressureCoefficient(8.0);
        Assert.InRange(CrossflowDrag.SectionCoefficient(8.0, 1.0e6), newtonian * 0.98, newtonian * 1.04);
    }

    [Fact]
    public void FiniteLengthFactorFollowsFinenessAndIsUnitySupersonic()
    {
        Assert.Equal(0.695, CrossflowDrag.Eta(0.0, 10.0), 6);
        Assert.True(CrossflowDrag.Eta(0.1, 5.0) < CrossflowDrag.Eta(0.1, 20.0));
        Assert.Equal(1.0, CrossflowDrag.Eta(1.6, 6.0), 9);
        Assert.Equal(1.0, CrossflowDrag.Eta(4.0, 30.0), 9);
        // Fig. 5: η·C_dn measured on l/d 10-12 bodies of revolution.
        Assert.InRange(CrossflowDrag.Coefficient(0.6, 6.0e5, 11.0), 1.10, 1.30);
        Assert.InRange(CrossflowDrag.Coefficient(1.2, 6.0e5, 11.0), 1.55, 1.70);
        Assert.InRange(CrossflowDrag.Coefficient(1.6, 6.0e5, 11.0), 1.42, 1.52);
    }
}
