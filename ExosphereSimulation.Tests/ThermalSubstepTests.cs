namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Physics;
using Xunit;

/// <summary>
/// Pins the sub-step contract of <see cref="ThermalModel.StepTwoNode"/> to the tick slicing
/// in <see cref="Universe"/>.
///
/// <para>The two-node integration is explicit Euler, so its accuracy depends entirely on the
/// sub-step it is walked at. That sub-step stays at <see cref="ThermalModel.MaxSubStep"/> only
/// while the requested dt fits inside <c>MaxSubStep · MaxSubSteps</c>; past that the sub-step
/// count saturates and the step silently STRETCHES. The scheduler is what keeps dt small, so
/// these tests assert the coupling instead of assuming it.</para>
/// </summary>
public sealed class ThermalSubstepTests
{
    // Defaults from PartDefinition: a Starship-class TPS tile over an aluminium-class skin.
    private const double TpsCapacityPerArea       = 7_000.0;
    private const double TpsConductance           = 5.0;
    private const double StructureCapacityPerArea = 15_800.0;

    /// <summary>Peak-entry stagnation flux, same order as the PhysicsRegressionTests entry case.</summary>
    private const double PeakEntryFlux = 500_000.0;

    /// <summary>
    /// The sub-step ceiling must cover the loosest cap the scheduler can hand this model.
    /// If <see cref="Universe.MaxCoastStep"/> is ever raised past the product, every thermal
    /// call at that dt starts integrating at a coarser step with no error — so break here
    /// instead of degrading the physics quietly.
    /// </summary>
    [Fact]
    public void SubStepCeilingCoversTheLoosestSchedulerStepCap()
    {
        double ceiling = ThermalModel.MaxSubStep * ThermalModel.MaxSubSteps;

        Assert.True(ceiling >= Universe.MaxCoastStep,
            $"ThermalModel can hold its {ThermalModel.MaxSubStep} s sub-step only up to "
            + $"{ceiling} s, but Universe.MaxCoastStep is {Universe.MaxCoastStep} s.");

        // Deliberate headroom: the ceiling is not merely adequate, it is oversized, so a
        // scheduler change has room to be noticed rather than landing exactly on the edge.
        Assert.True(ceiling >= Universe.MaxCoastStep * 10.0,
            $"Expected ≥10× headroom over MaxCoastStep, got {ceiling / Universe.MaxCoastStep:F1}×.");
    }

    /// <summary>
    /// <see cref="Universe.MaxCoastStep"/> (2.0 s) is the largest dt <c>Universe.Tick</c> can hand
    /// <c>ApplyPostIntegrationPhysics</c>. Sweep well past it for margin: the effective sub-step
    /// must never exceed <see cref="ThermalModel.MaxSubStep"/>, and one call over the whole
    /// interval must reproduce the same interval walked in explicit sub-step pieces exactly.
    /// </summary>
    [Fact]
    public void EffectiveSubStepNeverExceedsMaxSubStepForAnySchedulerDeliverableDt()
    {
        for (double dt = 0.001; dt <= 40.0; dt *= 1.7)
        {
            double h = ThermalModel.EffectiveSubStep(dt);

            Assert.True(h <= ThermalModel.MaxSubStep,
                $"dt = {dt:R} s integrates at h = {h:R} s, above MaxSubStep "
                + $"{ThermalModel.MaxSubStep} s.");

            int steps = (int)System.Math.Round(dt / h);
            Assert.True(steps >= 1);

            var single = Step(2_000.0, 300.0, dt);

            double skin = 2_000.0;
            double structure = 300.0;
            for (int i = 0; i < steps; i++)
                (skin, structure) = Step(skin, structure, h);

            Assert.Equal(single.Skin, skin, 12);
            Assert.Equal(single.Structure, structure, 12);
        }
    }

    /// <summary>
    /// At the largest step the scheduler can deliver the radiative term must behave: a cold
    /// skin under entry flux warms monotonically and never overshoots the radiative
    /// equilibrium it is asymptotically approaching. An unstable explicit step would
    /// oscillate or blow straight past it.
    /// </summary>
    [Fact]
    public void RadiativeTermIsMonotonicAndBoundedAtTheLargestSchedulerStep()
    {
        double equilibrium = ThermalModel.RadiativeEquilibrium(PeakEntryFlux);
        Assert.True(equilibrium > 0.0);

        double skin = 300.0;
        double structure = 300.0;
        for (int tick = 0; tick < 200; tick++)
        {
            double previous = skin;
            (skin, structure) = Step(skin, structure, Universe.MaxCoastStep);

            Assert.True(skin >= previous,
                $"Skin temperature fell from {previous:F3} K to {skin:F3} K at tick {tick}.");
            Assert.True(skin < equilibrium,
                $"Skin temperature {skin:F3} K exceeded radiative equilibrium "
                + $"{equilibrium:F3} K at tick {tick}.");
        }

        // 200 ticks is 400 s of entry — long enough that the fast skin node has effectively
        // arrived, which is what makes the "never overshoots" bound meaningful.
        Assert.True(skin > 0.99 * equilibrium,
            $"Skin reached only {skin:F3} K of {equilibrium:F3} K; the node never settled.");
    }

    private static (double Skin, double Structure) Step(double skin, double structure, double dt) =>
        ThermalModel.StepTwoNode(
            skin,
            structure,
            PeakEntryFlux,
            dt,
            shieldedFraction: 1.0,
            TpsCapacityPerArea,
            TpsConductance,
            StructureCapacityPerArea);
}
