namespace ExosphereSimulation.Tests;

using System;
using System.IO;
using Exosphere.Simulation;
using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// First-order zonal J2 in <see cref="CelestialBody.GetGravityAt"/> plus the WGS84
/// ellipsoid Earth ships in JSON. Osculating Kepler on-rails still ignores J2
/// (same class as no third body); active/RK4 vessels feel the field.
/// </summary>
public sealed class J2GravityTests
{
    [Fact]
    public void ShippedEarthJsonArmsJ2AndWgs84Ellipsoid()
    {
        var earth = LoadEarth();
        Assert.InRange(earth.J2, 1.082e-3, 1.083e-3);
        Assert.Equal(6_378_137.0, earth.EquatorialRadius);
        Assert.InRange(earth.PolarRadius, 6_356_752.0, 6_356_753.0);
        Assert.True(earth.IsOblate);
        var pos = earth.GetSurfacePosition(0.0, 0.0, 0.0);
        var pointMass = ClonePointMass(earth);
        Assert.True((earth.GetGravityAt(pos) - pointMass.GetGravityAt(pos)).Magnitude > 1e-4);
    }

    [Fact]
    public void ValladoBranchApproachesPointMassAsJ2Vanishes()
    {
        var spherical = ClonePointMass(BuildOblateEarth());
        var tiny = new CelestialBody
        {
            GM = spherical.GM,
            Radius = spherical.Radius,
            EquatorialRadius = 6_378_137.0,
            AxialTilt = spherical.AxialTilt,
            Position = spherical.Position,
            J2 = 1e-12,
        };
        var pos = spherical.Position + new Vector3d(spherical.Radius * 0.4, spherical.Radius * 0.7, spherical.Radius * 0.5);
        var aTiny = tiny.GetGravityAt(pos);
        var aPoint = spherical.GetGravityAt(pos);
        double scale = aPoint.Magnitude;
        Assert.True((aTiny - aPoint).Magnitude / scale < 1e-9);
    }

    [Fact]
    public void ZeroJ2MatchesPointMassBitIdentically()
    {
        var earth = BuildOblateEarth();
        var point = ClonePointMass(earth);
        var zero = new CelestialBody
        {
            GM = earth.GM,
            Radius = earth.Radius,
            EquatorialRadius = earth.EquatorialRadius,
            PolarRadius = earth.PolarRadius,
            AxialTilt = earth.AxialTilt,
            Position = earth.Position,
            J2 = 0.0,
        };
        var pos = earth.Position + new Vector3d(
            earth.Radius * 0.3, earth.Radius * 0.8, earth.Radius * 0.4);
        Assert.Equal(0.0, (zero.GetGravityAt(pos) - point.GetGravityAt(pos)).Magnitude);
    }

    [Fact]
    public void PolarSurfaceGravityExceedsEquatorialSurfaceGravity()
    {
        var earth = LoadEarth();
        var equator = earth.GetSurfacePosition(0.0, 0.0, 0.0);
        var pole = earth.GetSurfacePosition(90.0, 0.0, 0.0);
        double gEq = earth.GetGravityAt(equator).Magnitude;
        double gPole = earth.GetGravityAt(pole).Magnitude;
        Assert.True(gPole > gEq, $"polar {gPole} should exceed equatorial {gEq} on the WGS84 surface");
        Assert.InRange(gEq, 9.80, 9.82);
        Assert.InRange(gPole, 9.82, 9.85);
        Assert.InRange(earth.GetAltitude(equator), -0.05, 0.05);
        Assert.InRange(earth.GetAltitude(pole), -0.05, 0.05);
    }

    [Fact]
    public void GeodeticAltitudeRoundTripsOnTheEllipsoid()
    {
        var earth = LoadEarth();
        foreach (double lat in new[] { -80.0, -28.6, 0.0, 28.6, 80.0 })
        foreach (double lon in new[] { -80.6, 0.0, 90.0 })
        {
            var pos = earth.GetSurfacePosition(lat, lon, 12.0);
            Assert.Equal(12.0, earth.GetAltitude(pos), 3);
            Assert.Equal(lat, earth.GetLatitude(pos), 6);
        }
    }

    [Fact]
    public void GeodeticUpPlacesTheSameLatitudeWithoutANorthwardJump()
    {
        var earth = LoadEarth();
        foreach (double lat in new[] { 0.0, 28.6, 45.0, 60.0, 80.0 })
        {
            var pos = earth.GetSurfacePosition(lat, -80.6, 1.0);
            var again = earth.GetSurfacePositionFromGeodeticUp(earth.GetGeodeticUp(pos), 1.0);
            Assert.Equal(lat, earth.GetLatitude(again), 6);
            Assert.True(
                (again - pos).Magnitude < 0.05,
                $"lat {lat} jumped {(again - pos).Magnitude:N1} m");
        }
    }

    [Fact]
    public void GeocentricRayReconstructionKeepsGeodeticLatitude()
    {
        var earth = LoadEarth();
        foreach (double lat in new[] { 0.0, 28.6, 45.0, 60.0, 80.0 })
        {
            var pad = earth.GetSurfacePosition(lat, 0.0, 4.0);
            var dir = (pad - earth.Position).Normalized;
            var again = earth.GetPositionAlongDirection(dir, 4.0);
            Assert.Equal(lat, earth.GetLatitude(again), 6);
            Assert.True(
                (again - pad).Magnitude < 0.05,
                $"lat {lat} slid {(again - pad).Magnitude:N1} m along the geocentric ray");
        }
        Assert.Equal(
            earth.EquatorialRadius,
            earth.GeocentricRadiusOfEllipsoid(EquatorialUnit(earth)),
            6);
        Assert.Equal(
            earth.PolarRadius,
            earth.GeocentricRadiusOfEllipsoid(earth.RotationAxis),
            6);
    }

    [Fact]
    public void MidLatitudeImpactStaysOnTheSameMeridian()
    {
        var earth = LoadEarth();
        var start = earth.GetSurfacePosition(45.0, 0.0, -0.4);
        var vessel = new Vessel("geodetic-anchor")
        {
            Position = start,
            Velocity = earth.Velocity + earth.GetSurfaceVelocity(start) - earth.GetGeodeticUp(start) * 80.0,
            ReferenceBodyId = earth.Id,
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "anchor-mass",
            CategoryStr = "command",
            MassDry = 1_000.0,
        }));
        var universe = new Universe { ActiveVessel = vessel };
        universe.AddBody(earth);
        universe.AddVessel(vessel);
        universe.Tick(0.1);

        Assert.True(vessel.IsSurfaceSettled || vessel.IsDestroyed);
        Assert.Equal(45.0, earth.GetLatitude(vessel.Position), 3);
        Assert.True(
            (vessel.Position - earth.GetSurfacePosition(45.0, 0.0, earth.GetAltitude(vessel.Position))).Magnitude < 50.0,
            "impact should not slide kilometres along the meridian");
    }

    [Fact]
    public void MidLatitudeGroundHoldStaysOnTheSameMeridian()
    {
        var earth = LoadEarth();
        var pad = earth.GetSurfacePosition(45.0, 0.0, 3.0);
        var geocentric = (pad - earth.Position).Normalized;
        var vessel = new Vessel("geodetic-hold")
        {
            Position = pad + geocentric * 4.0,
            Velocity = earth.Velocity + earth.GetSurfaceVelocity(pad),
            Orientation = Quaterniond.FromTo(Vector3d.Up, earth.GetGeodeticUp(pad)),
            ReferenceBodyId = earth.Id,
            IsGroundHeld = true,
            GroundNormal = geocentric,
            GroundOffset = 4.0,
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "hold-mass",
            CategoryStr = "command",
            MassDry = 1_000.0,
        }));
        var universe = new Universe { ActiveVessel = vessel };
        universe.AddBody(earth);
        universe.AddVessel(vessel);
        for (int i = 0; i < 500; i++)
            universe.Tick(0.02);

        Assert.True(vessel.IsGroundHeld);
        Assert.Equal(45.0, earth.GetLatitude(vessel.Position), 3);
        Assert.Equal(4.0, earth.GetAltitude(vessel.Position), 2);
        var expected = earth.GetSurfacePositionAtTime(45.0, 0.0, universe.CurrentTime, 4.0);
        Assert.True(
            (vessel.Position - expected).Magnitude < 1.0,
            $"hold slid {(vessel.Position - expected).Magnitude:N1} m");
    }

    [Fact]
    public void EqualRadiusEquatorialGravityExceedsPolarGravity()
    {
        // At equal geocentric radius the J2 term ALONE makes |g| larger on the
        // equator than at the pole (Vallado 8-36). Stronger polar surface gravity
        // on Earth comes from the smaller polar radius plus zero centrifugal force,
        // not from this harmonic at constant r.
        var earth = BuildOblateEarth();
        Assert.True(earth.J2 > 0.0);
        double r = earth.EquatorialRadius;
        var equator = earth.Position + EquatorialUnit(earth) * r;
        var pole = earth.Position + earth.RotationAxis * r;

        double gEq = earth.GetGravityAt(equator).Magnitude;
        double gPole = earth.GetGravityAt(pole).Magnitude;
        Assert.True(gEq > gPole, $"equatorial {gEq} should exceed polar {gPole} at equal r");
    }

    [Fact]
    public void GravityIsFiniteAtSurfaceAndLeo()
    {
        var earth = BuildOblateEarth();
        var surface = earth.Position + EquatorialUnit(earth) * earth.EquatorialRadius;
        var leo = earth.Position + EquatorialUnit(earth) * (earth.Radius + 400_000.0);

        foreach (var pos in new[] { surface, leo })
        {
            var g = earth.GetGravityAt(pos);
            Assert.True(double.IsFinite(g.X) && double.IsFinite(g.Y) && double.IsFinite(g.Z));
            Assert.True(g.Magnitude > 0.0);
        }
    }

    [Fact]
    public void InclinedLeoTwoBodyEnergyIsNotConservedButJ2RatesHaveValladoSign()
    {
        var earth = BuildOblateEarth();
        double a = earth.Radius + 400_000.0;
        double e = 0.05;
        double inc = 28.0 * MathUtils.DEG_TO_RAD;
        double p = a * (1.0 - e * e);
        double nu = 0.0;
        double r0 = p / (1.0 + e * System.Math.Cos(nu));
        double sqrtMuP = System.Math.Sqrt(earth.GM / p);

        EquatorialBasis(earth, out var ex, out var ey, out var ez);
        // Perifocal in equatorial frame at Ω=ω=0: r along êx, v in the i-inclined plane.
        var rEq = new Vector3d(r0, 0.0, 0.0);
        var vEq = new Vector3d(
            -sqrtMuP * System.Math.Sin(nu),
            sqrtMuP * (e + System.Math.Cos(nu)) * System.Math.Cos(inc),
            sqrtMuP * (e + System.Math.Cos(nu)) * System.Math.Sin(inc));
        var pos = earth.Position + ex * rEq.X + ey * rEq.Y + ez * rEq.Z;
        var vel = ex * vEq.X + ey * vEq.Y + ez * vEq.Z;

        double twoBody0 = TwoBodyEnergy(pos, vel, earth);
        double j2Energy0 = J2MechanicalEnergy(pos, vel, earth);
        var el0 = ElementsInEquatorialFrame(earth, pos, vel, epoch: 0.0);

        double period = 2.0 * System.Math.PI * System.Math.Sqrt(a * a * a / earth.GM);
        double dt = 5.0;
        int steps = (int)System.Math.Round(period * 8.0 / dt);
        double t = 0.0;
        for (int i = 0; i < steps; i++)
        {
            (pos, vel) = RK4Integrator.StepPosVel(
                pos, vel, t, dt,
                (p, _, _) => earth.GetGravityAt(p));
            t += dt;
        }

        double twoBody1 = TwoBodyEnergy(pos, vel, earth);
        double j2Energy1 = J2MechanicalEnergy(pos, vel, earth);
        var el1 = ElementsInEquatorialFrame(earth, pos, vel, epoch: t);

        Assert.True(
            System.Math.Abs(twoBody1 - twoBody0) > 10.0 * System.Math.Abs(j2Energy1 - j2Energy0) + 0.1,
            $"two-body energy should drift under J2 (Δε={twoBody1 - twoBody0}, ΔE_J2={j2Energy1 - j2Energy0})");
        Assert.True(
            System.Math.Abs(j2Energy1 - j2Energy0) / System.Math.Max(1.0, System.Math.Abs(j2Energy0)) < 1e-6,
            $"J2 mechanical energy drifted {j2Energy1 - j2Energy0}");

        // Vallado 9-41: dΩ/dt = −(3/2) n J2 (Re/p)² cos i  → negative for prograde i=28°.
        double dLan = WrapPi(el1.LongitudeOfAscendingNode - el0.LongitudeOfAscendingNode);
        Assert.True(dLan < 0.0, $"RAAN should regress, ΔΩ={dLan}");

        // Vallado 9-42: dω/dt = +(3/4) n J2 (Re/p)² (5 cos²i − 1) → positive at 28°.
        double dAop = WrapPi(el1.ArgumentOfPeriapsis - el0.ArgumentOfPeriapsis);
        Assert.True(dAop > 0.0, $"argument of periapsis should advance, Δω={dAop}");
    }

    private static double TwoBodyEnergy(Vector3d pos, Vector3d vel, CelestialBody body)
    {
        double r = (pos - body.Position).Magnitude;
        return 0.5 * vel.MagnitudeSquared - body.GM / r;
    }

    private static double J2MechanicalEnergy(Vector3d pos, Vector3d vel, CelestialBody body)
    {
        var rVec = pos - body.Position;
        double r = rVec.Magnitude;
        double sinLat = rVec.Normalized.Dot(body.RotationAxis);
        double reR = body.EquatorialRadius / r;
        double phi = -body.GM / r * (1.0 - 0.5 * body.J2 * reR * reR * (3.0 * sinLat * sinLat - 1.0));
        return 0.5 * vel.MagnitudeSquared + phi;
    }

    private static OrbitalElements ElementsInEquatorialFrame(
        CelestialBody body, Vector3d pos, Vector3d vel, double epoch)
    {
        EquatorialBasis(body, out var ex, out var ey, out var ez);
        var rel = pos - body.Position;
        var rEq = new Vector3d(rel.Dot(ex), rel.Dot(ey), rel.Dot(ez));
        var vEq = new Vector3d(vel.Dot(ex), vel.Dot(ey), vel.Dot(ez));
        return OrbitalElements.FromStateVector(rEq, vEq, body.GM, body.Id, epoch);
    }

    private static void EquatorialBasis(
        CelestialBody body, out Vector3d ex, out Vector3d ey, out Vector3d ez)
    {
        ez = body.RotationAxis;
        var seed = System.Math.Abs(ez.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        ex = seed.Cross(ez).Normalized;
        ey = ez.Cross(ex).Normalized;
    }

    private static Vector3d EquatorialUnit(CelestialBody body)
    {
        EquatorialBasis(body, out var ex, out _, out _);
        return ex;
    }

    private static CelestialBody ClonePointMass(CelestialBody earth) => new()
    {
        GM = earth.GM,
        Radius = earth.Radius,
        AxialTilt = earth.AxialTilt,
        Position = earth.Position,
        J2 = 0.0,
    };

    private static double WrapPi(double rad)
    {
        double wrapped = (rad + System.Math.PI) % (2.0 * System.Math.PI);
        if (wrapped < 0.0) wrapped += 2.0 * System.Math.PI;
        return wrapped - System.Math.PI;
    }

    private static CelestialBody BuildOblateEarth()
    {
        var spherical = LoadEarth();
        return new CelestialBody
        {
            Id = spherical.Id,
            Name = spherical.Name,
            GM = spherical.GM,
            Radius = spherical.Radius,
            EquatorialRadius = 6_378_137.0,
            PolarRadius = 6_356_752.314245,
            J2 = 1.08262668e-3,
            AxialTilt = spherical.AxialTilt,
            Position = spherical.Position,
            RotationalPeriod = spherical.RotationalPeriod,
        };
    }

    private static CelestialBody LoadEarth() =>
        CelestialBody.LoadFromJson(Path.Combine(FindRepoRoot().FullName, "data", "bodies", "earth.json"));

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
                return dir;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
