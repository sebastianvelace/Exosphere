namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;
using Xunit;

public sealed class NavigationRegressionTests
{
    private const double SunGm = 1.32712440018e20;
    private const double Au = 1.495978707e11;

    [Fact]
    public void BodyHierarchyPropagationIsIndependentOfInputOrder()
    {
        static (CelestialBody sun, CelestialBody planet, CelestialBody moon) MakeSystem()
        {
            var sun = new CelestialBody { Id = "sun", GM = SunGm };
            var planet = new CelestialBody
            {
                Id = "planet", GM = 3.986e14,
                OrbitalElements = new OrbitalElements
                {
                    SemiMajorAxis = Au, Eccentricity = 0.01,
                    MeanAnomalyAtEpoch = 0.4, ReferenceBodyId = "sun",
                },
            };
            var moon = new CelestialBody
            {
                Id = "moon", GM = 4.9e12,
                OrbitalElements = new OrbitalElements
                {
                    SemiMajorAxis = 384_400_000.0, Eccentricity = 0.055,
                    MeanAnomalyAtEpoch = 1.2, ReferenceBodyId = "planet",
                },
            };
            return (sun, planet, moon);
        }

        const double t = 86_400.0;
        var a = MakeSystem();
        KeplerPropagator.PropagateAllBodies(new[] { a.sun, a.planet, a.moon }, t);
        var b = MakeSystem();
        KeplerPropagator.PropagateAllBodies(new[] { b.moon, b.planet, b.sun }, t);

        Assert.True((a.planet.Position - b.planet.Position).Magnitude < 1e-6);
        Assert.True((a.moon.Position - b.moon.Position).Magnitude < 1e-6);
        Assert.True((a.moon.Velocity - b.moon.Velocity).Magnitude < 1e-9);
    }

    [Fact]
    public void EarthToMarsHohmannHasExpectedSignAndFlightTime()
    {
        var plan = HohmannTransfer.Compute(SunGm, Au, 1.523679 * Au);

        Assert.True(plan.FirstBurnDeltaV > 0.0);
        Assert.True(plan.SecondBurnDeltaV > 0.0);
        Assert.InRange(plan.TimeOfFlight / 86400.0, 250.0, 270.0);
        Assert.InRange(plan.RequiredPhaseAngle, 0.0, 2.0 * System.Math.PI);
    }

    [Fact]
    public void EarthToVenusHohmannStartsWithRetrogradeBurn()
    {
        var plan = HohmannTransfer.Compute(SunGm, Au, 0.723332 * Au);

        Assert.True(plan.FirstBurnDeltaV < 0.0);
        Assert.True(plan.SecondBurnDeltaV < 0.0);
        Assert.InRange(plan.TimeOfFlight / 86400.0, 140.0, 160.0);
        Assert.InRange(plan.RequiredPhaseAngle, 0.0, 2.0 * System.Math.PI);
    }

    [Fact]
    public void HohmannRejectsDegenerateRadii()
    {
        Assert.Throws<ArgumentException>(() => HohmannTransfer.Compute(SunGm, Au, Au));
        Assert.Throws<ArgumentOutOfRangeException>(() => HohmannTransfer.Compute(SunGm, -1.0, Au));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Patched-conic SOI transition tests (on-rails propagation).
    //
    // Continuity strategy: propagating the SAME initial condition for the SAME total
    // sim time must give (nearly) the same INERTIAL end state regardless of the warp
    // sub-step size. If the SOI re-framing introduced a positional/velocity jump at the
    // boundary, the coarse-warp run would diverge from the fine-warp run. The two paths
    // cross the boundary at slightly different points, so we use physical tolerances
    // (metres on position, m/s on velocity) rather than bit-exact equality.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VesselLeavingEarthSoiReReferencesToSunWithoutStateJump()
    {
        // Hyperbolic escape from Earth, started inside Earth's SOI and aimed radially
        // outward so it climbs out of the SOI into heliocentric space.
        // Two independent universes (same fresh epoch) so the only variable between the
        // runs is the warp sub-step size — Earth must occupy the same spot at t = 0.
        double r0 = 700_000_000.0;   // inside Earth's SOI (≈ 924 000 km)
        Vector3d startOffset, startVelEarth;
        {
            var seed = NewSolarSystem().GetBody("earth")!;
            double escapeSpeed = System.Math.Sqrt(2.0 * seed.GM / r0);
            // Velocity TRANSVERSE to the radius → a genuine hyperbola (non-radial, h ≠ 0)
            // that climbs out through the SOI boundary rather than a radial fall/lob.
            startOffset   = new Vector3d(r0, 0.0, 0.0);
            startVelEarth = new Vector3d(0.0, 1.25 * escapeSpeed, 0.0);
        }

        var fineUniverse = NewSolarSystem();
        var fine = MakeEscapingVessel(fineUniverse.GetBody("earth")!, startOffset, startVelEarth);
        fineUniverse.AddVessel(fine);
        var (finePos, fineVel) = RunWarp(fineUniverse, fine, totalSeconds: 700_000.0, stepSeconds: 1.0);

        var coarseUniverse = NewSolarSystem();
        var coarse = MakeEscapingVessel(coarseUniverse.GetBody("earth")!, startOffset, startVelEarth);
        coarseUniverse.AddVessel(coarse);
        var (coarsePos, coarseVel) = RunWarp(coarseUniverse, coarse, totalSeconds: 700_000.0, stepSeconds: 4.0);

        // The vessel must have actually left Earth's SOI and been re-referenced to the Sun.
        Assert.Equal("sun", fine.ReferenceBodyId);
        Assert.Equal("sun", fine.OrbitalState!.ReferenceBodyId);
        Assert.Equal("sun", coarse.ReferenceBodyId);

        AssertFinite(finePos);
        AssertFinite(fineVel);
        AssertFinite(coarsePos);
        AssertFinite(coarseVel);

        // Inertial continuity: coarse and fine warp agree to within physical tolerance,
        // proving the SOI re-frame introduced no spurious jump. The residual is the
        // sub-step granularity of the existing on-rails loop — bounded by
        // (step − step) × heliocentric speed (~30 km/s) ≈ a few hundred km here — NOT a
        // discontinuity at the boundary, which would be orders of magnitude larger.
        double posError = (finePos - coarsePos).Magnitude;
        double velError = (fineVel - coarseVel).Magnitude;
        Assert.True(posError < 500_000.0, $"Inertial position diverged by {posError:N1} m across warp resolutions.");
        Assert.True(velError < 50.0, $"Inertial velocity diverged by {velError:N3} m/s across warp resolutions.");
    }

    // Regression guard for warp-resolution-independent SOI transitions. At real max warp
    // (×100000 @ 50 Hz → 2000 s of sim time advanced per single Tick) the bodies are propagated
    // to the tick's END time before the vessel is propagated, so BOTH the initial conic and the
    // SOI-crossing reconstruction must use the reference body's state at the EPOCH / crossing time
    // (BodyStateAt), not its end-of-tick position. Using the stale end-of-tick body position
    // biased the orbit by (body velocity × dt) — at 2000 s that is ~60 000 km, which shifted the
    // SOI exit by ~99 000 s of sim time and the heliocentric injection by ~1.6e8 m. A hyperbolic
    // Earth→Sun escape must now end at (nearly) the same inertial state at any warp sub-step size.
    [Fact]
    public void EarthSoiExitStaysContinuousAtRealMaxWarpTick()
    {
        double r0 = 700_000_000.0;   // inside Earth's SOI
        var seed = NewSolarSystem().GetBody("earth")!;
        double escapeSpeed = System.Math.Sqrt(2.0 * seed.GM / r0);
        var startOffset   = new Vector3d(r0, 0.0, 0.0);
        var startVelEarth = new Vector3d(0.0, 1.25 * escapeSpeed, 0.0);

        var fineUniverse = NewSolarSystem();
        var fine = MakeEscapingVessel(fineUniverse.GetBody("earth")!, startOffset, startVelEarth);
        fineUniverse.AddVessel(fine);
        var (finePos, fineVel) = RunWarp(fineUniverse, fine, totalSeconds: 700_000.0, stepSeconds: 1.0);

        // Coarse = one full max-warp Tick worth of sim time per Tick (2000 s).
        var coarseUniverse = NewSolarSystem();
        var coarse = MakeEscapingVessel(coarseUniverse.GetBody("earth")!, startOffset, startVelEarth);
        coarseUniverse.AddVessel(coarse);
        var (coarsePos, coarseVel) = RunWarp(coarseUniverse, coarse, totalSeconds: 700_000.0, stepSeconds: 2_000.0);

        Assert.Equal("sun", fine.OrbitalState!.ReferenceBodyId);
        Assert.Equal("sun", coarse.OrbitalState!.ReferenceBodyId);
        AssertFinite(coarsePos);
        AssertFinite(coarseVel);

        double posError = (finePos - coarsePos).Magnitude;
        double velError = (fineVel - coarseVel).Magnitude;
        Assert.True(posError < 500_000.0, $"SOI exit jumped {posError:N1} m (vel {velError:N4} m/s) at max-warp Tick.");
        Assert.True(velError < 50.0, $"SOI exit velocity jumped {velError:N3} m/s at max-warp Tick.");
    }

    [Fact]
    public void VesselEnteringMoonSoiReReferencesToMoon()
    {
        var universe = NewSolarSystem();
        var earth = universe.GetBody("earth")!;
        var moon = universe.GetBody("moon")!;

        // Start just outside the Moon's SOI (≈ 66 100 km) on the Earth-facing side,
        // aimed toward the Moon so the vessel crosses into the lunar SOI within the run.
        var toMoon  = (moon.Position - earth.Position).Normalized;
        var lateral = new Vector3d(-toMoon.Y, toMoon.X, 0.0).Normalized;
        var startInertial = moon.Position - toMoon * 68_000_000.0;   // 68 000 km Earthward of the Moon (SOI ≈ 66 100 km)
        // Moon's heliocentric velocity plus a closing component toward it and a grazing
        // term: a fast, lateral approach so the vessel enters the lunar SOI on a fly-by
        // (periapsis well above the surface) instead of falling straight onto the Moon.
        var approach = moon.Velocity + toMoon * 800.0 + lateral * 400.0;

        var vessel = new Vessel
        {
            Position        = startInertial,
            Velocity        = approach,
            IsOnRails       = true,
            ReferenceBodyId = earth.Id,
        };
        universe.AddVessel(vessel);

        // Sanity: it starts dominated by Earth (outside the lunar SOI).
        Assert.Equal("earth", universe.GetDominantBody(vessel.Position).Id);

        // Tick under warp until the on-rails propagation re-references the conic onto the
        // Moon (i.e. the SOI boundary was crossed). Capture that exact moment: the patched
        // conic re-frame is what we are validating. We stop at the transition so the
        // subsequent (suborbital, in this geometry) lunar arc does not muddy the assertion.
        universe.TimeScale = 100_000.0;
        bool crossed = false;
        string refAtCrossing = "";
        string orbitRefAtCrossing = "";
        Vector3d relAtCrossing = Vector3d.Zero;
        for (int i = 0; i < 6000; i++)
        {
            universe.Tick(10.0 / universe.TimeScale);
            if (vessel.ReferenceBodyId == "moon")
            {
                // Capture the re-frame the instant it happens. In this geometry the lunar
                // approach is suborbital, so the surface-impact guard may null OrbitalState
                // within the very same tick — so snapshot the conic reference here too.
                crossed = true;
                refAtCrossing      = vessel.ReferenceBodyId;
                orbitRefAtCrossing = vessel.OrbitalState?.ReferenceBodyId ?? "";
                relAtCrossing      = vessel.Position - universe.GetBody("moon")!.Position;
                break;
            }
            if (vessel.IsDestroyed) break;
        }

        // The conic was re-referenced onto the Moon when the lunar SOI was entered —
        // both the vessel's reference body and the recomputed conic frame are "moon".
        Assert.True(crossed, "Vessel never crossed into the lunar SOI / re-referenced to the Moon.");
        Assert.Equal("moon", refAtCrossing);
        Assert.Equal("moon", orbitRefAtCrossing);

        // Re-framed relative state is genuinely Moon-centric, finite, and inside the SOI.
        AssertFinite(vessel.Position);
        AssertFinite(vessel.Velocity);
        Assert.True(relAtCrossing.Magnitude <= moon.SphereOfInfluence + 1.0,
            "Vessel should be at/inside the lunar SOI at the transition.");
    }

    [Fact]
    public void LongEarthToMarsCruisePropagatesWithoutDriftOrNaN()
    {
        var universe = NewSolarSystem();
        var earth = universe.GetBody("earth")!;
        var sun = universe.GetBody("sun")!;

        // Heliocentric transfer: depart from just outside Earth's SOI on a Hohmann-like
        // arc toward Mars. Reference is the Sun from the outset (already past Earth's SOI).
        double rDepart = (earth.Position - sun.Position).Magnitude + 1_500_000_000.0; // outside Earth SOI
        var radial = (earth.Position - sun.Position).Normalized;
        var prograde = new Vector3d(-radial.Y, radial.X, 0.0).Normalized;            // +90° in-plane

        double rTarget = 1.523679 * Au;
        var plan = HohmannTransfer.Compute(sun.GM, rDepart, rTarget);
        double vCircular = System.Math.Sqrt(sun.GM / rDepart);
        double vTransfer = vCircular + plan.FirstBurnDeltaV;

        var startInertial = sun.Position + radial * rDepart;
        var startVel = prograde * vTransfer;

        var vessel = new Vessel
        {
            Position        = startInertial,
            Velocity        = startVel,
            IsOnRails       = true,
            ReferenceBodyId = sun.Id,
        };
        universe.AddVessel(vessel);

        // Conserved heliocentric specific energy at departure.
        var rel0 = startInertial - sun.Position;
        double energy0 = startVel.MagnitudeSquared * 0.5 - sun.GM / rel0.Magnitude;

        // Cruise roughly half the transfer time of flight under high warp.
        var (endPos, endVel) = RunWarp(universe, vessel, totalSeconds: plan.TimeOfFlight * 0.5, stepSeconds: 2_000.0);

        AssertFinite(endPos);
        AssertFinite(endVel);
        Assert.Equal("sun", vessel.ReferenceBodyId);

        // Specific orbital energy is conserved for a force-free heliocentric coast —
        // it must not drift even after a long propagation.
        var relEnd = endPos - sun.Position;
        double energyEnd = endVel.MagnitudeSquared * 0.5 - sun.GM / relEnd.Magnitude;
        double scale = System.Math.Max(1.0, System.Math.Abs(energy0));
        Assert.True(System.Math.Abs(energy0 - energyEnd) <= scale * 1e-6,
            $"Heliocentric specific energy drifted: {energy0:R} -> {energyEnd:R}.");

        // The vessel must have travelled outward toward Mars' orbit, not stalled.
        Assert.True(relEnd.Magnitude > rDepart, "Vessel should climb toward the target orbit.");
    }

    // ── No-regression: a high circular Earth orbit keeps its reference and shape ──
    [Fact]
    public void StableLeoDoesNotSpuriouslyChangeReferenceBody()
    {
        var universe = NewSolarSystem();
        var earth = universe.GetBody("earth")!;

        // Above ThermosphereTopAltitude so residual drag (B3) does not force RK4 — this
        // asserts rails SOI stability, not LEO lifetime.
        double r = earth.Radius + 1_200_000.0;
        double vCircular = System.Math.Sqrt(earth.GM / r);
        var vessel = new Vessel
        {
            Position        = earth.Position + new Vector3d(r, 0.0, 0.0),
            Velocity        = earth.Velocity + new Vector3d(0.0, vCircular, 0.0),
            IsOnRails       = true,
            ReferenceBodyId = earth.Id,
        };
        universe.AddVessel(vessel);

        double period = 2.0 * System.Math.PI * System.Math.Sqrt(r * r * r / earth.GM);
        RunWarp(universe, vessel, totalSeconds: period * 2.0, stepSeconds: 2.0);

        // The vessel must NOT spuriously re-reference to another body or be destroyed:
        // it is deep inside Earth's SOI and never approaches a boundary.
        Assert.Equal("earth", vessel.ReferenceBodyId);
        Assert.True(vessel.IsOnRails);
        Assert.Equal("earth", vessel.OrbitalState!.ReferenceBodyId);
        Assert.False(vessel.IsDestroyed);

        // The orbit stays bounded. A small periodic radius variation is expected: the
        // conic is referenced to Earth, which accelerates around the Sun.
        double rEnd = (vessel.Position - earth.Position).Magnitude;
        Assert.True(System.Math.Abs(rEnd - r) < 250_000.0, $"orbit radius diverged to {rEnd:N1} m (start {r:N1}).");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Vessel MakeEscapingVessel(CelestialBody earth, Vector3d relPos, Vector3d relVel) =>
        new()
        {
            Position        = earth.Position + relPos,
            Velocity        = earth.Velocity + relVel,
            IsOnRails       = true,
            ReferenceBodyId = earth.Id,
        };

    /// <summary>
    /// Drives the universe forward by <paramref name="totalSeconds"/> of sim time in
    /// pure-rails warp slices of <paramref name="stepSeconds"/>, returning the vessel's
    /// final inertial position and velocity. TimeScale &gt; 1000 forces the all-on-rails
    /// branch (TickRails), the path the SOI transition must handle correctly.
    /// </summary>
    private static (Vector3d position, Vector3d velocity) RunWarp(
        Universe universe, Vessel vessel, double totalSeconds, double stepSeconds)
    {
        universe.TimeScale = 100_000.0;            // pure-rails branch
        double realStep = stepSeconds / universe.TimeScale;
        int steps = (int)System.Math.Ceiling(totalSeconds / stepSeconds);
        for (int i = 0; i < steps; i++)
        {
            universe.Tick(realStep);
            if (vessel.IsDestroyed) break;
        }
        return (vessel.Position, vessel.Velocity);
    }

    private static Universe NewSolarSystem() =>
        Universe.LoadFromDataDirectory(FindRepoRoot().FullName + "/data");

    private static void AssertFinite(Vector3d v)
    {
        Assert.False(double.IsNaN(v.X) || double.IsNaN(v.Y) || double.IsNaN(v.Z), "Vector component is NaN.");
        Assert.False(double.IsInfinity(v.X) || double.IsInfinity(v.Y) || double.IsInfinity(v.Z), "Vector component is Infinite.");
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
            {
                return dir;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Encounter prediction (forward propagation vs. a moving target body).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EncounterFindsClosestApproachWhenPathsCrossButMisses()
    {
        // Vessel on a Hohmann transfer ellipse from 1 AU out to 1.5 AU; the target rides a
        // circular 1.5 AU orbit but is phased so the vessel arrives at apoapsis with the
        // target far away → no SOI capture, only a finite closest approach.
        double rDepart = Au, rTarget = 1.5 * Au;
        var plan = HohmannTransfer.Compute(SunGm, rDepart, rTarget);

        var startPos = new Vector3d(rDepart, 0.0, 0.0);
        double vTransfer = System.Math.Sqrt(SunGm * (2.0 / rDepart - 1.0 / plan.TransferSemiMajorAxis));
        var startVel = new Vector3d(0.0, vTransfer, 0.0);   // prograde at periapsis
        var vesselOrbit = OrbitalElements.FromStateVector(startPos, startVel, SunGm, "sun", 0.0);

        // Target starts at +x (same side as the vessel's start) → vessel reaches apoapsis on
        // the −x axis but the target is nowhere near it. Circular rate ω = √(μ/r³).
        double omega = System.Math.Sqrt(SunGm / (rTarget * rTarget * rTarget));
        Vector3d Target(double t) =>
            new(rTarget * System.Math.Cos(omega * t), rTarget * System.Math.Sin(omega * t), 0.0);

        var result = TrajectoryPrediction.FindEncounter(
            vesselOrbit, SunGm, Target, targetSoiRadius: 5.77e8 /* Mars SOI */,
            startTime: 0.0, searchWindow: plan.TimeOfFlight * 1.05);

        Assert.True(double.IsFinite(result.ClosestApproachDistance));
        Assert.True(result.ClosestApproachDistance > 0.0);
        Assert.InRange(result.TimeOfClosestApproach, 0.0, plan.TimeOfFlight * 1.05);
    }

    [Fact]
    public void EncounterDetectsSoiEntryWhenTargetIsPhasedForArrival()
    {
        // Same transfer ellipse, but the target is phased so it sits at the vessel's apoapsis
        // (angle π) exactly at arrival (t = ToF) → SOI capture must trigger.
        double rDepart = Au, rTarget = 1.5 * Au;
        var plan = HohmannTransfer.Compute(SunGm, rDepart, rTarget);

        var startPos = new Vector3d(rDepart, 0.0, 0.0);
        double vTransfer = System.Math.Sqrt(SunGm * (2.0 / rDepart - 1.0 / plan.TransferSemiMajorAxis));
        var startVel = new Vector3d(0.0, vTransfer, 0.0);
        var vesselOrbit = OrbitalElements.FromStateVector(startPos, startVel, SunGm, "sun", 0.0);

        double omega = System.Math.Sqrt(SunGm / (rTarget * rTarget * rTarget));
        Vector3d Target(double t)
        {
            double ang = System.Math.PI - omega * (plan.TimeOfFlight - t);   // π at t = ToF
            return new Vector3d(rTarget * System.Math.Cos(ang), rTarget * System.Math.Sin(ang), 0.0);
        }

        var result = TrajectoryPrediction.FindEncounter(
            vesselOrbit, SunGm, Target, targetSoiRadius: 5.77e8,
            startTime: 0.0, searchWindow: plan.TimeOfFlight * 1.05);

        Assert.True(result.HasEncounter, "Phased rendezvous should enter the target SOI.");
        Assert.True(result.ClosestApproachDistance <= 5.77e8);
        Assert.True(double.IsFinite(result.TimeOfSoiEntry));
        Assert.InRange(result.TimeOfSoiEntry, 0.0, plan.TimeOfFlight * 1.05);
        Assert.True(result.TimeOfSoiEntry <= result.TimeOfClosestApproach + 1.0);
    }

    [Fact]
    public void EncounterReportsNoCaptureForAComfortablyDistantTarget()
    {
        // Vessel circular at 1 AU; target circular at 5 AU, never within a small SOI.
        var startPos = new Vector3d(Au, 0.0, 0.0);
        double vCirc = System.Math.Sqrt(SunGm / Au);
        var startVel = new Vector3d(0.0, vCirc, 0.0);
        var vesselOrbit = OrbitalElements.FromStateVector(startPos, startVel, SunGm, "sun", 0.0);

        double rTarget = 5.0 * Au;
        double omega = System.Math.Sqrt(SunGm / (rTarget * rTarget * rTarget));
        Vector3d Target(double t) =>
            new(rTarget * System.Math.Cos(omega * t), rTarget * System.Math.Sin(omega * t), 0.0);

        double period = 2.0 * System.Math.PI * System.Math.Sqrt(Au * Au * Au / SunGm);
        var result = TrajectoryPrediction.FindEncounter(
            vesselOrbit, SunGm, Target, targetSoiRadius: 5.77e8,
            startTime: 0.0, searchWindow: period);

        Assert.False(result.HasEncounter);
        Assert.True(double.IsPositiveInfinity(result.TimeOfSoiEntry));
        Assert.True(result.ClosestApproachDistance >= rTarget - Au - 1.0);
    }
}
