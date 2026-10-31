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
    // Rigid-body quantities. Vessel.Position is the datum (lowest point of the stack); the
    // physical state is the centre of mass, moving at v_datum + ω×(q·c).
    private static Vector3d CenterOfMassVelocity(Vessel v) =>
        v.Velocity + v.AngularVelocity.Cross(v.Orientation.Rotate(v.Parts.CenterOfMass));

    private static Vector3d AngularMomentumAbout(Vessel v, Vector3d point)
    {
        var tensor = v.Parts.GetMassProperties().InertiaBody;
        var spin = v.Orientation.Rotate(tensor.Multiply(v.Orientation.Inverse().Rotate(v.AngularVelocity)));
        return spin + (v.CenterOfMass - point).Cross(CenterOfMassVelocity(v)) * v.TotalMass;
    }

    [Fact]
    public void StagingConservesLinearMomentum()
    {
        // Asymmetric masses/lengths on purpose: Σmᵢ·v_COM,i = M·v_COM must hold for any mass
        // ratio and any rotation, with the separation impulse equal and opposite.
        var vessel = BuildTwoPartStack(
            upperMass: 4_800_000.0, lowerMass: 1_500_000.0,
            upperLength: 90.0, lowerLength: 50.0);
        vessel.Velocity = new Vector3d(1_200.0, -400.0, 7_300.0);
        vessel.AngularVelocity = new Vector3d(0.02, -0.03, 0.01);
        Vector3d momentumBefore = CenterOfMassVelocity(vessel) * vessel.TotalMass;

        var debris = vessel.Stage();
        Assert.NotNull(debris);

        Vector3d momentumAfter = CenterOfMassVelocity(vessel) * vessel.TotalMass
            + CenterOfMassVelocity(debris!) * debris.TotalMass;
        AssertVectorClose(momentumBefore, momentumAfter, 1e-9 * momentumBefore.Magnitude);
    }

    [Fact]
    public void StagingConservesCentreOfMass()
    {
        // Every part keeps its place at separation, so the mass-weighted centre is exact.
        var vessel = BuildTwoPartStack(
            upperMass: 20_000.0, lowerMass: 300_000.0,
            upperLength: 12.0, lowerLength: 70.0);
        vessel.Position = new Vector3d(1.0e7, 2.0e7, -3.0e7);
        vessel.Orientation = Quaterniond.FromAxisAngle(new Vector3d(1, 1, 0).Normalized, 0.7);
        Vector3d comBefore = vessel.CenterOfMass;
        double massBefore = vessel.TotalMass;

        var debris = vessel.Stage();
        Assert.NotNull(debris);

        double massAfter = vessel.TotalMass + debris!.TotalMass;
        Vector3d comAfter = (vessel.CenterOfMass * vessel.TotalMass
            + debris.CenterOfMass * debris.TotalMass) / massAfter;
        AssertClose(massBefore, massAfter, 1e-12);
        AssertVectorClose(comBefore, comAfter, 1e-6);
    }

    [Fact]
    public void StagingConservesAngularMomentumAboutTheCombinedCentreOfMass()
    {
        // Full rigid-body bookkeeping: each fragment's spin (its tensor about its own centre
        // of mass) plus the orbital term m·(r−C)×v_COM. The axial opening impulse acts along
        // the line joining the two centres, so it adds no moment either.
        var vessel = BuildTwoPartStack(
            upperMass: 1_000.0, lowerMass: 3_000.0,
            upperLength: 10.0, lowerLength: 25.0,
            diameter: 2.0);
        vessel.Orientation = Quaterniond.FromAxisAngle(new Vector3d(0, 0, 1), 0.3);
        vessel.AngularVelocity = new Vector3d(0.02, -0.03, 0.01);
        Vector3d centre = vessel.CenterOfMass;
        Vector3d lBefore = AngularMomentumAbout(vessel, centre);

        var debris = vessel.Stage();
        Assert.NotNull(debris);

        Vector3d lAfter = AngularMomentumAbout(vessel, centre) + AngularMomentumAbout(debris!, centre);
        AssertVectorClose(lBefore, lAfter, 1e-9 * System.Math.Max(lBefore.Magnitude, 1.0));
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
    // Physical stack nodes so parts sit end to end; without them every part shares the
    // root position and the per-part inertia tensor loses the stack's parallel-axis term.
    private static List<AttachmentNodeDef> StackNodes(double length) =>
    [
        new AttachmentNodeDef { Id = "top", Position = [0.0, length * 0.5, 0.0] },
        new AttachmentNodeDef { Id = "bottom", Position = [0.0, -length * 0.5, 0.0] },
    ];

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
            AttachmentNodes = StackNodes(upperLength),
            CategoryStr = "command",
            MassDry = upperMass,
            LengthM = upperLength,
            DiameterM = diameter,
        });
        var decoupler = new Part(new PartDefinition
        {
            Id = "test-decoupler",
            AttachmentNodes = StackNodes(0.0),
            CategoryStr = "decoupler",
            MassDry = 0.0,
            LengthM = 0.0,
            DiameterM = diameter,
            SeparationImpulseNs = separationImpulseNs,
        });
        var lower = new Part(new PartDefinition
        {
            Id = "lower-stage",
            AttachmentNodes = StackNodes(lowerLength),
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
