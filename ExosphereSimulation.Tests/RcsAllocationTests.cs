namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

/// <summary>
/// Reaction control from declared thrusters only: moments and forces follow from each nozzle's
/// position, direction and thrust; duties stay in [0, 1]; propellant flows at F/(Isp·g0).
/// Analytic gates of fidelity phase F3.
/// </summary>
[Trait("FidelityGate", "F3")]
[Trait("GateKind", "analytic")]
public sealed class RcsAllocationTests
{
    private const double Thrust = 400.0;
    private const double Isp = 290.0;
    private const double Radius = 2.0;

    /// <summary>Four tangential roll pairs and four axial pitch/yaw pairs on a ring at y = 0.</summary>
    private static List<RcsThrusterGeometry> Ring(out Part owner)
    {
        owner = new Part(new PartDefinition { Id = "ring", CategoryStr = "command", MassDry = 1_000.0 }, "ring");
        owner.RcsAttitudeEnabled = true;
        var list = new List<RcsThrusterGeometry>();
        foreach (var (px, pz) in new[] { (Radius, 0.0), (-Radius, 0.0), (0.0, Radius), (0.0, -Radius) })
        {
            var position = new Vector3d(px, 0.0, pz);
            var tangent = new Vector3d(-pz, 0.0, px).Normalized;
            foreach (var direction in new[] { tangent, -tangent, Vector3d.Up, -Vector3d.Up })
                list.Add(new RcsThrusterGeometry(owner, position, direction, Thrust, Isp));
        }
        return list;
    }

    [Fact]
    public void AThrusterMomentIsLeverCrossForce()
    {
        var owner = new Part(new PartDefinition { Id = "p", CategoryStr = "command" }, "p") { RcsAttitudeEnabled = true };
        var thruster = new RcsThrusterGeometry(owner, new Vector3d(0.0, 5.0, 2.0), new Vector3d(1.0, 0.0, 0.0), Thrust, Isp);
        var torque = thruster.Torque(new Vector3d(0.0, 1.0, 0.0));
        // r = (0, 4, 2), F = (400, 0, 0): r×F = (0, 800, −1600).
        Assert.Equal(0.0, torque.X, 9);
        Assert.Equal(800.0, torque.Y, 9);
        Assert.Equal(-1600.0, torque.Z, 9);
        Assert.Equal(Thrust / (Isp * RcsThrusterGeometry.StandardGravity), thruster.MassFlowKgPerS, 12);
    }

    [Fact]
    public void ARollRequestFiresACoupleWithNoNetForce()
    {
        var thrusters = Ring(out _);
        var duty = new double[thrusters.Count];
        var desired = new Vector3d(0.0, 1_000.0, 0.0);
        var result = RcsAllocation.Allocate(thrusters, Vector3d.Zero, desired, Vector3d.Zero, duty);
        Assert.Equal(1_000.0, result.TorqueBody.Y, 3);
        Assert.InRange(new Vector3d(result.TorqueBody.X, 0.0, result.TorqueBody.Z).Magnitude, 0.0, 1e-3);
        Assert.InRange(result.ForceBody.Magnitude, 0.0, 1e-3);
        Assert.All(duty, u => Assert.InRange(u, 0.0, 1.0));
        double expectedFlow = duty.Sum() * Thrust / (Isp * RcsThrusterGeometry.StandardGravity);
        Assert.Equal(expectedFlow, result.MassFlowKgPerS, 12);
    }

    [Fact]
    public void ATranslationRequestFiresForceWithNoNetMoment()
    {
        var thrusters = Ring(out _);
        var duty = new double[thrusters.Count];
        var result = RcsAllocation.Allocate(thrusters, Vector3d.Zero, Vector3d.Zero, new Vector3d(0.0, 1_200.0, 0.0), duty);
        Assert.Equal(1_200.0, result.ForceBody.Y, 3);
        Assert.InRange(result.TorqueBody.Magnitude, 0.0, 1e-2);
    }

    [Fact]
    public void ADemandBeyondAuthoritySaturatesAtFullDuty()
    {
        var thrusters = Ring(out _);
        var duty = new double[thrusters.Count];
        var authority = RcsAllocation.Authority(thrusters, Vector3d.Zero);
        // Roll: four tangential thrusters per sense at the ring radius.
        Assert.Equal(4.0 * Thrust * Radius, authority.Y, 9);
        var result = RcsAllocation.Allocate(thrusters, Vector3d.Zero, new Vector3d(0.0, 1e9, 0.0), Vector3d.Zero, duty);
        Assert.Equal(authority.Y, result.TorqueBody.Y, 3);
        Assert.All(duty, u => Assert.InRange(u, 0.0, 1.0));
    }

    [Fact]
    public void ABrokenOrDisabledPartDoesNotFire()
    {
        var thrusters = Ring(out var owner);
        owner.RcsAttitudeEnabled = false;
        var duty = new double[thrusters.Count];
        var result = RcsAllocation.Allocate(thrusters, Vector3d.Zero, new Vector3d(0.0, 1_000.0, 0.0), Vector3d.Zero, duty);
        Assert.Equal(Vector3d.Zero, result.TorqueBody);
        Assert.Equal(0.0, RcsAllocation.Authority(thrusters, Vector3d.Zero).Y);
    }
}
