namespace ExosphereSimulation.Tests;

using System.IO;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Construction;

/// <summary>
/// RF-07 acceptance, at full orbital energy — the case the Godot playtest harness cannot
/// reach, because its EDL tail times out before peak heating (VAL-01).
///
/// This flies a real de-orbiting Starship through the real atmosphere with the real
/// integrator, and asserts the contract the whole vehicle exists to satisfy: belly-first,
/// the tiles take the plasma and the hull lives; tail-first, the hull takes it and dies.
///
/// It is deliberately harness-independent: attitude is imposed rather than flown, so this
/// tests the THERMAL protection given a correct attitude, not the EDL autopilot's ability
/// to hold one.
/// </summary>
public sealed class OrbitalReentrySurvivalTests
{
    private const double EntryAltitude = 120_000.0;
    private const double EntrySpeed    = 7_600.0;    // m/s — orbital energy
    private const double FlightPathDeg = -1.6;       // shallow, like a real de-orbit
    private const double Dt            = 0.5;
    private const double MaxDuration   = 900.0;      // s of simulated entry

    /// <summary>Propellant left at entry: enough for the landing burn, nothing more.</summary>
    private const double LandingPropellantFraction = 0.06;

    /// <summary>Flies an entry holding a fixed attitude and reports what the shielded parts saw.</summary>
    private static (double PeakStructure, double PeakSkin, double Damage, bool Destroyed) FlyEntry(bool bellyFirst)
    {
        var universe = Universe.LoadFromDataDirectory(Path.Combine(RepoRoot(), "data"));
        var earth = universe.GetBody("earth")!;

        var vessel = BuildStarship();

        // Start on a shallow descending trajectory at entry interface.
        var up = new Vector3d(1, 0, 0);
        var east = new Vector3d(0, 0, 1);
        vessel.Position = earth.Position + up * (earth.Radius + EntryAltitude);

        double fpa = FlightPathDeg * MathUtils.DEG_TO_RAD;
        var velDir = (east * System.Math.Cos(fpa) + up * System.Math.Sin(fpa)).Normalized;
        vessel.Velocity = earth.Velocity + velDir * EntrySpeed;

        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;

        // Peak temperatures are tracked on the SHIELDED parts. The engine cluster carries no
        // tiles by design (it is refractory, and it rides in the wake), so including it would
        // measure the wrong thing.
        double peakStructure = 0.0;
        double peakSkin = 0.0;
        double damage = 0.0;

        for (double t = 0.0; t < MaxDuration; t += Dt)
        {
            // Impose the attitude under test. The heat shield is the vessel's local -X face,
            // so belly-first means the airflow arrives along -X.
            var surfVel = vessel.GetSurfaceVelocity(earth);
            if (surfVel.Magnitude > 1.0)
            {
                var flow = surfVel.Normalized;
                vessel.Orientation = bellyFirst
                    ? Quaterniond.FromTo(-Vector3d.Right, flow)   // tiles into the flow
                    : Quaterniond.FromTo(Vector3d.Right, flow);   // tiles turned away
            }

            universe.Tick(Dt);

            foreach (var part in vessel.Parts.Parts)
            {
                if (!part.Definition.HasHeatShield) continue;
                if (part.Temperature > peakStructure) peakStructure = part.Temperature;
                if (part.SkinTemperature > peakSkin)  peakSkin = part.SkinTemperature;
                if (part.ThermalDamage > damage)      damage = part.ThermalDamage;
            }

            if (vessel.IsDestroyed) break;
            if (earth.GetAltitude(vessel.Position) < 30_000.0) break;   // through the fire
        }

        return (peakStructure, peakSkin, damage, vessel.IsDestroyed);
    }

    [Fact]
    public void BellyFirst_TilesGlowAndTheHullSurvivesUndamaged()
    {
        var entry = FlyEntry(bellyFirst: true);

        Assert.False(entry.Destroyed,
            $"a belly-first entry must survive (structure peaked at {entry.PeakStructure:F0} K)");

        // The tiles are supposed to run white-hot — that is how they shed the heat.
        Assert.True(entry.PeakSkin > 1_000.0,
            $"the TPS face should glow (peaked at only {entry.PeakSkin:F0} K)");

        // The hull behind them must stay clear of its tolerance, and take no damage at all.
        Assert.True(entry.PeakStructure < 900.0,
            $"an intact shield must keep the hull cool (got {entry.PeakStructure:F0} K)");
        Assert.Equal(0.0, entry.Damage);
    }

    [Fact]
    public void TailFirst_TheBareHullCooksPastItsTolerance()
    {
        var entry = FlyEntry(bellyFirst: false);

        // Same flight, shield turned away: the plasma meets bare metal. The hull is driven
        // past its rated tolerance and takes irreversible damage.
        //
        // Note it does not necessarily burn THROUGH within the entry window: ThermalDamage
        // accrues at (ratio − 1)·dt·0.5, so a hull sitting just over its limit chars slowly.
        // That damage-rate calibration is a separate knob from this thermal model, and is
        // deliberately left alone here.
        Assert.True(entry.PeakStructure > 1_800.0,
            $"a bare hull must cook past tolerance (only reached {entry.PeakStructure:F0} K)");
        Assert.True(entry.Damage > 0.0,
            "exceeding tolerance must leave irreversible damage");
    }

    [Fact]
    public void AttitudeIsWhatDecides_NotLuck()
    {
        var belly = FlyEntry(bellyFirst: true);
        var tail  = FlyEntry(bellyFirst: false);

        // Same vehicle, same trajectory, same air. Only the attitude differs — and it is
        // worth a thousand kelvin on the structure, and the difference between an undamaged
        // hull and a charred one.
        Assert.True(tail.PeakStructure > belly.PeakStructure + 800.0,
            $"attitude must dominate the outcome (belly {belly.PeakStructure:F0} K vs tail {tail.PeakStructure:F0} K)");
        Assert.True(tail.Damage > belly.Damage);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Vessel BuildStarship()
    {
        var vessel = new Vessel { Name = "EntryTest" };
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "data", "parts"));

        var command = new Part(catalog["starship_command"]);
        var tank    = new Part(catalog["starship_tank"]);
        var engines = new Part(catalog["starship_engines"]);

        vessel.Parts.SetRoot(command);
        vessel.Parts.AddPart(tank);
        vessel.Parts.AddPart(engines);
        vessel.Parts.AddJoint(new Joint(command, tank,    "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank,    engines, "bottom", "top"));

        // A vehicle that is COMING BACK is nearly empty — it spent its propellant getting up
        // there. Entering with full tanks would give a ballistic coefficient an order of
        // magnitude too high, so the ship would knife through the upper atmosphere without
        // slowing and see heating no real entry ever produces.
        foreach (var part in vessel.Parts.Parts)
        {
            part.LiquidFuel *= LandingPropellantFraction;
            part.Oxidizer   *= LandingPropellantFraction;
        }

        return vessel;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root not found");
    }
}
