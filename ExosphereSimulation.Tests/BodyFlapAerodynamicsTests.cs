namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

[Trait("FidelityGate", "F2")]
[Trait("GateKind", "analytic")]
public sealed class BodyFlapAerodynamicsTests
{
    private const double Density = 1.2;
    private const double Temperature = 288.15; // 50 m/s is Mach 0.15: subsonic plate law.
    private const double HalfPi = System.Math.PI / 2.0;

    private static Part Hull() => new(new PartDefinition
    {
        Id = "hull", CategoryStr = "structure", MassDry = 10_000.0, LengthM = 30.0, DiameterM = 9.0,
    }, "hull");

    private static BodyFlapGeometry Flap(Part owner, double y, int side, double length = 10.0, double span = 4.0) =>
        BodyFlapGeometry.From(owner, Vector3d.Zero, new BodyFlapDef
        {
            Id = "f", HingeYM = y, Side = side, LengthM = length, SpanM = span,
        });

    [Fact]
    public void StagnationPressureCoefficientMatchesRayleighPitot()
    {
        // Cp,max = 2/(γM²)·(p02/p∞ − 1): p02/p∞ = 5.640 at Mach 2; Newtonian limit 1.839.
        Assert.InRange(BodyFlapAerodynamics.StagnationPressureCoefficient(2.0), 1.650, 1.665);
        Assert.InRange(BodyFlapAerodynamics.StagnationPressureCoefficient(25.0), 1.835, 1.840);
    }

    [Fact]
    public void ExtendedFlapBroadsideToABellyFirstFlowIsAFlatPlate()
    {
        var flap = Flap(Hull(), 0.0, side: 1);
        // Belly (−X) leads: the vehicle moves along −X through still air.
        var force = BodyFlapAerodynamics.PlateForce(flap, HalfPi, new Vector3d(-50.0, 0.0, 0.0), Density, Temperature);
        double expected = 0.5 * Density * 50.0 * 50.0 * flap.AreaM2 * BodyFlapAerodynamics.SubsonicBroadsideCoefficient;
        Assert.Equal(expected, force.X, 6);
        Assert.InRange(System.Math.Abs(force.Y) + System.Math.Abs(force.Z), 0.0, 1e-9);
    }

    [Fact]
    public void FoldedFlapCarriesNoLoadInABellyFirstFlow()
    {
        var flap = Flap(Hull(), 0.0, side: -1);
        var force = BodyFlapAerodynamics.PlateForce(flap, 0.0, new Vector3d(-50.0, 0.0, 0.0), Density, Temperature);
        Assert.Equal(Vector3d.Zero, force);
    }

    [Fact]
    public void ASymmetricPairOnlyPitchesAndADifferentialPairRolls()
    {
        var hull = Hull();
        var flaps = new[] { Flap(hull, -10.0, 1), Flap(hull, -10.0, -1) };
        var flow = new Vector3d(-50.0, 0.0, 0.0);
        var centre = new Vector3d(0.0, 0.0, 0.0);
        var symmetric = BodyFlapAerodynamics.Evaluate(flaps, new[] { 0.8, 0.8 }, Quaterniond.Identity,
            Vector3d.Zero, centre, flow, Density, Temperature);
        Assert.True(symmetric.ForceWorld.X > 0.0, "the flaps are drag against a belly-first flow");
        Assert.InRange(System.Math.Abs(symmetric.TorqueBody.Y), 0.0, 1e-6 * symmetric.TorqueBody.Magnitude);
        // Aft drag (y < centre) on a belly-first body is a pure pitch moment about Z.
        Assert.True(System.Math.Abs(symmetric.TorqueBody.Z) > 0.0);

        var differential = BodyFlapAerodynamics.Evaluate(flaps, new[] { 1.2, 0.4 }, Quaterniond.Identity,
            Vector3d.Zero, centre, flow, Density, Temperature);
        Assert.True(System.Math.Abs(differential.TorqueBody.Y) > 0.05 * differential.TorqueBody.Magnitude,
            $"unequal left/right exposure must roll the body: {differential.TorqueBody}");
    }

    [Fact]
    public void FlapsDampARotationThroughTheLocalFlow()
    {
        var hull = Hull();
        var flaps = new[] { Flap(hull, -10.0, 1), Flap(hull, -10.0, -1) };
        var flow = new Vector3d(-50.0, 0.0, 0.0);
        var still = BodyFlapAerodynamics.Evaluate(flaps, new[] { HalfPi, HalfPi }, Quaterniond.Identity,
            Vector3d.Zero, Vector3d.Zero, flow, Density, Temperature).TorqueBody;
        var omega = new Vector3d(0.0, 0.0, 0.3);
        var rotating = BodyFlapAerodynamics.Evaluate(flaps, new[] { HalfPi, HalfPi }, Quaterniond.Identity,
            omega, Vector3d.Zero, flow, Density, Temperature).TorqueBody;
        Assert.True((rotating.Z - still.Z) * omega.Z < 0.0, $"{rotating} vs {still}");
    }
}
