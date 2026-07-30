namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// Verifies <see cref="Vessel.Stage"/> (routed through the shared
/// <c>Vessel.ApplyMassSplitKinematics</c> helper): the fix for the bug where the surviving
/// vessel was teleported by the FULL detached-stage length with no complementary offset on
/// the debris (injecting potential energy, breaking centre-of-mass conservation) and with no
/// ω × r transport term (breaking angular-momentum conservation about the combined centre of
/// mass). See <see cref="StarshipRealismTests.StagingPreservesDetachedStageRigidBodyMotion"/>
/// for the corresponding rewrite of the test that used to pin the bug via exact equality.
/// </summary>
public sealed class StageSeparationConservationTests
{
    [Fact]
    public void StagingConservesLinearMomentum()
    {
        // Asymmetric masses/lengths on purpose: Σmᵢvᵢ = M·v must hold for ANY mass ratio,
        // not just a balanced split — this is what m_s·r_s + m_d·r_d = 0 guarantees for any
        // configuration (see ApplyMassSplitKinematics), independent of the inertia model.
        var vessel = BuildTwoPartStack(
            upperMass: 4_800_000.0, lowerMass: 1_500_000.0,
            upperLength: 90.0, lowerLength: 50.0);
        vessel.Velocity = new Vector3d(1_200.0, -400.0, 7_300.0);
        vessel.AngularVelocity = new Vector3d(0.02, -0.03, 0.01);

        double massBefore = vessel.TotalMass;
        Vector3d momentumBefore = vessel.Velocity * massBefore;

        var debris = vessel.Stage();
        Assert.NotNull(debris);

        Vector3d momentumAfter =
            vessel.Velocity * vessel.TotalMass + debris!.Velocity * debris.TotalMass;

        AssertVectorClose(momentumBefore, momentumAfter, 1e-6 * momentumBefore.Magnitude);
    }

    [Fact]
    public void StagingConservesCentreOfMass()
    {
        var vessel = BuildTwoPartStack(
            upperMass: 20_000.0, lowerMass: 300_000.0,
            upperLength: 12.0, lowerLength: 70.0);
        vessel.Position = new Vector3d(1.0e7, 2.0e7, -3.0e7);

        Vector3d comBefore = vessel.Position;
        double massBefore = vessel.TotalMass;

        var debris = vessel.Stage();
        Assert.NotNull(debris);

        double massAfter = vessel.TotalMass + debris!.TotalMass;
        Vector3d comAfter =
            (vessel.Position * vessel.TotalMass + debris.Position * debris.TotalMass) / massAfter;

        AssertClose(massBefore, massAfter, 1e-12);
        AssertVectorClose(comBefore, comAfter, 1e-9);
    }

    [Fact]
    public void StagingConservesAngularMomentumAboutTheCombinedCentreOfMass()
    {
        // Symmetric 1000 kg / 10 m fixture on each side of the decoupler, same diameter
        // throughout (2 m). This is the one configuration where the fix's heuristic
        // "gap = debris length, split by mass ratio" position offset exactly reproduces the
        // true parallel-axis decomposition of a uniform rod split at its midpoint — so the
        // conservation identity below is exact, not approximate, and can be checked to a
        // tight tolerance.
        //
        // Hand derivation (Orientation = Identity so body/world frames coincide and the
        // transverse/axial split applies directly in world axes):
        //   M = 2000 kg, L = 20 m, r = 1 m (diameter/2)
        //   I_total_trans = M(3r²+L²)/12 = 2000·403/12 = 67166.667 kg·m²
        //   I_total_axial = 0.5·M·r² = 1000 kg·m²
        //   Each fragment: m=1000 kg, L=10 m → I_frag_trans = 1000·103/12 = 8583.333 kg·m²,
        //   I_frag_axial = 500 kg·m²
        //   gap = debris.VehicleLength = 10 m; m_s=m_d=1000 kg, M=2000 kg
        //   r_s = axis·(gap·m_d/M) = axis·5,  r_d = -axis·(gap·m_s/M) = -axis·5
        //     (axis = world +Y)
        //   ω = (0.02, -0.03, 0.01) rad/s
        //   ω×r_s = (-0.05, 0, 0.1);  ω×r_d = (0.05, 0, -0.1)
        //   opening = DefaultSeparationOpeningMs = 1.0 m/s (no declared impulse)
        //     v_s' = ω×r_s + axis·(opening·m_d/M) = (-0.05, 0.5, 0.1)
        //     v_d' = ω×r_d - axis·(opening·m_s/M) = (0.05, -0.5, -0.1)
        //   r_s×v_s' = (0.5, 0, 0.25); r_d×v_d' = (0.5, 0, 0.25)  [opening term is parallel
        //     to r, so it contributes nothing to either cross product]
        //   L_after.X = (I_s_trans+I_d_trans)·ωx + 1000·(0.5+0.5) = 17166.667·0.02 + 1000
        //             = 343.333 + 1000 = 1343.333
        //   L_after.Y = (I_s_axial+I_d_axial)·ωy + 0            = 1000·(-0.03) = -30
        //   L_after.Z = 17166.667·0.01 + 500                    = 171.667 + 500 = 671.667
        //   L_before.X = I_total_trans·ωx = 67166.667·0.02 = 1343.333
        //   L_before.Y = I_total_axial·ωy = 1000·(-0.03) = -30
        //   L_before.Z = 67166.667·0.01 = 671.667
        var vessel = BuildTwoPartStack(
            upperMass: 1_000.0, lowerMass: 1_000.0,
            upperLength: 10.0, lowerLength: 10.0,
            diameter: 2.0);
        vessel.AngularVelocity = new Vector3d(0.02, -0.03, 0.01);
        Vector3d omega = vessel.AngularVelocity;
        Vector3d posBefore = vessel.Position;
        Vector3d velBefore = vessel.Velocity;

        double iTotalTrans = vessel.Parts.TransverseMomentOfInertia;
        double iTotalAxial = vessel.Parts.AxialMomentOfInertia;
        Vector3d lBefore = new(
            iTotalTrans * omega.X,
            iTotalAxial * omega.Y,
            iTotalTrans * omega.Z);

        var debris = vessel.Stage();
        Assert.NotNull(debris);

        double iShipTrans = vessel.Parts.TransverseMomentOfInertia;
        double iShipAxial = vessel.Parts.AxialMomentOfInertia;
        double iDebrisTrans = debris!.Parts.TransverseMomentOfInertia;
        double iDebrisAxial = debris.Parts.AxialMomentOfInertia;

        Vector3d rShip = vessel.Position - posBefore;
        Vector3d rDebris = debris.Position - posBefore;
        Vector3d vShipRel = vessel.Velocity - velBefore;
        Vector3d vDebrisRel = debris.Velocity - velBefore;

        Vector3d orbitalShip = rShip.Cross(vShipRel) * vessel.TotalMass;
        Vector3d orbitalDebris = rDebris.Cross(vDebrisRel) * debris.TotalMass;

        Vector3d lAfter = new(
            iShipTrans * omega.X + iDebrisTrans * omega.X
                + orbitalShip.X + orbitalDebris.X,
            iShipAxial * omega.Y + iDebrisAxial * omega.Y
                + orbitalShip.Y + orbitalDebris.Y,
            iShipTrans * omega.Z + iDebrisTrans * omega.Z
                + orbitalShip.Z + orbitalDebris.Z);

        AssertClose(1_343.333, lBefore.X, 1e-3);
        AssertClose(-30.0, lBefore.Y, 1e-9);
        AssertClose(671.667, lBefore.Z, 1e-3);

        AssertVectorClose(lBefore, lAfter, 1e-6 * System.Math.Max(lBefore.Magnitude, 1.0));
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(1.0)]
    [InlineData(20.0)]
    public void StagingOpensTheFullRendererGapRegardlessOfMassRatio(double upperToLowerMassRatio)
    {
        const double lowerMass = 100_000.0;
        double upperMass = lowerMass * upperToLowerMassRatio;
        var vessel = BuildTwoPartStack(
            upperMass: upperMass, lowerMass: lowerMass,
            upperLength: 30.0, lowerLength: 45.0);

        var debris = vessel.Stage();
        Assert.NotNull(debris);

        Vector3d axis = Vector3d.Up; // Orientation is Identity in this fixture.
        double gap = (vessel.Position - debris!.Position).Dot(axis);
        AssertClose(debris.VehicleLength, gap, 1e-9);
    }

    [Fact]
    public void StagingProducesNonZeroRelativeVelocityFromTheSimLayerAlone()
    {
        var vessel = BuildTwoPartStack(
            upperMass: 1_500_000.0, lowerMass: 3_300_000.0,
            upperLength: 50.0, lowerLength: 70.0);

        var debris = vessel.Stage();
        Assert.NotNull(debris);

        Vector3d axis = Vector3d.Up;
        double relativeOpening = (vessel.Velocity - debris!.Velocity).Dot(axis);
        AssertClose(Vessel.DefaultSeparationOpeningMs, relativeOpening, 1e-9);
    }

    [Fact]
    public void DeclaredSeparationImpulseOverridesTheDefaultOpeningRate()
    {
        // m1 (ship after detach) = 2000 kg, m2 (debris) = 500 kg → reduced mass
        // μ = m1·m2/(m1+m2) = 1_000_000/2500 = 400 kg.
        // J = 10_000 N·s → relative opening rate = J/μ = 10_000/400 = 25 m/s.
        const double impulseNs = 10_000.0;
        const double shipMass = 2_000.0;
        const double debrisMass = 500.0;
        const double expectedOpening = 25.0;

        var vessel = BuildTwoPartStack(
            upperMass: shipMass, lowerMass: debrisMass,
            upperLength: 4.0, lowerLength: 3.0,
            separationImpulseNs: impulseNs);

        var debris = vessel.Stage();
        Assert.NotNull(debris);

        Vector3d axis = Vector3d.Up;
        double relativeOpening = (vessel.Velocity - debris!.Velocity).Dot(axis);
        AssertClose(expectedOpening, relativeOpening, 1e-9);
    }

    /// <summary>
    /// Builds a command → decoupler → structure stack: <c>Stage()</c> detaches the lower
    /// (structure) part as debris while the decoupler stays with the surviving vessel, same
    /// topology as the real Starship/Super Heavy stack (ring stays with Starship, booster
    /// detaches).
    /// </summary>
    private static Vessel BuildTwoPartStack(
        double upperMass,
        double lowerMass,
        double upperLength,
        double lowerLength,
        double diameter = 3.0,
        double separationImpulseNs = 0.0)
    {
        var upper = new Part(new PartDefinition
        {
            Id = "upper-stage",
            CategoryStr = "command",
            MassDry = upperMass,
            LengthM = upperLength,
            DiameterM = diameter,
        });
        var decoupler = new Part(new PartDefinition
        {
            Id = "test-decoupler",
            CategoryStr = "decoupler",
            MassDry = 0.0,
            LengthM = 0.0,
            DiameterM = diameter,
            SeparationImpulseNs = separationImpulseNs,
        });
        var lower = new Part(new PartDefinition
        {
            Id = "lower-stage",
            CategoryStr = "structure",
            MassDry = lowerMass,
            LengthM = lowerLength,
            DiameterM = diameter,
        });

        var vessel = new Vessel { Name = "Test Stack" };
        vessel.Parts.SetRoot(upper);
        vessel.Parts.AddJoint(new Joint(upper, decoupler, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(decoupler, lower, "bottom", "top"));
        return vessel;
    }

    private static void AssertClose(double expected, double actual, double tolerance)
    {
        Assert.True(System.Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected:R}, got {actual:R} (tolerance {tolerance:R}).");
    }

    private static void AssertVectorClose(Vector3d expected, Vector3d actual, double tolerance)
    {
        Assert.True(System.Math.Abs(expected.X - actual.X) <= tolerance,
            $"X: expected {expected.X:R}, got {actual.X:R}.");
        Assert.True(System.Math.Abs(expected.Y - actual.Y) <= tolerance,
            $"Y: expected {expected.Y:R}, got {actual.Y:R}.");
        Assert.True(System.Math.Abs(expected.Z - actual.Z) <= tolerance,
            $"Z: expected {expected.Z:R}, got {actual.Z:R}.");
    }
}
