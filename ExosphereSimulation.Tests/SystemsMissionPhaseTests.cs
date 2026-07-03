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
