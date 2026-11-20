namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class CatchArmRailsTests
{
    private static readonly CatchArmRails Rails = new(Vector3d.Right, 9.5, 10.0, 1.5);

    [Theory]
    [InlineData(0.0, 0.0, true)]
    [InlineData(0.0, 3.0, true)]     // arms close onto a ship 3 m off the centreline
    [InlineData(0.0, 5.0, true)]
    [InlineData(0.0, 6.0, false)]    // beyond the open arms' reach (10 − 4.5 m)
    [InlineData(9.0, 0.0, true)]
    [InlineData(10.0, 0.0, false)]   // past the catch zone along the arm
    public void FittingsRestOnTheRailsWhereverTheClosingArmsCanReach(double alongM, double acrossM, bool onRails)
    {
        var (universe, vessel) = Drop(alongM, acrossM, rollDeg: 0.0);
        for (int i = 0; i < 200 && !vessel.IsCaught; i++) universe.Tick(0.02);

        Assert.Equal(onRails, vessel.IsCaught);
    }

    [Theory]
    [InlineData(15.0, true)]
    [InlineData(30.0, false)]   // fittings swing off the rails: 4.8 cos 30° < 4.5 m hull
    [InlineData(165.0, true)]   // either fitting may take either arm
    public void FittingsMustFaceTheArms(double rollDeg, bool onRails)
    {
        var (universe, vessel) = Drop(0.0, 0.0, rollDeg);
        for (int i = 0; i < 200 && !vessel.IsCaught; i++) universe.Tick(0.02);

        Assert.Equal(onRails, vessel.IsCaught);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(20.0, -20.0)]
    [InlineData(-35.0, 35.0)]
    [InlineData(170.0, 10.0)]
    public void RollErrorTurnsThePinsAcrossTheArmsTheShortWay(double rollDeg, double expectedErrorDeg)
    {
        var roll = Quaterniond.FromAxisAngle(Vector3d.Up, rollDeg * MathUtils.DEG_TO_RAD);
        var baseline = TowerCatchBurn.PinRollErrorRad(Quaterniond.Identity, Vector3d.Up, Vector3d.Right);

        double error = TowerCatchBurn.PinRollErrorRad(roll, Vector3d.Up, Vector3d.Right) - baseline;

        Assert.Equal(expectedErrorDeg, error / MathUtils.DEG_TO_RAD, 6);
    }

    private static (Universe, Vessel) Drop(double alongM, double acrossM, double rollDeg)
    {
        var body = new CelestialBody { Id = "rails-body", Mass = 5.972e24, Radius = 6.371e6, GM = 3.986e14 };
        var vessel = new Vessel { ReferenceBodyId = body.Id, SASEnabled = false };
        var nose = new Part(new PartDefinition
        {
            Id = "rails-nose", CategoryStr = "command", MassDry = 150_000.0, LengthM = 19.0, DiameterM = 9.0,
            CatchPinLateralOffsetM = 4.4, CatchPinOffsetYM = 0.0, CatchPinRadiusM = 0.4,
            CatchPinSpringStiffnessNPerM = 2.5e6,
        });
        vessel.Parts.SetRoot(nose);
        vessel.Parts.AddPart(nose);
        vessel.ConfigureCatchContactsFromParts();
        var up = Vector3d.Up;
        var cradle = body.Position + up * (body.Radius + 500.0);
        var pin = vessel.CatchContactPoints[0];
        var across = up.Cross(Vector3d.Right).Normalized;
        vessel.Orientation = Quaterniond.FromAxisAngle(up, rollDeg * MathUtils.DEG_TO_RAD);
        vessel.Position = cradle + up * (pin.ContactRadiusM - pin.LocalPositionFromDatum.Y + 0.05)
            + Vector3d.Right * alongM + across * acrossM;
        vessel.IsAttemptingTowerCatch = true;
        vessel.CatchTargetPositionWorld = cradle;
        vessel.CatchTargetUpWorld = up;
        vessel.CatchTargetVelocityWorld = Vector3d.Zero;
        vessel.CatchTargetRails = Rails;
        var universe = new Universe { TimeScale = 1.0, ActiveVessel = vessel };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return (universe, vessel);
    }
}
