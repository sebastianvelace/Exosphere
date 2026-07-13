namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Systems;
using Xunit;

public sealed class SystemsMissionPhaseTests
{
    private const double EarthRadius = 6_371_000.0;
    private const double SpeedOfLight = 3e8;

    [Fact]
    public void EarthUmbra_DetectsVesselInShadow()
    {
        var earthPos   = Vector3d.Zero;
        var sunPos     = new Vector3d(150_000_000_000.0, 0.0, 0.0);
        var vesselPos  = new Vector3d(-EarthRadius * 2.0, 0.0, 0.0);

        Assert.True(MissionGeometry.IsInEarthUmbra(vesselPos, earthPos, sunPos, EarthRadius));
    }

    [Fact]
    public void EarthUmbra_SunlitSideHasNoShadow()
    {
        var earthPos  = Vector3d.Zero;
        var sunPos    = new Vector3d(150_000_000_000.0, 0.0, 0.0);
        var vesselPos = new Vector3d(EarthRadius * 2.0, 0.0, 0.0);

        Assert.False(MissionGeometry.IsInEarthUmbra(vesselPos, earthPos, sunPos, EarthRadius));
    }

    [Fact]
    public void SolarDiscVisibilityResolvesSunlightTotalityAndPenumbra()
    {
        var observer = Vector3d.Zero;
        var sun = new Vector3d(150_000_000_000.0, 0.0, 0.0);
        const double sunRadius = 696_000_000.0;

        double clear = MissionGeometry.SolarDiscVisibility(
            observer, new Vector3d(0.0, 10_000_000.0, 0.0), EarthRadius, sun, sunRadius);
        double total = MissionGeometry.SolarDiscVisibility(
            observer, new Vector3d(10_000_000.0, 0.0, 0.0), EarthRadius, sun, sunRadius);
        double partial = MissionGeometry.SolarDiscVisibility(
            observer, new Vector3d(10_000_000.0, 6_350_000.0, 0.0), EarthRadius, sun, sunRadius);

        Assert.Equal(1.0, clear, 12);
        Assert.Equal(0.0, total, 12);
        Assert.InRange(partial, 0.01, 0.99);
    }

    [Fact]
    public void ApparentSolarRadiusMatchesHalfDegreeDiscAtOneAu()
    {
        double radius = MissionGeometry.ApparentAngularRadius(696_000_000.0, 149_597_870_700.0);

        Assert.InRange(radius * 2.0 * 180.0 / System.Math.PI, 0.52, 0.54);
        Assert.Equal(0.0, MissionGeometry.ApparentAngularRadius(double.NaN, 1.0));
        Assert.Equal(0.0, MissionGeometry.ApparentAngularRadius(1.0, -1.0));
    }

    [Fact]
    public void SolarDiscVisibilityResolvesAnnularEclipse()
    {
        var observer = Vector3d.Zero;
        var sun = new Vector3d(150_000_000_000.0, 0.0, 0.0);
        double visibility = MissionGeometry.SolarDiscVisibility(
            observer, new Vector3d(500_000_000.0, 0.0, 0.0), 1_000_000.0,
            sun, 696_000_000.0);

        Assert.InRange(visibility, 0.01, 0.99);
    }

    [Fact]
    public void DiscOverlapIsScaleInvariantAndMatchesAnalyticEqualDiscs()
    {
        double expected = 1.0 - (2.0 * System.Math.PI / 3.0
            - System.Math.Sqrt(3.0) / 2.0) / System.Math.PI;

        Assert.Equal(expected, MissionGeometry.DiscVisibility(1.0, 1.0, 1.0), 12);
        Assert.Equal(MissionGeometry.DiscVisibility(1.0, 0.7, 0.9),
            MissionGeometry.DiscVisibility(1e-9, 0.7e-9, 0.9e-9), 12);
    }

    [Fact]
    public void DiscVisibilityIsMonotonicAcrossPenumbra()
    {
        double previous = 0.0;
        for (int i = 0; i <= 1000; i++)
        {
            double visible = MissionGeometry.DiscVisibility(1.0, 0.8, i * 1.8 / 1000.0);
            Assert.InRange(visible, previous - 1e-12, 1.0);
            previous = visible;
        }
    }

    [Fact]
    public void PowerSystem_EclipseZeroesSolarOutput()
    {
        var power = new PowerSystem();
        var sunPos = new Vector3d(150_000_000_000.0, 0.0, 0.0);
        var vesselPos = new Vector3d(EarthRadius + 400_000.0, 0.0, 0.0);

        power.Tick(1.0, vesselPos, sunPos, inEclipse: false);
        double sunlitSolar = power.SolarOutputKw;
        Assert.True(sunlitSolar > 0.0);

        power.Tick(1.0, vesselPos, sunPos, inEclipse: true);
        Assert.Equal(0.0, power.SolarOutputKw);
    }

    [Fact]
    public void PowerSystemPenumbraProducesProportionalSolarOutput()
    {
        var full = new PowerSystem();
        var half = new PowerSystem();
        var sun = new Vector3d(149_597_870_700.0, 0.0, 0.0);

        full.Tick(1.0, Vector3d.Zero, sun, 1.0);
        half.Tick(1.0, Vector3d.Zero, sun, 0.5);

        Assert.Equal(full.SolarOutputKw * 0.5, half.SolarOutputKw, 10);
    }

    [Fact]
    public void PowerSystem_LifeSupportLoadDrainsBatteryInEclipse()
    {
        var power = new PowerSystem();
        var sunPos = new Vector3d(150_000_000_000.0, 0.0, 0.0);
        var vesselPos = new Vector3d(EarthRadius + 400_000.0, 0.0, 0.0);
        double startBattery = power.BatteryKwh;

        power.Tick(3600.0, vesselPos, sunPos, inEclipse: true, extraLoadKw: 2.0);

        Assert.True(power.BatteryKwh < startBattery);
        Assert.Equal(2.0, power.ExtraLoadKw);
    }

    [Fact]
    public void CommsDelay_ScalesWithEarthDistance()
    {
        var earthPos = Vector3d.Zero;
        var leoPos   = new Vector3d(EarthRadius + 400_000.0, 0.0, 0.0);
        var moonPos  = new Vector3d(384_400_000.0, 0.0, 0.0);

        double leoDelay  = MissionGeometry.SignalDelaySeconds(leoPos, earthPos, SpeedOfLight, EarthRadius);
        double moonDelay = MissionGeometry.SignalDelaySeconds(moonPos, earthPos, SpeedOfLight, EarthRadius);

        Assert.InRange(leoDelay, 0.001, 0.010);
        Assert.InRange(moonDelay, 1.0, 1.5);
        Assert.True(moonDelay > leoDelay * 100.0);
    }

    [Fact]
    public void CommsSystem_ReportsRoundTripDelayFromPosition()
    {
        var comms = new CommsSystem();
        var earthPos = Vector3d.Zero;
        var vesselPos = new Vector3d(EarthRadius + 400_000.0, 0.0, 0.0);
        var earth = new CelestialBody { Id = "earth", Radius = EarthRadius };
        var bodies = new List<CelestialBody> { earth };

        comms.Tick(1.0, vesselPos, earthPos, bodies);

        double expected = MissionGeometry.SignalDelaySeconds(vesselPos, earthPos, speedOfLight: 3e8, EarthRadius);
        Assert.InRange(comms.SignalDelaySeconds, expected * 0.999, expected * 1.001);
        Assert.True(comms.HasSignal);
    }

    [Fact]
    public void LifeSupport_ActivePhaseDrawsMoreEcThanIdle()
    {
        var ls = new LifeSupportSystem();
        int crew = 4;

        double activeKw = ls.GetEcLoadKw(crew, SystemsMissionPhase.Active);
        double idleKw   = ls.GetEcLoadKw(crew, SystemsMissionPhase.Idle);

        Assert.True(activeKw > idleKw);
        Assert.Equal(0.45 * crew, activeKw, precision: 6);
        Assert.Equal(0.15, idleKw, precision: 6);
    }

    [Fact]
    public void LifeSupport_IdlePhaseDoesNotConsumeOxygen()
    {
        var ls = new LifeSupportSystem();
        double startO2 = ls.OxygenKg;

        ls.Tick(3600.0, crewCount: 4, SystemsMissionPhase.Idle);

        Assert.Equal(startO2, ls.OxygenKg);
    }

    [Fact]
    public void LifeSupport_ActivePhaseConsumesOxygen()
    {
        var ls = new LifeSupportSystem();
        double startO2 = ls.OxygenKg;

        ls.Tick(3600.0, crewCount: 4, SystemsMissionPhase.Active);

        Assert.True(ls.OxygenKg < startO2);
    }
}
