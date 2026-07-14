namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Xunit;

/// <summary>C3 — mission phase track ordering and deorbit/EDL cue copy.</summary>
public sealed class MissionPhaseTrackTests
{
    [Fact]
    public void Sequence_IncludesCoastAndRetroBeforeEntry()
    {
        int orbit = System.Array.IndexOf(MissionPhaseTrack.Sequence, "ORBIT");
        int coast = System.Array.IndexOf(MissionPhaseTrack.Sequence, "COAST");
        int retro = System.Array.IndexOf(MissionPhaseTrack.Sequence, "RETRO_BURN");
        int entry = System.Array.IndexOf(MissionPhaseTrack.Sequence, "ENTRY");

        Assert.True(orbit >= 0);
        Assert.True(coast == orbit + 1);
        Assert.True(retro == coast + 1);
        Assert.True(entry == retro + 1);
    }

    [Fact]
    public void IndexOf_DeorbitRetroIsBeforeEntry_LandingRetroMapsNearFinal()
    {
        int deorbitRetro = MissionPhaseTrack.IndexOf("RETRO_BURN", afterEntryInterface: false);
        int entry = MissionPhaseTrack.IndexOf("ENTRY");
        int landingRetro = MissionPhaseTrack.IndexOf("RETRO_BURN", afterEntryInterface: true);
        int finalDescent = MissionPhaseTrack.IndexOf("FINAL_DESCENT");

        Assert.True(deorbitRetro < entry);
        Assert.Equal(finalDescent, landingRetro);
    }

    [Fact]
    public void NextPhase_OrbitToCoastToRetroToEntry()
    {
        Assert.Equal("COAST", MissionPhaseTrack.NextPhase("ORBIT"));
        Assert.Equal("RETRO_BURN", MissionPhaseTrack.NextPhase("COAST"));
        Assert.Equal("ENTRY", MissionPhaseTrack.NextPhase("RETRO_BURN"));
        Assert.Equal("PEAK_HEATING", MissionPhaseTrack.NextPhase("ENTRY"));
    }

    [Fact]
    public void PreLaunchAndIgnition_MapSensibly()
    {
        Assert.Equal(-1, MissionPhaseTrack.IndexOf("PRE_LAUNCH"));
        Assert.Equal(MissionPhaseTrack.IndexOf("COUNTDOWN"), MissionPhaseTrack.IndexOf("IGNITION"));
    }

    [Fact]
    public void PeriapsisInAtmosphere_DetectsDeorbitPe()
    {
        Assert.True(MissionPhaseTrack.PeriapsisInAtmosphere(80_000.0, 140_000.0));
        Assert.False(MissionPhaseTrack.PeriapsisInAtmosphere(250_000.0, 140_000.0));
        Assert.True(MissionPhaseTrack.PeriapsisInAtmosphere(-1000.0, 140_000.0));
    }

    [Fact]
    public void ApproximateTimeToPeriapsis_NearApoapsisIsAboutHalfPeriod()
    {
        // Circular-ish ellipse a=6671 km (300 km LEO mean), M=π ⇒ half orbit to Pe.
        const double a = 6_671_000.0;
        const double mu = 3.986004418e14;
        double period = 2.0 * System.Math.PI * System.Math.Sqrt(a * a * a / mu);
        double t = MissionPhaseTrack.ApproximateTimeToPeriapsisSec(a, 0.01, System.Math.PI, mu);

        Assert.False(double.IsNaN(t));
        Assert.InRange(t, period * 0.45, period * 0.55);
    }

    [Fact]
    public void FormatActionableCue_DeorbitAndCoastPaths()
    {
        Assert.Equal("DEORBIT BURN",
            MissionPhaseTrack.FormatActionableCue("RETRO_BURN", true, 600.0));
        Assert.Null(
            MissionPhaseTrack.FormatActionableCue("RETRO_BURN", true, 60.0, afterEntryInterface: true));
        Assert.Equal("ENTRY INTERFACE",
            MissionPhaseTrack.FormatActionableCue("ENTRY", true, double.NaN));
        Assert.Equal("ENTRY INTERFACE in ~10m",
            MissionPhaseTrack.FormatActionableCue("COAST", true, 600.0));
        Assert.Equal("ENTRY INTERFACE in ~45s",
            MissionPhaseTrack.FormatActionableCue("ORBIT", true, 45.0));
        Assert.Null(
            MissionPhaseTrack.FormatActionableCue("ORBIT", periapsisInAtmosphere: false, double.NaN));
    }
}
