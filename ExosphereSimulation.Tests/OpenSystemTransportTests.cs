namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

/// <summary>
/// F4 open-system transport: the declared internal-flow path satisfies continuity exactly,
/// mass shed at its own station carries no moment, the production step reproduces the rocket
/// equation with the feed-momentum impulses at ignition and cutoff, and jet damping follows an
/// independently integrated open-system rotation law.
/// </summary>
[Trait("FidelityGate", "F4")]
[Trait("GateKind", "analytic")]
public sealed class OpenSystemTransportTests
{
    private static readonly Matrix3x3d Zero = Matrix3x3d.Diagonal(0, 0, 0);

    [Fact]
    public void TheFeedPathSatisfiesContinuityExactly()
    {
        // A draining column and a fixed store feed two outlets in one group.
        double lambda = 500.0;
        var dry = new FixedShapeMassComponent("dry", new(0.3, 2, 0), Matrix3x3d.Diagonal(1, 0.5, 1), [new MassKnot(0, 800)]);
        var column = FixedShapeMassComponent.SettledColumn("lox", new(0, -3, 0), Vector3d.Up, lambda, 0.8,
            [new MassKnot(0, 2000), new MassKnot(2, 1200)]);
        var fixedStore = new FixedShapeMassComponent("fuel", new(0, 4, 0.2), Matrix3x3d.Diagonal(0.4, 0.2, 0.4),
            [new MassKnot(0, 600), new MassKnot(2, 400)]);
        var distribution = new BodyMassDistribution(2, [dry, column, fixedStore]);
        var group = new TransportFlowGroup("pool",
            [new TransportStoreDebit(1, 400, 0, 2), new TransportStoreDebit(2, 100, 0, 2)],
            [new TransportOutlet(new(1.5, -5, 0), -Vector3d.Up, 0.3, 3, 0, 2),
             new TransportOutlet(new(-1.5, -5, 0.4), -Vector3d.Up, 0.3, 1, 0, 2)]);
        var transport = new OpenSystemTransport(distribution, [group]);
        var mass = distribution.Sample(0.7);
        var sample = transport.Sample(0.7, MassBoundarySide.Right, mass, new Vector3d(0.01, 0.02, -0.03));
        var c = mass.CenterOfMassBodyM;
        var flux = (new Vector3d(1.5, -5, 0) - c) * 375 + (new Vector3d(-1.5, -5, 0.4) - c) * 125;
        var identity = mass.CenterOfMassRateBodyMps * mass.MassKg + flux;
        Assert.InRange((sample.RelativeLinearMomentumBodyKgMps - identity).Magnitude, 0, 1e-9);
        Assert.Equal(500, sample.OutflowKgPerS, 12);
        Assert.Equal(-500, mass.MassRateKgPerS, 12);
        // Independently reconstruct each route from its geometric endpoints, then
        // translate the sum from the fixed datum to the instantaneous mass centre.
        var linearByStore = new Vector3d[3];
        var angularByStore = new Vector3d[3];
        foreach (var (time, side) in new[] { (0.0, MassBoundarySide.Right),
            (0.7, MassBoundarySide.Right), (2.0, MassBoundarySide.Left) })
        {
            transport.SampleMomentumByComponent(time, side, linearByStore, angularByStore);
            var exit = new Vector3d(0.75, -5, 0.1);
            var loss = new Vector3d(0, -3 + (2000 - 400 * time) / lambda, 0);
            Assert.InRange((linearByStore[1] - (exit - loss) * 400).Magnitude, 0, 1e-10);
            Assert.InRange((angularByStore[1] - (loss.Cross(new(0, -3, 0))
                + new Vector3d(0, -3, 0).Cross(exit)) * 400).Magnitude, 0, 1e-10);
            Assert.InRange((linearByStore[2] - (exit - fixedStore.PositionBodyM) * 100).Magnitude, 0, 1e-10);
            var at = distribution.Sample(time, side);
            var aggregate = transport.Sample(time, side, at, Vector3d.Zero);
            var p = linearByStore.Aggregate(Vector3d.Zero, (sum, value) => sum + value);
            var h = angularByStore.Aggregate(Vector3d.Zero, (sum, value) => sum + value)
                - at.CenterOfMassBodyM.Cross(p);
            Assert.InRange((p - aggregate.RelativeLinearMomentumBodyKgMps).Magnitude, 0, 1e-9);
            Assert.InRange((h - aggregate.RelativeAngularMomentumBodyKgM2PerS).Magnitude, 0, 1e-9);
        }
        transport.SampleMomentumByComponent(2, MassBoundarySide.Right, linearByStore, angularByStore);
        Assert.All(linearByStore, value => Assert.Equal(Vector3d.Zero, value));
        Assert.All(angularByStore, value => Assert.Equal(Vector3d.Zero, value));
    }

    [Fact]
    public void AnExhaustedColumnsLeftDerivativeDoesNotDisappearWithItsZeroLengthFeedPath()
    {
        var dry = new FixedShapeMassComponent("dry", new(0, 4, 0), Matrix3x3d.Diagonal(2, 1, 2), [new MassKnot(0, 4000)]);
        var column = FixedShapeMassComponent.SettledColumn("liquid", new(0.2, 1, -0.1), Vector3d.Up, 100, 2,
            [new MassKnot(0, 3), new MassKnot(1, 0)]);
        var distribution = new BodyMassDistribution(1, [dry, column]);
        var exit = new Vector3d(0.25, -2, 0.1);
        var transport = new OpenSystemTransport(distribution,
            [new("drain", [new(1, 3, 0, 1)], [new(exit, -Vector3d.Up, 0, 3, 0, 1)])]);
        var mass = distribution.Sample(1, MassBoundarySide.Left);
        var end = transport.Sample(1, MassBoundarySide.Left, mass, Vector3d.Zero);
        const double epsilon = 1e-5;
        var before = transport.Sample(1 - epsilon, MassBoundarySide.Right, distribution.Sample(1 - epsilon), Vector3d.Zero);
        var pRate = (end.RelativeLinearMomentumBodyKgMps - before.RelativeLinearMomentumBodyKgMps) / epsilon;
        var expected = mass.CenterOfMassAccelerationBodyMps2 * mass.MassKg - pRate;
        Assert.InRange((expected - end.ForceBodyN).Magnitude, 0, 1e-9);
        // Independent finite difference of datum-referenced angular route momentum.
        var beforeMass = distribution.Sample(1 - epsilon);
        var endH = end.RelativeAngularMomentumBodyKgM2PerS + mass.CenterOfMassBodyM.Cross(end.RelativeLinearMomentumBodyKgMps);
        var beforeH = before.RelativeAngularMomentumBodyKgM2PerS
            + beforeMass.CenterOfMassBodyM.Cross(before.RelativeLinearMomentumBodyKgMps);
        var torqueDatum = end.TorqueBodyNm + mass.CenterOfMassBodyM.Cross(
            end.ForceBodyN - mass.CenterOfMassAccelerationBodyMps2 * mass.MassKg);
        Assert.InRange((torqueDatum + (endH - beforeH) / epsilon).Magnitude, 0, 1e-8);
    }

    [Fact]
    public void MassShedAtItsOwnStationCarriesNoMoment()
    {
        // A co-rotating point leaving where it sits: inertia loss and exit flux cancel, and the
        // remaining body feels only the moving-centroid kinematics.
        var dry = new FixedShapeMassComponent("dry", Vector3d.Zero, Matrix3x3d.Diagonal(2, 3, 2), [new MassKnot(0, 50)]);
        var lump = new FixedShapeMassComponent("lump", new(1, 2, 0), Zero, [new MassKnot(0, 10), new MassKnot(1, 4)]);
        var distribution = new BodyMassDistribution(1, [dry, lump]);
        var transport = new OpenSystemTransport(distribution,
            [new TransportFlowGroup("shed", [new TransportStoreDebit(1, 6, 0, 1)],
                [new TransportOutlet(new(1, 2, 0), Vector3d.Up, 0, 1, 0, 1)])]);
        var omega = new Vector3d(0.3, -0.2, 0.5);
        var mass = distribution.Sample(0.4);
        var sample = transport.Sample(0.4, MassBoundarySide.Right, mass, omega);
        Assert.InRange(sample.TorqueBodyNm.Magnitude, 0, 1e-12);
        Assert.InRange(sample.RelativeLinearMomentumBodyKgMps.Magnitude, 0, 1e-15);
        var expected = (mass.CenterOfMassAccelerationBodyMps2 + omega.Cross(mass.CenterOfMassRateBodyMps) * 2) * mass.MassKg;
        Assert.InRange((sample.ForceBodyN - expected).Magnitude, 0, 1e-12);
    }

    [Fact]
    public void ABurnFollowsTheRocketEquationWithItsFeedImpulses()
    {
        var (universe, vessel, engine) = FreeEngine(fuel: 0.6, oxidizer: 1.8, transport: true);
        double flow = engine.GetMassFlow();
        double exhaust = engine.GetThrustMagnitude() / flow;
        double initialMass = vessel.TotalMass;
        // The store (fixed at the part centre) feeds the exit at the part's aft end: p = q (e − s).
        double feedMomentum = flow * 0.5;
        double finalMass = initialMass - 2.4;
        for (int i = 0; i < 300 && !engine.FuelDepleted; i++) universe.Tick(0.02);
        universe.Tick(0.02);
        Assert.True(engine.FuelDepleted);
        Assert.Equal(finalMass, vessel.TotalMass, 9);
        // Ignition pushes the structure forward by p/m0; the cutoff hands p back over m1.
        double expected = exhaust * System.Math.Log(initialMass / finalMass)
            + feedMomentum * (1 / initialMass - 1 / finalMass);
        Assert.Equal(expected, vessel.Velocity.Y, 9);
        Assert.Equal("no-outflow", vessel.LastVariableMassStatus);
    }

    [Fact]
    public void JetDampingFollowsTheIndependentOpenSystemRotationLaw()
    {
        // A pitching stack: the engine at the aft end sheds mass from a store forward of it.
        var (universe, vessel, engine) = FreeEngine(fuel: 30, oxidizer: 90, transport: true, dryCore: 4000, thrustScale: 100);
        vessel.AngularVelocity = new Vector3d(0.05, 0, 0);
        double omega = 0.05, time = 0, dt = 0.02;
        var core = vessel.Parts.Root!;
        Assert.True(vessel.Parts.TryGetPartLocalPosition(core, out var corePosition));
        Assert.True(vessel.Parts.TryGetPartLocalPosition(engine, out var enginePosition));
        double flow = engine.GetMassFlow();
        double exitY = enginePosition.Y - 0.5, storeY = enginePosition.Y;
        double dryEngine = engine.EffectiveMassDry, dryCore = core.EffectiveMassDry;
        double propellant0 = engine.LiquidFuel + engine.Oxidizer;
        // Independent law for a body of point-like stations plus their own cylinders.
        double Inertia(double t, out double center, out double rate)
        {
            double p = propellant0 - flow * t, m = dryCore + dryEngine + p;
            center = (dryCore * corePosition.Y + (dryEngine + p) * enginePosition.Y) / m;
            double own = dryCore * (3 * 1 + 4) / 12.0 + (dryEngine + p) * (3 * 0.25 + 1) / 12.0;
            double inertia = own + dryCore * System.Math.Pow(corePosition.Y - center, 2)
                + (dryEngine + p) * System.Math.Pow(enginePosition.Y - center, 2);
            double h = 1e-6, pp = propellant0 - flow * (t + h), mp = dryCore + dryEngine + pp;
            double cp = (dryCore * corePosition.Y + (dryEngine + pp) * enginePosition.Y) / mp;
            double ip = dryCore * (3 + 4) / 12.0 + (dryEngine + pp) * 1.75 / 12.0 + dryCore * System.Math.Pow(corePosition.Y - cp, 2)
                + (dryEngine + pp) * System.Math.Pow(enginePosition.Y - cp, 2);
            rate = (ip - inertia) / h;
            return inertia;
        }
        double Derivative(double t, double w)
        {
            double inertia = Inertia(t, out double c, out double rate);
            // J_out about the moving centre plus I_dot; the store-to-exit duct is on the axis.
            double jet = flow * System.Math.Pow(exitY - c, 2);
            return -(rate + jet) * w / inertia;
        }
        for (int i = 0; i < 50; i++)
        {
            universe.Tick(dt);
            for (int k = 0; k < 20; k++)
            {
                double h = dt / 20;
                double k1 = Derivative(time, omega), k2 = Derivative(time + h / 2, omega + h / 2 * k1);
                double k3 = Derivative(time + h / 2, omega + h / 2 * k2), k4 = Derivative(time + h, omega + h * k3);
                omega += h / 6 * (k1 + 2 * k2 + 2 * k3 + k4);
                time += h;
            }
        }
        Assert.Equal("active", vessel.LastVariableMassStatus);
        Assert.True(omega < 0.05 * 0.999, "the law must damp measurably over the burn");
        Assert.Equal(omega, vessel.AngularVelocity.Magnitude, 7);
    }

    private static (Universe Universe, Vessel Vessel, Part Engine) FreeEngine(double fuel, double oxidizer,
        bool transport, double dryCore = 0, double thrustScale = 1)
    {
        var engine = new Part(new PartDefinition
        {
            Id = "engine", CategoryStr = "engine", MassDry = 1000, LengthM = 1, DiameterM = 1,
            ThrustVac = 980.665 * thrustScale, ThrustSL = 980.665 * thrustScale, IspVac = 100, IspSL = 100, ThrustPositionYM = -0.5,
            MixtureRatio = 3, FuelTypeStr = "liquidfuel", FuelCapacityLF = fuel, FuelCapacityOx = oxidizer,
            AttachmentNodes = [new AttachmentNodeDef { Id = "top", Position = [0.0, 0.5, 0.0], Size = 1 }],
        }, "engine") { IsStagingActive = true, ThrottleLevel = 1 };
        var vessel = new Vessel("transport")
        {
            Position = Vector3d.Right * 10_000, ReferenceBodyId = "body", Throttle = 1, SASEnabled = false,
        };
        if (dryCore > 0)
        {
            var core = new Part(new PartDefinition
            {
                Id = "core", CategoryStr = "command", MassDry = dryCore, LengthM = 2, DiameterM = 2,
                AttachmentNodes = [new AttachmentNodeDef { Id = "bottom", Position = [0.0, -1.0, 0.0], Size = 1 }],
            }, "core");
            vessel.Parts.SetRoot(core);
            vessel.Parts.AddJoint(new Joint(core, engine, "bottom", "top"));
        }
        else vessel.Parts.SetRoot(engine);
        var universe = new Universe { ActiveVessel = vessel, VariableMassTransportEnabled = transport };
        universe.AddBody(new CelestialBody { Id = "body", Radius = 1, SphereOfInfluence = 1e9 });
        universe.AddVessel(vessel);
        return (universe, vessel, engine);
    }
}
