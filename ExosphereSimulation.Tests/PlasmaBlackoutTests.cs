namespace ExosphereSimulation.Tests;

using System.IO;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Systems;

/// <summary>
/// Acceptance for the entry comms blackout and the peak-g tracker.
///
/// The entry profiles below are kinematic waypoint tables (altitude, airspeed) driven
/// through the REAL USSA-76 atmosphere loaded from <c>data/bodies</c>, so the densities and
/// therefore the Sutton-Graves fluxes are the ones the simulation itself would produce.
/// The trajectories are not integrated: this tests the blackout DECISION given a flight
/// condition, not the flight mechanics that produce it (those are covered by
/// <see cref="OrbitalReentrySurvivalTests"/>), and it must stay independent of them.
/// </summary>
public sealed class PlasmaBlackoutTests
{
    /// <summary>Effective nose radius of a 9 m Starship-class body (m).</summary>
    private const double ShipNoseRadius = 4.5;

    /// <summary>Effective nose radius of a Mercury-class capsule (m).</summary>
    private const double CapsuleNoseRadius = 0.95;

    private const double Dt = 0.5;
    private const int StepsPerWaypoint = 10;   // 5 s dwell per waypoint

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "data")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static CelestialBody Earth() =>
        Universe.LoadFromDataDirectory(Path.Combine(RepoRoot(), "data")).GetBody("earth")!;

    /// <summary>
    /// Orbital de-orbit entry: interface at 120 km / 7.6 km/s, aerodynamically braked to
    /// terminal descent. Speeds follow the classic shallow orbital entry deceleration curve.
    /// </summary>
    private static readonly (double AltM, double SpeedMs)[] OrbitalEntry =
    {
        (120_000, 7_600), (110_000, 7_590), (100_000, 7_575), (95_000, 7_560),
        ( 90_000, 7_550), ( 85_000, 7_520), ( 80_000, 7_450), (75_000, 7_330),
        ( 70_000, 7_180), ( 65_000, 6_800), ( 60_000, 6_300), (55_000, 5_200),
        ( 50_000, 4_000), ( 45_000, 2_900), ( 40_000, 2_000), (35_000, 1_300),
        ( 30_000,   800), ( 25_000,   450), ( 20_000,   300), (10_000,  150),
        (  5_000,   110), (  1_000,    80), (      0,     2),
    };

    /// <summary>
    /// Mercury-Redstone class ballistic hop: apogee ~187 km, reentry peaking near 2.1 km/s.
    /// Freedom 7 kept voice contact throughout — the model must agree.
    /// </summary>
    private static readonly (double AltM, double SpeedMs)[] SuborbitalHop =
    {
        (187_000,    50), (150_000,  1_000), (120_000, 1_450), (100_000, 1_700),
        ( 90_000, 1_820), ( 80_000,  1_930), ( 70_000, 2_010), ( 60_000, 2_060),
        ( 55_000, 2_090), ( 50_000,  2_100), ( 45_000, 2_060), ( 40_000, 1_960),
        ( 35_000, 1_680), ( 30_000,  1_150), ( 25_000,   620), ( 20_000,   330),
        ( 15_000,   210), ( 10_000,    160), (  5_000,   110), (  2_000,     9),
        (      0,      9),
    };

    private static PlasmaBlackoutInput Condition(
        CelestialBody earth, double altitude, double speed, double noseRadius)
    {
        double density = earth.Atmosphere!.GetDensity(altitude);
        return new PlasmaBlackoutInput
        {
            HeatFluxWm2 = ThermalModel.ComputeHeatFlux(density, speed, noseRadius),
            DensityKgM3 = density,
            AirspeedMs  = speed,
        };
    }

    /// <summary>Flies a waypoint profile and reports where the blackout opened and closed.</summary>
    private static (bool Engaged, double EngageAltM, double ClearAltM, double FinalBlackout,
                    double LongestSeconds)
        Fly((double AltM, double SpeedMs)[] profile, double noseRadius)
    {
        var earth = Earth();
        var model = new PlasmaBlackoutModel();

        bool engaged = false;
        double engageAlt = double.NaN, clearAlt = double.NaN;

        foreach (var (alt, speed) in profile)
        {
            var condition = Condition(earth, alt, speed, noseRadius);
            for (int i = 0; i < StepsPerWaypoint; i++)
            {
                bool before = model.IsBlackedOut;
                model.Tick(Dt, condition);
                if (!before && model.IsBlackedOut)
                {
                    engaged = true;
                    if (double.IsNaN(engageAlt)) engageAlt = alt;
                }
                else if (before && !model.IsBlackedOut)
                {
                    clearAlt = alt;
                }
            }
        }

        return (engaged, engageAlt, clearAlt,
            model.IsBlackedOut ? 1.0 : 0.0, model.LongestBlackoutSeconds);
    }

    [Fact]
    public void PlasmaBlackout_EngagesDuringPeakHeatingOfOrbitalEntry()
    {
        var result = Fly(OrbitalEntry, ShipNoseRadius);

        Assert.True(result.Engaged, "an orbital-speed entry must lose the link");
        // Real orbital entries go dark high in the mesosphere — not at the 120 km interface
        // (still rarefied) and not down in the stratosphere. Measured onset is 85 km; the
        // band is kept tight enough that a retune of any engage threshold moves it out.
        Assert.InRange(result.EngageAltM, 78_000.0, 88_000.0);
    }

    [Fact]
    public void PlasmaBlackout_ClearsWellBeforeLandingOnOrbitalEntry()
    {
        var result = Fly(OrbitalEntry, ShipNoseRadius);

        Assert.True(result.Engaged);
        Assert.Equal(0.0, result.FinalBlackout);      // link is back at touchdown
        Assert.False(double.IsNaN(result.ClearAltM), "blackout never cleared");
        // Recovery happens once aerobraking drops the vehicle below ionisation speed —
        // historically 40-55 km, and always long before any landing event. Measured: 45 km.
        Assert.InRange(result.ClearAltM, 40_000.0, 52_000.0);
        Assert.True(result.ClearAltM < result.EngageAltM);
    }

    [Fact]
    public void PlasmaBlackout_NeverEngagesForSlowSuborbitalHop()
    {
        var result = Fly(SuborbitalHop, CapsuleNoseRadius);

        Assert.False(result.Engaged,
            "a ~2 km/s ballistic hop does not ionise its shock layer; Freedom 7 kept comms");
        Assert.Equal(0.0, result.LongestSeconds);
    }

    [Fact]
    public void PlasmaBlackout_SuborbitalHopSitsBelowBothEngageGates()
    {
        // Documents HOW MUCH margin each gate has, measured rather than assumed, and acts as
        // the sentinel that fails first if either engage threshold is retuned.
        //
        // The ballistic hop peaks at ~89 kW/m² — only ~11% under the engage flux threshold.
        // So heat flux does block it, but with almost no margin: a different nose radius or
        // a slightly steeper profile would flip it. Airspeed peaks at 2.1 km/s, a factor of
        // 2.4 under the ionisation gate. That asymmetry is the whole reason the model gates
        // on velocity as well as flux — velocity is the term that makes "suborbital never
        // blacks out" a robust statement instead of a lucky one.
        var earth = Earth();
        double peakFlux = 0.0;
        double peakSpeed = 0.0;
        foreach (var (alt, speed) in SuborbitalHop)
        {
            peakFlux = System.Math.Max(peakFlux,
                Condition(earth, alt, speed, CapsuleNoseRadius).HeatFluxWm2);
            peakSpeed = System.Math.Max(peakSpeed, speed);
        }

        Assert.True(peakFlux > PlasmaBlackoutModel.EngageHeatFluxWm2 * 0.8,
            $"flux is expected to be a WEAK discriminator here, saw {peakFlux:F0} W/m²");
        Assert.True(peakFlux < PlasmaBlackoutModel.EngageHeatFluxWm2);
        Assert.True(peakSpeed < PlasmaBlackoutModel.EngageAirspeedMs * 0.5,
            "the velocity gate must reject the hop by a wide margin");
    }

    [Fact]
    public void PlasmaBlackout_HasHysteresisAndDoesNotFlicker()
    {
        var model = new PlasmaBlackoutModel();
        var deep = new PlasmaBlackoutInput
        {
            AirspeedMs  = 7_000.0,
            DensityKgM3 = 5.0e-5,
            HeatFluxWm2 = 4.0e5,
        };
        // Sits BETWEEN the engage and clear sets: below the engage flux, above the clear
        // flux, and between the two airspeed gates. A memoryless threshold would toggle here.
        var marginal = new PlasmaBlackoutInput
        {
            AirspeedMs  = 4_500.0,
            DensityKgM3 = 5.0e-5,
            HeatFluxWm2 = 8.0e4,
        };

        for (int i = 0; i < 20; i++) model.Tick(Dt, deep);
        Assert.True(model.IsBlackedOut);

        int transitions = 0;
        for (int i = 0; i < 400; i++)
        {
            bool before = model.IsBlackedOut;
            model.Tick(1.0 / 60.0, i % 2 == 0 ? marginal : deep);
            if (before != model.IsBlackedOut) transitions++;
        }

        Assert.Equal(0, transitions);
        Assert.True(model.IsBlackedOut);
    }

    [Fact]
    public void PlasmaBlackout_HoldsForMinimumDurationOnceEngaged()
    {
        var model = new PlasmaBlackoutModel();
        var deep = new PlasmaBlackoutInput
        {
            AirspeedMs  = 7_000.0,
            DensityKgM3 = 5.0e-5,
            HeatFluxWm2 = 4.0e5,
        };

        model.Tick(PlasmaBlackoutModel.EngageDwellSeconds, deep);
        Assert.True(model.IsBlackedOut);

        // Condition vanishes instantly; the state must still ride out the minimum hold.
        model.Tick(0.1, PlasmaBlackoutInput.None);
        Assert.True(model.IsBlackedOut);

        model.Tick(PlasmaBlackoutModel.MinimumHoldSeconds, PlasmaBlackoutInput.None);
        Assert.False(model.IsBlackedOut);
        Assert.True(model.LongestBlackoutSeconds >= PlasmaBlackoutModel.MinimumHoldSeconds);
    }

    [Fact]
    public void PlasmaBlackout_RequiresDwellBeforeEngaging()
    {
        var model = new PlasmaBlackoutModel();
        var deep = new PlasmaBlackoutInput
        {
            AirspeedMs  = 7_000.0,
            DensityKgM3 = 5.0e-5,
            HeatFluxWm2 = 4.0e5,
        };

        model.Tick(PlasmaBlackoutModel.EngageDwellSeconds * 0.5, deep);
        Assert.False(model.IsBlackedOut);

        model.Tick(0.01, PlasmaBlackoutInput.None);   // brief dropout resets the dwell
        model.Tick(PlasmaBlackoutModel.EngageDwellSeconds * 0.6, deep);
        Assert.False(model.IsBlackedOut);
    }

    [Fact]
    public void CommsSystem_ReportsLossOfSignalDuringPlasmaBlackoutAndRecovers()
    {
        const double earthRadius = 6_371_000.0;
        var comms = new CommsSystem();
        var earthPos = Vector3d.Zero;
        var vesselPos = new Vector3d(earthRadius + 70_000.0, 0.0, 0.0);
        var bodies = new List<CelestialBody>
        {
            new() { Id = "earth", Radius = earthRadius },
        };

        var deep = new PlasmaBlackoutInput
        {
            AirspeedMs  = 7_200.0,
            DensityKgM3 = 8.0e-5,
            HeatFluxWm2 = 4.0e5,
        };

        // Line of sight is perfect: only the sheath can take the link away.
        comms.Tick(1.0, vesselPos, earthPos, bodies);
        Assert.True(comms.HasSignal);
        Assert.False(comms.PlasmaBlackout);

        comms.Tick(1.0, vesselPos, earthPos, bodies, deep);
        Assert.True(comms.PlasmaBlackout);
        Assert.False(comms.HasSignal);
        Assert.True(comms.LossOfSignalAlert);
        Assert.Equal(0.0, comms.SignalStrength);

        // Subsonic descent: sheath gone, link back, and the delay readout still works.
        for (int i = 0; i < 10; i++)
            comms.Tick(1.0, vesselPos, earthPos, bodies, PlasmaBlackoutInput.None);
        Assert.False(comms.PlasmaBlackout);
        Assert.True(comms.HasSignal);
        Assert.False(comms.LossOfSignalAlert);
        Assert.True(comms.LongestPlasmaBlackoutSeconds > 0.0);

        comms.ResetPlasmaBlackout();
        Assert.Equal(0.0, comms.LongestPlasmaBlackoutSeconds);
    }

    // ── Peak g-load tracking ────────────────────────────────────────────────────

    [Fact]
    public void EntryLoadTracker_HoldsPeakAfterLoadSubsides()
    {
        var tracker = new EntryLoadTracker();
        Assert.False(tracker.HasSample);

        foreach (double g in new[] { 0.2, 1.4, 3.1, 4.8, 4.2, 1.1, 0.3 })
            tracker.Update(1.0, g);

        Assert.Equal(0.3, tracker.CurrentG, 6);
        Assert.Equal(4.8, tracker.PeakG, 6);
        Assert.Equal(3.0, tracker.SecondsSincePeak, 6);
        Assert.True(tracker.HasSample);

        tracker.Reset();
        Assert.Equal(0.0, tracker.PeakG, 6);
        Assert.False(tracker.HasSample);
    }

    [Fact]
    public void EntryLoadTracker_ClassifiesTheRealEntryBands()
    {
        Assert.Equal(GLoadBand.Nominal,  EntryLoadTracker.Classify(0.0));
        Assert.Equal(GLoadBand.Nominal,  EntryLoadTracker.Classify(1.9));
        Assert.Equal(GLoadBand.Elevated, EntryLoadTracker.Classify(2.0));
        Assert.Equal(GLoadBand.Elevated, EntryLoadTracker.Classify(3.9));
        Assert.Equal(GLoadBand.High,     EntryLoadTracker.Classify(4.5));   // orbital wall
        Assert.Equal(GLoadBand.Severe,   EntryLoadTracker.Classify(8.5));   // ballistic abort
        Assert.Equal(GLoadBand.Nominal,  EntryLoadTracker.Classify(double.NaN));
    }

    [Fact]
    public void EntryLoadTracker_IgnoresGarbageSamples()
    {
        var tracker = new EntryLoadTracker();
        tracker.Update(1.0, 3.5);
        tracker.Update(1.0, double.NaN);
        tracker.Update(1.0, double.PositiveInfinity);
        tracker.Update(1.0, -12.0);

        Assert.Equal(3.5, tracker.CurrentG, 6);
        Assert.Equal(3.5, tracker.PeakG, 6);
    }
}
